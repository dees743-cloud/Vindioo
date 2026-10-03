using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Vindioo.Services;

/// <summary>
/// De Pricewatch van Tweakers: wat kost dit <b>nieuw</b>, en - als het niet meer te koop is -
/// wat was de laatst bekende prijs. Dat is iets anders dan de prijsindicatie hiernaast, die de
/// vraagprijzen van tweedehandszoekertjes samenneemt. Nieuw is een <b>bovengrens</b>: wie een
/// Marantz CD6007 tweedehands voor € 250 ziet staan, weet met "nieuw vanaf € 395 bij 8 winkels"
/// meteen waar dat bedrag ligt.
///
/// Drie dingen bepalen hoe dit werkt, en alle drie komen ze uit een meting op 27 september 2026.
///
/// <b>1. Hun zoekpagina blijft met rust.</b> De robots.txt van Tweakers verbiedt élke zoekweg:
/// <c>/pricewatch/zoeken</c>, <c>/aanbod/zoeken</c>, <c>/zoeken</c> en <c>/search</c>. Wat ze
/// juist wél publiceren is een <b>sitemap met al hun producten</b> (13 bestanden, 303 122
/// producten). Het opzoeken gebeurt daarom hier op de pc, in een kopie van die sitemap, en er
/// gaat pas een verzoek naar Tweakers wanneer je een product aanklikt.
///
/// <b>2. De app kiest het product niet.</b> Een titel van een tweedehandszoekertje automatisch
/// aan een product koppelen werkt niet, en dat is nagemeten op verse zoekertjes: "Marantz CD5003"
/// kwam uit op de CD-70, "HP EliteBook 840 G7" op een losse Intel-processor en "Nintendo Wii Mini
/// spelcomputer" op een golfspelletje. Daarom levert <see cref="VoorstellenAsync"/>
/// <b>voorstellen</b> met hun volledige productnaam erbij, en klikt de gebruiker. Dezelfde keuze
/// als bij de namen die de AI-controle van een foto leest.
///
/// <b>3. De privacymuur kost één extra verzoek.</b> Het eerste verzoek belandt op de muur van DPG
/// (<c>myprivacy.dpgmedia.nl</c>). Die zet een sessiecookie en laat het tweede verzoek gewoon
/// door. Er wordt daarbij niets aanvaard - er gaat geen toestemmingskeuze mee - en het scheelt
/// de brug, dus dit werkt ook met Chrome dicht. De sitemap staat niet achter die muur.
/// </summary>
public static class Pricewatch
{
    private const string SitemapIndex = "https://tweakers.net/pricewatch/sitemap.xml";
    private const string PaginaBasis = "https://tweakers.net/pricewatch/";

    /// <summary>Hoe oud de kopie van de sitemap mag zijn voor ze opnieuw opgehaald wordt.</summary>
    private static readonly TimeSpan Houdbaar = TimeSpan.FromDays(30);

    private static string IndexBestand => Path.Combine(AppPaths.Folder, "pricewatch-index.txt");

    private static readonly SemaphoreSlim IndexSlot = new(1, 1);

    private static readonly HttpClient Http = Client();

    /// <summary>
    /// Negentig seconden, want hier hangt de sitemap aan: 11 MB in één keer.
    ///
    /// De koekjespot is hier de hele truc (zie <c>HaalAsync</c>): de privacymuur van DPG zet
    /// een sessiecookie en laat het tweede verzoek door. Die stond vroeger hier apart
    /// aangezet, maar <see cref="HttpFactory"/> heeft hem sowieso - een
    /// <c>SocketsHttpHandler</c> houdt zijn cookies vanzelf bij.
    /// </summary>
    private static HttpClient Client() => HttpFactory.MaakClient(TimeSpan.FromSeconds(90));

    /// <summary>Eén product uit de sitemap: genoeg om zijn pagina te bouwen en zijn naam te tonen.</summary>
    public sealed record Voorstel(string Id, string Slug)
    {
        /// <summary>De naam zoals een mens hem leest: "marantz-cd6007-zwart" wordt "Marantz cd6007 zwart".</summary>
        public string Naam => Slug.Length == 0
            ? Id
            : char.ToUpperInvariant(Slug[0]) + Slug[1..].Replace('-', ' ');

        public string Url => $"{PaginaBasis}{Id}/{Slug}.html";
    }

    /// <summary>
    /// Wat er van een productpagina te halen valt. Een prijs kan ontbreken: dan is het product
    /// niet meer te koop, en zegt <see cref="LaatstBekend"/> wat het laatst kostte.
    /// </summary>
    public sealed record Prijsbeeld(string Naam, string Url,
        decimal? Vanaf = null, decimal? Tot = null, int Winkels = 0,
        decimal? LaatstBekend = null, string LaatstBekendOp = "",
        int Advertenties = 0, decimal? TweedehandsVanaf = null, string? Fout = null)
    {
        /// <summary>De regel die in het venster komt te staan.</summary>
        public string Regel => Fout is not null ? Fout
            : Vanaf is { } v
                ? $"Nieuw vanaf {Bedrag(v)}" + (Winkels > 1 ? $" bij {Winkels} winkels" : "")
            : LaatstBekend is { } l
                ? $"Niet meer te koop; laatst bekend {Bedrag(l)}" +
                  (LaatstBekendOp.Length > 0 ? $" op {LaatstBekendOp}" : "")
                : "Geen prijs bekend bij Tweakers";

        /// <summary>Wat hun eigen tweedehandsmarkt voor dit product heeft, of leeg.</summary>
        public string TweedehandsRegel => Advertenties == 0
            ? ""
            : $"Vraag & Aanbod: {Advertenties} advertentie{(Advertenties == 1 ? "" : "s")}" +
              (TweedehandsVanaf is { } t ? $", vanaf {Bedrag(t)}" : "");

        private static string Bedrag(decimal d) => d == Math.Truncate(d)
            ? "€ " + d.ToString("0", CultureInfo.InvariantCulture)
            : "€ " + d.ToString("0.00", CultureInfo.GetCultureInfo("nl-BE"));
    }

    // ==================== opzoeken ====================

    /// <summary>
    /// De producten die het best bij deze zoekterm passen, met hun volledige naam, zodat de
    /// gebruiker ziet wát hij aanklikt. Haalt de kopie van de sitemap op als die er nog niet is
    /// of ouder is dan een maand.
    /// </summary>
    public static async Task<IReadOnlyList<Voorstel>> VoorstellenAsync(string term, int hoogstens = 6,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return Array.Empty<Voorstel>();

        var bestand = await ZorgVoorIndexAsync(status, ct);
        if (bestand is null) return Array.Empty<Voorstel>();

        // Een doorloop van 303 122 regels hoort niet op de schermdraad. Zonder deze Task.Run
        // duurde het opzoeken vanuit het venster 38 seconden waar het er in een console 2,0
        // deed: elke voortzetting van een await kwam terug op een draad die op dat moment het
        // scherm aan het tekenen was.
        return await Task.Run(() => Kies(term, Regels(bestand), hoogstens), ct);
    }

    /// <summary>
    /// De index regel per regel, zonder hem in het geheugen te trekken. Dat is met opzet: het
    /// zijn 303 122 producten (12 MB), en dit gebeurt enkel wanneer je er zelf om vraagt. Een
    /// doorloop kost een tiende seconde; een index die blijft staan kost de hele dag geheugen.
    /// </summary>
    private static IEnumerable<(string Id, string Slug)> Regels(string bestand)
    {
        foreach (var regel in File.ReadLines(bestand))
        {
            var streep = regel.IndexOf('|');
            if (streep > 0) yield return (regel[..streep], regel[(streep + 1)..]);
        }
    }

    /// <summary>
    /// Welke producten bij deze term passen. Apart van het inlezen, zodat het zonder netwerk
    /// en zonder bestand na te meten is.
    ///
    /// De regel die de meeste onzin tegenhoudt: <b>staat er een modelnummer in de term, dan moet
    /// dat in de productnaam staan</b>. Zonder die eis kwam "Marantz CD5003" uit op de CD-70 -
    /// een ander toestel, met een andere prijs.
    /// </summary>
    internal static List<Voorstel> Kies(string term, IEnumerable<(string Id, string Slug)> index, int hoogstens)
    {
        var woorden = Woorden(term).Where(w => w.Length > 1).ToHashSet();
        if (woorden.Count == 0) return new List<Voorstel>();

        var model = PriceIndicator.ParseTerm(term);
        var cijfers = model.HasModel ? model.Digits : null;

        var beste = new List<(int Score, int Extra, Voorstel V)>();

        foreach (var (id, slug) in index)
        {
            var delen = slug.Split('-');

            // Het modelnummer moet erin staan, los ("cd-70") of vastgeplakt ("cd6007").
            if (cijfers is not null && !delen.Any(d => d == cijfers || d.Contains(cijfers, StringComparison.Ordinal)))
                continue;

            // Distinct, want een productnaam kan een woord twee keer bevatten ("... Digital
            // Edition - Marvel's Wolverine Limited Edition"), en dan scoort de bundel hoger
            // dan het kale toestel waar je naar kijkt.
            var raak = delen.Distinct().Count(d => woorden.Contains(d));
            if (raak < 2) continue;

            // Hoe minder woorden er in de productnaam overblijven die niet gevraagd zijn, hoe
            // preciezer de treffer: "marantz cd6007 zwart" wint van "marantz cd6007 zilver ...".
            beste.Add((raak, delen.Length - raak, new Voorstel(id, slug)));
        }

        return beste
            .OrderByDescending(x => x.Score).ThenBy(x => x.Extra).ThenBy(x => x.V.Slug.Length)
            .Take(hoogstens)
            .Select(x => x.V)
            .ToList();
    }

    private static IEnumerable<string> Woorden(string tekst) =>
        Regex.Split(tekst.ToLowerInvariant(), @"[^a-z0-9]+").Where(w => w.Length > 0);

    // ==================== de kopie van de sitemap ====================

    /// <summary>
    /// Zorgt dat de index er is en niet te oud, en geeft het pad terug. Lukt het ophalen niet,
    /// dan geeft een bestaande (oudere) index nog altijd antwoord; is er ook die niet, dan null.
    /// </summary>
    private static async Task<string?> ZorgVoorIndexAsync(IProgress<string>? status, CancellationToken ct)
    {
        var pad = IndexBestand;
        var bestaat = File.Exists(pad);

        if (bestaat && DateTime.Now - File.GetLastWriteTime(pad) < Houdbaar) return pad;

        await IndexSlot.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Een tweede aanroeper kan hem intussen opgehaald hebben.
            if (File.Exists(pad) && DateTime.Now - File.GetLastWriteTime(pad) < Houdbaar) return pad;

            status?.Report("De productlijst van Tweakers ophalen; dat gebeurt hoogstens één keer per maand...");
            await Task.Run(() => BouwIndexAsync(pad, status, ct), ct).ConfigureAwait(false);
            return pad;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"pricewatch: de productlijst ophalen lukte niet: {ex.Message}");
            return bestaat ? pad : null;
        }
        finally
        {
            IndexSlot.Release();
        }
    }

    private static async Task BouwIndexAsync(string pad, IProgress<string>? status, CancellationToken ct)
    {
        var index = await Http.GetStringAsync(SitemapIndex, ct).ConfigureAwait(false);
        var kaarten = Regex.Matches(index, @"<loc>\s*([^<\s]+)\s*</loc>").Select(m => m.Groups[1].Value).ToList();

        var tijdelijk = pad + ".bezig";
        await using (var uit = new StreamWriter(tijdelijk, false))
        {
            var geteld = 0;

            foreach (var kaart in kaarten)
            {
                ct.ThrowIfCancellationRequested();

                var xml = await Http.GetStringAsync(kaart, ct).ConfigureAwait(false);

                foreach (Match m in Regex.Matches(xml,
                             @"<loc>\s*https://tweakers\.net/pricewatch/(\d+)/([^<\s]+?)\.html\s*</loc>"))
                {
                    await uit.WriteLineAsync($"{m.Groups[1].Value}|{m.Groups[2].Value}").ConfigureAwait(false);
                    geteld++;
                }

                status?.Report($"De productlijst van Tweakers ophalen... {geteld} producten");
            }

            Log.Write($"pricewatch: productlijst opgehaald, {geteld} producten uit {kaarten.Count} sitemaps");
        }

        File.Move(tijdelijk, pad, overwrite: true);
    }

    // ==================== de pagina van één product ====================

    /// <summary>
    /// Haalt de productpagina op en leest er de prijs van.
    ///
    /// <b>Twee verzoeken, en dat is met opzet.</b> Het eerste belandt op de privacymuur van DPG
    /// (<c>myprivacy.dpgmedia.nl</c>); die zet een sessiecookie (<c>authId</c>) en laat het
    /// tweede verzoek gewoon door - 296 kB met de prijs erin. Daar wordt dus niets aanvaard: er
    /// gaat geen toestemmingskeuze mee, enkel een sessie. Dat scheelt de brug, en dus je eigen
    /// Chrome: dit werkt ook wanneer die dicht staat.
    ///
    /// Zonder koekjespot lukt het nooit, hoe vaak je het ook probeert - dat was precies het
    /// verschil tussen <c>curl</c> (altijd de muur) en een browser (meteen de pagina).
    /// </summary>
    public static async Task<Prijsbeeld> HaalAsync(Voorstel product, CancellationToken ct = default)
    {
        try
        {
            var html = await PaginaAsync(product.Url, ct).ConfigureAwait(false);
            return await Task.Run(() => Lees(html, product), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"pricewatch: {product.Url} ophalen lukte niet: {ex.Message}");
            return new Prijsbeeld(product.Naam, product.Url, Fout: FriendlyError.Describe(ex));
        }
    }

    /// <summary>De pagina, met hoogstens één keer opnieuw wanneer de privacymuur ertussen komt.</summary>
    private static async Task<string> PaginaAsync(string url, CancellationToken ct)
    {
        for (var poging = 0; ; poging++)
        {
            using var antwoord = await Http.GetAsync(url, ct).ConfigureAwait(false);
            antwoord.EnsureSuccessStatusCode();

            var beland = antwoord.RequestMessage?.RequestUri?.Host ?? "";
            if (poging > 0 || !beland.Contains("myprivacy", StringComparison.OrdinalIgnoreCase))
                return await antwoord.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Leest een opgehaalde productpagina. Apart, zodat het zonder netwerk na te meten is.
    ///
    /// De prijs komt uit het <c>ld+json</c>-blok (schema.org), niet uit de opmaak: dat is
    /// dezelfde afweging als bij de foto's van 2dehands, en het staat er als
    /// <c>AggregateOffer</c> met <c>lowPrice</c>, <c>highPrice</c> en <c>offerCount</c>. Is er
    /// geen enkele winkel meer, dan staat er géén offers-blok en zegt de pagina het in een zin.
    /// </summary>
    internal static Prijsbeeld Lees(string html, Voorstel product)
    {
        var doc = new HtmlParser().ParseDocument(html ?? "");

        var naam = product.Naam;
        decimal? vanaf = null, tot = null;
        var winkels = 0;

        foreach (var blok in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(blok.TextContent);
                var wortel = json.RootElement;

                var knopen = wortel.ValueKind == JsonValueKind.Object &&
                             wortel.TryGetProperty("@graph", out var graaf)
                    ? graaf.EnumerateArray().ToList()
                    : new List<JsonElement> { wortel };

                foreach (var knoop in knopen)
                {
                    if (knoop.ValueKind != JsonValueKind.Object) continue;
                    if (!knoop.TryGetProperty("@type", out var soort) ||
                        soort.GetString() != "Product") continue;

                    if (knoop.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } tekst)
                        naam = tekst;

                    if (!knoop.TryGetProperty("offers", out var aanbod) ||
                        aanbod.ValueKind != JsonValueKind.Object) continue;

                    if (aanbod.TryGetProperty("lowPrice", out var laag)) vanaf = Getal(laag);
                    if (aanbod.TryGetProperty("highPrice", out var hoog)) tot = Getal(hoog);
                    if (aanbod.TryGetProperty("offerCount", out var aantal)) winkels = (int)(Getal(aantal) ?? 0);
                }
            }
            catch (JsonException)
            {
                // Een blok dat geen geldige JSON is, slaan we over: er staan er meerdere.
            }
        }

        // Niet meer te koop: "De laatst bekende laagste prijs was € 41,51 op 20 juni 2026."
        decimal? laatst = null;
        var laatstOp = "";

        var melding = doc.QuerySelector(".noPriceMessage")?.TextContent ?? "";
        var m = Regex.Match(melding, @"laatst bekende laagste prijs was\s*€?\s*([\d.,]+)\s*(?:op\s+([^.]+))?",
            RegexOptions.IgnoreCase);

        if (m.Success)
        {
            laatst = Bedrag(m.Groups[1].Value);
            laatstOp = m.Groups[2].Value.Trim();
        }

        // Hun eigen tweedehandsmarkt, op dezelfde pagina - dus zonder hun zoekpagina aan te raken.
        var va = doc.QuerySelector("#section-vraagaanbod")?.TextContent ?? "";
        va = Regex.Replace(va, @"\s+", " ");

        var advertenties = 0;
        decimal? tweedehands = null;

        var adv = Regex.Match(va, @"(\d+)\s*advertentie");
        if (adv.Success) advertenties = int.Parse(adv.Groups[1].Value);

        var vanafVa = Regex.Match(va, @"aangeboden,\s*vanaf\s*€?\s*([\d.,]+)", RegexOptions.IgnoreCase);
        if (vanafVa.Success) tweedehands = Bedrag(vanafVa.Groups[1].Value);

        return new Prijsbeeld(naam, product.Url, vanaf, tot, winkels, laatst, laatstOp,
            advertenties, tweedehands);
    }

    private static decimal? Getal(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDecimal(),
        JsonValueKind.String => Bedrag(e.GetString() ?? ""),
        _ => null
    };

    /// <summary>"€ 41,51" en "420,-" naar een getal. De komma is decimaal, de punt duizendtal.</summary>
    private static decimal? Bedrag(string tekst)
    {
        var m = Regex.Match(tekst ?? "", @"\d[\d.,]*");
        if (!m.Success) return null;

        var ruw = m.Value.TrimEnd(',', '.');

        if (ruw.Contains(',')) ruw = ruw.Replace(".", "").Replace(',', '.');
        else if (ruw.Count(c => c == '.') == 1 && ruw.Split('.')[1].Length == 3) ruw = ruw.Replace(".", "");

        return decimal.TryParse(ruw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}

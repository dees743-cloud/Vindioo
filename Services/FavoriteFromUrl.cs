using System.Net.Http;
using System.Text.RegularExpressions;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Maakt een favoriet van één webadres: wat er gebeurt wanneer je in Chrome op een zoekertje
/// rechtsklikt en <i>Zet in favorieten van Zentrix</i> kiest.
///
/// Dit is de <b>enige</b> plaats waar de brug de andere kant op werkt. Overal elders geeft de app
/// werk aan de extensie; hier komt er iets binnen dat de app niet gevraagd heeft. Daarom staan er
/// drie sloten op, en ze doen alle drie iets anders:
///
/// <list type="number">
///   <item>het verzoek moet <b>getekend</b> zijn met de koppelcode - dat regelt
///         <see cref="BridgeServer"/> al voor elk pad;</item>
///   <item>het adres moet <b>veilig</b> zijn (<see cref="SiteUrlCheck.IsVeiligAdres"/>): https,
///         en geen adres op je eigen netwerk. Wat hier binnenkomt gaat de app immers zelf
///         ophalen;</item>
///   <item>de site moet <b>bekend</b> zijn. Zonder sitebestand is er geen naam voor de bron en
///         geen manier om een id uit de link te halen, en dan zou dezelfde kavel twee keer in je
///         favorieten komen: één keer via het zoekresultaat en één keer via Chrome.</item>
/// </list>
/// </summary>
public static class FavoriteFromUrl
{
    /// <summary>Wat er van een poging terechtkwam. De tekst gaat naar Chrome, dus ze is voor jou.</summary>
    public record Uitkomst(bool Ok, string Melding, Listing? Favoriet = null);

    /// <summary>
    /// Het sitebestand waar dit adres bij hoort, of null.
    ///
    /// Vergelijkt op host, en laat <c>www.</c> ervoor of eraf los: een sitebestand dat naar
    /// <c>www.2dehands.be</c> zoekt, hoort ook bij een link naar <c>2dehands.be</c>.
    /// </summary>
    public static SiteDefinition? SiteVoor(string url, IEnumerable<SiteDefinition> sites)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var adres)) return null;

        return sites.FirstOrDefault(site =>
            SiteUrlCheck.Hosts(site).Any(host => ZelfdeHost(host, adres.Host)));
    }

    /// <summary>
    /// Het <c>IdPattern</c> van een sitebestand als regex, of null. Een kapot patroon in een
    /// gedeeld bestand mag niets stukmaken: dan wordt de volledige link de identiteit, net als
    /// in <see cref="GenericSource"/>.
    /// </summary>
    private static System.Text.RegularExpressions.Regex? Patroon(SiteDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.IdPattern)) return null;

        try
        {
            return new System.Text.RegularExpressions.Regex(def.IdPattern);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool ZelfdeHost(string a, string b)
    {
        a = Kaal(a);
        b = Kaal(b);

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string Kaal(string host) =>
            host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// Haalt de pagina op, leest eruit wat er te lezen valt en zet het bij de favorieten.
    ///
    /// Geeft altijd een zin terug die je iets zegt, ook wanneer het niet lukte: dit gebeurt in
    /// een ander venster dan Zentrix, dus een stille mislukking zou je nooit opmerken.
    /// </summary>
    public static async Task<Uitkomst> VoegToeAsync(string url, IReadOnlyList<SiteDefinition> sites,
                                                    HistoryStore history, CancellationToken ct = default)
    {
        if (!SiteUrlCheck.IsVeiligAdres(url, out var reden))
            return new Uitkomst(false, $"Dit adres gaat niet: {reden}.");

        var def = SiteVoor(url, sites);

        // Geen sitebestand? Dan is het misschien een veilinghuis dat op een van je veilingsites
        // meeloopt: AlleVeilingen verzamelt er twintig. Zie ViaVeilingsiteAsync.
        if (def is null)
            return await ViaVeilingsiteAsync(url, sites, history, ct);

        string html;

        try
        {
            html = await DetailFetcher.HaalPaginaAsync(def, url, ct);
        }
        catch (Exception fout)
        {
            Log.Write($"favoriet uit Chrome mislukte voor {url} - {fout.Message}");
            return new Uitkomst(false, $"De pagina kwam niet binnen: {FriendlyError.Describe(fout)}");
        }

        return await VanPaginaAsync(url, def, html, history, ct);
    }

    // ---------- een kavel van een veilinghuis dat Zentrix niet kent ----------
    //
    // De aanleiding: je staat op bopa.be en wil dat kavel bewaren, maar bopa.be staat niet bij je
    // sites. Een sitebestand per veilinghuis maken is geen antwoord - AlleVeilingen verzamelt er
    // twintig, en die lijst verandert.
    //
    // Wat wél werkt, en wat de hele opzet draagt: een kavelpagina van AlleVeilingen draagt een
    // link TERUG naar het veilinghuis ("Bekijk dit kavel op Bopa"). Daarmee hoeft Zentrix niet te
    // raden of twee kavels hetzelfde zijn - ze kan het adres waarop jij klikte terugvinden in de
    // pagina van de kandidaat. Komt het er niet in voor, dan is het een ander kavel. Punt.
    //
    // Dat maakt de zoekterm onbelangrijk: hij hoeft enkel goed genoeg te zijn om het kavel ergens
    // in de lijst te krijgen. Een misser levert "niet teruggevonden" op, nooit een verkeerde
    // favoriet. Nagemeten op 2 oktober 2026 met lot 1 van BOPA:
    //
    //   bopa.be/auction/520/lot/57380  ->  <h1> "Lot 1: Elektrische fiets Villette"
    //     ->  eerste kandidaat /nl/Bopa/kavel/12528539/lot-1---elektrische-fiets-villette
    //     ->  die pagina bevat "bopa.be/auction/520/lot/57380"  ->  treffer
    //     ->  einddatum 08/10/2026 19:30, zoals op de site van BOPA zelf.
    //
    // Drie verzoeken, geen browser, en geen toestemming per site nodig.

    /// <summary>Hoeveel kandidaten er per veilingsite opengemaakt worden. Elk kost één verzoek.</summary>
    private const int MaxKandidaten = 8;

    /// <summary>
    /// Het pad van een adres, zonder schema en zonder <c>www.</c> - waarmee we in de pagina van
    /// een kandidaat zoeken. Korter dan dit en het bewijst niets: <c>bopa.be/</c> staat op elke
    /// pagina van die site.
    /// </summary>
    private const int MinimumKern = 12;

    /// <summary>
    /// Zoekt een kavel van een onbekend veilinghuis terug op de veilingsites die je wél hebt.
    /// Bewaart enkel bij een <b>zekere</b> treffer; anders wordt er niets bewaard en wordt gezegd
    /// waarom.
    /// </summary>
    private static async Task<Uitkomst> ViaVeilingsiteAsync(string url, IReadOnlyList<SiteDefinition> sites,
                                                            HistoryStore history, CancellationToken ct)
    {
        var kern = Kern(url);

        if (kern.Length < MinimumKern)
            return new Uitkomst(false, "Zentrix kent deze site niet, en dit adres is te kort om een kavel terug te vinden.");

        // Enkel veilingsites, en de goedkoopste eerst: een site die rechtstreeks antwoordt kost
        // een verzoek, een brugsite een rondje langs jouw Chrome.
        var veilingsites = sites
            .Where(s => s.Enabled && s.IsAuction && !string.IsNullOrWhiteSpace(s.IdPattern))
            .Where(s => SiteVoor(url, new[] { s }) is null)
            .OrderBy(s => s.UseBridge ? 2 : s.NeedsBrowser ? 1 : 0)
            .ToList();

        if (veilingsites.Count == 0)
            return new Uitkomst(false, "Zentrix kent deze site niet, en er is geen veilingsite om het kavel op terug te zoeken.");

        using var client = HttpFactory.MaakClient(TimeSpan.FromSeconds(20));

        string html;

        try
        {
            html = await client.GetStringAsync(url, ct);
        }
        catch (Exception fout)
        {
            Log.Write($"favoriet uit Chrome: {url} kwam niet binnen - {fout.Message}");
            return new Uitkomst(false, $"Zentrix kent deze site niet en kon de pagina niet ophalen: {FriendlyError.Describe(fout)}");
        }

        var zoekterm = Zoekterm(html);

        if (zoekterm.Length == 0)
            return new Uitkomst(false, "Zentrix kent deze site niet, en er viel geen titel uit de pagina te lezen.");

        foreach (var site in veilingsites)
        {
            var treffer = await ZoekTerugAsync(site, zoekterm, kern, client, ct);
            if (treffer is null) continue;

            // De pagina is al binnen; ze nog eens ophalen zou een verzoek voor niets zijn.
            //
            // En de titel komt uit de h1 van die pagina, niet uit de gewone weg: AlleVeilingen
            // hangt de naam van het veilinghuis achter haar og:title en haar <title> ("Lot 1 -
            // elektrische fiets villette | Bopa"), de h1 niet.
            var (adres, pagina) = treffer.Value;
            var uitkomst = await VanPaginaAsync(adres, site, pagina, history, ct, Zoekterm(pagina));

            Log.Write($"favoriet uit Chrome: '{zoekterm}' teruggevonden op {site.Name} ({adres})");

            return uitkomst.Ok
                ? uitkomst with { Melding = uitkomst.Melding + $" (gevonden via {site.Name})" }
                : uitkomst;
        }

        return new Uitkomst(false,
            $"'{Kort(zoekterm)}' staat niet op je veilingsites. Er is niets bewaard.");
    }

    /// <summary>
    /// Zoekt op één site en geeft het adres van de kandidaat terug wiens pagina het
    /// oorspronkelijke adres bevat, of null.
    ///
    /// De kandidaten worden uit de <b>gewone</b> zoekpagina geplukt met het <c>IdPattern</c> van
    /// het sitebestand: dat patroon bestaat juist om een kavellink te herkennen. Daardoor is er
    /// geen browser nodig, ook niet bij een site die er voor het zoeken zelf wél een vraagt - de
    /// volledige kaarten komen daar pas met JavaScript, de links staan er al in. Gemeten bij
    /// AlleVeilingen: 30 kavellinks in de kale HTML, maar 3 van de 30 kaarten.
    /// </summary>
    private static async Task<(string Adres, string Pagina)?> ZoekTerugAsync(
        SiteDefinition site, string zoekterm, string kern, HttpClient client, CancellationToken ct)
    {
        string zoekpagina;

        try
        {
            zoekpagina = await client.GetStringAsync(SearchUrlBuilder.Build(site, zoekterm, null), ct);
        }
        catch (Exception fout)
        {
            Log.Write($"favoriet uit Chrome: zoeken op {site.Name} mislukte - {fout.Message}");
            return null;
        }

        foreach (var kandidaat in Kandidaten(zoekpagina, site, MaxKandidaten))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var pagina = await client.GetStringAsync(kandidaat, ct);
                if (pagina.Contains(kern, StringComparison.OrdinalIgnoreCase)) return (kandidaat, pagina);
            }
            catch (Exception fout)
            {
                Log.Write($"favoriet uit Chrome: kandidaat {kandidaat} niet na te kijken - {fout.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// De kavellinks uit een zoekpagina, met het <c>IdPattern</c> van het sitebestand als zeef.
    ///
    /// Dat patroon bestaat juist om een kavellink te herkennen, dus er hoeft hier niets nieuws
    /// ingesteld te worden. Relatieve adressen krijgen het basisadres ervoor, en dubbels vallen
    /// weg: in een zoekpagina staat dezelfde kavel vaak twee keer (de foto en de titel).
    /// </summary>
    internal static List<string> Kandidaten(string zoekpagina, SiteDefinition site, int max)
    {
        var kandidaten = new List<string>();

        if (string.IsNullOrWhiteSpace(site.IdPattern)) return kandidaten;

        Regex patroon;

        try
        {
            patroon = new Regex(site.IdPattern);
        }
        catch (ArgumentException)
        {
            return kandidaten;
        }

        foreach (Match raak in Regex.Matches(zoekpagina, "[\"'](/[^\"' <>]+|https?://[^\"' <>]+)[\"']"))
        {
            var adres = raak.Groups[1].Value;
            if (!patroon.IsMatch(adres)) continue;

            var volledig = adres.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? adres
                : site.BaseUrl.TrimEnd('/') + adres;

            if (!kandidaten.Contains(volledig)) kandidaten.Add(volledig);
            if (kandidaten.Count >= max) break;
        }

        return kandidaten;
    }

    /// <summary>
    /// Waarmee we op de veilingsite gaan zoeken. <c>&lt;h1&gt;</c> staat vooraan en niet
    /// <c>&lt;title&gt;</c>: op een kavelpagina is de h1 de naam van het kavel, terwijl de titel
    /// vaak die van de site is. Bij BOPA staat er letterlijk "BOPA Veilingen | Uw veiling
    /// makelaar voor online veilingen" in de titel, en "Lot 1: Elektrische fiets Villette" in de
    /// h1 - ook ná het renderen, dus zelfs de extensie zou er niets aan hebben.
    ///
    /// Een misgok is hier onschuldig: de terugkoppeling beslist of het hetzelfde kavel is.
    /// Daarom mag dit losser zijn dan <see cref="FavoriteWatch.TitelUitPagina"/>, die de titel
    /// van een favoriet bepaalt en dus wél precies moet zijn.
    /// </summary>
    internal static string Zoekterm(string html)
    {
        var h1 = Regex.Match(html, "<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (h1.Success)
        {
            var tekst = System.Net.WebUtility.HtmlDecode(Regex.Replace(h1.Groups[1].Value, "<[^>]+>", " "));
            tekst = Regex.Replace(tekst, @"\s+", " ").Trim();

            if (tekst.Length > 0) return tekst;
        }

        return FavoriteWatch.TitelUitPagina(html);
    }

    /// <summary>Het adres zonder schema en zonder <c>www.</c>, en zonder volgparameters.</summary>
    internal static string Kern(string url) =>
        Regex.Replace(GenericSource.SchoonAdres(url), "^https?://(www\\.)?", "",
                      RegexOptions.IgnoreCase).TrimEnd('/');

    private static string Kort(string tekst) => tekst.Length <= 60 ? tekst : tekst[..57] + "...";

    /// <summary>
    /// Maakt de favoriet uit een pagina die al binnen is. Los van
    /// <see cref="VoegToeAsync"/> gehouden omdat het twee verschillende dingen zijn - ophalen en
    /// ervan maken - en omdat het ophalen https eist: de proefsite van de controles draait op
    /// <c>http://127.0.0.1</c>, en die regel verzwakken om haar te kunnen testen zou precies het
    /// slot weghalen waar het om gaat.
    /// </summary>
    /// <param name="titel">
    /// Een betere titel dan de pagina generiek oplevert, of null. Gebruikt door de weg langs een
    /// veilingsite: daar staat de nette naam in de h1 en hangt er aan de gewone titel de naam van
    /// het veilinghuis.
    /// </param>
    internal static async Task<Uitkomst> VanPaginaAsync(string url, SiteDefinition def, string html,
                                                        HistoryStore history, CancellationToken ct = default,
                                                        string? titel = null)
    {
        // Hetzelfde id als het zoekresultaat eruit zou halen, zodat dezelfde kavel niet twee
        // keer in je favorieten komt: één keer via het zoekresultaat en één keer via Chrome.
        var listing = new Listing
        {
            Source = def.Name,
            ExternalId = GenericSource.IdUitLink(Patroon(def), url),
            Url = url,
            IsFavorite = true
        };

        if (history.GetFavoriteKeys().Contains(listing.Key))
            return new Uitkomst(false, "Dit zoekertje staat al bij je favorieten.");

        listing.Title = string.IsNullOrWhiteSpace(titel) ? FavoriteWatch.TitelUitPagina(html) : titel;

        if (string.IsNullOrWhiteSpace(listing.Title))
        {
            // Zonder titel is het een lege kaart in je favorieten, en dan is "niet gelukt"
            // eerlijker dan iets bewaren waar je niets aan hebt.
            return new Uitkomst(false, $"Er viel geen titel uit deze pagina te lezen ({def.Name}).");
        }

        listing.Price = await FavoriteWatch.PrijsAsync(def, html, ct);

        // De einddatum en een foto, voor zover het sitebestand zegt waar ze staan. Allebei
        // mogen mislukken: dan staat de favoriet er gewoon zonder.
        if (!string.IsNullOrWhiteSpace(def.DetailEndDateSelector))
        {
            var tekst = await GenericSource.ReadFieldAsync(html, def.DetailEndDateSelector, ct);
            listing.EndsAt = DetailFetcher.LeesDatum(tekst);
        }

        if (!string.IsNullOrWhiteSpace(def.DetailImagesSelector))
        {
            var fotos = await GenericSource.ReadFieldsAsync(html, def.DetailImagesSelector, ct);
            if (fotos.Count > 0) listing.ImageUrls.Add(fotos[0]);
        }

        history.AddFavorite(listing);
        Log.Write($"favoriet uit Chrome: '{listing.Title}' van {def.Name}");

        var prijs = listing.Price is { } p ? $" (€{p:0.##})" : "";
        return new Uitkomst(true, $"Bij je favorieten gezet: {listing.Title}{prijs}", listing);
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Haalt aan wat niet op de zoekpagina van een site staat. Vandaag is dat één ding: de
/// sluitingsdatum van een veiling, langs twee wegen:
/// <list type="bullet">
/// <item><see cref="FillAsync"/>: de pagina van elk kavel apart
/// (<see cref="SiteDefinition.DetailEndDateSelector"/>, AlleVeilingen);</item>
/// <item><see cref="FillFromApiAsync"/>: een API die veel kavels tegelijk beantwoordt
/// (<see cref="SiteDefinition.EndTimeApi"/>, Catawiki: één verzoek per 24 kavels).</item>
/// </list>
/// Wat hieronder over "enkel wat je ziet" staat, geldt voor de eerste weg. De tweede is zo
/// goedkoop dat ze altijd voor alle zoekertjes van die site gaat.
///
/// Waarom dit bestaat: AlleVeilingen zet op zijn kaarten wel het huidige bod en het aantal
/// biedingen, maar niet wanneer de veiling sluit — en juist dat wil je weten. Op de pagina
/// van het kavel staat het wel, en die komt met een gewoon verzoek binnen in ongeveer twee
/// tiende van een seconde.
///
/// Wat het NIET doet: alles ophalen wat binnenkwam. Een zoekopdracht levert tot vijfhonderd
/// zoekertjes per site, en dan zouden dat vijfhonderd extra verzoeken zijn. Het gaat enkel
/// over de zoekertjes die op dat moment op het scherm staan (zie <c>ToonPagina</c>), zes
/// tegelijk, en wat één keer opgehaald is blijft onthouden zolang de app draait. Zo koste
/// het bij een pagina van vijftig kavels ongeveer twee seconden, op de achtergrond, terwijl
/// de resultaten al te zien zijn.
///
/// Eén uitzondering, zo gekozen door de eigenaar: bij de volgorde "Veiling die het eerst
/// afloopt" gaat het over álle zoekertjes in de lijst. Die volgorde heeft het einde van elk
/// kavel nodig; anders belanden de kavels zonder datum achteraan, komen ze nooit in beeld en
/// krijgen ze dus ook nooit een datum. Bij AlleVeilingen is dat meestal 60 tot 100 verzoeken
/// per zoekopdracht, hoogstens 300.
/// </summary>
public static class DetailFetcher
{
    /// <summary>Hoeveel pagina's er tegelijk opgehaald worden.</summary>
    private const int Tegelijk = 6;

    private static readonly SemaphoreSlim Poort = new(Tegelijk);

    /// <summary>
    /// Wat we al opgehaald hebben, op de sleutel van het zoekertje. Ook een mislukking komt
    /// hierin (als niets), anders probeert elke verversing van de lijst het opnieuw.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime?> Onthouden = new();

    private static readonly HttpClient Http = MaakClient();

    private static HttpClient MaakClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

        // Korter dan de 30 seconden van een zoekopdracht: dit is een extraatje bij
        // resultaten die al op het scherm staan, en niemand wacht erop.
        client.Timeout = TimeSpan.FromSeconds(10);

        return client;
    }

    /// <summary>
    /// Vult de sluitingsdatum aan van de zoekertjes die meegegeven worden, voor zover hun
    /// site er een selector voor heeft en ze die datum nog niet hebben. Werkt door tot alles
    /// binnen is of tot er geannuleerd wordt; een fout bij één zoekertje raakt de rest niet.
    /// Geeft terug hoeveel zoekertjes er een datum bijkregen: bij "loopt het eerst af" moet
    /// de lijst dan opnieuw op volgorde, en anders niet.
    /// </summary>
    public static async Task<int> FillAsync(IEnumerable<Listing> listings, IReadOnlyList<SiteDefinition> sites,
                                            CancellationToken ct = default)
    {
        var werk = new List<(Listing Listing, string Selector)>();
        var aangevuld = 0;

        foreach (var listing in listings)
        {
            if (listing.EndsAt is not null || !string.IsNullOrWhiteSpace(listing.TimeLeft)) continue;

            var def = sites.FirstOrDefault(s =>
                string.Equals(s.Name, listing.Source, StringComparison.OrdinalIgnoreCase));

            if (def is null || string.IsNullOrWhiteSpace(def.DetailEndDateSelector)) continue;
            if (!IsWebadres(listing.Url)) continue;

            // Al eens opgehaald: meteen invullen, zonder verzoek. Bladeren naar een vorige
            // pagina en terug kost zo niets.
            if (Onthouden.TryGetValue(listing.Key, out var bekend))
            {
                if (bekend is not null)
                {
                    Zet(listing, bekend);
                    aangevuld++;
                }
                continue;
            }

            werk.Add((listing, def.DetailEndDateSelector));
        }

        if (werk.Count == 0) return aangevuld;

        var mislukt = 0;

        await Task.WhenAll(werk.Select(async paar =>
        {
            await Poort.WaitAsync(ct);

            try
            {
                var datum = await HaalAsync(paar.Listing.Url, paar.Selector, ct);
                Onthouden[paar.Listing.Key] = datum;

                if (datum is not null)
                {
                    Zet(paar.Listing, datum);
                    Interlocked.Increment(ref aangevuld);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // De gebruiker bladerde verder of zocht opnieuw: gewoon stoppen.
            }
            catch (Exception)
            {
                // Eén kavelpagina die niet lukt, mag de rest niet tegenhouden. Niet
                // onthouden als fout: de volgende keer mag het opnieuw geprobeerd worden.
                Interlocked.Increment(ref mislukt);
            }
            finally
            {
                Poort.Release();
            }
        }));

        if (mislukt > 0)
            Log.Write($"einddatum: {mislukt} van de {werk.Count} kavelpagina's gaven niets");

        return aangevuld;
    }

    /// <summary>
    /// Vult het exacte sluitingstijdstip aan via de API van een site
    /// (<see cref="SiteDefinition.EndTimeApi"/>), voor álle meegegeven zoekertjes van die site:
    /// het is één verzoek per reeks van <see cref="EndTimeApiOptions.BatchSize"/>, dus bij
    /// Catawiki één tot zes per zoekopdracht. Langs de weg van de site zelf: via de brug als de
    /// site die gebruikt, en dan enkel als de extensie zich net nog meldde - hiervoor start de
    /// app geen Chrome. Geeft terug hoeveel zoekertjes er een datum bijkregen.
    /// </summary>
    public static async Task<int> FillFromApiAsync(IEnumerable<Listing> listings, IReadOnlyList<SiteDefinition> sites,
                                                   CancellationToken ct = default)
    {
        var aangevuld = 0;
        var reeksen = new List<(SiteDefinition Def, EndTimeApiOptions Api, Listing[] Reeks)>();

        foreach (var groep in listings.Where(l => l.EndsAt is null && !string.IsNullOrWhiteSpace(l.ExternalId))
                                      .GroupBy(l => l.Source, StringComparer.OrdinalIgnoreCase))
        {
            var def = sites.FirstOrDefault(s => string.Equals(s.Name, groep.Key, StringComparison.OrdinalIgnoreCase));
            var api = def?.EndTimeApi;

            if (def is null || api is null ||
                string.IsNullOrWhiteSpace(api.UrlTemplate) || string.IsNullOrWhiteSpace(api.EndPath)) continue;

            if (def.UseBridge && !BridgeServer.Instance.ExtensionAlive) continue;

            var nieuw = new List<Listing>();

            foreach (var listing in groep)
            {
                if (Onthouden.TryGetValue(listing.Key, out var bekend))
                {
                    if (bekend is not null)
                    {
                        Zet(listing, bekend);
                        aangevuld++;
                    }
                    continue;
                }

                nieuw.Add(listing);
            }

            foreach (var reeks in nieuw.Chunk(Math.Max(1, api.BatchSize)))
                reeksen.Add((def, api, reeks));
        }

        if (reeksen.Count == 0) return aangevuld;

        await Task.WhenAll(reeksen.Select(async r =>
        {
            await Poort.WaitAsync(ct);

            try
            {
                Interlocked.Add(ref aangevuld, await HaalReeksAsync(r.Def, r.Api, r.Reeks, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Nieuwe zoekopdracht of andere pagina: gewoon stoppen.
            }
            catch (Exception ex)
            {
                // Een reeks die niet lukt, houdt de rest niet tegen; de timer toont dan de
                // tekst van de site, zoals zonder API.
                Log.Write($"einddatum via de API van {r.Def.Name} mislukt - {ex.Message}");
            }
            finally
            {
                Poort.Release();
            }
        }));

        return aangevuld;
    }

    /// <summary>Eén verzoek aan de API, voor één reeks zoekertjes van dezelfde site.</summary>
    private static async Task<int> HaalReeksAsync(SiteDefinition def, EndTimeApiOptions api, Listing[] reeks,
                                                  CancellationToken ct)
    {
        var ids = string.Join(",", reeks.Select(l => l.ExternalId));
        var url = api.UrlTemplate.Replace("{ids}", Uri.EscapeDataString(ids));

        string json;

        if (def.UseBridge)
        {
            // Dezelfde soort opdracht als bij Discogs: de extensie haalt de API op vanuit een
            // pagina van die site, zodat het de echte Chrome van de gebruiker is die vraagt.
            json = await BridgeServer.Instance.FetchAsync(url, ct, headers: def.Headers, rawText: true);
        }
        else
        {
            using var verzoek = new HttpRequestMessage(HttpMethod.Get, url);
            verzoek.Headers.Accept.ParseAdd("application/json");

            foreach (var (naam, waarde) in def.Headers ?? new Dictionary<string, string>())
                verzoek.Headers.TryAddWithoutValidation(naam, waarde);

            using var antwoord = await Http.SendAsync(verzoek, ct);
            antwoord.EnsureSuccessStatusCode();
            json = await antwoord.Content.ReadAsStringAsync(ct);
        }

        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var lijst = doc.RootElement;
        if (!string.IsNullOrWhiteSpace(api.ListPath) && !GenericSource.TryWalk(doc.RootElement, api.ListPath, out lijst))
            return 0;
        if (lijst.ValueKind != System.Text.Json.JsonValueKind.Array) return 0;

        // Eenzelfde id kan twee keer in de lijst staan (dezelfde kavel onder twee zoektermen).
        var perId = reeks.GroupBy(l => l.ExternalId).ToDictionary(g => g.Key, g => g.ToList());
        var gevonden = 0;

        foreach (var element in lijst.EnumerateArray())
        {
            if (!GenericSource.TryWalk(element, api.IdPath, out var idWaarde) ||
                !GenericSource.TryWalk(element, api.EndPath, out var eindWaarde)) continue;

            var id = idWaarde.ValueKind == System.Text.Json.JsonValueKind.String ? idWaarde.GetString() : idWaarde.ToString();
            var einde = LeesDatum(eindWaarde.ValueKind == System.Text.Json.JsonValueKind.String
                ? eindWaarde.GetString() ?? "" : eindWaarde.ToString());

            if (id is null || einde is null || !perId.TryGetValue(id, out var zoekertjes)) continue;

            foreach (var listing in zoekertjes)
            {
                Onthouden[listing.Key] = einde;
                Zet(listing, einde);
                gevonden++;
            }
        }

        return gevonden;
    }

    /// <summary>
    /// Alle foto's van één zoekertje. De zoekpagina geeft er meestal één; op de pagina van het
    /// zoekertje zelf staan er vijf of tien, en juist daarop staat vaak wat je wil zien - het
    /// label achteraan, de doos van binnen, de krassen. Waar ze staan zegt het sitebestand
    /// (<see cref="SiteDefinition.DetailImagesSelector"/>).
    ///
    /// De foto die we al hebben staat altijd vooraan; die is er zeker, en zo kan de AI-controle
    /// meteen beginnen terwijl de pagina nog opgehaald wordt. Heeft de site geen selector of
    /// lukt het ophalen niet, dan blijft het bij die ene - een uitzondering is dit niet, de
    /// meeste sitebestanden hebben dit veld voorlopig nog niet.
    ///
    /// Bij een site die via de brug werkt, gaat het ook via de brug: een gewoon verzoek krijgt
    /// daar een 403. Meldde de extensie zich niet net nog, dan wordt het overgeslagen - hiervoor
    /// start de app geen Chrome, net als bij de API hierboven.
    /// </summary>
    public static async Task<List<string>> FotosAsync(Listing listing, IReadOnlyList<SiteDefinition> sites,
                                                      CancellationToken ct = default)
        => (await DetailsAsync(listing, sites, ct)).Fotos.ToList();

    /// <summary>
    /// Wat de pagina van één zoekertje meer vertelt dan de zoekpagina. Leeg wat de site niet
    /// geeft; <see cref="Fout"/> zegt waarom er niets kwam, of null wanneer alles lukte.
    /// </summary>
    public record ListingDetails(IReadOnlyList<string> Fotos, string Verkoper, string Sinds,
                                 string Beschrijving = "", string? Fout = null)
    {
        /// <summary>
        /// Enkel wat op de pagina zelf stond, zonder de foto van de zoekpagina ervoor.
        /// <see cref="Fotos"/> heeft die er wél bij, want de AI-controle wil meteen kunnen
        /// beginnen met de foto die er zeker is.
        ///
        /// Het detailvenster heeft juist dit nodig: de foto van de zoekpagina moet verdwijnen
        /// zodra de pagina er betere geeft. Het verschil is niet af te leiden uit de
        /// samengevoegde lijst - bij 2dehands ís de foto van de zoekpagina letterlijk de eerste
        /// van de pagina, en die mag dus blijven; bij Facebook is het een ander adres van
        /// dezelfde foto, 260 px in plaats van 960, en die moet weg.
        /// </summary>
        public IReadOnlyList<string> PaginaFotos { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Alles van de pagina van één zoekertje in <b>één</b> verzoek: de foto's, de verkoper en
    /// sinds wanneer het online staat. Eén verzoek en niet drie - het is dezelfde pagina, en bij
    /// een brugsite kost elk verzoek zo'n vier seconden.
    ///
    /// Wat er gelezen wordt, zegt het sitebestand: <see cref="SiteDefinition.DetailImagesSelector"/>,
    /// <see cref="SiteDefinition.DetailSellerSelector"/> en
    /// <see cref="SiteDefinition.DetailPostedSelector"/>. Staat er geen enkele, dan wordt de
    /// pagina niet opgehaald.
    /// </summary>
    public static async Task<ListingDetails> DetailsAsync(Listing listing, IReadOnlyList<SiteDefinition> sites,
                                                          CancellationToken ct = default)
    {
        var fotos = new List<string>();
        if (!string.IsNullOrWhiteSpace(listing.LargeImage)) fotos.Add(listing.LargeImage);

        var def = sites.FirstOrDefault(s =>
            string.Equals(s.Name, listing.Source, StringComparison.OrdinalIgnoreCase));

        if (def is null) return new ListingDetails(fotos, "", "", "", "Deze site staat niet meer in Sites beheren.");

        var leest = !string.IsNullOrWhiteSpace(def.DetailImagesSelector) ||
                    !string.IsNullOrWhiteSpace(def.DetailSellerSelector) ||
                    !string.IsNullOrWhiteSpace(def.DetailPostedSelector) ||
                    !string.IsNullOrWhiteSpace(def.DetailDescriptionSelector);

        if (!leest)
            return new ListingDetails(fotos, "", "", "",
                $"Het sitebestand van {def.Name} zegt nog niet waar de foto's en de verkoper staan.");

        if (!IsWebadres(listing.Url))
            return new ListingDetails(fotos, "", "", "", "Dit zoekertje heeft geen webadres.");

        if (def.UseBridge && !BridgeServer.Instance.ExtensionAlive)
            return new ListingDetails(fotos, "", "", "",
                $"{def.Name} loopt via de brug, en die meldde zich niet. Staat Chrome open?");

        if (OnthoudenDetails.TryGetValue(listing.Url, out var bekend))
        {
            Voeg(fotos, bekend.Fotos);
            return bekend with { Fotos = fotos };
        }

        try
        {
            var html = await HaalPaginaAsync(def, listing.Url, ct);

            var gevonden = string.IsNullOrWhiteSpace(def.DetailImagesSelector)
                ? new List<string>()
                : await GenericSource.ReadFieldsAsync(html, def.DetailImagesSelector, ct);

            // Een site mag zijn foto's met een pad opgeven in plaats van een volledig adres.
            var volledig = gevonden
                .Select(f => Volledig(f, def.BaseUrl))
                .Where(f => f.Length > 0)
                .ToList();

            var verkoper = string.IsNullOrWhiteSpace(def.DetailSellerSelector)
                ? ""
                : (await GenericSource.ReadFieldAsync(html, def.DetailSellerSelector, ct)).Trim();

            var sinds = string.IsNullOrWhiteSpace(def.DetailPostedSelector)
                ? ""
                : (await GenericSource.ReadFieldAsync(html, def.DetailPostedSelector, ct)).Trim();

            // Voor de beschrijving gaat er een kopie in waarin <br> een regeleinde geworden is.
            // Een selector leest de tekst van een element, en die kent geen tags meer: zonder
            // dit plakt "eerste regel<br>tweede regel" aan elkaar tot één brij.
            var beschrijving = string.IsNullOrWhiteSpace(def.DetailDescriptionSelector)
                ? ""
                : Opschonen(await GenericSource.ReadFieldAsync(
                    MetRegeleindes(html), def.DetailDescriptionSelector, ct));

            var uitkomst = new ListingDetails(volledig, verkoper, sinds, beschrijving)
            {
                PaginaFotos = volledig
            };

            OnthoudenDetails[listing.Url] = uitkomst;

            Voeg(fotos, volledig);

            Log.Write($"pagina van '{Kort(listing.Title)}': {volledig.Count} foto's" +
                      $"{(verkoper.Length > 0 ? ", verkoper '" + verkoper + "'" : "")}" +
                      $"{(sinds.Length > 0 ? ", sinds '" + sinds + "'" : "")}" +
                      $"{(beschrijving.Length > 0 ? ", " + beschrijving.Length + " tekens beschrijving" : "")}");

            return uitkomst with { Fotos = fotos };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Niet onthouden: de volgende keer mag het opnieuw geprobeerd worden.
            Log.Write($"pagina van '{Kort(listing.Title)}' niet op te halen - {ex.Message}");
            return new ListingDetails(fotos, "", "", "", FriendlyError.Describe(ex));
        }
    }

    /// <summary>
    /// Wat we van de pagina van een zoekertje lazen, op het adres van die pagina. Op het adres
    /// en niet op <see cref="Listing.Key"/>: die is <c>Source:ExternalId</c>, en bij een leeg id
    /// zouden twee zoekertjes van dezelfde site elkaars foto's krijgen. Het adres is bovendien
    /// precies wat opgehaald wordt, dus het is ook de eerlijke sleutel. Zie <see cref="Onthouden"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ListingDetails> OnthoudenDetails = new();

    private static void Voeg(List<string> doel, IEnumerable<string> erbij)
    {
        foreach (var foto in erbij)
            if (!doel.Contains(foto, StringComparer.OrdinalIgnoreCase)) doel.Add(foto);
    }

    /// <summary>Een pad naar een volledig adres, tegen de basis van de site.</summary>
    private static string Volledig(string adres, string basis)
    {
        if (adres.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            adres.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return adres;

        return Uri.TryCreate(new Uri(basis), adres, out var samen) ? samen.ToString() : "";
    }

    private static string Kort(string titel) => titel.Length <= 40 ? titel : titel[..40] + "...";

    /// <summary>
    /// De pagina van een zoekertje, langs dezelfde weg als de zoekpagina van die site:
    /// via de brug, via de aangemelde browser, of gewoon.
    ///
    /// Die derde weg - de browser - kwam er voor Facebook (26 september 2026). Zijn
    /// advertentiepagina is enkel met een aangemeld profiel te openen, en dat is precies wat
    /// <see cref="BrowserFetcher"/> heeft. Het kost wel meer: een paar seconden in plaats van
    /// een paar honderd milliseconden, en Chrome moet mogelijk eerst starten. Dat is te
    /// verantwoorden omdat het pas gebeurt wanneer je zelf op een zoekertje dubbelklikt -
    /// niet tijdens het zoeken.
    /// </summary>
    private static async Task<string> HaalPaginaAsync(SiteDefinition def, string url, CancellationToken ct)
    {
        if (def.UseBridge)
            return await BridgeServer.Instance.FetchAsync(url, ct, headers: def.Headers);

        if (def.NeedsBrowser)
        {
            // Dezelfde gedeelde Chrome als een zoekopdracht; de lener zorgt dat hij weer
            // dichtgaat wanneer niemand hem nog nodig heeft.
            using var lening = BrowserPool.Lease();
            return await BrowserPool.Get().GetHtmlAsync(url, CssDeel(def.DetailImagesSelector), ct);
        }

        using var antwoord = await Http.GetAsync(url, ct);
        antwoord.EnsureSuccessStatusCode();

        return await antwoord.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Het kale CSS-stuk van een selector: zonder <c>@attribuut</c> en zonder <c>::replace</c>
    /// of <c>::match</c>. Daarmee kan de browser wachten tot die foto er echt staat.
    ///
    /// Zonder dat wachten lees je een pagina die nog niet af is. Bij Facebook is dat geen
    /// randgeval: op een pagina die we ophaalden stond geen enkele <c>img</c>, terwijl de
    /// gewone weg 9 MB aan omhulsel al binnen had.
    /// </summary>
    internal static string? CssDeel(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;

        var kaal = selector;

        var regels = kaal.IndexOf("::", StringComparison.Ordinal);
        if (regels > 0) kaal = kaal[..regels];

        var at = kaal.LastIndexOf('@');
        if (at > 0) kaal = kaal[..at];

        kaal = kaal.Trim();

        return kaal.Length > 0 ? kaal : null;
    }

    /// <summary>
    /// Waar een regel eindigde, in een teken dat het opschonen overleeft. Een selector plakt
    /// alle witruimte plat tot één spatie - terecht voor een titel of een prijs, maar het maakt
    /// van een beschrijving één brij. Een gewone <c>\n</c> zou dus sneuvelen; dit stuurteken
    /// staat niet in <c>\s</c> en komt in geen enkele advertentie voor.
    /// </summary>
    private const char Regeleinde = '\u001F';

    /// <summary>
    /// Een kopie van de pagina waarin <c>&lt;br&gt;</c> en het einde van een alinea gemarkeerd
    /// staan met <see cref="Regeleinde"/>. Enkel voor de beschrijving: daar is de indeling de
    /// helft van de leesbaarheid, en een selector geeft enkel tekst terug - zonder dit plakt
    /// "eerste regel&lt;br&gt;tweede regel" aan elkaar.
    /// </summary>
    private static string MetRegeleindes(string html) =>
        System.Text.RegularExpressions.Regex.Replace(
            html, @"<\s*(br\s*/?|/p|/div|/li)\s*>", "$0" + Regeleinde,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Een beschrijving zoals ze leesbaar is: geen rij lege regels, geen spaties aan de rand,
    /// en niet eindeloos lang. Sites zetten er graag drie witregels en een blok voorwaarden
    /// achter.
    /// </summary>
    private static string Opschonen(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return "";

        // Sommige sites zetten een regeleinde per ongeluk als de twee tékens \ en n in hun
        // pagina - 2dehands doet dat in het stuk dat uit hun eigen databank komt. Onbewerkt
        // staat er dan "(LOSSE CD SPELER)\nOpslaglocatie: Onbekend" op het scherm. Enkel in
        // een beschrijving: daar is een backslash-n vrijwel zeker een mislukt regeleinde.
        var regels = tekst.Replace("\\n", "\n")
                          .Replace(Regeleinde, '\n').Replace("\r\n", "\n").Split('\n')
                          .Select(r => r.Trim())
                          .ToList();

        var uit = new List<string>();
        foreach (var regel in regels)
        {
            // Hoogstens één lege regel na elkaar.
            if (regel.Length == 0 && (uit.Count == 0 || uit[^1].Length == 0)) continue;
            uit.Add(regel);
        }

        var samen = string.Join("\n", uit).Trim();

        return samen.Length <= MaxBeschrijving ? samen : samen[..MaxBeschrijving].TrimEnd() + " […]";
    }

    /// <summary>
    /// Hoeveel tekens van de beschrijving er getoond worden. Ruim genoeg voor een gewone
    /// advertentie; een handelaar die er zijn hele algemene voorwaarden achter plakt, wordt
    /// afgekapt met een teken dat er meer is - en de knop naar de site staat ernaast.
    /// </summary>
    private const int MaxBeschrijving = 4000;

    /// <summary>Haalt één pagina op en leest er de datum uit.</summary>
    private static async Task<DateTime?> HaalAsync(string url, string selector, CancellationToken ct)
    {
        using var antwoord = await Http.GetAsync(url, ct);
        if (!antwoord.IsSuccessStatusCode) return null;

        var html = await antwoord.Content.ReadAsStringAsync(ct);
        var tekst = await GenericSource.ReadFieldAsync(html, selector, ct);

        return LeesDatum(tekst);
    }

    /// <summary>
    /// Zet de datum bij het zoekertje en laat het scherm het weten. Dat laatste moet op de
    /// schermdraad gebeuren: dit loopt op een achtergronddraad, en een melding van daar
    /// bereikt de binding niet — het zoekertje stond dan wel goed in het geheugen, maar op
    /// de kaart bleef enkel de plaats staan. Nagemeten met het hoofdscherm buiten beeld.
    /// Zonder venster (de controles) gebeurt het gewoon meteen.
    /// </summary>
    private static void Zet(Listing listing, DateTime? datum)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            listing.EndsAt = datum;
            listing.MeldTijdGewijzigd();
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            listing.EndsAt = datum;
            listing.MeldTijdGewijzigd();
        });
    }

    /// <summary>
    /// Een datum zoals een Europese site ze schrijft. De vaste vormen staan voorop, want
    /// <see cref="DateTime.TryParse(string, out DateTime)"/> leest "29/09/2026" op een
    /// Engelstalige Windows als een dag die niet bestaat, en geeft dan niets terug.
    /// </summary>
    internal static DateTime? LeesDatum(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return null;

        string[] vormen =
        {
            "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy HH:mm", "d-M-yyyy H:mm", "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm"
        };

        if (DateTime.TryParseExact(tekst, vormen, CultureInfo.InvariantCulture,
                                   DateTimeStyles.AllowWhiteSpaces, out var exact))
            return exact;

        // Een ISO-tijdstip (time@datetime) of wat de instellingen van deze pc aankunnen.
        if (DateTime.TryParse(tekst, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var iso))
            return iso.Kind == DateTimeKind.Utc ? iso.ToLocalTime() : iso;

        return DateTime.TryParse(tekst, out var lokaal) ? lokaal : null;
    }

    /// <summary>Enkel http en https: een sitebestand kan van een vreemde komen.</summary>
    private static bool IsWebadres(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Wat er onthouden is vergeten — enkel voor de controles.</summary>
    internal static void Vergeet() => Onthouden.Clear();
}

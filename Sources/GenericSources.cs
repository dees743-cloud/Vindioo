using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Zentrix.Models;

namespace Zentrix.Sources;

/// <summary>
/// Voert een SiteDefinition uit. Eén klasse die elke site aankan
/// waarvoor een geldige beschrijving bestaat.
/// </summary>
public class GenericSource : ISearchSource
{
    private readonly SiteDefinition _def;

    public GenericSource(SiteDefinition definition)
    {
        _def = definition;

        // Een kapot patroon in een gedeeld sitebestand mag de site niet onbruikbaar maken:
        // dan gewoon de volledige link, zoals zonder patroon.
        if (!string.IsNullOrWhiteSpace(definition.IdPattern))
        {
            try
            {
                _idPattern = new Regex(definition.IdPattern, RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                Services.Log.Write($"{definition.Name}: IdPattern ongeldig, de volledige link wordt de identiteit - {ex.Message}");
            }
        }
    }

    private readonly Regex? _idPattern;

    /// <summary>
    /// Wat een zoekertje uniek maakt: het id uit de link als het sitebestand een
    /// <see cref="SiteDefinition.IdPattern"/> heeft, anders de link, en zonder link de titel.
    /// </summary>
    private string Identiteit(Listing listing)
    {
        if (string.IsNullOrEmpty(listing.Url)) return listing.Title;
        return IdUitLink(_idPattern, listing.Url);
    }

    /// <summary>Het id uit een link, of de link zelf als het patroon niet past.</summary>
    internal static string IdUitLink(Regex? patroon, string link)
    {
        if (patroon is null) return link;

        var match = patroon.Match(link);
        return match.Success && match.Groups.Count > 1 && match.Groups[1].Value.Length > 0
            ? match.Groups[1].Value
            : link;
    }

    public string Name => _def.Name;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Gecomprimeerde antwoorden vragen en uitpakken. Zonder dit stuurde de app geen
        // Accept-Encoding mee: pagina's kwamen ongecomprimeerd binnen (HTML en JSON zijn
        // gezipt doorgaans vijf tot tien keer kleiner), en een Chrome-User-Agent die geen
        // compressie aankan, is precies wat een robot verraadt.
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<List<Listing>> SearchAsync(string query, int maxResults,
        SearchFilters? filters = null, IProgress<List<Listing>>? progress = null,
        CancellationToken ct = default)
    {
        // Eén browser voor alle pagina's én voor alle sites van deze zoekopdracht.
        // Hij komt uit BrowserPool en wordt hier dus NIET afgesloten: de zoekopdracht
        // die hem geleend heeft doet dat.
        var browser = _def.NeedsBrowser && !_def.UseBridge
            ? Services.BrowserPool.Get()
            : null;

        var all = new List<Listing>();
        var known = new HashSet<string>();

        // Hoeveel zoekertjes er op de eerste pagina stonden. Een latere pagina met
        // veel minder is de laatste; zie de stopregel verderop.
        var eerstePagina = 0;

        // Veiligheidsrem: nooit meer pagina's dan dit, ook niet bij een site die blijft
        // antwoorden. 20 bij een site die rechtstreeks antwoordt, anders 10.
        var maxPages = _def.PageLimit();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = SearchUrlBuilder.Build(_def, query, filters, page);

            // De volledige zoek-URL in het logboek: dat is de snelste manier om
            // na te gaan of de filters er wel echt in terechtkomen.
            Services.Log.Write($"{_def.Name}: pagina {page} -> {url}");

            var content = await FetchAsync(url, browser, maxResults, progress, vervolgpagina: page > 1, ct);
            var listings = await ParseAsync(content, maxResults, ct);

            if (page == 1)
            {
                eerstePagina = listings.Count;

                // Geen enkel zoekertje, en de pagina is een controlepagina of bijna leeg: dat
                // is geen "niets gevonden", maar een site die de app niet binnenlaat. Een
                // echte resultatenpagina - ook zonder resultaten - is groot. Zo kwam eBay in
                // september 2026 met 0 resultaten "klaar", na "uitlezen 2ms".
                if (listings.Count == 0 && _def.Kind != SiteKind.Json)
                    ControleerLegePagina(content);
            }

            // Enkel wat we nog niet hadden. Een site die het paginanummer negeert
            // geeft dezelfde lijst terug; dan stoppen we hier vanzelf.
            var fresh = listings.Where(l => known.Add(l.Key)).ToList();

            if (page > 1)
                Services.Log.Write($"{_def.Name}: pagina {page} -> {fresh.Count} nieuwe resultaten");

            if (fresh.Count == 0) break;

            // Niet meer teruggeven dan gevraagd.
            if (all.Count + fresh.Count > maxResults)
                fresh = fresh.Take(maxResults - all.Count).ToList();

            all.AddRange(fresh);

            // Elke pagina meteen melden, ook de eerste. Die kwam vroeger pas met de
            // returnwaarde terug, dus na de laatste pagina: bij AlleVeilingen stond pagina 1
            // 10,6 s later in beeld dan nodig, en achter pagina 2 tot 10. Wie meeleest,
            // ontdubbelt zelf; wat later nog eens terugkomt, vult enkel aan.
            if (fresh.Count > 0) progress?.Report(fresh);

            if (all.Count >= maxResults) break;
            if (!SearchUrlBuilder.SupportsPaging(_def)) break;   // site kent geen paginering

            // Een eerste pagina met maar een handvol zoekertjes is de enige. Geen enkele site
            // toont er minder dan tien per pagina, en een pagina voorbij het einde kostte
            // telkens een lege wachttijd: bij AlleVeilingen 2 zoekertjes en daarna nog 10 s.
            if (page == 1 && listings.Count < MinimumVoorVervolgpagina) break;

            // Een pagina met minder dan de helft van de eerste is de laatste. Zonder deze
            // regel vroeg de app nog een pagina voorbij het einde, en die wachtte op
            // zoekertjes die nooit kwamen: bij AlleVeilingen 10 van de 13 seconden. De
            // helft en niet "minder dan de eerste": een gewone vervolgpagina levert vaak
            // net iets minder op, door dubbels of advertenties.
            if (page > 1 && listings.Count < eerstePagina / 2) break;

            // Na de eerste pagina weten we hoeveel zoekertjes er op een pagina
            // passen, en dus hoeveel pagina's er nog nodig zijn. Bij de brug halen
            // we die SAMEN op in plaats van een voor een: elke ronde kost daar ruim
            // vier seconden, en Catawiki heeft er met 24 kavels per pagina vijf
            // nodig voor honderd. Ze wachten enkel op elkaar omdat wij ze een voor
            // een vroegen, niet omdat het moet.
            //
            // Enkel bij de brug. Playwright deelt één browserprofiel, en een
            // rechtstreekse site levert doorgaans alles in één keer.
            if (page == 1 && _def.UseBridge && fresh.Count > 0)
            {
                // Eén pagina marge. Een latere pagina levert vaak net iets minder op
                // (dubbels, advertenties), en dan kom je een handvol tekort: bij
                // leboncoin gaf de kale schatting 97 van de 100. Die extra pagina
                // gaat toch in dezelfde ronde mee en kost dus geen tijd.
                var nog = (int)Math.Ceiling((maxResults - all.Count) / (double)fresh.Count) + 1;
                var laatste = Math.Min(maxPages, 1 + nog);

                // In golven van drie - zoveel neemt de extensie er tegelijk aan - en na elke
                // golf dezelfde stopregel als hierboven: een lege of korte pagina is de
                // laatste. Tot september 2026 gingen pagina 2 tot 10 in één keer de deur uit,
                // ook als pagina 1 er maar twee gaf: bij leboncoin twaalf seconden aan lege
                // pagina's, en negen verzoeken vanuit je eigen browser op een paar seconden -
                // precies wat een robotbeveiliging opvalt.
                for (var van = 2; van <= laatste; van += GolfGrootte)
                {
                    var tot = Math.Min(laatste, van + GolfGrootte - 1);
                    var golf = await HaalPaginasSamenAsync(query, filters, van, tot, ct);

                    var nieuwInGolf = new List<Listing>();
                    var stoppen = false;

                    foreach (var pagina in golf)
                    {
                        var vers = pagina.Where(l => known.Add(l.Key)).ToList();

                        foreach (var listing in vers)
                        {
                            if (all.Count >= maxResults) break;

                            all.Add(listing);
                            nieuwInGolf.Add(listing);
                        }

                        // Een mislukte pagina komt hier ook als lege pagina aan; ook dan stoppen.
                        if (vers.Count == 0 || pagina.Count < eerstePagina / 2) stoppen = true;
                    }

                    // Meteen melden, zodat het op het scherm staat voor de volgende golf vertrekt.
                    if (nieuwInGolf.Count > 0) progress?.Report(nieuwInGolf);

                    Services.Log.Write($"{_def.Name}: pagina {van} t/m {tot} samen -> " +
                                       $"{nieuwInGolf.Count} nieuwe resultaten");

                    if (stoppen || all.Count >= maxResults) break;
                }

                break;
            }
        }

        return all;
    }

    /// <summary>
    /// Haalt een reeks pagina's tegelijk op en geeft ze terug in paginavolgorde, per pagina
    /// apart: de aanroeper beslist per pagina of het de laatste was. Een pagina die faalt,
    /// komt terug als lege pagina; de rest is nog altijd bruikbaar.
    /// </summary>
    private async Task<List<List<Listing>>> HaalPaginasSamenAsync(string query, SearchFilters? filters,
        int van, int tot, CancellationToken ct)
    {
        var taken = new List<Task<List<Listing>>>();

        for (var page = van; page <= tot; page++)
        {
            var url = SearchUrlBuilder.Build(_def, query, filters, page);
            Services.Log.Write($"{_def.Name}: pagina {page} -> {url}");

            taken.Add(HaalEenPaginaAsync(url, ct));
        }

        var uit = new List<List<Listing>>();
        foreach (var taak in taken) uit.Add(await taak);

        return uit;
    }

    private async Task<List<Listing>> HaalEenPaginaAsync(string url, CancellationToken ct)
    {
        try
        {
            var content = await Services.BridgeServer.Instance.FetchAsync(url, ct,
                waitSelector: _def.ItemSelector, itemTimeoutMs: VervolgWachttijdMs);

            return await ParseAsync(content, int.MaxValue, ct);
        }
        catch (Exception ex)
        {
            Services.Log.Write($"{_def.Name}: pagina mislukt - {ex.Message}");
            return new List<Listing>();
        }
    }

    /// <summary>Kleiner dan dit, en zonder één zoekertje, is geen echte resultatenpagina.</summary>
    internal const int KleinstePagina = 20_000;

    /// <summary>Gooit een fout als een lege eerste pagina geen echte resultatenpagina is.</summary>
    private void ControleerLegePagina(string content)
    {
        if (Services.SiteAnalyzer.LooksBlocked(content))
            throw new InvalidOperationException(
                "toonde een controlepagina tegen robots in plaats van resultaten. " +
                "Probeer in Sites beheren 'Browser gebruiken' of de brug, of meld je aan.");

        if (content.Length < KleinstePagina)
            throw new InvalidOperationException(
                $"gaf een bijna lege pagina terug ({content.Length} tekens) in plaats van resultaten. " +
                "Mogelijk een controlepagina tegen robots; probeer de site in Sites beheren met Testen.");
    }

    /// <summary>
    /// Zoveel zoekertjes moet pagina 1 minstens hebben voor er een pagina 2 gevraagd wordt.
    /// De sites van nu tonen er 20 tot 240 per pagina; minder dan tien is een zoekterm met
    /// weinig resultaten, geen volle pagina.
    /// </summary>
    internal const int MinimumVoorVervolgpagina = 10;

    /// <summary>Hoeveel vervolgpagina's de brug tegelijk vraagt: zoveel neemt de extensie aan (MAX_TEGELIJK).</summary>
    private const int GolfGrootte = 3;

    /// <summary>
    /// Hoe lang een vervolgpagina op haar eerste zoekertje wacht. Pagina 1 heeft dan
    /// al bewezen dat de selector werkt, en in het logboek staat een zoekertje er
    /// mediaan na 48 ms (p90 130 ms). Een vervolgpagina waar het na drie seconden nog
    /// niet staat, is leeg - en die kostte met de gewone acht seconden telkens bijna
    /// tien seconden.
    /// </summary>
    private const int VervolgWachttijdMs = 3000;

    /// <summary>Haalt één pagina op, langs de weg die deze site nodig heeft.</summary>
    private async Task<string> FetchAsync(string url, Services.BrowserFetcher? browser,
        int maxResults, IProgress<List<Listing>>? progress, bool vervolgpagina, CancellationToken ct)
    {
        // Via de brug en een JSON-API: dan vragen we de ruwe tekst in plaats van de
        // HTML van een tabblad, en sturen we de kopregels van de site mee. Een API
        // laat zich niet als pagina openen - dan krijg je de JSON in een <pre> en
        // kan je geen kopregels meegeven.
        if (_def.UseBridge && _def.Kind == SiteKind.Json)
        {
            return await Services.BridgeServer.Instance.FetchAsync(url, ct,
                headers: _def.Headers, rawText: true);
        }

        if (_def.UseBridge)
        {
            // De extensie stuurt tussentijdse versies van de pagina terwijl ze laadt.
            // Die lezen we meteen uit, zodat de eerste zoekertjes al in beeld komen.
            return await Services.BridgeServer.Instance.FetchAsync(url, ct,
                waitSelector: _def.ItemSelector,
                itemTimeoutMs: vervolgpagina ? VervolgWachttijdMs : null,
                // Geen melder, dan ook geen ontvanger: de brug zegt de extensie dan dat ze
                // niet hoeft te streamen. Tot september 2026 was deze ontvanger er altijd,
                // en keek hij pas binnenin of iemand meelas - dus streamde de extensie ook
                // voor de planner, voor niemand.
                onPartial: progress is null ? null : async partialHtml =>
            {
                var partial = await ParseAsync(partialHtml, maxResults, ct);
                Services.Log.Write($"{_def.Name}: tussentijdse pagina uitgelezen -> {partial.Count} resultaten");

                if (partial.Count > 0) progress.Report(partial);
            });
        }

        if (browser is not null)
        {
            // Een JSON-API laat zich niet als pagina openen: dan zie je de JSON wel
            // staan, maar kan je geen kopregels meesturen. Daarom haalt de browser
            // ze in dat geval op vanuit de pagina van die site.
            return _def.Kind == SiteKind.Json
                ? await browser.GetJsonAsync(url, _def.Headers, ct)
                : await browser.GetHtmlAsync(url, _def.ItemSelector, ct, vervolgpagina);
        }

        HttpRequestMessage Verzoek()
        {
            // Een JSON-API vraagt om application/json; een HTML-pagina om text/html.
            var v = new HttpRequestMessage(HttpMethod.Get, url);
            v.Headers.Accept.ParseAdd(_def.Kind == SiteKind.Json ? "application/json" : "text/html");

            // Wat de site verder nog eist. Een onbruikbare kopregel mag de zoekopdracht
            // niet laten vallen, dus die wordt overgeslagen in plaats van te werpen.
            foreach (var (naam, waarde) in _def.Headers ?? new Dictionary<string, string>())
            {
                if (!v.Headers.TryAddWithoutValidation(naam, waarde))
                    Services.Log.Write($"{_def.Name}: kopregel '{naam}' werd niet aanvaard");
            }

            return v;
        }

        var response = await Http.SendAsync(Verzoek(), ct);

        // Sommige sites sturen een eerste bezoek naar een tussenpagina op een ánder
        // domein: een toestemmingsmuur. Die zet een sessiecookie en laat het volgende
        // verzoek gewoon door - bij Tweakers is dat myprivacy.dpgmedia.nl, en het tweede
        // verzoek geeft de echte pagina. De HttpClient houdt zijn cookies bij, dus één
        // keer opnieuw proberen volstaat. Zonder dit kwam er van zo'n site nooit een
        // resultaat binnen, hoe vaak je ook zocht: elk verzoek belandde op de muur.
        var beland = response.RequestMessage?.RequestUri?.Host ?? "";
        var gevraagd = new Uri(url).Host;

        if (!string.Equals(beland, gevraagd, StringComparison.OrdinalIgnoreCase))
        {
            Services.Log.Write($"{_def.Name}: kwam op {beland} uit in plaats van {gevraagd}; nog eens proberen");
            response.Dispose();
            response = await Http.SendAsync(Verzoek(), ct);
        }

        using var _ = response;

        // Een 404 op een zoek-URL betekent niet dat er iets stuk is, maar dat de
        // site dit woord niet kent. AutoScout24 doet dat bij elk woord dat geen
        // automerk is, en "Response status code does not indicate success: 404
        // (Not Found)" zegt de gebruiker daar niets over.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Zonder de sitenaam: de aanroeper zet die er al voor.
            throw new HttpRequestException(
                "kent dit zoekwoord niet."
                + (_def.AllowsEmptyQuery
                    ? " Deze site zoekt op een vaste lijst; laat de zoekbalk leeg en gebruik de filters."
                    : ""));
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Leest de opgehaalde inhoud uit, als JSON of als HTML, op een achtergronddraad.
    ///
    /// Waarom niet gewoon hier: de zoekopdracht start op de schermdraad, en zonder
    /// Task.Run liep het uitlezen daar ook. Een pagina van leboncoin is ruim een
    /// miljoen tekens; dat deed het scherm haperen terwijl je al door de eerste
    /// resultaten scrolde, en de "tegelijk" zoekende sites lazen in feite na elkaar
    /// uit. Wat na de await komt, loopt weer op de draad van de aanroeper - het
    /// samenvoegen van resultaten blijft dus op de schermdraad, waar het hoort.
    /// </summary>
    private Task<List<Listing>> ParseAsync(string content, int maxResults, CancellationToken ct) =>
        Task.Run(() => _def.Kind == SiteKind.Json
            ? Task.FromResult(ParseJson(content, maxResults))
            : ParseHtmlAsync(content, maxResults, ct), ct);

    /// <summary>
    /// Leest een pagina uit die al opgehaald is. Bedoeld voor de analyse van een
    /// nieuwe site: die telt met exact deze motor na wat de AI voorstelt, in plaats
    /// van het antwoord op zijn woord te geloven.
    /// </summary>
    public Task<List<Listing>> ReadPageAsync(string content, CancellationToken ct = default) =>
        ParseAsync(content, int.MaxValue, ct);

    /// <summary>
    /// Hoeveel resultaten de ItemSelector (HTML) of het pad ernaartoe (JSON) vindt,
    /// ook de resultaten waar geen titel uit komt. Het verschil met wat
    /// <see cref="ReadPageAsync"/> oplevert, wijst aan welke selector niet deugt.
    /// </summary>
    public static int CountItems(SiteDefinition def, string content)
    {
        if (string.IsNullOrWhiteSpace(def.ItemSelector)) return 0;

        if (def.Kind == SiteKind.Json)
        {
            using var doc = JsonDocument.Parse(content);
            return TryWalk(doc.RootElement, def.ItemSelector, out var array) && array.ValueKind == JsonValueKind.Array
                ? array.GetArrayLength()
                : 0;
        }

        return new HtmlParser().ParseDocument(content).QuerySelectorAll(def.ItemSelector).Length;
    }

    /// <summary>
    /// Leest één veld uit een losse pagina — niet uit een zoekpagina met resultaten, maar
    /// uit de pagina van één zoekertje. Dezelfde notatie als elders (<c>@attribuut</c>,
    /// <c>::replace</c>, <c>::match</c>), zodat er maar één manier is om een veld aan te
    /// wijzen. Gebruikt door <c>DetailFetcher</c> voor de einddatum van een veiling.
    /// </summary>
    public static async Task<string> ReadFieldAsync(string html, string selector, CancellationToken ct = default)
    {
        var wortel = await WortelAsync(html, ct);
        return wortel is null ? "" : Pick(wortel, selector);
    }

    /// <summary>
    /// Hetzelfde, maar dan alles wat past in plaats van het eerste. Voor de foto's van
    /// één zoekertje (<see cref="SiteDefinition.DetailImagesSelector"/>): daar wil je ze
    /// allemaal, in de volgorde waarin ze op de pagina staan.
    ///
    /// Lege waarden en dubbels vallen weg. Dubbels komen er echt: een site zet dezelfde foto
    /// vaak twee keer op de pagina, één keer als miniatuur in het rijtje eronder en één keer
    /// groot bovenaan.
    /// </summary>
    public static async Task<List<string>> ReadFieldsAsync(string html, string selector,
                                                           CancellationToken ct = default)
    {
        var wortel = await WortelAsync(html, ct);
        return wortel is null ? new List<string>() : PickAll(wortel, selector);
    }

    /// <summary>
    /// Waar de twee hierboven in zoeken: de hele pagina, dus <c>&lt;html&gt;</c> en niet
    /// <c>&lt;body&gt;</c>. Op een zoekpagina maakt dat niets uit, maar op de pagina van één
    /// zoekertje wel: 2dehands zet al zijn foto's in het blok
    /// <c>application/ld+json</c> - de webstandaard die ook Google leest - en dat staat in de
    /// <c>&lt;head&gt;</c>. Met alleen de body vond de selector daar nul elementen, terwijl de
    /// foto's er gewoon stonden.
    /// </summary>
    private static async Task<AngleSharp.Dom.IElement?> WortelAsync(string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var document = await new HtmlParser().ParseDocumentAsync(html, ct);
        return document.DocumentElement;
    }

    // ---------- HTML ----------

    private async Task<List<Listing>> ParseHtmlAsync(string html, int maxResults, CancellationToken ct)
    {
        var results = new List<Listing>();
        var document = await new HtmlParser().ParseDocumentAsync(html, ct);
        var overgeslagen = 0;

        foreach (var node in document.QuerySelectorAll(_def.ItemSelector).Take(maxResults))
        {
            var listing = new Listing
            {
                Source = _def.Name,
                Title = Pick(node, _def.TitleSelector),
                Description = Pick(node, _def.DescriptionSelector),
                Location = Pick(node, _def.LocationSelector),
                TimeLeft = Pick(node, _def.TimeLeftSelector),
                Seller = Pick(node, _def.SellerSelector),
                Url = MakeAbsolute(Pick(node, _def.UrlSelector))
            };

            if (VanEenVeilinghuis(listing))
            {
                overgeslagen++;
                continue;
            }

            var priceText = Pick(node, _def.PriceSelector);
            listing.PriceLabel = priceText;
            listing.Price = ParsePrice(priceText);

            var image = MakeAbsolute(Pick(node, _def.ImageSelector));
            if (!string.IsNullOrEmpty(image)) listing.ImageUrls.Add(image);

            var largeImage = MakeAbsolute(Pick(node, _def.LargeImageSelector));
            if (!string.IsNullOrEmpty(largeImage)) listing.LargeImageUrl = largeImage;

            if (DateTime.TryParse(Pick(node, _def.DateSelector), out var date))
                listing.Date = date;

            // De link is uniek genoeg om dubbels te herkennen, of het id eruit (IdPattern).
            listing.ExternalId = Identiteit(listing);

            if (!string.IsNullOrWhiteSpace(listing.Title))
                results.Add(listing);
        }

        MeldOvergeslagen(overgeslagen);
        return results;
    }

    /// <summary>
    /// Een advertentie van een veilinghuis dat op deze site adverteert (zie
    /// <see cref="SiteDefinition.AuctionSellers"/>). Die slaan we over: het is dezelfde kavel als
    /// op de veilingsite zelf, met een bod in plaats van een vraagprijs. Gemeten op 17 september
    /// 2026 was bij "marantz" een kwart van 2dehands en een derde van Marktplaats zo'n advertentie.
    /// </summary>
    private bool VanEenVeilinghuis(Listing listing) =>
        _def.AuctionSellers.Count > 0 &&
        !string.IsNullOrWhiteSpace(listing.Seller) &&
        _def.AuctionSellers.Any(s => string.Equals(s.Trim(), listing.Seller.Trim(), StringComparison.OrdinalIgnoreCase));

    private void MeldOvergeslagen(int aantal)
    {
        if (aantal > 0)
            Services.Log.Write($"{_def.Name}: {aantal} advertentie(s) van " +
                      $"{string.Join(", ", _def.AuctionSellers)} overgeslagen (veilinghuis)");
    }

    /// <summary>
    /// Herkent een vervangregel achter een selector: <c>::replace(oud,nieuw)</c>.
    /// De vorm lijkt op een CSS-pseudo-element, maar bestaat daar niet, dus hij
    /// botst nergens mee.
    /// </summary>
    private static readonly Regex RewritePattern =
        new(@"::replace\(([^,()]*),([^,()]*)\)", RegexOptions.Compiled);

    /// <summary>
    /// Herkent een patroonregel achter een selector: <c>::match(patroon)</c>. Een
    /// reguliere expressie zit vol komma's en haakjes, dus hier wordt alles tot het
    /// láátste haakje genomen — een patroonregel staat dus altijd achteraan.
    /// </summary>
    private static readonly Regex MatchRule =
        new(@"::match\((.*)\)\s*$", RegexOptions.Compiled);

    /// <summary>Wat er na het uitlezen nog met de waarde gebeurt: vervangen en/of uitknippen.</summary>
    private sealed record Opschoning(List<(string Van, string Naar)> Vervangingen, string Patroon);

    /// <summary>
    /// Splitst de opschoonregels van een selector af, en geeft de kale selector
    /// terug plus wat er achteraf met de waarde moet gebeuren.
    ///
    /// Waarvoor <c>::replace(oud,nieuw)</c> dient: verschillende sites zetten het
    /// formaat van een foto in het pad van de URL. De zoekpagina toont een miniatuur,
    /// maar dezelfde URL met een ander stukje erin geeft de grote versie — bij
    /// AutoScout24 gaat 250x188 (8 kB) zo naar 1024x768 (124 kB), bij eBay s-l500
    /// naar s-l1600, bij Catawiki cw_lot_card_ext naar cw_large en bij AlleVeilingen
    /// _S.webp naar _L.webp. Zonder deze regel konden de selectors enkel een
    /// attribuut uitlezen en was die grote foto onbereikbaar.
    ///
    /// Waarvoor <c>::match(patroon)</c> dient: soms staat het gezochte stuk middenin
    /// een langere tekst, en is er geen apart element voor. AlleVeilingen zet de
    /// plaats als "Rijksweg 2, 9681 Maarkedal, België" — wij willen enkel "Maarkedal"
    /// — en eBay zet het land als "van Nederland" in dezelfde soort regel als de
    /// verzendkosten. Een vervangregel helpt daar niet, want elke keer staat er iets
    /// anders. Wat in groep 1 van het patroon staat, blijft over.
    ///
    /// Meerdere vervangregels achter elkaar mag; ze worden op volgorde toegepast,
    /// en het patroon komt daarna.
    /// </summary>
    private static (string Selector, Opschoning Regels) SplitRewrites(string selector)
    {
        var patroon = "";
        var uitknippen = MatchRule.Match(selector);
        if (uitknippen.Success)
        {
            patroon = uitknippen.Groups[1].Value;
            selector = selector.Remove(uitknippen.Index, uitknippen.Length);
        }

        var regels = new List<(string, string)>();

        for (var match = RewritePattern.Match(selector); match.Success; match = RewritePattern.Match(selector))
        {
            regels.Add((match.Groups[1].Value, match.Groups[2].Value));
            selector = selector.Remove(match.Index, match.Length);
        }

        return (selector.Trim(), new Opschoning(regels, patroon));
    }

    /// <summary>Patronen die al eens fout bleken; zo staat een kapot sitebestand één keer in het logboek en niet per zoekertje.</summary>
    private static readonly HashSet<string> GemeldePatronen = new();

    private static string ApplyRewrites(string value, Opschoning regels)
    {
        foreach (var (van, naar) in regels.Vervangingen)
            if (van.Length > 0) value = value.Replace(van, naar);

        if (regels.Patroon.Length == 0) return value;

        try
        {
            // Past het patroon niet, dan hoort dit veld hier niet: bij eBay staat er
            // op een advertentiekaart geen land, en dan is leeg beter dan de
            // verzendkosten die toevallig op dezelfde plaats staan.
            var treffer = Regex.Match(value, regels.Patroon);
            if (!treffer.Success) return "";

            return (treffer.Groups.Count > 1 ? treffer.Groups[1].Value : treffer.Value).Trim();
        }
        catch (ArgumentException ex)
        {
            // Een ongeldig patroon in een sitebestand mag de zoekopdracht niet laten
            // vallen; de waarde blijft dan zoals ze was, en het logboek zegt het.
            lock (GemeldePatronen)
                if (GemeldePatronen.Add(regels.Patroon))
                    Services.Log.Write($"::match({regels.Patroon}) is geen geldig patroon - {ex.Message}");

            return value;
        }
    }

    /// <summary>
    /// Zoals <see cref="ApplyRewrites"/>, maar dan élke treffer van het patroon in plaats van de
    /// eerste. Enkel voor <see cref="PickAll"/>: daar betekent "alles wat past" ook echt alles, en
    /// dat maakt één blok met veel waarden erin bruikbaar.
    ///
    /// Dat is geen randgeval maar de gewone manier waarop sites hun foto's neerzetten: 2dehands
    /// heeft op de pagina van een zoekertje maar één <c>img</c> in de HTML - de rest zet
    /// JavaScript erin - terwijl álle foto's netjes in het <c>application/ld+json</c>-blok staan,
    /// de webstandaard die ook Google leest. Met dit gedrag wijs je dat blok aan en haal je ze er
    /// alle vijf uit; zonder zou je er één krijgen.
    /// </summary>
    private static List<string> ApplyRewritesAll(string value, Opschoning regels)
    {
        foreach (var (van, naar) in regels.Vervangingen)
            if (van.Length > 0) value = value.Replace(van, naar);

        if (regels.Patroon.Length == 0)
            return value.Length > 0 ? new List<string> { value } : new List<string>();

        try
        {
            return Regex.Matches(value, regels.Patroon)
                        .Select(t => (t.Groups.Count > 1 ? t.Groups[1].Value : t.Value).Trim())
                        .Where(t => t.Length > 0)
                        .ToList();
        }
        catch (ArgumentException ex)
        {
            lock (GemeldePatronen)
                if (GemeldePatronen.Add(regels.Patroon))
                    Services.Log.Write($"::match({regels.Patroon}) is geen geldig patroon - {ex.Message}");

            return new List<string> { value };
        }
    }

    /// <summary>
    /// Leest één veld uit. Schrijf "a@href" of "img@src" om een attribuut te nemen
    /// in plaats van de tekst. Hang er "::replace(oud,nieuw)" achter om in de
    /// gevonden waarde nog iets te vervangen, of "::match(patroon)" om er enkel een
    /// stuk uit te knippen — zie <see cref="SplitRewrites"/>.
    /// Laat leeg om het veld over te slaan.
    ///
    /// Zonder patroon is het altijd het eerste element dat past, zoals in CSS. **Mét een
    /// patroon** wordt het eerste element genomen waar dat patroon ook echt op past. Dat
    /// scheelt: op een kavelpagina van AlleVeilingen staat `div[title='Einddatum']` twee
    /// keer — de eerste bevat het kavelnummer (een foutje van de site), pas de tweede de
    /// datum — en bij eBay zien de regel met de verzendkosten en die met het land er
    /// hetzelfde uit. Zo hoef je daar geen bange selector als <c>:last-child</c> voor te
    /// verzinnen die bij de volgende opmaakwijziging omvalt.
    /// </summary>
    private static string Pick(AngleSharp.Dom.IElement node, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return "";

        // Eerst de vervangregels eraf: anders zou het apenstaartje van
        // "img@src::replace(a,b)" de hele staart als attribuutnaam nemen.
        var (kaal, regels) = SplitRewrites(selector);
        selector = kaal;

        string? attribute = null;
        var at = selector.LastIndexOf('@');
        if (at > 0)
        {
            attribute = selector[(at + 1)..];
            selector = selector[..at];
        }

        // Een punt betekent: het resultaat zelf, niet een kind ervan.
        var kandidaten = selector.Trim() == "."
            ? new[] { node }
            : node.QuerySelectorAll(selector).ToArray();

        foreach (var target in kandidaten)
        {
            var waarde = ApplyRewrites(Regex.Replace(Ruw(target, attribute), @"\s+", " ").Trim(), regels);

            // Zonder patroon telt het eerste element, ook als er niets in staat. Met een
            // patroon betekent leeg "dit is het niet", en mag het volgende geprobeerd worden.
            if (regels.Patroon.Length == 0 || waarde.Length > 0) return waarde;
        }

        return "";
    }

    /// <summary>
    /// Zoals <see cref="Pick"/>, maar dan alles wat past, ontdubbeld en zonder lege. Er is met
    /// opzet maar één plaats waar de notatie van een selector uitgelegd wordt
    /// (<c>@attribuut</c>, <c>::replace</c>, <c>::match</c>), dus dit deelt die uitpakkerij.
    ///
    /// Twee verschillen met <see cref="Pick"/>, en allebei betekenen ze "alles":
    /// élk element dat de selector vindt, en met <c>::match</c> élke treffer binnen zo'n element
    /// in plaats van enkel de eerste (zie <see cref="ApplyRewritesAll"/>).
    /// </summary>
    internal static List<string> PickAll(AngleSharp.Dom.IElement node, string selector)
    {
        var uit = new List<string>();
        if (string.IsNullOrWhiteSpace(selector)) return uit;

        var (kaal, regels) = SplitRewrites(selector);
        selector = kaal;

        string? attribute = null;
        var at = selector.LastIndexOf('@');
        if (at > 0)
        {
            attribute = selector[(at + 1)..];
            selector = selector[..at];
        }

        var kandidaten = selector.Trim() == "."
            ? new[] { node }
            : node.QuerySelectorAll(selector).ToArray();

        var gezien = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in kandidaten)
        {
            var ruw = Regex.Replace(Ruw(target, attribute), @"\s+", " ").Trim();

            foreach (var waarde in ApplyRewritesAll(ruw, regels))
                if (waarde.Length > 0 && gezien.Add(waarde)) uit.Add(waarde);
        }

        return uit;
    }

    /// <summary>De tekst of het attribuut van één element, nog zonder opschoonregels.</summary>
    private static string Ruw(AngleSharp.Dom.IElement target, string? attribute)
    {
        var value = attribute is null
            ? target.TextContent
            : target.GetAttribute(attribute) ?? "";

        // Bij lazy loading staat de foto soms in data-src en soms al in src.
        // Is het gevraagde attribuut leeg, dan het andere proberen.
        if (string.IsNullOrWhiteSpace(value) && attribute is not null)
        {
            var fallback = attribute == "data-src" ? "src"
                         : attribute == "src" ? "data-src"
                         : null;

            if (fallback is not null)
                value = target.GetAttribute(fallback) ?? "";
        }

        return value;
    }

    // ---------- JSON ----------

    /// <summary>De eerste foutmelding uit een antwoord zoals {"errors":[{"message":"..."}]}, of niets.</summary>
    private static string ApiFout(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("errors", out var fouten) && fouten.ValueKind == JsonValueKind.Array &&
                fouten.GetArrayLength() > 0 && fouten[0].TryGetProperty("message", out var bericht))
                return $" (de site zegt: {bericht.GetString()})";

            if (root.TryGetProperty("error", out var fout) && fout.ValueKind == JsonValueKind.String)
                return $" (de site zegt: {fout.GetString()})";
        }
        catch
        {
            // Een foutmelding in een onverwachte vorm: dan zonder.
        }

        return "";
    }

    private List<Listing> ParseJson(string json, int maxResults)
    {
        var results = new List<Listing>();
        using var doc = JsonDocument.Parse(json);

        if (!TryWalk(doc.RootElement, _def.ItemSelector, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            // Ontbreekt het pad al bij de eerste stap, dan is dit een ander soort antwoord
            // dan het sitebestand beschrijft - bij Discogs een GraphQL-fout wanneer de
            // opgeslagen zoekopdracht verlopen is. Tot september 2026 gaf dat stil 0
            // resultaten. Ontbreekt pas een dieper stuk, dan kan het een API zijn die een
            // lege lijst weglaat; dat blijft gewoon "niets gevonden".
            var eerste = _def.ItemSelector.Split('.')[0];
            if (doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty(eerste, out _))
                throw new InvalidOperationException(
                    "gaf een ander antwoord dan het sitebestand verwacht" + ApiFout(doc.RootElement) +
                    ". Misschien is de site veranderd.");

            return results;
        }

        var overgeslagen = 0;

        foreach (var item in array.EnumerateArray().Take(maxResults))
        {
            var listing = new Listing
            {
                Source = _def.Name,
                Title = Read(item, _def.TitleSelector),
                Description = Read(item, _def.DescriptionSelector),
                Location = Read(item, _def.LocationSelector),
                TimeLeft = Read(item, _def.TimeLeftSelector),
                Seller = Read(item, _def.SellerSelector),
                Url = MakeAbsolute(Read(item, _def.UrlSelector))
            };

            if (VanEenVeilinghuis(listing))
            {
                overgeslagen++;
                continue;
            }

            var priceText = Read(item, _def.PriceSelector);
            listing.PriceLabel = priceText;
            var price = ParsePrice(priceText);
            listing.Price = _def.PriceInCents && price.HasValue ? price / 100m : price;

            var image = MakeAbsolute(Read(item, _def.ImageSelector));
            if (!string.IsNullOrEmpty(image)) listing.ImageUrls.Add(image);

            var largeImage = MakeAbsolute(Read(item, _def.LargeImageSelector));
            if (!string.IsNullOrEmpty(largeImage)) listing.LargeImageUrl = largeImage;

            if (DateTime.TryParse(Read(item, _def.DateSelector), out var date))
                listing.Date = date;

            listing.ExternalId = Identiteit(listing);

            if (!string.IsNullOrWhiteSpace(listing.Title))
                results.Add(listing);
        }

        MeldOvergeslagen(overgeslagen);
        return results;
    }

    /// <summary>Volgt een pad als "priceInfo.priceCents" door de JSON heen.</summary>
    internal static bool TryWalk(JsonElement start, string path, out JsonElement result)
    {
        result = start;
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.ValueKind == JsonValueKind.Array)
            {
                // Bij een lijst nemen we het eerste element, bv. de eerste foto.
                var first = result.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Undefined) return false;
                result = first;
            }

            if (result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty(part, out result))
            {
                return false;
            }
        }

        return true;
    }

    private static string Read(JsonElement item, string path)
    {
        // Dezelfde opschoonregels als bij HTML: een JSON-API kan net zo goed een
        // fotoformaat in het pad van de URL zetten, of een plaats met straat erbij.
        var (kaal, regels) = SplitRewrites(path);

        if (!TryWalk(item, kaal, out var value)) return "";

        var tekst = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Array => value.EnumerateArray().FirstOrDefault().ToString(),
            _ => ""
        };

        return ApplyRewrites(tekst, regels);
    }

    // ---------- hulpjes ----------

    /// <summary>Haalt een bedrag uit tekst als "€ 1.499,00", "1499", "Bieden".</summary>
    private static decimal? ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Regex.Match(text, @"\d[\d.,\s]*");
        if (!match.Success) return null;

        var raw = match.Value.Replace(" ", "");

        // Nederlandse notatie: punt is duizendtal, komma is decimaal.
        if (raw.Contains(',')) raw = raw.Replace(".", "").Replace(',', '.');
        else if (raw.Count(c => c == '.') == 1 && raw.Split('.')[1].Length == 3) raw = raw.Replace(".", "");

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private string MakeAbsolute(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.StartsWith("//")) return "https:" + url;
        if (url.StartsWith("http")) return url;
        return _def.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
    }
}
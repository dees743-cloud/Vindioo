using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Zentrix.Models;

namespace Zentrix.Sources;

/// <summary>
/// Bouwt de zoek-URL van een site: de zoekterm, de filters die de site kent en
/// het paginanummer. Gedeeld door alle motoren, zodat elke site zich op dezelfde
/// manier laat filteren en doorbladeren — ook als zijn URL er anders uitziet.
///
/// Deze klasse kent geen enkele site bij naam. Wat een site bijzonder maakt,
/// staat in zijn bestand: de plaatshouders in de zoek-URL, de URL-stijl en de
/// fragmenten van zijn filters.
/// </summary>
public static class SearchUrlBuilder
{
    /// <summary>Bouwt de URL voor één pagina; pagina 1 is de eerste.</summary>
    public static string Build(SiteDefinition def, string query, SearchFilters? filters, int page = 1) =>
        def.UrlStyle switch
        {
            SiteUrlStyle.Base64Json => BuildBase64Json(def, query, filters, page),
            _ => BuildStandard(def, query, filters, page)
        };

    /// <summary>
    /// Kan deze site meer dan één pagina aanleveren? Dat kan op twee manieren: een
    /// stukje dat achteraan komt (<see cref="SiteDefinition.PageTemplate"/>), of het
    /// paginanummer rechtstreeks in de zoek-URL met {page} of {offset}.
    /// </summary>
    public static bool SupportsPaging(SiteDefinition def) =>
        def.SearchUrlTemplate.Contains(PaginaPlaatshouder, StringComparison.Ordinal) ||
        (def.SearchUrlTemplate.Contains(OffsetPlaatshouder, StringComparison.Ordinal) && def.PageSize > 0) ||
        !string.IsNullOrWhiteSpace(def.PageTemplate);

    /// <summary>
    /// Kent deze site de gevraagde filter? Dat staat in zijn Filters-mapping, of -
    /// voor "custom" - in zijn lijst met sitegebonden filters. Het hoofdscherm
    /// gebruikt dit om filters te verbergen die niets doen.
    /// </summary>
    public static bool Supports(SiteDefinition def, string key)
    {
        if (def.Filters is not null &&
            def.Filters.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // De sitegebonden filters zitten niet in de vaste mapping; daar beslist
        // de sitebeschrijving zelf of er iets te kiezen valt.
        return string.Equals(key, "custom", StringComparison.OrdinalIgnoreCase) &&
               def.CustomFilters.Count > 0;
    }

    // ---------- plaatshouders ----------

    /// <summary>
    /// De plaatshouder voor sites die hun filters niet als losse parameters
    /// aanvaarden maar binnen een blok in de URL. Staat hij in de zoek-URL, dan
    /// worden de fragmenten daar tussengezet in plaats van achteraan geplakt. Zo
    /// blijft het bij data: de site beschrijft zelf hoe zijn filters eruitzien, er
    /// komt geen code per site bij.
    ///
    /// Discogs is daar het voorbeeld van: zijn GraphQL-API verwacht
    /// `variables={"query":"…","filter":{ … }}` en negeert elke losse parameter.
    /// </summary>
    private const string FilterPlaatshouder = "{filters}";

    /// <summary>Het paginanummer, voor sites waar dat midden in de zoek-URL staat.</summary>
    private const string PaginaPlaatshouder = "{page}";

    private static string PageNumber(SiteDefinition def, int page) =>
        (def.FirstPage + page - 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Vanaf het hoeveelste zoekertje een pagina begint, voor sites die zo pagineren in plaats
    /// van met een paginanummer. De API van 2dehands en Marktplaats vraagt
    /// <c>limit=100&amp;offset=200</c> voor de derde pagina. Tot september 2026 kende de app enkel
    /// {page}, en haalde ze bij 2dehands daardoor nooit meer dan de eerste honderd op.
    /// </summary>
    private const string OffsetPlaatshouder = "{offset}";

    private static string Offset(SiteDefinition def, int page) =>
        ((page - 1) * Math.Max(0, def.PageSize)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Vult {page} en {offset} in.</summary>
    private static string Pagina(string tekst, SiteDefinition def, int page) =>
        tekst.Replace(PaginaPlaatshouder, PageNumber(def, page))
             .Replace(OffsetPlaatshouder, Offset(def, page));

    private static string Lijm(SiteDefinition def) =>
        string.IsNullOrEmpty(def.FilterJoin) ? "," : def.FilterJoin;

    /// <summary>
    /// De stukjes voor alle ingevulde filters: eerst de vaste uit de Filters-mapping,
    /// dan de sitegebonden, zodat de volgorde voorspelbaar blijft. Hoe een waarde
    /// versleuteld wordt, hangt af van waar ze terechtkomt; dat beslist de aanroeper
    /// met <paramref name="encode"/>, die de waarde krijgt en de scheiding tussen
    /// meerdere keuzes (leeg als er maar één waarde is).
    /// </summary>
    private static List<string> Fragments(SiteDefinition def, SearchFilters? filters,
        Func<string, string, string> encode)
    {
        var stukken = new List<string>();

        if (def.Filters is not null)
        {
            foreach (var (key, fragment) in def.Filters)
            {
                if (string.IsNullOrWhiteSpace(fragment)) continue;

                if (!fragment.Contains("{value}"))
                {
                    // Vast fragment (bv. "&sort=date"): altijd toevoegen.
                    stukken.Add(fragment);
                    continue;
                }

                var value = filters?.ValueFor(key) ?? "";
                if (string.IsNullOrWhiteSpace(value)) continue;

                stukken.Add(fragment.Replace("{value}", encode(value, "")));
            }
        }

        foreach (var custom in def.CustomFilters)
        {
            if (string.IsNullOrWhiteSpace(custom.Fragment)) continue;

            var waarde = filters is not null && filters.Custom.TryGetValue(custom.Key, out var gekozen)
                ? gekozen
                : "";

            // Niets gekozen. Meestal komt er dan niets in de URL, maar sommige sites
            // willen de sleutel toch zien: AlleVeilingen geeft zonder "r":[] andere
            // kavels dan met een lege lijst. Wat er dan moet staan, zegt EmptyFragment.
            if (string.IsNullOrWhiteSpace(waarde))
            {
                if (!string.IsNullOrEmpty(custom.EmptyFragment)) stukken.Add(custom.EmptyFragment);
                continue;
            }

            var scheiding = custom.Multiple ? custom.Separator : "";
            stukken.Add(custom.Fragment.Replace("{value}", encode(waarde, scheiding)));
        }

        return stukken;
    }

    // ---------- gewone parameters ----------

    /// <summary>
    /// Zet de zoekterm in de URL. Staat <c>{query}</c> in het PAD — dus voor het
    /// vraagteken — dan blijft een schuine streep een scheidingsteken tussen
    /// padstukken in plaats van een te versleutelen teken.
    ///
    /// Dat is nodig sinds AutoScout24: die kent geen vrije tekstzoekfunctie en
    /// zoekt met /lst/bmw/x5. Versleuteld wordt dat /lst/bmw%2Fx5 en antwoordt de
    /// site met 404. Alle andere sites zetten hun zoekterm in de querystring, en
    /// daar blijft alles gewoon versleuteld.
    /// </summary>
    private static string EncodeQuery(string template, string query)
    {
        var plek = template.IndexOf("{query}", StringComparison.Ordinal);
        var vraagteken = template.IndexOf('?');

        var inHetPad = plek >= 0 && (vraagteken < 0 || plek < vraagteken);

        return inHetPad
            ? string.Join("/", query.Split('/').Select(Uri.EscapeDataString))
            : Uri.EscapeDataString(query);
    }

    /// <summary>
    /// Haalt de lege plaatshouder uit een pad. Staat {query} in het pad en is de
    /// zoekterm leeg, dan blijft er een schuine streep te veel staan:
    /// ".../lst/?atype=C". AutoScout24 antwoordt daarop met een 308 naar de versie
    /// zonder die streep, en een omleiding halverwege kost een verzoek of laat de
    /// zoekopdracht stranden. Dus halen we hem er meteen zelf af.
    /// </summary>
    private static string TrimLegePadstuk(string url)
    {
        var vraagteken = url.IndexOf('?');
        var pad = vraagteken < 0 ? url : url[..vraagteken];
        var rest = vraagteken < 0 ? "" : url[vraagteken..];

        // Enkel de streep op het einde van het pad, en nooit die van "https://".
        return pad.Length > 9 && pad.EndsWith('/') ? pad[..^1] + rest : url;
    }

    private static string BuildStandard(SiteDefinition def, string query, SearchFilters? filters, int page)
    {
        // Staat {query} tussen aanhalingstekens, dan zit hij in een JSON-blok in de URL (de
        // GraphQL-API van Discogs: variables={"query":"{query}",...}). Dan eerst als JSON-tekst
        // ontsnappen, en pas daarna als URL. Anders maakte een aanhalingsteken in de zoekterm -
        // 12" is gewoon op Discogs - de JSON ongeldig, en gaf de site een fout.
        var term = def.SearchUrlTemplate.Contains("\"{query}\"", StringComparison.Ordinal) ? Json(query) : query;

        var url = def.SearchUrlTemplate.Replace("{query}", EncodeQuery(def.SearchUrlTemplate, term));

        if (string.IsNullOrEmpty(query)) url = TrimLegePadstuk(url);

        // In een URL wordt de samengevoegde waarde als geheel versleuteld: een
        // scheiding als & wordt zo vanzelf %26, en dat is precies wat Catawiki wil.
        var stukken = Fragments(def, filters, (waarde, _) => Uri.EscapeDataString(waarde));

        // Horen de filters in een blok, dan in één keer op hun plaats; anders
        // achteraan. Niets aangevinkt geeft een leeg blok, en dat mag: Discogs
        // aanvaardt `"filter":{}` en geeft dan gewoon alles terug (nagemeten).
        url = url.Contains(FilterPlaatshouder, StringComparison.Ordinal)
            ? url.Replace(FilterPlaatshouder, string.Join(Lijm(def), stukken))
            : url + string.Concat(stukken);

        url = Pagina(url, def, page);

        // De eerste pagina is gewoon de zoek-URL; pas daarna nummeren we.
        if (page > 1 && !string.IsNullOrWhiteSpace(def.PageTemplate))
            url += Pagina(def.PageTemplate, def, page);

        return url;
    }

    // ---------- JSON in een base64-parameter ----------

    /// <summary>
    /// Voor sites die hun hele zoekopdracht in één base64-parameter met JSON erin
    /// stoppen. De JSON staat in de zoek-URL, na het eerste "={", bv. bij AlleVeilingen:
    /// <code>
    /// https://alleveilingen.be/nl/kavels?fi={"s":"{query}","o":11,"pg":{page},"Type":2{filters}}
    /// </code>
    /// De app vult de plaatshouders in, versleutelt de JSON als base64 en zet die
    /// achter het isgelijkteken.
    ///
    /// Drie dingen gaan hier anders dan in een gewone URL:
    /// - Waarden worden als JSON-tekst ontsnapt, niet als URL: de URL-versleuteling
    ///   komt pas na de base64, over het geheel.
    /// - Bij meerdere keuzes gebeurt dat per keuze, zodat een scheiding als "," zelf
    ///   blijft staan: {"r":["BEWV","BEAN"]}.
    /// - {filters} staat achter de laatste vaste eigenschap. De komma ervoor hoort
    ///   er enkel bij als er ook echt filters zijn, en die zet de app er zelf bij.
    /// </summary>
    private static string BuildBase64Json(SiteDefinition def, string query, SearchFilters? filters, int page)
    {
        var template = def.SearchUrlTemplate;
        var begin = template.IndexOf("={", StringComparison.Ordinal);

        // Geen JSON-blok gevonden: dan valt er niets te versleutelen.
        if (begin < 0) return BuildStandard(def, query, filters, page);

        var adres = template[..(begin + 1)];

        var json = template[(begin + 1)..]
            .Replace("{query}", Json(query))
            .Replace(PaginaPlaatshouder, PageNumber(def, page));

        var stukken = Fragments(def, filters, (waarde, scheiding) => scheiding.Length == 0
            ? Json(waarde)
            : string.Join(scheiding, waarde.Split(scheiding).Select(Json)));

        json = json.Replace(FilterPlaatshouder,
            stukken.Count == 0 ? "" : "," + string.Join(Lijm(def), stukken));

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return adres + Uri.EscapeDataString(encoded);
    }

    /// <summary>Een tekst veilig binnen aanhalingstekens in JSON, zonder die aanhalingstekens zelf.</summary>
    private static string Json(string tekst) =>
        JsonEncodedText.Encode(tekst, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value;
}

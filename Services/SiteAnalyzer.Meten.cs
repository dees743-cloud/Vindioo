using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Het antwoord van de AI natellen met de echte motor, en het uitlezen.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
    // ==================== natellen ====================

    /// <summary>
    /// Voert de definitie uit op de volledige pagina - niet het ingekorte stuk - met
    /// dezelfde motor die later zoekt, en zegt wat er niet klopt.
    /// </summary>
    private static async Task<AnalysisCheck> MeasureAsync(SiteDefinition def, string page, CancellationToken ct)
    {
        var problems = new List<string>();
        int matched;
        List<Listing> listings;

        try
        {
            matched = GenericSource.CountItems(def, page);
            listings = await new GenericSource(def).ReadPageAsync(page, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Add(def.Kind == SiteKind.Json
                ? $"Het pad kon niet gevolgd worden: {ex.Message}"
                : $"De motor weigert een selector, vermoedelijk geen geldige CSS: {ex.Message}");

            return new AnalysisCheck(0, 0, 0, 0, 0, 0, 0, 0, problems, Array.Empty<Listing>());
        }

        var n = listings.Count;
        var prices = listings.Count(l => l.Price.HasValue);
        var links = listings.Count(l => !string.IsNullOrEmpty(l.Url));
        var uniqueLinks = listings.Where(l => !string.IsNullOrEmpty(l.Url)).Select(l => l.Url).Distinct().Count();
        var images = listings.Count(l => l.ImageUrls.Count > 0);
        var large = listings.Count(l => !string.IsNullOrEmpty(l.LargeImageUrl));
        var locations = listings.Count(l => !string.IsNullOrWhiteSpace(l.Location));

        if (matched == 0)
            problems.Add("De itemSelector vindt niets op de pagina.");
        else if (n == 0)
            problems.Add($"De itemSelector vindt {matched} elementen, maar de titelselector levert bij geen enkel iets op.");
        else if (n < 3)
            problems.Add($"Maar {n} zoekertjes herkend; een zoekpagina heeft er doorgaans veel meer.");
        else if (n < matched * 0.8)
            problems.Add($"Van de {matched} gevonden elementen hebben er maar {n} een titel. Vangt de itemSelector " +
                         "ook iets anders dan zoekertjes, of mist de titelselector een variant van de kaart?");

        if (n > 0)
        {
            if (links < n * 0.9)
                problems.Add($"Maar {links} van de {n} zoekertjes hebben een link. De app herkent zoekertjes aan " +
                             "hun link; zonder link vallen zoekertjes met dezelfde titel samen.");
            else if (uniqueLinks < n * 0.9)
                problems.Add($"Maar {uniqueLinks} verschillende links voor {n} zoekertjes: de urlSelector neemt " +
                             "vermoedelijk een link die op elke kaart dezelfde is.");

            if (prices < n * 0.5)
                problems.Add($"Maar bij {prices} van de {n} zoekertjes is een prijs herkend.");

            if (images < n * 0.5)
                problems.Add($"Maar {images} van de {n} zoekertjes hebben een foto.");

            if (def.LargeImageSelector.Contains("::replace") && large > 0 &&
                listings.All(l => l.ImageUrls.Count == 0 || l.LargeImageUrl == l.ImageUrls[0]))
                problems.Add("De grote foto is overal gelijk aan de miniatuur: het stukje in ::replace komt niet in de URL voor.");
        }

        return new AnalysisCheck(matched, n, prices, links, uniqueLinks, images, large, locations,
            problems, listings.Take(3).ToList());
    }

    private static string BuildFeedback(AnalysisCheck check)
    {
        var sb = new StringBuilder();

        sb.AppendLine("De app heeft je voorstel uitgevoerd met de motor, op de volledige pagina " +
                      "(niet enkel het stuk dat jij zag). Dit kwam eruit:");
        sb.AppendLine($"- itemSelector vond {check.Matched} elementen");
        sb.AppendLine($"- zoekertjes met titel: {check.Listings}");
        sb.AppendLine($"- met herkende prijs: {check.Prices}");
        sb.AppendLine($"- met link: {check.Links}, waarvan {check.UniqueLinks} verschillend");
        sb.AppendLine($"- met foto: {check.Images}, met grote foto: {check.LargeImages}");
        sb.AppendLine($"- met plaats: {check.Locations}");
        sb.AppendLine();
        sb.AppendLine("Wat niet klopt:");
        foreach (var problem in check.Problems) sb.AppendLine("- " + problem);

        if (check.Sample.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("De eerste zoekertjes zoals de app ze leest:");

            var i = 1;
            foreach (var l in check.Sample)
            {
                var price = l.Price?.ToString(CultureInfo.InvariantCulture) ?? "geen";
                var image = l.ImageUrls.Count > 0 ? l.ImageUrls[0] : "";
                sb.AppendLine($"{i++}. titel \"{Short(l.Title)}\" | prijstekst \"{Short(l.PriceLabel)}\" -> {price} | " +
                              $"link {Short(l.Url)} | foto {Short(image)} | grote foto {Short(l.LargeImageUrl)} | " +
                              $"plaats \"{Short(l.Location)}\"");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Verbeter wat niet klopt en laat staan wat werkt. Staat een gegeven echt niet op de " +
                      "pagina, laat dat veld dan leeg en zeg het in notes.");

        return sb.ToString();

        static string Short(string value) => value.Length <= 120 ? value : value[..120] + "…";
    }

    // ==================== het antwoord ====================

    private static SiteDefinition ToDefinition(string answer, string searchUrl, string url, bool isJson, FetchRoute route)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(answer);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("De AI gaf geen bruikbaar antwoord terug:\n" + answer);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var uri = new Uri(url);

            var pageTemplate = Get(root, "pageTemplate");
            if (!pageTemplate.Contains("{page}")) pageTemplate = "";

            // De AI mag de zoek-URL bijsturen, want paginering zit soms in het pad
            // (Kleinanzeigen: /s-seite:2/cd/k0) en dan kan {page} enkel daar staan.
            // Zonder dit veld kon ze dat enkel in notes melden - en dat deed ze ook:
            // "geen pageTemplate mogelijk; de motor kan enkel pagina 1 lezen", terwijl
            // SearchUrlBuilder {page} gewoon overal in de zoek-URL aanvaardt.
            //
            // Twee eisen, anders blijft staan wat de gebruiker intypte: {query} moet erin
            // blijven en het moet dezelfde site zijn. Of pagina 1 er nog mee werkt, wordt
            // daarna gemeten (zie ControleerPaginaEenAsync).
            var voorstel = Get(root, "searchUrlTemplate");
            if (voorstel.Length > 0 && voorstel.Contains("{query}") && ZelfdeHost(voorstel, searchUrl))
                searchUrl = voorstel;

            var definition = new SiteDefinition
            {
                Name = Get(root, "name"),
                ShortName = Get(root, "shortName"),
                BaseUrl = Get(root, "baseUrl"),
                ItemSelector = Get(root, "itemSelector"),
                TitleSelector = Get(root, "titleSelector"),
                DescriptionSelector = Get(root, "descriptionSelector"),
                PriceSelector = Get(root, "priceSelector"),
                LocationSelector = Get(root, "locationSelector"),
                DateSelector = Get(root, "dateSelector"),
                UrlSelector = Get(root, "urlSelector"),
                ImageSelector = Get(root, "imageSelector"),
                LargeImageSelector = Get(root, "largeImageSelector"),
                PriceInCents = root.TryGetProperty("priceInCents", out var cents) && cents.ValueKind == JsonValueKind.True,
                PageTemplate = pageTemplate,

                // Bij een veiling is de prijs een bod en geen vraagprijs. Dat bepaalt of
                // de site mee mag tellen in de prijsindicatie, en of er een aftelklok bij
                // hoort te staan; allebei staan ze gewoon op de zoekpagina te lezen.
                IsAuction = root.TryGetProperty("isAuction", out var auction) && auction.ValueKind == JsonValueKind.True,
                TimeLeftSelector = Get(root, "timeLeftSelector"),

                // Hoort bij {offset}: hoeveel zoekertjes er op een pagina passen. Zonder
                // dat getal telt {offset} niet als paginering en vraagt de app twintig
                // keer dezelfde pagina.
                PageSize = root.TryGetProperty("pageSize", out var size) && size.TryGetInt32(out var perPagina) &&
                           perPagina > 0
                    ? perPagina
                    : 0,
                FirstPage = root.TryGetProperty("firstPage", out var first) && first.TryGetInt32(out var number) && number >= 0
                    ? number
                    : 1,
                Notes = Get(root, "notes"),

                SearchUrlTemplate = searchUrl,
                Kind = isJson ? SiteKind.Json : SiteKind.Html,

                // De weg die werkte, niet wat de AI denkt dat nodig is.
                NeedsBrowser = route != FetchRoute.Direct,
                UseBridge = route == FetchRoute.Bridge
            };

            if (string.IsNullOrWhiteSpace(definition.BaseUrl))
                definition.BaseUrl = $"{uri.Scheme}://{uri.Host}";

            if (string.IsNullOrWhiteSpace(definition.Name))
                definition.Name = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;

            return definition;
        }
    }

    private static string Get(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static JsonObject UserMessage(string text) => new() { ["role"] = "user", ["content"] = text };

    private static readonly string[] StringFields =
    {
        "name", "shortName", "baseUrl", "itemSelector", "titleSelector", "descriptionSelector",
        "priceSelector", "locationSelector", "dateSelector", "urlSelector", "imageSelector",
        "largeImageSelector", "timeLeftSelector", "pageTemplate", "searchUrlTemplate", "notes"
    };

    /// <summary>
    /// De vorm van het antwoord. Met een schema geeft de API gegarandeerd geldige
    /// JSON terug; vroeger werd er met een reguliere expressie een blok uit de tekst
    /// gevist, en dat brak zodra er iets omheen stond.
    /// </summary>
    private static JsonObject BuildSchema()
    {
        var properties = new JsonObject();
        foreach (var field in StringFields) properties[field] = new JsonObject { ["type"] = "string" };
        properties["priceInCents"] = new JsonObject { ["type"] = "boolean" };
        properties["isAuction"] = new JsonObject { ["type"] = "boolean" };
        properties["firstPage"] = new JsonObject { ["type"] = "integer" };
        properties["pageSize"] = new JsonObject { ["type"] = "integer" };

        // De filters horen in ditzelfde schema en niet in een tweede vraag: zie
        // FilterSchemas voor wat dat scheelde.
        var (vast, eigen) = FilterSchemas();
        properties["filters"] = new JsonObject { ["type"] = "array", ["items"] = vast };
        properties["customFilters"] = new JsonObject { ["type"] = "array", ["items"] = eigen };

        var required = new JsonArray();
        foreach (var property in properties) required.Add(property.Key);

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    /// <summary>
    /// De opdracht en de vorm van het antwoord gaan mee als parameter: de zoekpagina en
    /// de advertentiepagina zijn twee verschillende vragen, met elk hun eigen velden.
    /// </summary>
    private static async Task<(JsonNode Content, string Text)> AskClaudeAsync(string apiKey, JsonArray messages,
        string systemPrompt, JsonObject schema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 16000,
            ["system"] = systemPrompt,

            // Bij een verbeterronde gaat dezelfde pagina opnieuw mee. Uit de cache
            // gelezen kost die een tiende van de prijs.
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },

            // Weigert het model een pagina (dat kan bij een beveiligingsfilter), dan
            // neemt de API zelf een ander model in plaats van niets terug te geven.
            ["fallbacks"] = "default",

            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema }
            },
            ["messages"] = messages.DeepClone()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await ApiHttp.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API-fout ({(int)response.StatusCode}): {json}");

        var root = JsonNode.Parse(json)!;
        var usage = root["usage"];
        // Alle vier de tellers: zonder "naar cache" lijkt een eerste verzoek bijna gratis,
        // want dan staat de hele pagina daar en niet bij de gewone invoer.
        Log.Write($"analyse: {root["model"]} - invoer {usage?["input_tokens"]}, naar cache " +
                  $"{usage?["cache_creation_input_tokens"]}, uit cache {usage?["cache_read_input_tokens"]}, " +
                  $"uitvoer {usage?["output_tokens"]}");

        switch (root["stop_reason"]?.GetValue<string>())
        {
            case "refusal":
                throw new InvalidOperationException("De AI weigerde deze pagina te analyseren.");
            case "max_tokens":
                throw new InvalidOperationException("Het antwoord van de AI werd afgebroken omdat het te lang werd.");
        }

        // Het antwoord bevat vóór de tekst ook een blok met het nadenken. Enkel
        // content[0] lezen, zoals vroeger, geeft dan een lege tekst.
        var content = root["content"] as JsonArray ?? new JsonArray();
        var text = string.Concat(content
            .Where(block => block?["type"]?.GetValue<string>() == "text")
            .Select(block => block!["text"]?.GetValue<string>() ?? ""));

        // Het hele blok wordt bewaard, ongewijzigd: bij een verbeterronde gaat het
        // terug naar de API, en die verwacht het nadenken zoals het was.
        return (content.DeepClone(), text);
    }
}

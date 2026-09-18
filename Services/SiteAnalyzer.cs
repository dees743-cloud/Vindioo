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

/// <summary>Langs welke weg de analyse de pagina te pakken kreeg.</summary>
public enum FetchRoute
{
    /// <summary>Een gewoon verzoek, zonder browser. De snelste weg.</summary>
    Direct,

    /// <summary>De Chrome van de app (Playwright), voor pagina's die JavaScript nodig hebben.</summary>
    Browser,

    /// <summary>De eigen Chrome van de gebruiker via de extensie, voor sites met bot-detectie.</summary>
    Bridge
}

/// <summary>
/// Wat de motor met een voorstel van de AI uit de pagina haalde. Dit is geen
/// schatting maar een telling: dezelfde GenericSource die later zoekt, op
/// dezelfde pagina.
/// </summary>
public sealed record AnalysisCheck(
    int Matched, int Listings, int Prices, int Links, int UniqueLinks,
    int Images, int LargeImages, int Locations,
    IReadOnlyList<string> Problems, IReadOnlyList<Listing> Sample)
{
    public bool IsGood => Problems.Count == 0;

    /// <summary>Om twee pogingen te vergelijken: hoeveel bruikbaars er uitkwam.</summary>
    public int Score => Listings + UniqueLinks + Prices + Images + Locations / 2 - Problems.Count * 5;

    public string Summary => Listings == 0
        ? "Geen zoekertjes herkend."
        : $"{Listings} zoekertjes herkend: {Prices} met prijs, {UniqueLinks} met een eigen link, " +
          $"{Images} met foto, {Locations} met plaats.";
}

/// <summary>Het resultaat van een analyse: de definitie, de telling en hoe het ging.</summary>
public sealed record SiteAnalysis(SiteDefinition Definition, AnalysisCheck Check, FetchRoute Route, int Rounds);

/// <summary>
/// Laat Claude uitzoeken hoe een onbekende site in elkaar zit, en geeft een
/// SiteDefinition terug die al met de echte motor is nagemeten.
///
/// Drie dingen maken het verschil met "stuur de pagina en geloof het antwoord",
/// en ze komen alle drie uit het inregelen van de bestaande sites:
///
/// 1. De app zoekt zelf de goedkoopste weg die werkt: eerst rechtstreeks, dan een
///    browser, pas bij een blokkade de brug. Vroeger ging alles via de browser en
///    kreeg elke nieuwe site "browser nodig" mee, ook een JSON-API die in een halve
///    seconde antwoordt.
/// 2. De pagina wordt eerst opgeschoond. Een zoekpagina bestaat voor het grootste
///    deel uit scripts; blind knippen leverde vaak een stuk JavaScript op in plaats
///    van de zoekertjes.
/// 3. Het antwoord wordt uitgevoerd en geteld. Klopt het niet, dan krijgt Claude die
///    telling terug met de eerste zoekertjes zoals de app ze leest, en mag hij
///    verbeteren. Dat is precies wat we met de hand deden bij AutoScout24 (18 van de
///    20 plaatsen) en Discogs (73 van de 100 zoekertjes).
/// </summary>
public class SiteAnalyzer
{
    private const string Model = "claude-opus-5";

    /// <summary>Eerste voorstel plus hoogstens twee verbeteringen.</summary>
    private const int MaxRounds = 3;

    // Genoeg voor een volledige resultatenlijst na het opschonen. Let op wat dat kost:
    // HTML telt ongeveer twee tekens per token, dus een volle 150 000 tekens zijn zo'n
    // 75 000 tokens. Gemeten bij Kleinanzeigen: 73 526 tokens, samen met het antwoord
    // ongeveer 50 cent voor een eerste ronde. Een verbeterronde leest de pagina uit de
    // cache en kost een fractie daarvan.
    private const int MaxHtmlChars = 150_000;
    private const int MaxJsonChars = 120_000;

    private static readonly HttpClient PageHttp = CreatePageClient();

    // Een analyse met nadenken erbij duurt al gauw een minuut; de oude limiet van
    // zestig seconden liep daar dus in.
    private static readonly HttpClient ApiHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static HttpClient CreatePageClient()
    {
        // Gecomprimeerd, net als GenericSource: de analyse moet de site zien zoals de app ze ziet.
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

    /// <summary>
    /// Haalt de zoekpagina op, laat de AI de selectors bepalen en telt ze na.
    /// </summary>
    /// <param name="searchUrl">Zoek-URL met {query} als plaatshouder.</param>
    /// <param name="testQuery">Woord om mee te testen; liefst een gewoon woord als "cd".</param>
    /// <param name="forceBridge">Meteen via de brug, zonder de andere wegen te proberen.</param>
    public async Task<SiteAnalysis> AnalyzeAsync(string searchUrl, string testQuery,
        bool forceBridge = false, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Geen API-sleutel gevonden. Plak je Claude API-sleutel bovenaan dit venster en probeer opnieuw.");

        var url = searchUrl.Replace("{query}", Uri.EscapeDataString(testQuery));

        var (page, route) = await FetchPageAsync(url, testQuery, forceBridge, status, ct);
        var isJson = LooksLikeJson(page);

        SaveForInspection(page, isJson);

        var messages = new JsonArray
        {
            UserMessage(BuildFirstPrompt(searchUrl, testQuery, page, isJson, route))
        };

        SiteDefinition? best = null;
        AnalysisCheck? bestCheck = null;
        var rounds = 0;

        for (var round = 1; round <= MaxRounds; round++)
        {
            rounds = round;
            status?.Report(round == 1
                ? "De AI bekijkt de pagina..."
                : $"De AI verbetert zijn selectors (poging {round})...");

            var (content, answer) = await AskClaudeAsync(apiKey, messages, ct);
            var definition = ToDefinition(answer, searchUrl, url, isJson, route);

            status?.Report("De selectors natellen op de pagina...");
            var check = await MeasureAsync(definition, page, ct);

            Log.Write($"analyse {definition.Name}: poging {round} via {route} -> {check.Summary}" +
                      (check.IsGood ? "" : " | " + string.Join(" | ", check.Problems)));

            var improved = bestCheck is null || check.Score > bestCheck.Score;
            if (improved)
            {
                best = definition;
                bestCheck = check;
            }

            if (check.IsGood) break;

            // Bracht een nieuwe poging niets bij, dan zit het vermoedelijk niet in de
            // selectors maar in de pagina zelf (een veilingsite zonder prijs). Nog eens
            // vragen kost dan enkel geld.
            if (!improved || round == MaxRounds) break;

            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
            messages.Add(UserMessage(BuildFeedback(check)));
        }

        return new SiteAnalysis(best!, bestCheck!, route, rounds);
    }

    // ==================== de pagina ophalen ====================

    /// <summary>
    /// Probeert de wegen van goedkoop naar duur. Welke weg werkte, bepaalt meteen
    /// hoe de site later gezocht wordt - dat wordt gemeten, niet aan de AI gevraagd.
    /// </summary>
    private static async Task<(string Page, FetchRoute Route)> FetchPageAsync(string url, string word,
        bool forceBridge, IProgress<string>? status, CancellationToken ct)
    {
        if (!forceBridge)
        {
            status?.Report("Pagina ophalen zonder browser...");
            var direct = await TryDirectAsync(url, ct);

            // Rechtstreeks is enkel goed genoeg als de zoekertjes er ook in staan. Bij een
            // site die haar lijst met JavaScript opbouwt, komt er een lege schil terug
            // waarin het zoekwoord hooguit in het zoekveld staat.
            if (direct is not null && !LooksBlocked(direct) &&
                (LooksLikeJson(direct) || CountWord(VisibleText(direct), word) >= 3))
            {
                return (direct, FetchRoute.Direct);
            }

            status?.Report("Pagina openen in een browser...");
            string? viaBrowser = null;

            try
            {
                await using var browser = new BrowserFetcher();
                viaBrowser = FromPre(await browser.GetHtmlAsync(url, null, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write($"analyse: browser mislukt - {ex.Message}");
            }

            if (viaBrowser is not null && !LooksBlocked(viaBrowser))
                return (viaBrowser, FetchRoute.Browser);

            // De browser kwam er niet, maar het gewone verzoek wel - alleen stond het
            // woord er weinig in. Dat is dan nog altijd de beste pagina die we hebben.
            if (viaBrowser is null && direct is not null && !LooksBlocked(direct))
                return (direct, FetchRoute.Direct);

            status?.Report("De site blokkeert de app; via je eigen Chrome proberen...");
        }
        else
        {
            status?.Report("Pagina ophalen via je eigen Chrome...");
        }

        var brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30), status);
        if (brug != BridgeStatus.Ready)
            throw new InvalidOperationException(
                "De site blokkeert de app, en de weg via je eigen Chrome lukt niet: " +
                ChromeLauncher.Describe(brug));

        var viaBridge = FromPre(await BridgeServer.Instance.FetchAsync(url, ct));

        if (LooksBlocked(viaBridge))
            throw new InvalidOperationException(
                "Ook via je eigen Chrome toont deze site een controlepagina in plaats van resultaten. " +
                "Open de site eens gewoon in Chrome, doorloop de controle en probeer opnieuw.");

        return (viaBridge, FetchRoute.Bridge);
    }

    private static async Task<string?> TryDirectAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/json;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "nl-BE,nl;q=0.9,en;q=0.8");

            using var response = await PageHttp.SendAsync(request, timeout.Token);

            // Een 403 of 503 is hier doorgaans bot-detectie; dan is de browser aan zet.
            if (!response.IsSuccessStatusCode)
            {
                Log.Write($"analyse: rechtstreeks {(int)response.StatusCode} voor {url}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Write($"analyse: rechtstreeks mislukt - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Een JSON-API in een tabblad openen geeft geen JSON maar een pagina met de
    /// JSON in een &lt;pre&gt;. Dan halen we die eruit, zodat de site als JSON-bron
    /// herkend wordt in plaats van als een pagina met één lang tekstblok.
    /// </summary>
    private static string FromPre(string page)
    {
        if (LooksLikeJson(page)) return page;
        if (!page.Contains("<pre", StringComparison.OrdinalIgnoreCase)) return page;

        var doc = new HtmlParser().ParseDocument(page);
        var pres = doc.QuerySelectorAll("pre");
        if (pres.Length != 1 || doc.Body is null || doc.Body.Children.Length > 3) return page;

        var inner = pres[0].TextContent.Trim();
        return LooksLikeJson(inner) ? inner : page;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('['))) return false;

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly string[] BlockSignals =
    {
        "accès temporairement restreint",
        "access denied",
        "access to this page has been denied",   // PerimeterX
        "px-captcha",
        "datadome",
        "checking your browser",
        "verifying you are human",
        "just a moment",                          // Cloudflare
        "attention required! | cloudflare",
        "enable javascript and cookies to continue",
        "pardon our interruption",               // Imperva
        "request unsuccessful. incapsula",
        "unusual traffic",
        "captcha"
    };

    /// <summary>
    /// Herkent controlepagina's van bot-beveiligingen zoals Datadome of Cloudflare. Ook de
    /// zoekmotor gebruikt dit, wanneer een pagina geen enkel zoekertje oplevert.
    /// </summary>
    internal static bool LooksBlocked(string page)
    {
        // Een echte resultatenpagina is groot; een blokkadepagina is klein.
        if (page.Length > 60000) return false;

        var lower = page.ToLowerInvariant();
        return BlockSignals.Any(lower.Contains);
    }

    /// <summary>
    /// Bewaart wat de AI te zien kreeg, voor wie wil nakijken waarom een analyse
    /// misliep. Stond vroeger op het bureaublad; daar hoort het niet.
    /// </summary>
    private static void SaveForInspection(string page, bool isJson)
    {
        try
        {
            var folder = AppPaths.Folder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "laatste-analyse" + (isJson ? ".json" : ".html")), page);
        }
        catch (Exception ex)
        {
            Log.Write($"analyse: pagina bewaren mislukt - {ex.Message}");
        }
    }

    // ==================== de pagina klaarmaken voor de AI ====================

    private static string BuildFirstPrompt(string searchUrl, string word, string page, bool isJson, FetchRoute route)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Zoek-URL: {searchUrl}");
        sb.AppendLine($"Voor deze analyse is gezocht op \"{word}\".");
        sb.AppendLine("Opgehaald " + route switch
        {
            FetchRoute.Direct => "met een gewoon verzoek, zonder browser. Wat hier niet in staat, ziet de motor ook niet.",
            FetchRoute.Browser => "met een browser, nadat het JavaScript gedraaid heeft.",
            _ => "via de eigen Chrome van de gebruiker, omdat de site de andere wegen blokkeert."
        });

        string sample;

        if (isJson)
        {
            sample = ShortenJson(page);
            sb.AppendLine("Soort inhoud: JSON. Lijsten zijn ingekort tot vier elementen.");
        }
        else
        {
            var clean = Clean(page, out var title, out var apiHints, out var visibleText);
            sample = PickExcerpt(clean, word, MaxHtmlChars);

            sb.AppendLine("Soort inhoud: HTML. Scripts, stijlen, svg en commentaar zijn weggehaald; " +
                          "lange attribuutwaarden zijn ingekort.");
            if (title.Length > 0) sb.AppendLine($"Paginatitel: {title}");
            sb.AppendLine($"\"{word}\" staat {CountWord(visibleText, word)} keer in de zichtbare tekst.");

            if (sample.Length < clean.Length)
                sb.AppendLine($"De opgeschoonde pagina telt {clean.Length} tekens; je krijgt het stuk rond de zoekresultaten.");

            if (apiHints.Count > 0)
            {
                sb.AppendLine("Sporen van een API of ingebedde data in de scripts:");
                foreach (var hint in apiHints) sb.AppendLine("- " + hint);
            }
        }

        sb.AppendLine();
        sb.AppendLine("<pagina>");
        sb.AppendLine(sample);
        sb.AppendLine("</pagina>");

        return sb.ToString();
    }

    /// <summary>
    /// Haalt alles weg wat voor selectors niets betekent. Wat er overblijft is de
    /// opbouw met zijn klassen en attributen - net wat de AI nodig heeft - en dat
    /// is doorgaans een fractie van de oorspronkelijke pagina.
    /// </summary>
    private static string Clean(string html, out string title, out List<string> apiHints, out string visibleText)
    {
        var doc = new HtmlParser().ParseDocument(html);
        title = Collapse(doc.Title ?? "");
        apiHints = new List<string>();

        // Scripts eerst doorzoeken op sporen van een API, want die zijn stabieler en
        // sneller dan HTML. Daarna mogen ze weg.
        foreach (var script in doc.QuerySelectorAll("script").ToList())
        {
            CollectApiHints(script, apiHints);
            script.Remove();
        }

        foreach (var element in doc.QuerySelectorAll("style, svg, noscript, iframe, link, meta, template, canvas").ToList())
            element.Remove();

        foreach (var comment in doc.Descendants<IComment>().ToList())
            comment.Parent?.RemoveChild(comment);

        foreach (var element in doc.All)
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                var name = attribute.Name;
                var value = attribute.Value;

                if (name == "style" || name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    element.RemoveAttribute(name);
                else if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    element.SetAttribute(name, "data:…");
                else if (value.Length > 300)
                    element.SetAttribute(name, value[..300] + "…");
            }
        }

        visibleText = Collapse(doc.Body?.TextContent ?? "");

        var body = doc.Body?.OuterHtml ?? doc.DocumentElement.OuterHtml;
        return Regex.Replace(Regex.Replace(body, @">\s+<", "><"), @"\s{2,}", " ");
    }

    private static string VisibleText(string html)
    {
        Clean(html, out _, out _, out var text);
        return text;
    }

    private static readonly Regex ApiTrace = new(
        @"(?:https?:)?//[\w.-]+/[^\s""'<>\\]{0,80}?\b(?:api|graphql)\b[^\s""'<>\\]{0,120}" +
        @"|(?<=[""'])/(?:api|graphql)\b[^\s""'<>\\]{0,120}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Adressen die altijd in een pagina staan maar niets met zoekertjes te maken hebben.
    private static readonly string[] NoiseHosts =
    {
        "google", "facebook.net", "doubleclick", "sentry", "datadome", "hotjar",
        "segment", "cookiebot", "onetrust", "didomi", "newrelic", "cloudflareinsights"
    };

    private static void CollectApiHints(IElement script, List<string> hints)
    {
        var text = script.TextContent;

        if (script.Id == "__NEXT_DATA__")
            Add("Next.js: de zoekresultaten staan vermoedelijk ook als JSON in <script id=\"__NEXT_DATA__\">");
        if (text.Contains("__NUXT__"))
            Add("Nuxt: ingebedde data in window.__NUXT__");
        if (text.Contains("__APOLLO_STATE__"))
            Add("Apollo/GraphQL: ingebedde data in __APOLLO_STATE__");
        if ((script.GetAttribute("type") ?? "").Contains("ld+json") && text.Contains("ItemList"))
            Add("JSON-LD met een ItemList");

        foreach (Match match in ApiTrace.Matches(text + " " + (script.GetAttribute("src") ?? "")))
        {
            if (hints.Count >= 12) break;

            var value = match.Value.TrimEnd('\\', ',', ';', ')');
            if (NoiseHosts.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

            Add(value.Length > 200 ? value[..200] : value);
        }

        void Add(string hint)
        {
            if (hints.Count < 12 && !hints.Contains(hint)) hints.Add(hint);
        }
    }

    /// <summary>
    /// Kiest het stuk van de pagina waar het zoekwoord het dichtst op elkaar staat:
    /// daar zit de resultatenlijst. Het eerste voorkomen nemen, zoals vroeger, landde
    /// vaak in het zoekveld of het menu.
    /// </summary>
    private static string PickExcerpt(string text, string word, int max)
    {
        if (text.Length <= max) return text;

        var start = text.Length / 4;
        var positions = WordPattern(word)?.Matches(text).Select(m => m.Index).ToList() ?? new List<int>();

        if (positions.Count > 0)
        {
            var window = (int)(max * 0.8);
            int bestIndex = 0, bestCount = 0, j = 0;

            for (var i = 0; i < positions.Count; i++)
            {
                while (j < positions.Count && positions[j] - positions[i] <= window) j++;
                if (j - i > bestCount)
                {
                    bestCount = j - i;
                    bestIndex = i;
                }
            }

            start = Math.Max(0, positions[bestIndex] - max / 10);
        }

        start = Math.Clamp(start, 0, text.Length - max);

        // Op het begin van een tag laten starten, niet midden in een attribuut.
        var tag = text.LastIndexOf('<', start);
        if (tag >= 0 && start - tag < 2000) start = tag;

        var length = Math.Min(max, text.Length - start);

        return (start > 0 ? "[... begin van de pagina weggelaten ...]\n" : "") +
               text.Substring(start, length) +
               (start + length < text.Length ? "\n[... rest van de pagina weggelaten ...]" : "");
    }

    /// <summary>
    /// Een JSON-antwoord met honderd zoekertjes bestaat uit honderd keer dezelfde
    /// opbouw. Vier volstaan om de paden te zien, en de rest kost enkel geld.
    /// </summary>
    private static string ShortenJson(string json)
    {
        string text;

        try
        {
            var node = JsonNode.Parse(json);
            Prune(node);
            text = node?.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) ?? json;
        }
        catch (JsonException)
        {
            text = json;
        }

        return text.Length <= MaxJsonChars ? text : text[..MaxJsonChars] + "…";
    }

    private static void Prune(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            while (array.Count > 4) array.RemoveAt(array.Count - 1);
            foreach (var item in array) Prune(item);
        }
        else if (node is JsonObject obj)
        {
            foreach (var property in obj) Prune(property.Value);
        }
    }

    private static Regex? WordPattern(string word) =>
        string.IsNullOrWhiteSpace(word)
            ? null
            : new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word.Trim())}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int CountWord(string text, string word) => WordPattern(word)?.Matches(text).Count ?? 0;

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

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
        "largeImageSelector", "pageTemplate", "notes"
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
        properties["firstPage"] = new JsonObject { ["type"] = "integer" };

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

    private static async Task<(JsonNode Content, string Text)> AskClaudeAsync(string apiKey, JsonArray messages,
        CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 16000,
            ["system"] = SystemPrompt,

            // Bij een verbeterronde gaat dezelfde pagina opnieuw mee. Uit de cache
            // gelezen kost die een tiende van de prijs.
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },

            // Weigert het model een pagina (dat kan bij een beveiligingsfilter), dan
            // neemt de API zelf een ander model in plaats van niets terug te geven.
            ["fallbacks"] = "default",

            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = BuildSchema() }
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

    /// <summary>
    /// Alles wat we geleerd hebben bij het inregelen van de bestaande sites, voor
    /// zover het voor de AI iets betekent. Wat de app zelf meet (welke weg, JSON of
    /// HTML) staat hier niet in: dat wordt niet gevraagd maar vastgesteld.
    /// </summary>
    private const string SystemPrompt = """
        Je helpt een Windows-app die tweedehands- en veilingsites doorzoekt. Voor elke site bestaat een
        beschrijving met selectors, en een generieke motor leest daarmee de zoekresultaten uit. Je krijgt
        de zoekpagina van een nieuwe site en bepaalt die selectors. De app voert je antwoord daarna uit op
        dezelfde pagina en telt wat eruit komt; klopt het niet, dan krijg je die telling terug.

        ## Hoe de motor een HTML-pagina leest
        - AngleSharp met gewone CSS-selectors. Geen XPath en geen jQuery-uitbreidingen zoals :contains() of :eq().
        - itemSelector vindt elk zoekertje precies één keer: de kaart, niet de lijst eromheen en niet een stuk
          binnenin. De andere selectors zijn relatief aan dat element en nemen het eerste element dat past.
        - Standaard neemt de motor de tekst. Met @attribuut neemt hij een attribuut: "a.title@href", "img@src".
          "." is het zoekertje zelf: ".@aria-label", ".@data-id".
        - Een komma werkt zoals in CSS en vangt twee opbouwvormen van dezelfde kaart op, bijvoorbeeld
          "[data-testid='dealer-address'], [data-testid='private-seller-address']" wanneer handelaars en
          particulieren anders getoond worden.
        - "::replace(oud,nieuw)" achter een selector vervangt achteraf een stukje in de gevonden waarde. Geen
          komma's of haakjes in oud en nieuw; meerdere na elkaar mag.
        - "::match(patroon)" achter een selector houdt enkel over wat in groep 1 van dat patroon staat (een
          .NET-reguliere expressie). Daarmee knip je iets uit een langere tekst waar geen apart element voor
          bestaat: "Rijksweg 2, 9681 Maarkedal, België" wordt "Maarkedal", "van Nederland" wordt "Nederland".
          Past het patroon niet, dan blijft het veld leeg - zo pik je één soort regel uit een rij die er
          hetzelfde uitziet. Het staat altijd achteraan, want een patroon mag komma's en haakjes bevatten.
        - Vraag je @src en is dat leeg, dan probeert de motor zelf data-src, en omgekeerd.
        - Witruimte wordt samengevoegd.
        - Een relatieve link of foto ("/pad", "pad" of "//host/pad") maakt de motor zelf volledig met baseUrl;
          een schuine streep vooraan wordt daarbij vanzelf opgevangen, dus daar is geen ::replace voor nodig.

        ## Per veld
        - titleSelector: zoekertjes zonder titel vallen weg.
        - urlSelector: de link naar het zoekertje. Dit is ook de identiteit waarmee de app dubbels herkent. Is
          hij leeg, dan valt de app terug op de titel en verdwijnen zoekertjes met dezelfde titel - bij een
          platenwinkel bleven er zo 73 van de 100 over. Staat er geen href in de pagina maar draagt de kaart
          een id (data-guid, data-id), neem dan dat attribuut en kies baseUrl zo dat baseUrl + "/" + id naar
          het zoekertje leidt. Zeg dat in notes, want dat moet nagekeken worden.
        - priceSelector: het element met enkel de prijs. De motor neemt het eerste getal uit de tekst
          ("€ 1.499,00" wordt 1499), dus een element waarin ook "3 x" of een verzendprijs staat geeft een
          verkeerd bedrag. Een verborgen element voor schermlezers (sr-only) is prima. Staat er een leeg vakje
          dat pas met JavaScript gevuld wordt, geef de selector toch en zeg het in notes.
        - dateSelector: de motor gebruikt DateTime.TryParse, dus liefst "time@datetime". Leeg laten als er
          enkel "vandaag" of "2 uur geleden" staat.
        - imageSelector: de miniatuur.
        - largeImageSelector: een grotere versie, als die af te leiden is. Veel sites zetten het formaat in
          het pad van de foto: s-l500 wordt s-l1600, 250x188 wordt 1024x768, _S.webp wordt _L.webp,
          cw_lot_card_ext wordt cw_large. Schrijf dan de fotoselector met ::replace erachter en kies een
          stukje dat uniek is in de URL (_S.webp, niet _S). Een srcset enkel als er één URL in staat. AVIF kan
          de app niet tonen; kies binnen <picture> de JPEG- of WebP-bron. Leeg laten als het niet uit de
          pagina af te leiden is - verzin geen formaten.
        - descriptionSelector: als hij er staat.
        - locationSelector: de app zet dit naast de prijs en wil daar enkel de naam van de stad zien, en
          anders die van het land. Staat er meer in hetzelfde element - een straat, een postcode, een land,
          of "van Nederland" - knip dat er dan af met ::match.

        ## Stabiele selectors
        Sites bouwen hun opmaak bij elke update opnieuw. Kies bij voorkeur data-testid en andere
        data-attributen, dan aria-label, dan betekenisvolle klassen (c-lot-card__title) en semantische tags.
        Klassen met een willekeurig achtervoegsel (styles_adCard__9GEgr, css-1q2w3e, sc-bdVaJa) veranderen bij
        de volgende update; gebruik ze enkel als er niets anders is, en zeg het dan in notes. Laat
        advertenties en gesponsorde kaarten buiten de itemSelector als ze te onderscheiden zijn.

        ## JSON
        Is de inhoud JSON, dan zijn het puntpaden: itemSelector "listings", priceSelector
        "priceInfo.priceCents". Er zijn geen indexen: kom je onderweg een lijst tegen, dan neemt de motor het
        eerste element, dus "pictures.largeUrl" werkt ook als pictures een lijst is. @ en komma's bestaan hier
        niet, ::replace wel. priceInCents is true wanneer het bedrag in centen staat.

        ## Vervolgpagina's
        pageTemplate is het stukje dat achter de zoek-URL komt voor pagina 2 en verder, met {page} als
        plaatshouder: "&page={page}", "&_pgn={page}". Zoek het in de links van de paginering. firstPage is het
        nummer van de eerste pagina, meestal 1. Laat pageTemplate leeg als het nummer in het pad zit, als de
        site met een offset werkt, of als je geen paginering ziet.

        ## Namen
        name: de naam van de site zoals ze zichzelf noemt. shortName: enkel als name langer is dan een
        twaalftal tekens, een kortere vorm voor een tabblad; anders leeg. baseUrl: schema en host
        (https://www.site.be), tenzij je meer nodig hebt voor een link die uit een id gebouwd wordt.

        ## notes
        Een paar zinnen in het Nederlands voor wie de site later onderhoudt: wat onzeker is en wat opviel.
        Bijvoorbeeld dat de prijs pas met JavaScript verschijnt, dat maar een deel van de zoekertjes in de
        pagina staat omdat de lijst meeschuift met het scrollen, of dat er klassen met willekeurige
        achtervoegsels gebruikt zijn. Een JSON-API is stabieler en sneller dan HTML: staan er sporen van in de
        meegestuurde lijst, noem dan de meest waarschijnlijke.

        Laat een veld leeg ("") wanneer het gegeven niet op de pagina staat.
        """;
}

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

/// <summary>
/// Wat de vier Detail-velden opleverden op de pagina van één echt zoekertje. Net als bij
/// de zoekpagina is dit een telling met de echte motor en geen schatting.
/// </summary>
public sealed record DetailCheck(string Url, int Images, string Seller, string Posted,
    int DescriptionLength, IReadOnlyList<string> Problems)
{
    public bool IsGood => Problems.Count == 0;

    public string Summary
    {
        get
        {
            var delen = new List<string> { $"{Images} foto's" };
            if (Seller.Length > 0) delen.Add($"verkoper \"{Seller}\"");
            if (Posted.Length > 0) delen.Add($"online sinds \"{Posted}\"");
            if (DescriptionLength > 0) delen.Add($"beschrijving {DescriptionLength} tekens");

            return "Op de pagina van één zoekertje: " + string.Join(", ", delen) + ".";
        }
    }
}

/// <summary>Het resultaat van een analyse: de definitie, de telling en hoe het ging.</summary>
public sealed record SiteAnalysis(SiteDefinition Definition, AnalysisCheck Check, FetchRoute Route, int Rounds)
{
    /// <summary>
    /// Wat de pagina van één zoekertje opleverde, of null wanneer die niet bekeken kon
    /// worden - er was geen bruikbare link, of de pagina kwam niet binnen.
    /// </summary>
    public DetailCheck? Detail { get; init; }
}

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

    /// <summary>
    /// De advertentiepagina krijgt minder ruimte dan de zoekpagina: daar moet een hele
    /// resultatenlijst in, hier gaat het om één advertentie. Samen met de datablokken
    /// hieronder is dat zo'n 45 000 tokens, ruwweg een kwartje per analyse.
    /// </summary>
    private const int MaxDetailChars = 80_000;

    /// <summary>
    /// Een ld+json- of __NEXT_DATA__-blok kan honderdduizenden tekens groot zijn, terwijl
    /// het er enkel om gaat dát het er staat en hoe de velden erin heten.
    /// </summary>
    private const int MaxBlockChars = 6_000;

    /// <summary>Eerste voorstel voor de advertentiepagina plus hoogstens één verbetering.</summary>
    private const int DetailRounds = 2;

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

            var (content, answer) = await AskClaudeAsync(apiKey, messages, SystemPrompt, BuildSchema(), ct);
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

        // De zoekpagina geeft één foto en zelden meer dan een titel en een prijs. Alles
        // wat je daarna wil weten - de andere foto's, wie het verkoopt, hoelang het er
        // staat, de volledige beschrijving - staat op de pagina van het zoekertje zelf.
        // Alle dertien sites die met de hand ingeregeld zijn, hebben die velden nodig
        // (foto's bij dertien, beschrijving bij tien, verkoper en datum bij negen), dus
        // de analyse kijkt daar nu zelf naar in plaats van het achteraf te laten invullen.
        var detail = await AdvertentieAsync(apiKey, best!, bestCheck!, route, status, ct);

        return new SiteAnalysis(best!, bestCheck!, route, rounds) { Detail = detail };
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

    // internal om dezelfde reden als SchoonAdvertentie: zo meet het wegwerpprojectje de
    // toestemmingsmuur na op de echte site in plaats van op een nabootsing.
    internal static async Task<string?> TryDirectAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            HttpRequestMessage Verzoek()
            {
                var v = new HttpRequestMessage(HttpMethod.Get, url);
                v.Headers.TryAddWithoutValidation("Accept", "text/html,application/json;q=0.9,*/*;q=0.8");
                v.Headers.TryAddWithoutValidation("Accept-Language", "nl-BE,nl;q=0.9,en;q=0.8");
                return v;
            }

            var response = await PageHttp.SendAsync(Verzoek(), timeout.Token);

            // Een toestemmingsmuur op een ander domein - bij Tweakers myprivacy.dpgmedia.nl -
            // zet een sessiecookie en laat het VOLGENDE verzoek gewoon door. PageHttp houdt
            // zijn koekjes bij, dus één keer opnieuw volstaat. Dezelfde regel staat al in
            // GenericSource en DetailFetcher; deze plaats ontbrak, en dan analyseert de app
            // de muur in plaats van de site. Gemeten op 29 september 2026: de advertentie-
            // pagina van Tweakers kwam binnen als "DPG Media Privacy Gate", 517 tekens,
            // 0 foto's.
            var gevraagd = new Uri(url).Host;
            var beland = response.RequestMessage?.RequestUri?.Host;

            if (!string.Equals(beland, gevraagd, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"analyse: kwam op {beland} uit in plaats van {gevraagd}; één keer opnieuw");
                response.Dispose();
                response = await PageHttp.SendAsync(Verzoek(), timeout.Token);
            }

            using (response)
            {
                // Een 403 of 503 is hier doorgaans bot-detectie; dan is de browser aan zet.
                if (!response.IsSuccessStatusCode)
                {
                    Log.Write($"analyse: rechtstreeks {(int)response.StatusCode} voor {url}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync(timeout.Token);
            }
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
    internal static string Clean(string html, out string title, out List<string> apiHints, out string visibleText)
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

    // ==================== de pagina van één zoekertje ====================

    /// <summary>Wat de AI over de advertentiepagina terugstuurt.</summary>
    internal sealed record DetailProposal(string Images, string Seller, string Posted,
        string Description, string Notes);

    /// <summary>
    /// Opent het eerste zoekertje dat de motor gevonden heeft en laat de AI de vier
    /// Detail-velden bepalen. Een aparte vraag en geen extra velden bij de eerste, want
    /// het is een andere pagina: de AI heeft de advertentie nooit gezien, en een galerij
    /// ziet er bij elke site anders uit.
    ///
    /// Gaat hier iets mis, dan blijft het bij een regel in het logboek. Dit is een extra
    /// bovenop een analyse die al gelukt is, en het mag die nooit onderuit halen.
    /// </summary>
    private async Task<DetailCheck?> AdvertentieAsync(string apiKey, SiteDefinition def, AnalysisCheck check,
        FetchRoute route, IProgress<string>? status, CancellationToken ct)
    {
        var adres = check.Sample
            .Select(l => l.Url)
            .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                                 (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

        if (adres is null)
        {
            Log.Write("analyse: geen bruikbare link naar een zoekertje, dus geen advertentiepagina bekeken");
            return null;
        }

        var basisNotes = def.Notes;

        try
        {
            status?.Report("De pagina van één zoekertje ophalen...");
            var pagina = await HaalViaRouteAsync(adres, route, ct);

            var messages = new JsonArray { UserMessage(BuildDetailPrompt(def, adres, pagina)) };
            DetailCheck? beste = null;

            for (var ronde = 1; ronde <= DetailRounds; ronde++)
            {
                status?.Report(ronde == 1
                    ? "De AI bekijkt de advertentie..."
                    : "De AI verbetert de velden van de advertentie...");

                var (content, answer) = await AskClaudeAsync(apiKey, messages, DetailPrompt, BuildDetailSchema(), ct);
                var voorstel = ReadDetail(answer);

                status?.Report("De velden natellen op de advertentie...");
                var meting = await MeasureDetailAsync(voorstel, adres, pagina, ct);

                Log.Write($"analyse {def.Name}: advertentiepagina poging {ronde} -> {meting.Summary}" +
                          (meting.IsGood ? "" : " | " + string.Join(" | ", meting.Problems)));

                if (beste is null || meting.Problems.Count < beste.Problems.Count)
                {
                    beste = meting;
                    Apply(def, voorstel, basisNotes);
                }

                if (meting.IsGood || ronde == DetailRounds) break;

                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                messages.Add(UserMessage(BuildDetailFeedback(meting)));
            }

            return beste;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"analyse: de advertentiepagina lukte niet - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Zet het voorstel in de definitie. De verkoper krijgt daarbij een uitzondering:
    /// staat hij al op de zoekpagina, dan hoeft die pagina er niet voor opgehaald te
    /// worden en staat de naam er meteen.
    /// </summary>
    private static void Apply(SiteDefinition def, DetailProposal voorstel, string basisNotes)
    {
        def.DetailImagesSelector = voorstel.Images;
        def.DetailSellerSelector = string.IsNullOrWhiteSpace(def.SellerSelector) ? voorstel.Seller : "";
        def.DetailPostedSelector = voorstel.Posted;
        def.DetailDescriptionSelector = voorstel.Description;

        // Vanuit basisNotes opgebouwd en niet aangevuld: bij een tweede ronde zou de
        // tekst er anders twee keer onder staan.
        def.Notes = string.IsNullOrWhiteSpace(voorstel.Notes)
            ? basisNotes
            : string.IsNullOrWhiteSpace(basisNotes) ? voorstel.Notes : basisNotes.TrimEnd() + "\n\n" + voorstel.Notes;
    }

    /// <summary>
    /// Eén pagina ophalen langs de weg die voor deze site al gemeten is. Geen proberen
    /// en terugvallen zoals bij de zoekpagina: welke weg werkt, weten we hier al.
    /// </summary>
    private static async Task<string> HaalViaRouteAsync(string url, FetchRoute route, CancellationToken ct)
    {
        switch (route)
        {
            case FetchRoute.Direct:
                return await TryDirectAsync(url, ct)
                       ?? throw new InvalidOperationException("de pagina kwam niet binnen");

            case FetchRoute.Browser:
                await using (var browser = new BrowserFetcher())
                    return FromPre(await browser.GetHtmlAsync(url, null, ct));

            default:
                var brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30), null);
                if (brug != BridgeStatus.Ready)
                    throw new InvalidOperationException(ChromeLauncher.Describe(brug));

                return FromPre(await BridgeServer.Instance.FetchAsync(url, ct));
        }
    }

    private static string BuildDetailPrompt(SiteDefinition def, string adres, string pagina)
    {
        var schoon = SchoonAdvertentie(pagina, out var titel, out var blokken);
        if (schoon.Length > MaxDetailChars) schoon = schoon[..MaxDetailChars];

        var sb = new StringBuilder();
        sb.AppendLine($"Site: {def.Name}");
        sb.AppendLine($"Dit is de pagina van één zoekertje: {adres}");
        if (titel.Length > 0) sb.AppendLine($"Titel van de pagina: {titel}");
        sb.AppendLine();

        sb.AppendLine(string.IsNullOrWhiteSpace(def.SellerSelector)
            ? "De zoekpagina van deze site geeft de verkoper niet, dus die mag van deze pagina komen."
            : $"De verkoper staat al op de zoekpagina ({def.SellerSelector}), dus laat " +
              "detailSellerSelector leeg.");

        if (blokken.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Datablokken op deze pagina, apart gezet omdat ze meestal de beste bron zijn " +
                          "(ingekort, en de selector staat erbij):");

            foreach (var (selector, inhoud) in blokken)
            {
                sb.AppendLine();
                sb.AppendLine(selector);
                sb.AppendLine(inhoud);
            }
        }

        sb.AppendLine();
        sb.AppendLine("De pagina, opgeschoond:");
        sb.AppendLine(schoon);

        return sb.ToString();
    }

    /// <summary>
    /// Opschonen voor de pagina van één zoekertje. Twee dingen gaan hier anders dan bij
    /// <see cref="Clean"/>, en dat is de kern van deze stap:
    ///
    /// - <c>application/ld+json</c> en <c>__NEXT_DATA__</c> blijven staan. Vier van de
    ///   dertien bestaande sites halen hun foto's, verkoper of datum daaruit, want zo'n
    ///   blok overleeft een opmaakwijziging en een klassenaam niet. Bij 2dehands staat
    ///   élke foto daarin terwijl de HTML er maar één toont.
    /// - De <c>head</c> blijft staan, want daar staat dat blok bij 2dehands en
    ///   Marktplaats. De gewone opschoning geeft enkel de body terug, en dan is het
    ///   onvindbaar - hoe goed de AI ook kijkt.
    ///
    /// De blokken komen apart terug, zodat ze niet wegvallen wanneer de pagina afgekapt
    /// wordt: ze staan vaak helemaal achteraan.
    /// </summary>
    // internal en niet private: een wegwerpprojectje meet dit na op een echte
    // advertentiepagina, en dat kan niet met de app erbij. Zie CLAUDE.md, "Controles".
    internal static string SchoonAdvertentie(string html, out string title,
        out List<(string Selector, string Inhoud)> blokken)
    {
        var doc = new HtmlParser().ParseDocument(html);
        title = Collapse(doc.Title ?? "");
        blokken = new List<(string, string)>();

        foreach (var script in doc.QuerySelectorAll("script").ToList())
        {
            var type = script.GetAttribute("type") ?? "";
            var id = script.GetAttribute("id") ?? "";

            var selector = type.Contains("ld+json", StringComparison.OrdinalIgnoreCase)
                ? "script[type='application/ld+json']"
                : id is "__NEXT_DATA__" or "__NUXT_DATA__"
                    ? $"script[id='{id}']"
                    : null;

            if (selector is not null)
            {
                var inhoud = Collapse(script.TextContent);
                if (inhoud.Length > MaxBlockChars) inhoud = inhoud[..MaxBlockChars] + "…";
                if (inhoud.Length > 0) blokken.Add((selector, inhoud));
            }

            script.Remove();
        }

        foreach (var element in doc.QuerySelectorAll("style, svg, noscript, iframe, link, template, canvas").ToList())
            element.Remove();

        // De og:-tags blijven: daar staat vaak de grote foto van de advertentie in.
        foreach (var meta in doc.QuerySelectorAll("meta").ToList())
        {
            var soort = meta.GetAttribute("property") ?? meta.GetAttribute("name") ?? "";
            if (!soort.StartsWith("og:", StringComparison.OrdinalIgnoreCase)) meta.Remove();
        }

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

        var heel = doc.DocumentElement?.OuterHtml ?? doc.Body?.OuterHtml ?? "";
        return Regex.Replace(Regex.Replace(heel, @">\s+<", "><"), @"\s{2,}", " ");
    }

    /// <summary>
    /// Voert de vier selectors uit op diezelfde pagina, met dezelfde motor die het
    /// detailvenster later gebruikt.
    /// </summary>
    internal static async Task<DetailCheck> MeasureDetailAsync(DetailProposal v, string adres, string pagina,
        CancellationToken ct)
    {
        var problems = new List<string>();

        var fotos = v.Images.Length == 0
            ? new List<string>()
            : await GenericSource.ReadFieldsAsync(pagina, v.Images, ct);

        // Enkel wat op een adres lijkt telt mee. Een ::match dat naast de foto's grijpt,
        // levert anders brokstukken tekst op die er in een telling goed uitzien.
        var geldig = fotos.Where(LijktOpAdres).ToList();

        var verkoper = await LeesAsync(v.Seller);
        var sinds = await LeesAsync(v.Posted);
        var beschrijving = await LeesAsync(v.Description);

        if (v.Images.Length == 0)
        {
            problems.Add("Er is geen fotoselector voorgesteld. Elke advertentie heeft foto's, en dit is " +
                         "het veld waar de app ze haalt.");
        }
        else if (geldig.Count == 0)
        {
            problems.Add(fotos.Count == 0
                ? "De fotoselector levert niets op deze pagina op."
                : $"De fotoselector levert {fotos.Count} waarden op, maar geen enkele ziet eruit als het " +
                  "adres van een foto.");
        }

        Klaagt(v.Seller, verkoper, "verkoperselector");
        Klaagt(v.Posted, sinds, "selector voor 'online sinds'");
        Klaagt(v.Description, beschrijving, "beschrijvingsselector");

        return new DetailCheck(adres, geldig.Count, verkoper, sinds, beschrijving.Length, problems);

        async Task<string> LeesAsync(string selector) =>
            selector.Length == 0 ? "" : await GenericSource.ReadFieldAsync(pagina, selector, ct);

        void Klaagt(string selector, string waarde, string naam)
        {
            if (selector.Length > 0 && waarde.Length == 0)
                problems.Add($"De {naam} levert niets op deze pagina op. Verbeter hem, of laat het veld " +
                             "leeg als dit gegeven er niet staat.");
        }
    }

    private static bool LijktOpAdres(string waarde) =>
        waarde.Length > 10 && waarde.Contains('/') &&
        (waarde.StartsWith("http", StringComparison.OrdinalIgnoreCase) || waarde.StartsWith('/'));

    private static string BuildDetailFeedback(DetailCheck check)
    {
        var sb = new StringBuilder();

        sb.AppendLine("De app heeft je voorstel uitgevoerd op diezelfde pagina, met de motor. Dit kwam eruit:");
        sb.AppendLine($"- foto's die op een adres lijken: {check.Images}");
        sb.AppendLine($"- verkoper: \"{check.Seller}\"");
        sb.AppendLine($"- online sinds: \"{check.Posted}\"");
        sb.AppendLine($"- beschrijving: {check.DescriptionLength} tekens");
        sb.AppendLine();
        sb.AppendLine("Wat niet klopt:");
        foreach (var problem in check.Problems) sb.AppendLine("- " + problem);
        sb.AppendLine();
        sb.AppendLine("Verbeter enkel wat niet klopt en laat staan wat werkt.");

        return sb.ToString();
    }

    private static readonly string[] DetailFields =
    {
        "detailImagesSelector", "detailSellerSelector", "detailPostedSelector",
        "detailDescriptionSelector", "notes"
    };

    private static JsonObject BuildDetailSchema()
    {
        var properties = new JsonObject();
        foreach (var field in DetailFields) properties[field] = new JsonObject { ["type"] = "string" };

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

    private static DetailProposal ReadDetail(string answer)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(answer);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("de AI gaf geen bruikbaar antwoord over de advertentiepagina");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return new DetailProposal(
                Get(root, "detailImagesSelector"),
                Get(root, "detailSellerSelector"),
                Get(root, "detailPostedSelector"),
                Get(root, "detailDescriptionSelector"),
                Get(root, "notes"));
        }
    }

    /// <summary>
    /// De opdracht voor de advertentiepagina. Staat los van <see cref="SystemPrompt"/>:
    /// het is een andere pagina met andere velden, en alles door elkaar zetten maakt
    /// allebei de vragen wolliger.
    /// </summary>
    private const string DetailPrompt = """
        Je helpt een Windows-app die tweedehands- en veilingsites doorzoekt. De zoekpagina van deze site is
        al ingeregeld. Nu krijg je de pagina van één zoekertje, en jij bepaalt vier velden die de app daar
        later uit haalt wanneer iemand op dat zoekertje dubbelklikt.

        ## De vier velden
        - detailImagesSelector: ALLE foto's van deze advertentie. De zoekpagina geeft er één; een advertentie
          heeft er vijf of tien, en juist op die andere staat wat je zoekt - het label achteraan, de doos van
          binnen, de krassen. Hier telt élke treffer mee en niet enkel de eerste; dubbels en lege waarden
          gooit de app zelf weg.
        - detailSellerSelector: de naam van de verkoper.
        - detailPostedSelector: sinds wanneer het online staat, als TEKST ("Sinds 24 sep. '26",
          "Eergisteren", "Vandaag"). Er wordt niets uitgerekend: elke site schrijft het anders op, en wat de
          site zelf toont klopt altijd met wat een bezoeker ziet.
        - detailDescriptionSelector: het element met de volledige beschrijving.

        ## Dezelfde notatie als op de zoekpagina
        Gewone CSS-selectors, @attribuut voor een attribuut, en ::replace(oud,nieuw) en ::match(patroon)
        erachter. ::match houdt over wat in groep 1 staat, of de hele treffer als er geen haakjes in staan;
        past het patroon niet, dan blijft het veld leeg. Dit geldt ook wanneer de zoekpagina van deze site
        JSON was: een advertentiepagina is gewone HTML.

        ## Een datablok is bijna altijd de beste bron
        Staat er hierboven een blok application/ld+json of __NEXT_DATA__ bij, kijk daar dan eerst. Zulke
        blokken zijn een webstandaard die ook Google leest, en ze overleven een opmaakwijziging; een
        klassenaam niet. Bij vier van de dertien sites die deze app al kent, komen de foto's daaruit, en bij
        twee ervan staat in de HTML zelf maar één foto terwijl alle foto's in dat blok staan. Twee dingen:
        - Schuine strepen staan er vaak als /, want het blok is JSON. Zet er dan ::replace(/,/)
          VOOR het patroon; ::replace gaat altijd eerst.
        - Eén element, veel treffers: met ::match levert dat ene blok élke treffer op. Zo haal je alle foto's
          uit één script. Twee vormen die in de praktijk werken:
          script[type='application/ld+json']::replace(/,/)::match(https://images\.site\.com/[A-Za-z0-9/._-]+)
          script[id='__NEXT_DATA__']::match("companyName":"([^"]+)")

        ## De grootste foto, en wanneer je er vanaf moet blijven
        - Laat de query weg als dat het origineel geeft: ::match(^[^?]+). Bij twee sites gaf de CDN zónder
          ?rule=... het origineel, en dat was het verschil tussen 726 en 1600 beeldpunten - en tussen 1 en 8
          foto's, want mét de query waren varianten van dezelfde foto verschillende teksten en viel de
          ontdubbeling in het water.
        - Staat het formaat in het pad, dan mag ::replace: /1280x960.webp wordt /2048x1536.webp,
          cw_ldp_l wordt cw_large. Kies een stukje dat uniek is in het adres.
        - MAAR is het adres ondertekend - een parameter als oh=, oe=, s=, sig= of stp=, of een hash in het
          pad - laat de maat dan staan. Die handtekening dekt het formaat mee, dus een groter formaat geeft
          403 in plaats van een grotere foto. Bij twee sites is dat nagemeten.
        - Het grootste is niet altijd het beste: een origineel van 5694x3202 en 1,65 MB is onbruikbaar in
          een fotostrook die er vijftig laadt. Rond de 2000 beeldpunten is ruim genoeg.
        - AVIF kan de app niet tonen; kies binnen <picture> de JPEG- of WebP-bron.

        ## Wat je leeg laat
        - Een veld dat niet op deze pagina staat. Liever leeg dan een selector die iets anders grijpt: het
          detailvenster toont dan gewoon wat er wel is.
        - detailSellerSelector wanneer hierboven staat dat de verkoper al van de zoekpagina komt.
        - Pas op bij "online sinds": sites zetten drie tellers naast elkaar ("7x bekeken", "0x bewaard",
          "Sinds 24 sep. '26"). Kies die niet met :nth-child - dat valt om bij de volgende opmaakwijziging -
          maar met ::match op het woord dat erin hoort te staan. De motor neemt dan het eerste element waar
          dat patroon ook écht op past.

        ## notes
        Eén of twee zinnen in het Nederlands over wat onzeker is aan deze vier velden. Laat leeg als er
        niets bijzonders aan is.
        """;

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

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
/// De pagina te pakken krijgen en klaarmaken voor de AI.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
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
                // Via de pool, niet een eigen browser: er kan een zoekopdracht lopen, en
                // twee Chrome's op dezelfde profielmap botsen.
                using var lening = BrowserPool.Lease();
                viaBrowser = FromPre(await BrowserPool.Get().GetHtmlAsync(url, null, ct));
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
}

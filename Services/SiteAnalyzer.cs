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

/// <summary>
/// Of de paginering echt een tweede pagina oplevert. Gemeten en niet geloofd: een site die
/// het paginanummer negeert, geeft gewoon pagina 1 terug en meldt geen enkele fout.
/// </summary>
public sealed record PagingCheck(bool Supported, int Page2, int Overlap, string? Problem)
{
    public string Summary => !Supported
        ? "Geen paginering gevonden: de app leest enkel de eerste pagina."
        : Problem
          ?? $"Paginering werkt: pagina 2 gaf {Page2} zoekertjes, waarvan er {Overlap} ook op pagina 1 stonden.";
}

/// <summary>Wat één voorgesteld filter deed toen de app het echt uitprobeerde.</summary>
public sealed record FilterResult(string Key, string Fragment, string Value, int Results, int Shared,
    bool Kept, string Reason);

/// <summary>
/// Het oordeel over alle voorgestelde filters samen. Filters zijn het enige deel van een
/// sitebeschrijving dat je niet kán zien: een parameter die er goed uitziet, kan door de
/// site aanvaard en meteen genegeerd worden. Daarom wordt elk voorstel uitgevoerd en
/// vergeleken met de ongefilterde eerste pagina.
/// </summary>
public sealed record FilterCheck(bool Reliable, int Proposed, int Kept, string? Warning,
    IReadOnlyList<FilterResult> Results)
{
    /// <summary>
    /// Hoeveel de resultaten al verschuiven zonder dat je iets filtert. Op een drukke site
    /// komen er tussen twee verzoeken zoekertjes bij en gaan er weg; die ruis bepaalt
    /// vanaf wanneer een verschil iets betekent.
    /// </summary>
    public double Noise { get; init; }

    public string Summary => !Reliable
        ? Warning ?? "De filters zijn niet na te meten op deze site."
        : Proposed == 0
            ? "Geen filters gevonden om na te meten."
            : $"Filters: {Kept} van de {Proposed} voorstellen doen echt iets; de rest is eruit " +
              $"(deze site verschuift al {Noise:P0} vanzelf).";
}

/// <summary>Het resultaat van een analyse: de definitie, de telling en hoe het ging.</summary>
public sealed record SiteAnalysis(SiteDefinition Definition, AnalysisCheck Check, FetchRoute Route, int Rounds)
{
    /// <summary>
    /// Wat de pagina van één zoekertje opleverde, of null wanneer die niet bekeken kon
    /// worden - er was geen bruikbare link, of de pagina kwam niet binnen.
    /// </summary>
    public DetailCheck? Detail { get; init; }

    /// <summary>Wat pagina 2 opleverde. Null wanneer er niet naar gekeken kon worden.</summary>
    public PagingCheck? Paging { get; init; }

    /// <summary>
    /// Waarom de grote foto eruit gehaald is, of null wanneer ze werkt. Een selector kan
    /// er goed uitzien en toch een adres opleveren dat de fotodienst weigert.
    /// </summary>
    public string? LargeImageProblem { get; init; }

    /// <summary>Wat er van de voorgestelde filters overbleef na het natellen.</summary>
    public FilterCheck? Filters { get; init; }

    /// <summary>
    /// De regel uit robots.txt die dit zoekpad verbiedt, of null. De app dwingt niets af -
    /// dat is een keuze van wie de site toevoegt - maar het hoort wel op het scherm.
    /// </summary>
    public string? RobotsRule { get; init; }
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

        // Het antwoord dat bij "best" hoort, want daar staan ook de filters in. Niet het
        // laatste antwoord: een latere ronde kan slechter zijn en wordt dan verworpen.
        string? besteAntwoord = null;

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
                besteAntwoord = answer;
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
        await ControleerPaginaEenAsync(best!, searchUrl, testQuery, route, status, ct);

        var grote = await ControleerGroteFotoAsync(best!, bestCheck!, status, ct);
        var paging = await ControleerPaginaTweeAsync(best!, searchUrl, page, testQuery, route, status, ct);

        var filters = await FiltersAsync(best!, besteAntwoord, url, page, route, status, ct);
        var robots = await RobotsAsync(url, ct);
        var detail = await AdvertentieAsync(apiKey, best!, bestCheck!, route, status, ct);

        return new SiteAnalysis(best!, bestCheck!, route, rounds)
        {
            Detail = detail,
            Paging = paging,
            RobotsRule = robots,
            LargeImageProblem = grote,
            Filters = filters
        };
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

    // ==================== de zoek-URL en de paginering ====================

    /// <summary>
    /// Dezelfde site? Een zoek-URL is een sjabloon met accolades erin, en die zijn in een
    /// echte URL niet geldig; daarom eerst invullen en dan pas lezen.
    /// </summary>
    private static bool ZelfdeHost(string a, string b)
    {
        static Uri? Lees(string sjabloon) =>
            Uri.TryCreate(sjabloon.Replace("{query}", "x").Replace("{page}", "1").Replace("{offset}", "0"),
                UriKind.Absolute, out var uri)
                ? uri
                : null;

        var eerste = Lees(a);
        var tweede = Lees(b);

        return eerste is not null && tweede is not null &&
               string.Equals(eerste.Host, tweede.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Heeft de AI de zoek-URL bijgestuurd - meestal om {page} in het pad te krijgen - dan
    /// moet pagina 1 daar nog wel mee werken. "s-seite:1" hoeft niet hetzelfde te geven als
    /// het adres zonder paginanummer, en dan is de site stuk in plaats van uitgebreid. Dat
    /// staat in SITES.md ook zo bij Kleinanzeigen: "Eerst meten."
    ///
    /// Lukt het niet, dan gaat de ingetypte URL terug. Beter één pagina die werkt dan
    /// twintig die niet bestaan.
    /// </summary>
    private static async Task ControleerPaginaEenAsync(SiteDefinition def, string origineel, string testQuery,
        FetchRoute route, IProgress<string>? status, CancellationToken ct)
    {
        if (def.SearchUrlTemplate == origineel) return;

        status?.Report("Nakijken of pagina 1 nog werkt met de aangepaste zoek-URL...");

        try
        {
            var url = SearchUrlBuilder.Build(def, testQuery, null, 1);
            var pagina = await HaalViaRouteAsync(url, route, ct);
            var aantal = GenericSource.CountItems(def, pagina);

            if (aantal >= MinimumOpPaginaEen)
            {
                Log.Write($"analyse: de aangepaste zoek-URL geeft op pagina 1 {aantal} zoekertjes -> aanvaard " +
                          $"({def.SearchUrlTemplate})");
                return;
            }

            Log.Write($"analyse: de aangepaste zoek-URL gaf op pagina 1 maar {aantal} zoekertjes; " +
                      "de ingetypte URL blijft staan");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"analyse: de aangepaste zoek-URL kwam niet binnen ({ex.Message}); " +
                      "de ingetypte URL blijft staan");
        }

        def.SearchUrlTemplate = origineel;
    }

    /// <summary>Zo weinig zoekertjes op pagina 1 betekent dat het adres niet deugt.</summary>
    private const int MinimumOpPaginaEen = 3;

    /// <summary>
    /// Haalt één grote foto écht op. Een selector kan er goed uitzien en toch een adres
    /// geven dat de fotodienst weigert, en dat is geen randgeval: bij Kleinanzeigen staat
    /// het formaat in een <c>rule</c>-parameter die verplicht is. Gemeten op 29 september
    /// 2026 op één foto van die site: <c>?rule=$_2.AUTO</c> gaf 157x200, <c>$_57.AUTO</c>
    /// 1164x1481, <c>$_59.AUTO</c> 755x960 - en het adres zónder query een <b>HTTP 400</b>.
    /// De analyse had precies dat adres voorgesteld, want "laat de query weg" werkt wél bij
    /// 2dehands en Marktplaats. Eén verzoek zegt het verschil.
    ///
    /// Lukt het niet, dan gaat de selector eruit. De app valt dan terug op de miniatuur, en
    /// dat is beter dan een kapotte foto in het detailvenster.
    /// </summary>
    internal static async Task<string?> ControleerGroteFotoAsync(SiteDefinition def, AnalysisCheck check,
        IProgress<string>? status, CancellationToken ct)
    {
        if (def.LargeImageSelector.Length == 0) return null;

        var adres = check.Sample
            .Select(l => l.LargeImageUrl)
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u) &&
                                 u.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        if (adres is null) return null;

        status?.Report("De grote foto ophalen om te zien of dat adres werkt...");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            using var antwoord = await PageHttp.GetAsync(adres, timeout.Token);
            var soort = antwoord.Content.Headers.ContentType?.MediaType ?? "";

            if (antwoord.IsSuccessStatusCode && soort.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = antwoord.Content.Headers.ContentLength;
                Log.Write($"analyse: de grote foto werkt ({soort}, {bytes?.ToString() ?? "?"} bytes)");
                return null;
            }

            var reden = antwoord.IsSuccessStatusCode
                ? $"gaf {soort} in plaats van een foto"
                : $"gaf HTTP {(int)antwoord.StatusCode}";

            def.LargeImageSelector = "";

            var melding = $"De grote foto {reden}, dus die selector is eruit gehaald; de app toont de " +
                          "miniatuur. Vaak staat het formaat in een parameter die verplicht is.";

            Log.Write("analyse: " + melding + $" ({adres})");
            return melding;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            def.LargeImageSelector = "";

            var melding = $"De grote foto kwam niet binnen ({ex.Message}), dus die selector is eruit gehaald.";
            Log.Write("analyse: " + melding);
            return melding;
        }
    }

    /// <summary>
    /// Haalt pagina 2 op en telt hoeveel zoekertjes daarop ook al op pagina 1 stonden. Dat
    /// is precies wat er met de hand gebeurde bij Delcampe ("0 overlap tussen pagina 1 en
    /// 2"), en het is de enige manier om te weten of paginering écht werkt: een site die
    /// het paginanummer negeert, geeft gewoon pagina 1 terug zonder een fout te melden.
    /// Tot nu was alles wat de AI over paginering zei een gok die niemand nakeek.
    ///
    /// Klopt het niet, dan gaat de paginering eruit. Eén pagina die werkt is beter dan
    /// twintig keer dezelfde pagina ophalen - dat valt bovendien op bij de site.
    /// </summary>
    private static async Task<PagingCheck> ControleerPaginaTweeAsync(SiteDefinition def, string origineel,
        string paginaEen, string testQuery, FetchRoute route, IProgress<string>? status, CancellationToken ct)
    {
        if (!SearchUrlBuilder.SupportsPaging(def)) return new PagingCheck(false, 0, 0, null);

        status?.Report("Pagina 2 ophalen om de paginering na te meten...");

        try
        {
            var bron = new GenericSource(def);
            var eerste = await bron.ReadPageAsync(paginaEen, ct);
            var sleutels = eerste.Select(l => l.Key).ToHashSet();

            var url = SearchUrlBuilder.Build(def, testQuery, null, def.FirstPage + 1);
            var tweede = await bron.ReadPageAsync(await HaalViaRouteAsync(url, route, ct), ct);
            var overlap = tweede.Count(l => sleutels.Contains(l.Key));

            string? probleem = null;

            if (tweede.Count == 0)
            {
                probleem = "Pagina 2 gaf geen enkel zoekertje, dus de paginering is eruit gehaald.";
            }
            else if (overlap >= tweede.Count * 0.8)
            {
                probleem = $"Pagina 2 gaf {tweede.Count} zoekertjes waarvan er {overlap} ook op pagina 1 " +
                           "stonden: de site negeert het paginanummer. De paginering is eruit gehaald.";
            }

            if (probleem is not null) GeenPaginering(def, origineel);

            Log.Write($"analyse: paginering -> pagina 2 gaf {tweede.Count} zoekertjes, {overlap} ook op " +
                      $"pagina 1{(probleem is null ? " -> aanvaard" : " -> " + probleem)}");

            return new PagingCheck(true, tweede.Count, overlap, probleem);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GeenPaginering(def, origineel);

            var probleem = $"Pagina 2 kwam niet binnen ({ex.Message}), dus de paginering is eruit gehaald.";
            Log.Write("analyse: " + probleem);

            return new PagingCheck(true, 0, 0, probleem);
        }
    }

    private static void GeenPaginering(SiteDefinition def, string origineel)
    {
        def.PageTemplate = "";

        // Zat het paginanummer in de zoek-URL zelf, dan moet die ook terug.
        if (def.SearchUrlTemplate.Contains("{page}") || def.SearchUrlTemplate.Contains("{offset}"))
            def.SearchUrlTemplate = origineel;
    }

    /// <summary>
    /// Wat robots.txt van deze site over dit zoekpad zegt. De app dwingt niets af - dat is
    /// een keuze van wie de site toevoegt - maar het hoort wel op het scherm te staan. Bij
    /// Tweakers V&amp;A is de paginering met opzet dichtgelaten omdat hun robots.txt het
    /// zoekpad verbiedt, en dat stond nergens behalve in het hoofd van wie het uitzocht.
    /// </summary>
    private static async Task<string?> RobotsAsync(string url, CancellationToken ct)
    {
        try
        {
            var uri = new Uri(url);
            var tekst = await TryDirectAsync($"{uri.Scheme}://{uri.Host}/robots.txt", ct);
            if (tekst is null) return null;

            var pad = uri.AbsolutePath;
            var voorIedereen = false;

            foreach (var regel in tekst.Split('\n'))
            {
                var schoon = regel.Split('#')[0].Trim();
                var punt = schoon.IndexOf(':');
                if (punt <= 0) continue;

                var sleutel = schoon[..punt].Trim().ToLowerInvariant();
                var waarde = schoon[(punt + 1)..].Trim();

                if (sleutel == "user-agent")
                {
                    voorIedereen = waarde == "*";
                }
                else if (sleutel == "disallow" && voorIedereen && waarde.Length > 0 &&
                         pad.StartsWith(waarde.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                {
                    Log.Write($"analyse: robots.txt verbiedt dit zoekpad ({waarde})");
                    return waarde;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"analyse: robots.txt niet gelezen - {ex.Message}");
            return null;
        }
    }

    // ==================== de filters ====================

    /// <summary>Een voorgesteld filter, met de waarde waarmee de app het uitprobeert.</summary>
    internal sealed record FilterKandidaat(string Key, string Fragment, string TestValue, CustomFilter? Eigen);

    /// <summary>De enige sleutels die de app van binnenuit kent; de rest is een eigen filter.</summary>
    private static readonly string[] BekendeSleutels =
    {
        "priceMin", "priceMax", "priceRangeEuro", "priceRangeCents",
        "postcode", "location", "radius", "radiusMeters"
    };

    /// <summary>Hoeveel filters er hoogstens uitgeprobeerd worden; elk kost een paginabezoek.</summary>
    private const int MaxGemetenFilters = 8;

    /// <summary>
    /// Vraagt de filters en meet ze daarna na. Dit was het laatste stuk dat met de hand
    /// moest: negen van de dertien sitebestanden hebben een <c>Filters</c>-mapping en acht
    /// hebben eigen filters, en de analyse liet allebei leeg.
    ///
    /// De vraag gaat in hetzelfde gesprek verder, zodat de pagina niet nog eens mee hoeft.
    /// </summary>
    private static async Task<FilterCheck?> FiltersAsync(SiteDefinition def, string? antwoord,
        string paginaEenUrl, string paginaEen, FetchRoute route, IProgress<string>? status,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(antwoord)) return null;

        try
        {
            return await MeetFiltersAsync(def, LeesFilters(antwoord), paginaEenUrl, paginaEen, route,
                status, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"analyse: de filters lukten niet - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Voert elk voorstel echt uit en vergelijkt met de ongefilterde pagina. Filters zijn
    /// het enige deel van een sitebeschrijving dat je niet kán zien; ze moeten gemeten
    /// worden. De valkuil staat in SITES.md: de API van 2dehands aanvaardt een verkeerd
    /// gevormd filter en negeert het, en het totaal dat hij teruggeeft telt de zoekterm en
    /// niet de filter. Wie enkel naar dat getal kijkt, besluit dat niets werkt.
    /// </summary>
    internal static async Task<FilterCheck> MeetFiltersAsync(SiteDefinition def,
        IReadOnlyList<FilterKandidaat> kandidaten, string paginaEenUrl, string paginaEen,
        FetchRoute route, IProgress<string>? status, CancellationToken ct)
    {
        var leeg = Array.Empty<FilterResult>();
        var bron = new GenericSource(def);
        var basis = await bron.ReadPageAsync(paginaEen, ct);

        if (basis.Count < MinimumOpPaginaEen)
        {
            return new FilterCheck(false, kandidaten.Count, 0,
                "Te weinig zoekertjes op pagina 1 om filters aan af te meten.", leeg);
        }

        if (kandidaten.Count == 0) return new FilterCheck(true, 0, 0, null, leeg);

        var basisSleutels = basis.Select(l => l.Key).ToHashSet();

        // De controle vooraf: een parameter die niet bestaat. Verandert die de resultaten
        // ook, dan meet je de onrust van de site en niet je filter. Precies daarom stuurt
        // tools/meet-filter.py er altijd een onzin-parameter bij.
        status?.Report("Nakijken of deze site stabiele resultaten geeft...");

        // Twee keer, en de grootste telt. Eén meting was te weinig: Vinted verschoof
        // tussen twee verzoeken al 17% van zijn eerste pagina, en daardoor haalde een
        // verzonnen filter de drempel. Op een drukke marktplaats komen er nu eenmaal
        // zoekertjes bij en gaan er weg terwijl je meet.
        var eerste = await LijstAsync(bron, Plak(paginaEenUrl, "zzzbestaatniet=1"), route, ct);
        var tweede = await LijstAsync(bron, Plak(paginaEenUrl, "zzzbestaatookniet=2"), route, ct);
        var ruis = Math.Max(Anders(basisSleutels, eerste), Anders(basisSleutels, tweede));

        // Vanaf hier telt een verschil pas als het ruim boven die ruis uitkomt. Liever een
        // filter te weinig dan een filter dat er staat en niets doet: dat laatste is stil
        // falen, en het staat ook nog eens in het venster te lezen wat eruit ging.
        var drempel = Math.Max(0.20, ruis * 2 + 0.10);

        Log.Write($"analyse: filters - de site verschuift zelf {ruis:P0}, drempel {drempel:P0}");

        if (ruis > 0.4)
        {
            Log.Write($"analyse: filters niet na te meten, de controle-parameter veranderde al {ruis:P0}");

            return new FilterCheck(false, kandidaten.Count, 0,
                $"Deze site geeft bij elk verzoek andere zoekertjes ({ruis:P0} verschil met een parameter " +
                "die niet eens bestaat), dus hier valt geen filter na te meten. Vul ze met de hand in.",
                leeg) { Noise = ruis };
        }

        var resultaten = new List<FilterResult>();
        var gehouden = 0;
        var mediaan = Mediaan(basis);

        foreach (var kandidaat in kandidaten.Take(MaxGemetenFilters))
        {
            status?.Report($"Filter \"{kandidaat.Key}\" uitproberen...");

            // Bij een prijsgrens rekent de app zelf een bedrag uit: de mediaan van wat er
            // op pagina 1 staat. Dat moet ongeveer de helft wegsnijden, en het is
            // betrouwbaarder dan een gok van de AI.
            var prijsgrens = kandidaat.Key is "priceMin" or "priceMax";
            var waarde = prijsgrens && mediaan is not null
                ? mediaan.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : kandidaat.TestValue;

            if (waarde.Length == 0) continue;

            var url = Plak(paginaEenUrl,
                kandidaat.Fragment.TrimStart('&', '?').Replace("{value}", Uri.EscapeDataString(waarde)));

            bool houden;
            string reden;
            var lijst = new List<Listing>();

            try
            {
                lijst = await LijstAsync(bron, url, route, ct);
                var anders = Anders(basisSleutels, lijst);

                if (lijst.Count == 0)
                {
                    houden = false;
                    reden = "gaf geen enkel zoekertje";
                }
                else if (prijsgrens && mediaan is not null && lijst.Count(l => l.Price is > 0) >= 5)
                {
                    // Bij een prijsgrens bestaat er een veel sterker bewijs dan "de lijst
                    // veranderde": de prijzen zelf. Zonder filter ligt per definitie de
                    // helft boven de mediaan; blijft daar na het filteren vrijwel niets
                    // van over, dan werkt hij - ook op een drukke site waar de ruis hoog
                    // is. En andersom is het de valkuil van 2dehands: aanvaard, en
                    // genegeerd.
                    //
                    // Dat was nodig. Op Vinted haalde priceMax met 41% net de drempel van
                    // 41% niet en vloog eruit, terwijl een run een dag eerder hem met 52%
                    // wél hield. Dezelfde site, dezelfde parameter, ander toeval.
                    var binnen = GrensOk(lijst, mediaan.Value, kandidaat.Key == "priceMax");

                    houden = binnen;
                    reden = binnen
                        ? $"de prijzen bleven binnen de grens van {waarde}"
                        : "werd aanvaard maar genegeerd - de prijzen bleven buiten de grens";
                }
                else if (anders < drempel)
                {
                    houden = false;
                    reden = $"veranderde maar {anders:P0} van de resultaten, en deze site verschuift " +
                            $"zelf al {ruis:P0}";
                }
                else
                {
                    houden = true;
                    reden = $"veranderde {anders:P0} van de resultaten";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                houden = false;
                reden = "kwam niet binnen: " + ex.Message;
            }

            if (houden)
            {
                gehouden++;

                if (kandidaat.Eigen is null) def.Filters[kandidaat.Key] = kandidaat.Fragment;
                else def.CustomFilters.Add(kandidaat.Eigen);
            }

            resultaten.Add(new FilterResult(kandidaat.Key, kandidaat.Fragment, waarde, lijst.Count,
                lijst.Count(l => basisSleutels.Contains(l.Key)), houden, reden));

            Log.Write($"analyse: filter {kandidaat.Key}={waarde} -> {lijst.Count} zoekertjes, " +
                      $"{(houden ? "gehouden" : "eruit")} ({reden})");
        }

        return new FilterCheck(true, kandidaten.Count, gehouden, null, resultaten) { Noise = ruis };
    }

    private static async Task<List<Listing>> LijstAsync(GenericSource bron, string url, FetchRoute route,
        CancellationToken ct) =>
        await bron.ReadPageAsync(await HaalViaRouteAsync(url, route, ct), ct);

    /// <summary>Een stukje achter de zoek-URL plakken, met het juiste scheidingsteken.</summary>
    private static string Plak(string url, string stuk) => url + (url.Contains('?') ? "&" : "?") + stuk;

    /// <summary>
    /// Hoeveel deze lijst verschilt van de ongefilterde pagina: 0 is precies hetzelfde,
    /// 1 is niets gemeen. Gedeeld door de grootste van de twee, zodat een filter dat de
    /// helft wegsnijdt ook echt als een verschil telt.
    /// </summary>
    private static double Anders(HashSet<string> basis, IReadOnlyList<Listing> lijst)
    {
        if (lijst.Count == 0) return 1;

        var gedeeld = lijst.Count(l => basis.Contains(l.Key));
        return 1.0 - (double)gedeeld / Math.Max(basis.Count, lijst.Count);
    }

    private static decimal? Mediaan(IReadOnlyList<Listing> lijst)
    {
        var prijzen = lijst.Where(l => l.Price is > 0).Select(l => l.Price!.Value).OrderBy(p => p).ToList();
        return prijzen.Count == 0 ? null : prijzen[prijzen.Count / 2];
    }

    /// <summary>
    /// Bleven de prijzen binnen de gevraagde grens? Een tiende buiten de lijn mag: een
    /// site kan een uitgelicht zoekertje bovenaan zetten dat zich aan niets houdt.
    /// </summary>
    private static bool GrensOk(IReadOnlyList<Listing> lijst, decimal grens, bool maximum)
    {
        var metPrijs = lijst.Where(l => l.Price is > 0).ToList();
        if (metPrijs.Count == 0) return true;

        var buiten = metPrijs.Count(l => maximum
            ? l.Price!.Value > grens * 1.01m
            : l.Price!.Value < grens * 0.99m);

        return buiten <= metPrijs.Count * 0.1;
    }

    /// <summary>
    /// De twee filterlijsten uit het antwoord van de eerste vraag. Alles wat er niet
    /// uitvoerbaar uitziet valt hier al weg; wat overblijft gaat naar de meting.
    /// </summary>
    private static List<FilterKandidaat> LeesFilters(string answer)
    {
        var lijst = new List<FilterKandidaat>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(answer);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("de AI gaf geen bruikbaar antwoord over de filters");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("filters", out var vaste) && vaste.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in vaste.EnumerateArray())
                {
                    var key = BekendeSleutels.FirstOrDefault(
                        s => string.Equals(s, Get(item, "key"), StringComparison.OrdinalIgnoreCase));

                    var fragment = Get(item, "fragment");

                    // Een sleutel die de app niet kent, doet niets: SearchUrlBuilder vult
                    // hem nooit in. Dan is hij een eigen filter, geen vast.
                    if (key is null || !fragment.Contains("{value}")) continue;

                    lijst.Add(new FilterKandidaat(key, fragment, Get(item, "testValue"), null));
                }
            }

            if (root.TryGetProperty("customFilters", out var eigen) && eigen.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in eigen.EnumerateArray())
                {
                    var key = Get(item, "key");
                    var fragment = Get(item, "fragment");
                    if (key.Length == 0 || !fragment.Contains("{value}")) continue;

                    var soort = Get(item, "kind");
                    var filter = new CustomFilter
                    {
                        Key = key,
                        Label = Get(item, "label"),
                        Fragment = fragment,
                        Kind = soort.Equals("Number", StringComparison.OrdinalIgnoreCase) ? CustomFilterKind.Number
                            : soort.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ? CustomFilterKind.Toggle
                            : CustomFilterKind.Choice,
                        Multiple = item.TryGetProperty("multiple", out var m) && m.ValueKind == JsonValueKind.True
                    };

                    if (item.TryGetProperty("options", out var opties) && opties.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var optie in opties.EnumerateArray())
                        {
                            var waarde = Get(optie, "value");
                            if (waarde.Length == 0) continue;

                            filter.Options.Add(new FilterChoice { Value = waarde, Label = Get(optie, "label") });
                        }
                    }

                    // Een keuzelijst zonder keuzes valt weg: daar valt niets te kiezen en
                    // niets na te meten.
                    if (filter.Kind == CustomFilterKind.Choice && filter.Options.Count == 0) continue;

                    var proef = filter.Kind switch
                    {
                        CustomFilterKind.Toggle => "1",
                        CustomFilterKind.Choice => filter.Options[0].Value,
                        _ => Get(item, "testValue")
                    };

                    if (proef.Length == 0) continue;

                    lijst.Add(new FilterKandidaat(key, fragment, proef, filter));
                }
            }
        }

        return lijst;
    }

    /// <summary>
    /// De twee filterlijsten, voor in het schema van de eerste vraag. Ze zaten eerst in
    /// een eigen vraag verderop in hetzelfde gesprek, met de bedoeling dat de pagina uit
    /// de cache kwam. Gemeten op 30 september 2026 deed ze dat niet: het schema hoort bij
    /// het gecachete begin, dus een andere antwoordvorm betekent een nieuwe cache -
    /// "naar cache 75122, uit cache 0", terwijl een gewone verbeterronde er 71 220 uit
    /// las. Zo kostte één analyse 186 000 tokens in plaats van 76 000. In hetzelfde schema
    /// is het gratis: de app meet de filters toch zelf na en heeft die extra ronde niet
    /// nodig.
    /// </summary>
    private static (JsonObject Vast, JsonObject Eigen) FilterSchemas()
    {
        static JsonObject Tekst() => new() { ["type"] = "string" };

        var vast = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["key"] = Tekst(),
                ["fragment"] = Tekst(),
                ["testValue"] = Tekst()
            },
            ["required"] = new JsonArray { "key", "fragment", "testValue" },
            ["additionalProperties"] = false
        };

        var keuze = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["value"] = Tekst(), ["label"] = Tekst() },
            ["required"] = new JsonArray { "value", "label" },
            ["additionalProperties"] = false
        };

        var eigen = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["key"] = Tekst(),
                ["label"] = Tekst(),
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "Choice", "Number", "Toggle" }
                },
                ["multiple"] = new JsonObject { ["type"] = "boolean" },
                ["fragment"] = Tekst(),
                ["testValue"] = Tekst(),
                ["options"] = new JsonObject { ["type"] = "array", ["items"] = keuze }
            },
            ["required"] = new JsonArray { "key", "label", "kind", "multiple", "fragment", "testValue", "options" },
            ["additionalProperties"] = false
        };

        return (vast, eigen);
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
                using (BrowserPool.Lease())
                    return FromPre(await BrowserPool.Get().GetHtmlAsync(url, null, ct));

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
          particulieren anders getoond worden. Het @attribuut hoort er dan ÉÉN keer bij, helemaal achteraan,
          en geldt voor de hele lijst: "a.foto, img.foto@src". Schrijf je het twee keer ("a@src, img@src"),
          dan knipt de motor enkel bij het laatste apenstaartje, blijft er ongeldige CSS over en wordt de
          selector geweigerd.
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
          dat pas met JavaScript gevuld wordt, geef de selector toch en zeg het in notes. Staan er twee
          bedragen bij een zoekertje - een vraagprijs en een totaal met verzending of kopersbescherming
          erbij - neem dan de vraagprijs: dat is wat de verkoper vraagt, en waarmee de app vergelijkt.
        - dateSelector: de motor gebruikt DateTime.TryParse, dus liefst "time@datetime". Leeg laten als er
          enkel "vandaag" of "2 uur geleden" staat.
        - imageSelector: de miniatuur. Bij lazy loading staat het echte adres soms in een ander attribuut
          dan src. De motor probeert zelf src en data-src, maar niet data-original, data-lazy of
          data-zoom-image; noem die dus met de hand (img.thumb@data-original).
        - largeImageSelector: een grotere versie, als die af te leiden is. Veel sites zetten het formaat in
          het pad van de foto: s-l500 wordt s-l1600, 250x188 wordt 1024x768, _S.webp wordt _L.webp,
          cw_lot_card_ext wordt cw_large. Schrijf dan de fotoselector met ::replace erachter en kies een
          stukje dat uniek is in de URL (_S.webp, niet _S). Een srcset enkel als er één URL in staat. AVIF kan
          de app niet tonen; kies binnen <picture> de JPEG- of WebP-bron. Leeg laten als het niet uit de
          pagina af te leiden is - verzin geen formaten. Twee dingen die duur geleerd zijn:
          (1) staat het formaat in de QUERY (?rule=..., ?w=500), dan geeft het adres ZONDER query SOMS het
          origineel: ::match(^[^?]+). Bij twee sites was dat het verschil tussen 726 en 1600 beeldpunten,
          maar bij een derde is die parameter verplicht en geeft het kale adres HTTP 400. Gok dus niet:
          neem bij voorkeur een variant die ELDERS IN DE PAGINA staat - in een srcset, of bij een andere
          foto - en zeg in notes waarop je je baseert. De app haalt die grote foto daarna echt op en gooit
          de selector weg wanneer het adres niet werkt, dus een miniatuur is beter dan een gok.
          (2) is het adres ONDERTEKEND - een parameter als oh=, oe=, s=, sig= of stp=, of een hash in het
          pad - laat de maat dan staan. Die handtekening dekt het formaat mee, dus een groter formaat geeft
          403 in plaats van een grotere foto. Bij twee sites is dat nagemeten.
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

        Draagt elk veld het nummer van het zoekertje in zijn id of data-testid, zoals
        "product-item-id-123--price-text", gebruik dan het einde of het begin:
        [data-testid$='--price-text'], [data-testid^='item-photo-']. Zo hoef je dat nummer niet te kennen.

        Let ten slotte op de TAAL. Een site antwoordt soms in een andere taal dan de gebruiker later ziet,
        want de app kan later een kopregel meesturen die de taal omzet. Leun je in een ::match op een woord
        uit de pagina ("Sinds", "van", "Ajouté"), zeg dat dan in notes.

        ## Veilingen
        isAuction: true wanneer de prijs op deze site een BOD is en geen vraagprijs - je ziet dat aan
        "Huidig bod", een aantal biedingen, of een aftelklok op elke kaart. De app telt zo'n site niet mee
        wanneer ze uitrekent wat een toestel ongeveer waard is: een bod dat nog loopt, zegt daar niets over.
        timeLeftSelector: het element met hoelang er nog geboden kan worden, zoals de site het schrijft
        ("Nog 3 dagen", "9d 12u"). Dat komt op het scherm achter de plaats te staan. Leeg laten bij een site
        zonder veilingen, en ook bij een veilingsite die het niet op haar zoekpagina zet.
        Een site kan gemengd zijn, met vaste prijzen én veilingen op dezelfde pagina. Zet isAuction dan op
        false - de meeste prijzen zijn dan vraagprijzen - en zeg het in notes.

        ## JSON
        Is de inhoud JSON, dan zijn het puntpaden: itemSelector "listings", priceSelector
        "priceInfo.priceCents". Er zijn geen indexen: kom je onderweg een lijst tegen, dan neemt de motor het
        eerste element, dus "pictures.largeUrl" werkt ook als pictures een lijst is. @ en komma's bestaan hier
        niet, ::replace wel. priceInCents is true wanneer het bedrag in centen staat.

        ## Vervolgpagina's
        Zoek in de links van de paginering wat er verandert tussen pagina 1 en pagina 2. Er zijn drie vormen
        en de app kan ze alle drie:

        - Een stukje dat ACHTER de zoek-URL komt: pageTemplate met {page} erin, zoals "&page={page}" of
          "&_pgn={page}".
        - Het nummer staat middenin het PAD, bijvoorbeeld /s-seite:2/cd/k0. Zet {page} dan daar, in
          searchUrlTemplate, en laat pageTemplate leeg. {query} moet erin blijven staan en het moet dezelfde
          site blijven. De app kijkt daarna zelf na of pagina 1 met dat adres nog zoekertjes geeft en zet je
          voorstel terug als dat niet zo is - een fout is hier dus geen ramp, maar zwijgen wel: zonder dit
          veld blijft zo'n site steken op de ene pagina die je ziet.
        - De site telt niet in pagina's maar in zoekertjes: "limit=100&offset=200" is de derde pagina. Zet
          dan {offset} in searchUrlTemplate of in pageTemplate, en pageSize op hoeveel er op één pagina
          passen. Zonder pageSize telt {offset} niet als paginering.

        firstPage is het nummer van de eerste pagina, meestal 1 en soms 0. searchUrlTemplate laat je leeg
        wanneer het adres goed is zoals het is.

        ## Namen
        name: de naam van de site zoals ze zichzelf noemt. shortName: enkel als name langer is dan een
        twaalftal tekens, een kortere vorm voor een tabblad; anders leeg. baseUrl: schema en host
        (https://www.site.be), tenzij je meer nodig hebt voor een link die uit een id gebouwd wordt.

        ## Filters
        Twee lijsten, en allebei mogen ze leeg blijven wanneer je geen filters ziet.

        "filters" zijn de vaste, die de app van binnenuit kent omdat ze op elke site hetzelfde betekenen.
        Enkel deze sleutels bestaan; noem je iets anders, dan doet het niets:
        - priceMin, priceMax: een bedrag in euro
        - priceRangeEuro, priceRangeCents: beide grenzen in één parameter, als "min:max"
        - postcode of location: een postcode of een plaatsnaam
        - radius: een straal in kilometer. radiusMeters: dezelfde straal in meter
        Het fragment is het stukje URL met {value} erin, bijvoorbeeld "&priceTo={value}". Let op de eenheid:
        staat er in de URL van de site een bedrag in centen, kies dan priceRangeCents, en bij een straal in
        meter radiusMeters. Dat is bij een echte site fout gegaan.

        "customFilters" is alles wat enkel op deze site bestaat: staat, categorie, brandstof, soort verkoper,
        alleen met verzending. kind is "Choice" (een keuze uit een lijst; multiple op true als er meerdere
        tegelijk mogen), "Number" (een vrij getal) of "Toggle" (aan of uit). Bij Choice geef je de keuzes
        zoals de site ze verwacht, met het label in de taal van de site. Hoogstens acht, en dan de nuttigste.

        Geef bij elk filter een testValue die op deze site echt geldig is: een postcode die bestaat, een
        bedrag in de juiste eenheid. Bij een Choice neemt de app de eerste keuze, en bij priceMin en
        priceMax rekent ze zelf een bedrag uit.

        Waar je ze vindt: het filterformulier in de pagina (een select, een input of een checkbox met een
        name), de links van de filters in de zijbalk, of een blok met de hele filterlijst (__NEXT_DATA__,
        een taxonomy). Neem de namen letterlijk over, en noem geen parameter die al in de zoek-URL staat:
        die komt er dan twee keer in, en sommige sites antwoorden daarop met een serverfout.

        De app probeert elk filter daarna ECHT uit op deze zoekopdracht en vergelijkt de resultaten met de
        ongefilterde pagina. Wat niets verandert gaat eruit, en er gaan twee parameters mee die niet bestaan
        als controle. Een gok die fout blijkt kost dus niets, maar een filter dat je niet noemt bestaat
        nooit. Een sortering is géén filter: die verandert de volgorde en niet wat er te zien valt.

        ## notes
        Een paar zinnen in het Nederlands voor wie de site later onderhoudt: wat onzeker is en wat opviel.
        Bijvoorbeeld dat de prijs pas met JavaScript verschijnt, dat maar een deel van de zoekertjes in de
        pagina staat omdat de lijst meeschuift met het scrollen, of dat er klassen met willekeurige
        achtervoegsels gebruikt zijn. Een JSON-API is stabieler en sneller dan HTML: staan er sporen van in de
        meegestuurde lijst, noem dan de meest waarschijnlijke.

        Laat een veld leeg ("") wanneer het gegeven niet op de pagina staat.
        """;
}

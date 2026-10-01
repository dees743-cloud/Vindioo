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
public partial class SiteAnalyzer
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

    /// <summary>
    /// Dezelfde client als de zoekmotor, en dat is hier geen detail: de analyse moet de site
    /// zien zoals de app ze straks ziet. Stond er een andere User-Agent of geen compressie,
    /// dan meet je een pagina die de gebruiker nooit krijgt.
    /// </summary>
    private static HttpClient CreatePageClient() => HttpFactory.MaakClient(TimeSpan.FromSeconds(30));

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

}

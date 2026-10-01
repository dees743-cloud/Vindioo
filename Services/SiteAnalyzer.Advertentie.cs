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
/// De pagina van één zoekertje: foto's, verkoper, datum en beschrijving.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
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
}

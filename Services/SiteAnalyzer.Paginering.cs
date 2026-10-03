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
using Vindioo.Models;
using Vindioo.Sources;

namespace Vindioo.Services;

/// <summary>
/// De zoek-URL, de paginering en robots.txt nameten.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
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
}

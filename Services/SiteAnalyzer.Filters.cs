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
/// De filters: de AI stelt voor, de app meet na.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
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
}

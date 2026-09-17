using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Sources;

/// <summary>
/// De linkmotor: voor sites waarvan de klassenamen versleuteld zijn en bij elke
/// update veranderen, zodat een selector per veld niet werkt. Wat wél vast blijft, is
/// de link naar een zoekertje. Deze motor zoekt die links op en leest de tekst rond
/// zo'n link uit: de regel met een euroteken is de prijs, de eerste andere regel de
/// titel, de laatste de plaats.
///
/// Alles wat bij één site hoort - welke links, welk id, welke tekens een prijs
/// aanduiden - staat in het sitebestand (<see cref="LinkTextOptions"/>). Tot september
/// 2026 heette deze klasse FacebookSource en stond dat allemaal in de code.
/// </summary>
public class LinkTextSource : ISearchSource
{
    private readonly SiteDefinition _def;
    private readonly LinkTextOptions _options;
    private readonly Regex? _idPattern;
    private readonly Regex? _locationPattern;

    public LinkTextSource(SiteDefinition definition)
    {
        _def = definition;
        _options = definition.LinkText ?? new LinkTextOptions();

        _idPattern = string.IsNullOrWhiteSpace(_options.IdPattern)
            ? null
            : new Regex(_options.IdPattern, RegexOptions.Compiled);

        _locationPattern = string.IsNullOrWhiteSpace(_options.LocationPattern)
            ? null
            : new Regex(_options.LocationPattern, RegexOptions.Compiled);
    }

    public string Name => _def.Name;

    public async Task<List<Listing>> SearchAsync(string query, int maxResults,
        SearchFilters? filters = null, IProgress<List<Listing>>? progress = null,
        CancellationToken ct = default)
    {
        // Deze bron levert in één keer: hij leest de pagina pas uit als de browser
        // klaar is, dus er valt niets tussentijds te melden via progress.
        var url = SearchUrlBuilder.Build(_def, query, filters);

        // De gedeelde browser van deze zoekopdracht; niet zelf afsluiten. Er wordt
        // gewacht op de eerste link naar een zoekertje: dat is het enige vaste.
        var browser = BrowserPool.Get();
        var html = await browser.GetHtmlAsync(url, _def.ItemSelector, ct);

        if (_options.LoginMarkers.Any(m => html.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Niet aangemeld bij {Name}. Meld je één keer aan: tandwiel > Sites beheren > " +
                $"{Name} > Aanmelden.");
        }

        // Op een achtergronddraad, zoals bij de gewone motor: zie GenericSource.ParseAsync.
        return await Task.Run(() => ReadPage(html, maxResults), ct);
    }

    /// <summary>
    /// Leest een pagina uit die al opgehaald is. Los van <see cref="SearchAsync"/>, zodat
    /// een bewaarde pagina opnieuw te lezen is - zo is deze motor ook nagemeten tegen de
    /// oude Facebook-code, op exact dezelfde HTML.
    /// </summary>
    public List<Listing> ReadPage(string html, int maxResults = int.MaxValue)
    {
        var results = new List<Listing>();
        if (string.IsNullOrWhiteSpace(_def.ItemSelector)) return results;

        var document = new HtmlParser().ParseDocument(html);
        var seen = new HashSet<string>();

        foreach (var link in document.QuerySelectorAll(_def.ItemSelector))
        {
            var href = link.GetAttribute("href") ?? "";

            string id;
            if (_idPattern is null)
            {
                id = href;
            }
            else
            {
                var match = _idPattern.Match(href);
                if (!match.Success) continue;
                id = match.Groups[1].Value;
            }

            if (id.Length == 0 || !seen.Add(id)) continue;   // elk zoekertje maar één keer

            var listing = ReadListing(link, id, href);
            if (listing is not null) results.Add(listing);

            if (results.Count >= maxResults) break;
        }

        return results;
    }

    /// <summary>
    /// Leest prijs, titel en plaats uit de tekst van één link. De link bevat de hele
    /// kaart, met de gegevens als losse tekstregels onder elkaar.
    /// </summary>
    private Listing? ReadListing(IElement link, string id, string href)
    {
        var lines = Regels(link);
        if (lines.Count == 0) return null;

        var listing = new Listing
        {
            Source = Name,
            ExternalId = id,
            Url = BuildUrl(id, href)
        };

        // De prijs: liefst een regel die enkel een prijs is ("€ 80", "Gratis"), anders de
        // eerste regel met een prijsteken erin.
        var prijs = lines.FindIndex(IsPrijsRegel);
        if (prijs < 0)
            prijs = lines.FindIndex(l => _options.PriceMarkers.Any(m => l.Contains(m, StringComparison.Ordinal)));

        if (prijs >= 0)
        {
            listing.PriceLabel = lines[prijs];
            listing.Price = ParsePrice(lines[prijs]);

            // Wat voor de prijs staat, is een label van de site ("Zojuist geplaatst"), en
            // een tweede prijs erna is de oude, doorgestreepte. Geen van beide is een titel.
            lines = lines.Skip(prijs + 1).Where(l => !IsPrijsRegel(l)).ToList();
        }

        // Wat overblijft: eerst de titel, als laatste de plaats. Eén regel die op een
        // plaatsnaam lijkt, is enkel de plaats: Facebook toont soms een kaart zonder titel.
        if (lines.Count == 1 && _locationPattern?.IsMatch(lines[0]) == true)
        {
            listing.Location = lines[0];
        }
        else
        {
            listing.Title = lines.Count > 0 ? lines[0] : "";
            listing.Location = lines.Count > 1 ? lines[^1] : "";
        }

        // Veiligheidsnet: staat het einde van een plaatsnaam in de titel en niet in de
        // plaats, dan zijn die twee omgewisseld.
        if (_locationPattern is not null &&
            _locationPattern.IsMatch(listing.Title) &&
            !_locationPattern.IsMatch(listing.Location))
        {
            (listing.Title, listing.Location) = (listing.Location, listing.Title);
        }

        // Foto zoeken binnen of vlak bij de link.
        var image = link.QuerySelector("img") ?? link.ParentElement?.QuerySelector("img");
        if (image is not null)
        {
            var src = image.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src)) listing.ImageUrls.Add(src);

            // Soms staat er een grotere variant in srcset; die is bruikbaar voor de
            // hover-preview. Staat er niets, dan blijft enkel de kaartfoto over.
            var larger = LargestFromSrcSet(image.GetAttribute("srcset"));
            if (!string.IsNullOrWhiteSpace(larger) && larger != src)
                listing.LargeImageUrl = larger;
        }

        return string.IsNullOrWhiteSpace(listing.Title) ? null : listing;
    }

    /// <summary>
    /// De link naar het zoekertje: uit het id met de sjabloon uit het sitebestand, of
    /// anders de link zoals hij op de pagina staat, volledig gemaakt.
    /// </summary>
    private string BuildUrl(string id, string href)
    {
        if (!string.IsNullOrWhiteSpace(_options.UrlTemplate))
            return _options.UrlTemplate.Replace("{id}", id);

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        if (href.StartsWith("//")) return "https:" + href;

        return _def.BaseUrl.TrimEnd('/') + "/" + href.TrimStart('/');
    }

    /// <summary>
    /// Kiest de grootste variant uit een srcset, zoals "url 1x, url 2x" of
    /// "url 320w, url 640w". Geeft leeg terug wanneer er niets bruikbaars staat.
    /// </summary>
    private static string LargestFromSrcSet(string? srcSet)
    {
        if (string.IsNullOrWhiteSpace(srcSet)) return "";

        var best = "";
        var bestScore = 0d;

        foreach (var candidate in srcSet.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = candidate.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0) continue;

            // Zonder maataanduiding telt de variant als de kleinste (1).
            var score = 1d;
            if (pieces.Length > 1)
            {
                double.TryParse(pieces[1].TrimEnd('w', 'x'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out score);
            }

            if (score >= bestScore)
            {
                bestScore = score;
                best = pieces[0];
            }
        }

        return best;
    }

    /// <summary>
    /// De tekstregels van een kaart. Liefst de losse tekststukken, elk uit zijn eigen
    /// element: bij Facebook "Zojuist geplaatst | € 50 | € 70 | Meisjes fiets 26 | Kortrijk, VLG".
    ///
    /// Tot september 2026 las de motor de aaneengeplakte tekst van de hele kaart en knipte
    /// die op hoofdletters ("€ 50€ 70Meisjes fiets 26Kortrijk, VLG"). Dat gaf prijslabels als
    /// "Zojuist geplaatst€ 300" en plaatsen als "GB SSDRoeselare, VLG", en een kaart als
    /// "€ 1.234Giant fiets Ronse, VLG" viel helemaal weg: daar staat geen hoofdletter tegen
    /// een kleine letter. Op dezelfde bewaarde pagina gaf de oude manier 25 van de 30
    /// zoekertjes met een titel, de nieuwe alle 30.
    /// </summary>
    private static List<string> Regels(IElement link)
    {
        var stukken = link.Descendants<IText>()
            .Select(t => t.Text.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        if (stukken.Count > 1) return stukken;

        // Staat alles in één tekststuk, dan de oude weg: eerst op regeleinden, en anders
        // splitsen waar een nieuw woord met een hoofdletter tegen het vorige aan staat.
        var lines = link.TextContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return lines.Count <= 1 ? SplitSingleLine(link.TextContent) : lines;
    }

    /// <summary>Is deze regel enkel een prijs: een prijsteken met cijfers, of het teken alleen ("Gratis")?</summary>
    private bool IsPrijsRegel(string regel)
    {
        var rest = regel;
        var teken = false;

        foreach (var marker in _options.PriceMarkers)
        {
            if (!rest.Contains(marker, StringComparison.Ordinal)) continue;
            teken = true;
            rest = rest.Replace(marker, "", StringComparison.Ordinal);
        }

        return teken && Regex.IsMatch(rest, @"^[\d\s.,]*$");
    }

    /// <summary>Tekst die aaneen geplakt staat: "€ 175Commodore 64Aalter".</summary>
    private static List<string> SplitSingleLine(string text)
    {
        var parts = Regex.Split(text, @"(?<=\d)(?=[A-ZÀ-Ü])|(?<=[a-zà-ü])(?=[A-ZÀ-Ü])");
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private static decimal? ParsePrice(string text)
    {
        var match = Regex.Match(text, @"\d[\d.,\s]*");
        if (!match.Success) return null;

        var raw = match.Value.Replace(" ", "").Replace(".", "").Replace(',', '.');

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

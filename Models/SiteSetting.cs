using Zentrix.Models;

namespace Zentrix.Models;

/// <summary>
/// De instellingen van één site binnen één zoekopdracht: of hij meezoekt en met
/// welke filters. Dit is de bewaarbare tegenhanger van <see cref="SiteTab"/> —
/// die laatste leeft in het scherm, deze gaat naar de databank.
///
/// Elke zoekopdracht houdt dus zijn eigen postcode, prijs en eigen filters per
/// site bij. Zoeken op "commodore" mag een andere straal hebben dan zoeken op
/// "aanhangwagen", en dat op elke site apart.
///
/// Oude zoekopdrachten hebben nog de velden "Provinces" en "AuctionHouses" in hun
/// JSON. Die worden bij het inlezen gewoon overgeslagen; ze waren bij geen enkele
/// bewaarde zoekopdracht ingevuld toen ze verdwenen.
///
/// Hetzelfde geldt voor "MaxResults", het veld "Max. resultaten" uit het venster van een
/// zoekopdracht. Tot 22 september 2026 onthield elke zoekopdracht per site hoeveel die mocht
/// leveren: 100 bij de oudste, 500 bij de rest. Nu hangt dat af van de site zelf
/// (<see cref="SiteDefinition.ResultLimit"/>); met het oude getal was de hogere rem voor snelle
/// sites nooit bij "Computer" of "Commodore" aangekomen.
/// </summary>
public class SiteSetting
{
    /// <summary>Naam van de site, zoals in <see cref="SiteDefinition.Name"/>.</summary>
    public string Site { get; set; } = "";

    /// <summary>Zoekt deze site mee in deze zoekopdracht?</summary>
    public bool Enabled { get; set; }

    public decimal? PriceMin { get; set; }
    public decimal? PriceMax { get; set; }

    public string Postcode { get; set; } = "";
    public int RadiusKm { get; set; }

    /// <summary>
    /// De sitegebonden filters, met als sleutel de <see cref="CustomFilter.Key"/>
    /// uit de sitebeschrijving. Gaat mee in een bewaarde zoekopdracht, zodat de
    /// planner met dezelfde brandstof en kilometerstand blijft zoeken.
    /// </summary>
    public Dictionary<string, string> Custom { get; set; } = new();

    /// <summary>
    /// Zet deze instellingen om naar de filters die een bron verwacht. De bron
    /// kent <see cref="SiteSetting"/> niet; die krijgt gewoon een
    /// <see cref="SearchFilters"/> zoals bij een zoekopdracht met de hand.
    /// </summary>
    public SearchFilters ToFilters()
    {
        var filters = new SearchFilters
        {
            PriceMin = PriceMin,
            PriceMax = PriceMax,
            Postcode = Postcode,
            RadiusKm = RadiusKm
        };

        foreach (var (sleutel, waarde) in Custom) filters.Custom[sleutel] = waarde;

        return filters;
    }

    /// <summary>Neemt de waarden over uit een tabblad in het zoekscherm.</summary>
    public static SiteSetting FromTab(SiteTab tab) => new()
    {
        Site = tab.Name,
        Enabled = tab.IsEnabled,
        PriceMin = tab.Filters.PriceMin,
        PriceMax = tab.Filters.PriceMax,
        Postcode = tab.Filters.Postcode,
        RadiusKm = tab.Filters.RadiusKm,
        Custom = new Dictionary<string, string>(tab.Filters.Custom)
    };

    /// <summary>Schrijft deze waarden terug naar een tabblad in het zoekscherm.</summary>
    public void ApplyTo(SiteTab tab)
    {
        tab.IsEnabled = Enabled;

        tab.Filters.PriceMin = PriceMin;
        tab.Filters.PriceMax = PriceMax;
        tab.Filters.Postcode = Postcode;
        tab.Filters.RadiusKm = RadiusKm;

        tab.Filters.Custom.Clear();
        foreach (var (sleutel, waarde) in Custom) tab.Filters.Custom[sleutel] = waarde;
    }
}

using System.Globalization;

namespace Vindioo.Models;

/// <summary>
/// De filterwaarden die de gebruiker in het hoofdscherm invult. Elke bron mag
/// ze op zijn eigen manier gebruiken: de generieke motor plakt ze via de
/// <see cref="SiteDefinition.Filters"/>-mapping in de zoek-URL, ingebouwde
/// bronnen kunnen ze rechtstreeks uitlezen.
/// </summary>
public class SearchFilters
{
    public decimal? PriceMin { get; set; }
    public decimal? PriceMax { get; set; }

    public string Postcode { get; set; } = "";

    /// <summary>Straal in kilometer rond de postcode; 0 betekent overal.</summary>
    public int RadiusKm { get; set; }

    /// <summary>
    /// De waarden van de sitegebonden filters, met als sleutel de
    /// <see cref="CustomFilter.Key"/> uit de sitebeschrijving. Bij een filter
    /// met meerdere keuzes staat hier alles aan elkaar geplakt met de
    /// <see cref="CustomFilter.Separator"/> van dat filter, bv. "B,D".
    /// Een lege of ontbrekende waarde betekent: dat filter staat uit.
    ///
    /// Hier zitten sinds september 2026 ook de provincies en veilinghuizen van
    /// AlleVeilingen; die hadden eerst elk een eigen lijst in deze klasse.
    /// </summary>
    public Dictionary<string, string> Custom { get; } = new();

    /// <summary>
    /// De waarde voor een bekende filternaam, klaar om in de zoek-URL te zetten,
    /// of een lege tekst wanneer de gebruiker die filter niet invulde. De namen
    /// komen overeen met de sleutels in <see cref="SiteDefinition.Filters"/>.
    /// </summary>
    public string ValueFor(string key) => key.Trim().ToLowerInvariant() switch
    {
        "pricemin" => PriceMin?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
        "pricemax" => PriceMax?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
        "postcode" or "location" => Postcode,
        "radius" => RadiusKm > 0 ? RadiusKm.ToString(CultureInfo.InvariantCulture) : "",
        "radiusmeters" => RadiusKm > 0 ? (RadiusKm * 1000).ToString(CultureInfo.InvariantCulture) : "",
        "pricerangecents" => PriceRangeCents(),
        "pricerangeeuro" => PriceRangeEuro(),
        _ => ""
    };

    /// <summary>
    /// Prijsbereik als "min:max" in centen, voor sites die beide grenzen in één
    /// parameter verwachten (2dehands en Marktplaats: attributeRanges[]=PriceCents:...).
    /// Een open grens blijft leeg, dus "10000:" of ":20000" — dat aanvaardt die API.
    /// </summary>
    private string PriceRangeCents()
    {
        if (PriceMin is null && PriceMax is null) return "";

        var min = PriceMin is null ? "" : ToCents(PriceMin.Value);
        var max = PriceMax is null ? "" : ToCents(PriceMax.Value);

        return $"{min}:{max}";
    }

    /// <summary>
    /// Prijsbereik als "min-max" in hele euro's, voor sites die beide grenzen in
    /// één parameter verwachten (leboncoin: price=100-200). Een open bovengrens
    /// mag leeg blijven ("100-"), maar een open ondergrens niet: "-80" wordt daar
    /// genegeerd, dus dan zetten we er een nul voor ("0-80").
    /// </summary>
    private string PriceRangeEuro()
    {
        if (PriceMin is null && PriceMax is null) return "";

        var min = PriceMin is null ? "0" : ToWholeEuro(PriceMin.Value);
        var max = PriceMax is null ? "" : ToWholeEuro(PriceMax.Value);

        return $"{min}-{max}";
    }

    private static string ToCents(decimal euro) =>
        ((long)Math.Round(euro * 100)).ToString(CultureInfo.InvariantCulture);

    private static string ToWholeEuro(decimal euro) =>
        ((long)Math.Round(euro)).ToString(CultureInfo.InvariantCulture);
}

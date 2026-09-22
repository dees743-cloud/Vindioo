using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Zentrix.Sources;

namespace Zentrix.Models;

/// <summary>
/// Eén regel in het instellingenscherm van een zoekopdracht: een site met zijn
/// vinkje en zijn filters, klaar om aan de XAML te hangen.
///
/// Dit staat los van <see cref="SiteSetting"/> (wat bewaard wordt) en van
/// <see cref="SiteTab"/> (wat in het zoekscherm leeft). De reden is de invoer:
/// een prijs is in de databank een getal, maar in een tekstvak een stuk tekst
/// dat ook leeg of half ingetypt mag zijn. Die vertaling hoort niet in het model
/// dat naar de databank gaat.
///
/// De velden Toon… bepalen wat er van deze site te zien is. Die komen uit
/// <see cref="SearchUrlBuilder.Supports"/>, dus er staat nergens een lijstje
/// "welke site kan wat" — dat leidt de app af uit de sitebeschrijving zelf.
/// </summary>
public class SiteEditor : ObservableObject
{
    public SiteEditor(SiteDefinition def, SiteSetting? setting)
    {
        Def = def;
        Site = def.Name;

        ToonPostcode = SearchUrlBuilder.Supports(def, "postcode") ||
                       SearchUrlBuilder.Supports(def, "location");
        ToonStraal = SearchUrlBuilder.Supports(def, "radius") ||
                     SearchUrlBuilder.Supports(def, "radiusmeters");

        if (setting is not null) Neem(setting);
    }

    public SiteDefinition Def { get; }

    public string Site { get; }

    /// <summary>Korte omschrijving onder de naam, bv. "via de brug".</summary>
    public string Manier => Def.UseBridge ? "via de brug (Chrome)"
                          : Def.NeedsBrowser ? "via een browser"
                          : "rechtstreeks";

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    // ---------- prijs als tekst ----------

    private string _priceMin = "";
    public string PriceMin
    {
        get => _priceMin;
        set => SetProperty(ref _priceMin, value);
    }

    private string _priceMax = "";
    public string PriceMax
    {
        get => _priceMax;
        set => SetProperty(ref _priceMax, value);
    }

    private string _postcode = "";
    public string Postcode
    {
        get => _postcode;
        set => SetProperty(ref _postcode, value);
    }

    /// <summary>De straal in kilometer; als tekst, want een ComboBox vergelijkt tekst.</summary>
    private string _radius = "0";
    public string Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, value);
    }

    /// <summary>
    /// De waarden van de filters die enkel op deze site bestaan, met als sleutel
    /// <see cref="CustomFilter.Key"/>. Het venster bouwt de invoer ervoor in code op,
    /// met dezelfde bouwstenen als het zoekscherm (CustomFilterControls).
    ///
    /// Dit stond hier eerst niet, en dat was een echte fout: <see cref="ToSetting"/>
    /// maakte een nieuwe instelling zonder deze waarden, dus wie in dit venster op
    /// Bewaren klikte, wiste de eigen filters van elke site in de zoekopdracht.
    /// </summary>
    public Dictionary<string, string> Custom { get; } = new();

    // ---------- wat er van deze site te zien is ----------

    public bool ToonPostcode { get; }
    public bool ToonStraal { get; }

    /// <summary>Neemt de bewaarde waarden over.</summary>
    private void Neem(SiteSetting setting)
    {
        Enabled = setting.Enabled;
        PriceMin = setting.PriceMin?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        PriceMax = setting.PriceMax?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        Postcode = setting.Postcode;
        Radius = setting.RadiusKm.ToString(CultureInfo.InvariantCulture);

        foreach (var (sleutel, waarde) in setting.Custom) Custom[sleutel] = waarde;
    }

    /// <summary>Zet de ingevulde waarden om naar wat er bewaard wordt.</summary>
    public SiteSetting ToSetting() => new()
    {
        Site = Site,
        Enabled = Enabled,
        PriceMin = Getal(PriceMin),
        PriceMax = Getal(PriceMax),
        Postcode = ToonPostcode ? Postcode.Trim() : "",
        RadiusKm = ToonStraal && int.TryParse(Radius, out var km) ? km : 0,
        Custom = new Dictionary<string, string>(Custom)
    };

    /// <summary>
    /// Een prijs uit een tekstvak. Zowel de komma als de punt mag als
    /// decimaalteken: wie "12,50" typt bedoelt hetzelfde als wie "12.50" typt.
    /// </summary>
    private static decimal? Getal(string tekst)
    {
        tekst = tekst.Trim().Replace(',', '.');
        if (tekst.Length == 0) return null;

        return decimal.TryParse(tekst, NumberStyles.Number, CultureInfo.InvariantCulture, out var waarde)
            ? waarde
            : null;
    }

    /// <summary>Korte samenvatting van de ingestelde filters, voor naast de naam.</summary>
    public string Samenvatting
    {
        get
        {
            var delen = new List<string>();

            if (PriceMin.Length > 0 || PriceMax.Length > 0)
                delen.Add($"€{(PriceMin.Length > 0 ? PriceMin : "0")}-{(PriceMax.Length > 0 ? PriceMax : "")}");

            if (ToonPostcode && Postcode.Length > 0) delen.Add(Postcode);
            if (ToonStraal && Radius != "0") delen.Add($"{Radius} km");

            // Enkel filters die deze site nog kent: een oude sleutel uit een eerder
            // sitebestand telt niet mee.
            var eigen = Def.CustomFilters.Count(f => Custom.TryGetValue(f.Key, out var w) && w.Length > 0);
            if (eigen > 0) delen.Add(eigen == 1 ? "1 eigen filter" : $"{eigen} eigen filters");

            return delen.Count > 0 ? string.Join(" · ", delen) : "geen filters";
        }
    }

    /// <summary>Meldt dat de samenvatting herberekend moet worden.</summary>
    public void RefreshSamenvatting() => OnPropertyChanged(nameof(Samenvatting));
}

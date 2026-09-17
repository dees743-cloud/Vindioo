using CommunityToolkit.Mvvm.ComponentModel;
using Zentrix.Sources;

namespace Zentrix.Models;

/// <summary>
/// Eén tabblad in het zoekscherm: de site, of hij meezoekt, of zijn tab open
/// staat, en zijn eigen filterwaarden. Elke site heeft dus zijn eigen prijs,
/// postcode en straal — die staan niet meer één keer voor de hele app.
/// </summary>
public class SiteTab : ObservableObject
{
    public SiteTab(SiteDefinition definition, ISearchSource source)
    {
        Def = definition;
        Source = source;
    }

    private SiteTab()
    {
    }

    /// <summary>
    /// Het tabblad "Alles": geen site, maar alle resultaten samen. Het hoort bij
    /// geen enkele bron, dus het heeft geen beschrijving en geen motor — vandaar
    /// dat <see cref="Def"/> en <see cref="Source"/> leeg mogen zijn. Handig voor
    /// een snelle blik: zeven keer op een tab klikken om te zien wat er binnen is
    /// gekomen, is geen overzicht.
    /// </summary>
    public static SiteTab Alles() => new();

    public SiteDefinition? Def { get; }
    public ISearchSource? Source { get; }

    /// <summary>Is dit het tabblad met alle sites samen?</summary>
    public bool IsAll => Def is null;

    public string Name => Source?.Name ?? "Alles";

    /// <summary>
    /// De naam zoals hij op de tab staat. Een site mag een kortere schrijven in
    /// zijn bestand ("Facebook" in plaats van "Facebook Marketplace"): in de
    /// tabstrip staat alles naast elkaar en is breedte het schaarse goed. Overal
    /// elders blijft de volledige naam staan, want daar is die de identiteit.
    /// </summary>
    public string TabName => string.IsNullOrWhiteSpace(Def?.ShortName) ? Name : Def!.ShortName;

    /// <summary>
    /// De filters van deze site: elk tabblad houdt zijn eigen waarden bij. De
    /// sitegebonden filters - ook de provincies en veilinghuizen van AlleVeilingen -
    /// staan in <see cref="SearchFilters.Custom"/>.
    /// </summary>
    public SearchFilters Filters { get; } = new();

    /// <summary>
    /// De straal als tekst, want een ComboBox vergelijkt zijn Tag (tekst) met de
    /// gekozen waarde. Schrijft door naar <see cref="SearchFilters.RadiusKm"/>.
    /// </summary>
    public string RadiusText
    {
        get => Filters.RadiusKm.ToString();
        set
        {
            Filters.RadiusKm = int.TryParse(value, out var km) ? km : 0;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Hoeveel zoekertjes deze site hoogstens mag opleveren.
    ///
    /// Stond op honderd zolang dat ook het aantal op het scherm was. Sinds de
    /// resultaten over pagina's verdeeld worden, is dat geen goede rem meer: dan
    /// zou je nooit verder komen dan één pagina per site. Vijfhonderd is ruim
    /// genoeg om door te bladeren en houdt het ophalen binnen de perken; de echte
    /// veiligheidsgrens blijft het maximum aantal pagina's per site.
    /// </summary>
    private int _maxResults = 500;
    public int MaxResults
    {
        get => _maxResults;
        set => SetProperty(ref _maxResults, value);
    }

    /// <summary>Zoekt deze site mee? Dat is het vinkje naast de naam.</summary>
    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Staat dit tabblad open? Dan tonen we alleen de resultaten van deze site.</summary>
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>Aantal resultaten dat deze site bij de laatste zoekopdracht gaf.</summary>
    private int _resultCount;
    public int ResultCount
    {
        get => _resultCount;
        set => SetProperty(ref _resultCount, value);
    }

    /// <summary>
    /// Wat er bij de laatste zoekopdracht misliep op deze site, of leeg. De tab toont
    /// dan een waarschuwingsteken met deze tekst als tooltip. Vroeger stonden alle
    /// fouten achter elkaar in de statusregel, die bij de vensterrand afgeknipt werd:
    /// wat er bij de laatste sites misliep, viel gewoon buiten beeld.
    /// </summary>
    private string _errorText = "";
    public string ErrorText
    {
        get => _errorText;
        set
        {
            if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorText.Length > 0;
}

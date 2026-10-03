namespace Vindioo.Models;

/// <summary>Wat voor invoer een sitegebonden filter vraagt.</summary>
public enum CustomFilterKind
{
    /// <summary>Een keuze uit een vaste lijst; met <see cref="CustomFilter.Multiple"/> mogen er meerdere.</summary>
    Choice,

    /// <summary>Een vrij getal, bv. een bouwjaar of een vermogen.</summary>
    Number,

    /// <summary>Aan of uit; aangevinkt voegt het stukje URL toe met waarde 1.</summary>
    Toggle
}

/// <summary>
/// Waar een sitegebonden filter in het zoekscherm staat. Standaard bij de andere
/// filters van die site, achter de schuifregelaars. Een filter dat over de plaats
/// gaat - de provincies van AlleVeilingen - hoort bij de locatie: daar zoek je het.
/// </summary>
public enum CustomFilterSection
{
    /// <summary>In de popup met de filters van deze site.</summary>
    Site,

    /// <summary>In de popup voor locatie en afstand.</summary>
    Location
}

/// <summary>Eén keuze in een <see cref="CustomFilter"/>: wat de site wil zien en wat de gebruiker leest.</summary>
public class FilterChoice
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// Een filter dat maar op één site bestaat, beschreven in het sitebestand zelf.
///
/// De vaste filters (prijs, locatie, aantal, volgorde) kent de app van binnenuit,
/// want die betekenen overal hetzelfde. Maar AutoScout24 heeft brandstof,
/// kilometerstand, carrosserie en nog een pak meer, en een veilingsite heeft daar
/// niets aan. Zulke filters horen dus bij de site en niet in de code: je voegt er
/// een toe door een regel in een JSON-bestand te zetten, niet door de app aan te
/// passen.
///
/// <see cref="Fragment"/> is het stukje URL met {value} erin, net als bij de
/// vaste filters in <see cref="SiteDefinition.Filters"/>.
/// </summary>
public class CustomFilter
{
    /// <summary>Sleutel waaronder de gekozen waarde bewaard wordt; uniek binnen de site.</summary>
    public string Key { get; set; } = "";

    /// <summary>Wat de gebruiker leest, bv. "Brandstof".</summary>
    public string Label { get; set; } = "";

    public CustomFilterKind Kind { get; set; } = CustomFilterKind.Choice;

    /// <summary>Het stukje URL met {value}, bv. "&amp;fuel={value}".</summary>
    public string Fragment { get; set; } = "";

    /// <summary>Mogen er meerdere keuzes tegelijk aan staan?</summary>
    public bool Multiple { get; set; }

    /// <summary>Waarmee meerdere keuzes aan elkaar geplakt worden.</summary>
    public string Separator { get; set; } = ",";

    /// <summary>
    /// Wat er in de URL komt wanneer er niets gekozen is. Meestal leeg: dan komt er
    /// gewoon niets. Maar "niets gekozen" is niet voor elke site hetzelfde als "niets
    /// vermelden": AlleVeilingen gaf zonder "r":[] in zijn zoekblok 0 van de 30
    /// dezelfde kavels als met een lege lijst. Dan staat hier "r":[].
    /// </summary>
    public string EmptyFragment { get; set; } = "";

    /// <summary>Korte uitleg onder het filter; laat leeg als het duidelijk is.</summary>
    public string Hint { get; set; } = "";

    /// <summary>In welke popup het filter staat; standaard bij de filters van de site.</summary>
    public CustomFilterSection Section { get; set; } = CustomFilterSection.Site;

    /// <summary>De keuzes bij <see cref="CustomFilterKind.Choice"/>.</summary>
    public List<FilterChoice> Options { get; set; } = new();
}

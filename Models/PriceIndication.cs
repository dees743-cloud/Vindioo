using System.Globalization;

namespace Zentrix.Models;

/// <summary>Waarom een gevonden zoekertje wel of niet meetelt voor de marktwaarde.</summary>
public enum ComparableKind
{
    /// <summary>Hetzelfde model met een vraagprijs: telt mee.</summary>
    Counted,

    /// <summary>Hetzelfde modelnummer met een ander achtervoegsel, bv. DCD-520AE bij een DCD-520.</summary>
    Variant,

    /// <summary>Een toestel uit dezelfde reeks, bv. een DCD-895. Enkel bij het verbreden.</summary>
    Related,

    /// <summary>Een lopende veiling: de prijs is een bod dat nog kan stijgen.</summary>
    Auction,

    /// <summary>Verkocht samen met andere toestellen.</summary>
    Set,

    /// <summary>Toebehoren of een onderdeel vóór het model: "Afstandsbediening Denon RC-253 DCD-520".</summary>
    Accessory,

    /// <summary>Defect of voor onderdelen.</summary>
    Defect,

    /// <summary>Iemand die zelf zoekt.</summary>
    Wanted,

    /// <summary>Geen bedrag: bieden, zie beschrijving.</summary>
    NoPrice,

    /// <summary>Ligt ver buiten de rest (de gewone kwartielregel).</summary>
    Outlier,

    /// <summary>Gaat over iets anders: het model staat niet in de titel.</summary>
    Other
}

/// <summary>Eén gevonden zoekertje, met wat de prijsindicatie ervan denkt.</summary>
public class PriceComparable
{
    public required Listing Listing { get; init; }

    public ComparableKind Kind { get; set; }

    /// <summary>Het model zoals de app het in de titel las, bv. "DCD-520AE". Leeg zonder modelnummer.</summary>
    public string Model { get; set; } = "";

    public string PriceText => Listing.Price is { } p && p > 0 ? PriceRange.Euro(p) : "geen prijs";

    /// <summary>Wat er onder de titel staat: de site, en waarom het niet meetelt.</summary>
    public string Detail
    {
        get
        {
            var reden = Kind switch
            {
                ComparableKind.Auction => "veiling, huidig bod",
                ComparableKind.Set => "in een set",
                ComparableKind.Accessory => "toebehoren of onderdeel",
                ComparableKind.Defect => "defect of voor onderdelen",
                ComparableKind.Wanted => "iemand die zoekt",
                ComparableKind.NoPrice => "geen prijs",
                ComparableKind.Outlier => "uitschieter",
                ComparableKind.Related or ComparableKind.Variant when Model.Length > 0 => Model,
                _ => ""
            };

            var plaats = string.IsNullOrWhiteSpace(Listing.Location) ? "" : " · " + Listing.Location;
            return Listing.Source + plaats + (reden.Length > 0 ? " · " + reden : "");
        }
    }
}

/// <summary>Een prijsvork: de mediaan, en waar de middelste prijzen liggen.</summary>
public class PriceRange
{
    public int Count { get; init; }
    public decimal Median { get; init; }

    /// <summary>Vanaf vier prijzen het eerste kwartiel, daaronder de laagste prijs.</summary>
    public decimal Low { get; init; }

    /// <summary>Vanaf vier prijzen het derde kwartiel, daaronder de hoogste prijs.</summary>
    public decimal High { get; init; }

    private static readonly CultureInfo Belgisch = CultureInfo.GetCultureInfo("nl-BE");

    /// <summary>
    /// Een bedrag zoals een mens het leest: vanaf tien euro zonder centen (een marktwaarde
    /// van € 84,99 is schijnnauwkeurigheid), daaronder met.
    /// </summary>
    public static string Euro(decimal bedrag) =>
        bedrag >= 10
            ? "€ " + Math.Round(bedrag, MidpointRounding.AwayFromZero).ToString("#,0", Belgisch)
            : "€ " + bedrag.ToString("0.00", Belgisch);

    /// <summary>"€ 80 – € 90", of één bedrag als ze gelijk zijn.</summary>
    public string Vork => Low == High ? Euro(Low) : $"{Euro(Low)} – {Euro(High)}";
}

/// <summary>
/// Wat een prijsindicatie opleverde: de marktwaarde van het exacte model, de varianten apart,
/// en bij te weinig gegevens een verbreding naar toestellen uit dezelfde reeks. Zie
/// <c>Services.PriceIndicator</c> voor hoe dat bepaald wordt.
/// </summary>
public class PriceIndication
{
    /// <summary>Waarop gezocht werd, bv. "Denon DCD-520".</summary>
    public string Term { get; init; } = "";

    /// <summary>Het model uit de zoekterm, bv. "DCD-520". Leeg als de zoekterm er geen heeft.</summary>
    public string Model { get; set; } = "";

    public DateTime CheckedAt { get; init; } = DateTime.Now;

    /// <summary>Alles wat de sites teruggaven voor de zoekterm, behalve wat over iets anders ging.</summary>
    public List<PriceComparable> Items { get; } = new();

    /// <summary>Hoeveel treffers over iets anders gingen (een ander model, of het woord in de beschrijving).</summary>
    public int OtherCount { get; set; }

    /// <summary>De marktwaarde van het exacte model, of null als er geen enkele vraagprijs was.</summary>
    public PriceRange? Market { get; set; }

    /// <summary>Per variant (bv. "DCD-520AE") zijn eigen prijsvork.</summary>
    public Dictionary<string, PriceRange> Variants { get; } = new();

    /// <summary>De verbrede zoekterm, bv. "Denon DCD". Leeg als er niet verbreed werd.</summary>
    public string BroadTerm { get; set; } = "";

    /// <summary>De toestellen uit dezelfde reeks, bij het verbreden.</summary>
    public List<PriceComparable> BroadItems { get; } = new();

    public PriceRange? Broad { get; set; }

    /// <summary>Waarom er niet verbreed kon worden, als dat nodig was. Anders leeg.</summary>
    public string BroadNote { get; set; } = "";

    /// <summary>De sites die doorzocht werden.</summary>
    public List<string> SitesSearched { get; } = new();

    /// <summary>Per site wat er misliep, in gewone taal.</summary>
    public Dictionary<string, string> SiteErrors { get; } = new();

    /// <summary>Geen enkele site staat aan voor prijsindicatie.</summary>
    public bool NoSites { get; set; }

    /// <summary>Is de prijs van het zoekertje waarvoor dit gevraagd werd een bod?</summary>
    public bool OriginIsAuction { get; set; }
}

namespace Zentrix.Models;

/// <summary>
/// Waar een site het exacte sluitingstijdstip van haar veilingen in bulk geeft: een API die
/// voor een reeks ids tegelijk antwoordt. Staat dit blok in een sitebestand, dan vult
/// <c>DetailFetcher</c> na het zoeken de einddatum van alle zoekertjes van die site aan,
/// langs dezelfde weg als de site zelf (rechtstreeks, of via de brug).
///
/// Waarom: Catawiki zet op zijn kaarten enkel "Nog 3 dagen" of "Nog 21 uur". Daarmee kan je
/// sorteren, maar geen timer laten aftellen: de echte sluiting kan evengoed 23 uur later zijn.
/// De zoekpagina van Catawiki vraagt het exacte tijdstip zelf op bij
/// <c>/buyer/api/v3/bidding/lots?ids=...</c>, voor alle 24 kavels van de pagina samen. Wij
/// doen hetzelfde: één verzoek per 24 kavels.
///
/// Zo staat het in <c>catawiki.json</c>:
/// <code>
/// "EndTimeApi": {
///   "UrlTemplate": "https://www.catawiki.com/buyer/api/v3/bidding/lots?ids={ids}",
///   "BatchSize": 24, "ListPath": "lots", "IdPath": "id", "EndPath": "bidding_end_time" }
/// </code>
/// </summary>
public class EndTimeApiOptions
{
    /// <summary>
    /// Het adres, met <c>{ids}</c> waar de ids komen, met komma's ertussen. Het id is dat van
    /// het zoekertje (<c>ExternalId</c>), dus de site heeft een <c>IdPattern</c> nodig.
    /// </summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>Hoeveel ids er in één verzoek gaan. Catawiki vraagt er zelf 24 per keer.</summary>
    public int BatchSize { get; set; } = 24;

    /// <summary>Puntpad naar de lijst in het antwoord, bv. <c>lots</c>.</summary>
    public string ListPath { get; set; } = "";

    /// <summary>Puntpad naar het id binnen één element van die lijst.</summary>
    public string IdPath { get; set; } = "id";

    /// <summary>Puntpad naar het sluitingstijdstip, bv. <c>bidding_end_time</c> (ISO, in UTC).</summary>
    public string EndPath { get; set; } = "";
}

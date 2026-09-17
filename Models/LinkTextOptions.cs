namespace Zentrix.Models;

/// <summary>
/// De instellingen van de linkmotor (<see cref="SiteEngine.LinkText"/>), uit het
/// sitebestand. Die motor is er voor sites waarvan de opbouw niet met een selector
/// per veld te beschrijven is, omdat hun klassenamen versleuteld zijn en bij elke
/// update veranderen. Wat wel vast blijft, is de link naar een zoekertje; de motor
/// volgt die links (<see cref="SiteDefinition.ItemSelector"/>) en haalt prijs, titel
/// en plaats uit de tekstregels van zo'n link.
///
/// Tot september 2026 heette dit de Facebook-motor, met alles hieronder vast in de
/// code. Facebook Marketplace is nog altijd de enige site die hem gebruikt, maar de
/// app kent die site niet meer bij naam.
/// </summary>
public class LinkTextOptions
{
    /// <summary>
    /// Een reguliere expressie met één groep: het id van een zoekertje uit de link,
    /// bv. "/marketplace/item/(\d+)". Een link die er niet op past, is geen zoekertje.
    /// Leeg laten betekent: de volledige link is de identiteit.
    ///
    /// Waarom een id en niet gewoon de link: in de link staan volgcodes die bij elke
    /// keer laden anders zijn. Met de link als identiteit zou elk zoekertje bij elke
    /// beurt als nieuw tellen.
    /// </summary>
    public string IdPattern { get; set; } = "";

    /// <summary>
    /// De link naar een zoekertje, opgebouwd uit het id met {id}. Leeg laten betekent:
    /// de link zoals hij op de pagina staat, volledig gemaakt met de basis-URL.
    /// </summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>Een tekstregel met één van deze tekens of woorden is de prijs, bv. "€" en "Gratis".</summary>
    public List<string> PriceMarkers { get; set; } = new() { "€" };

    /// <summary>
    /// Een reguliere expressie waarop een plaatsnaam eindigt, bv. ",\s*(VLG|WAL|BRU)$".
    /// Past de titel erop en de plaats niet, dan zijn ze omgewisseld en zet de motor
    /// ze recht. Leeg laten betekent: niet controleren.
    /// </summary>
    public string LocationPattern { get; set; } = "";

    /// <summary>
    /// Tekst waaraan je ziet dat de site je naar het aanmeldscherm stuurde in plaats
    /// van de zoekresultaten te tonen. Hoofdletters tellen niet.
    /// </summary>
    public List<string> LoginMarkers { get; set; } = new();
}

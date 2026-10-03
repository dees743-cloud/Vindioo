using Zentrix.Models;

namespace Zentrix.Sources;

/// <summary>
/// Contract waar elke bron aan voldoet. Zolang een nieuwe site dit implementeert,
/// werkt hij automatisch mee in de rest van de app.
/// </summary>
public interface ISearchSource
{
    /// <summary>Naam zoals hij in het vinkje in de interface verschijnt.</summary>
    string Name { get; }

    /// <summary>
    /// Zoek op een term en geef genormaliseerde resultaten terug. De filters zijn
    /// optioneel: een bron gebruikt ze wanneer hij ze kent, en negeert de rest
    /// (de app filtert achteraf ook nog zelf als vangnet).
    /// </summary>
    /// <param name="progress">
    /// Optioneel. Bronnen die gaandeweg resultaten kunnen aanleveren (zoals sites
    /// via de brug) melden hier tussentijdse lijsten, zodat de app al iets kan
    /// tonen voor de pagina volledig geladen is. Dubbels mag de ontvanger zelf
    /// weglaten; de uiteindelijke lijst bevat sowieso alles.
    /// </param>
    Task<List<Listing>> SearchAsync(string query, int maxResults,
        SearchFilters? filters = null, IProgress<List<Listing>>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Deze site kan met dít zoekwoord niets aanvangen. Geen fout, maar een antwoord.
///
/// <para>AutoScout24 is het geval waar dit voor bestaat: die site heeft <b>geen vrije
/// tekstzoekfunctie</b>. Het zoekwoord ís het merk - <c>volkswagen</c>, <c>bmw/x5</c>,
/// <c>land-rover</c> - en het staat in het pad van de zoek-URL. Een woord dat geen merk is,
/// geeft daar een <b>404</b>. Gemeten op 3 oktober 2026: "volkswagen", "bmw/x5", "land-rover"
/// en een lege zoekterm geven 200, terwijl "cd", "cd speler" en "commodore" alle drie 404
/// geven.</para>
///
/// <para><b>Waarom dat een eigen soort verdient.</b> Vroeger werd dit een gewone mislukking, en
/// dat heeft gevolgen die niemand wil. Een bewaarde zoekopdracht naar "cd speler" met
/// AutoScout24 aangevinkt, zou bij elke beurt opnieuw mislukken: na twee beurten stuurt
/// <see cref="Services.SavedSearch"/> daarover een melding, en daarna blijft die zoekopdracht
/// voor altijd rood staan. Terwijl er niets stuk is - die site gaat gewoon niet over
/// cd-spelers. Een waarschuwing die nooit meer weggaat, leert je waarschuwingen negeren.</para>
///
/// <para>Wat er wél gebeurt: het staat op de tab van die site, en in de regel onderaan. Je weet
/// dus waarom er niets van AutoScout24 kwam, zonder dat de zoekopdracht kapot heet te zijn.</para>
/// </summary>
public class UnsupportedQueryException : Exception
{
    public UnsupportedQueryException(string message) : base(message) { }
}
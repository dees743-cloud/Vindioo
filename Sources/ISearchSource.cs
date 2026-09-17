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
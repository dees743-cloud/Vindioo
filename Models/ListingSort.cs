using System.Collections;

namespace Zentrix.Models;

/// <summary>In welke volgorde de resultaten op het scherm staan.</summary>
public enum ListingSort
{
    /// <summary>Zoals de sites ze aanleveren; bij hen is dat meestal relevantie.</summary>
    Default = 0,

    PriceAscending = 1,
    PriceDescending = 2,

    /// <summary>
    /// Eerst wat sinds de vorige beurt bijkwam, daarna nieuwste eerst (of bij een
    /// veiling wat het eerst sluit).
    /// </summary>
    Newest = 3
}

/// <summary>
/// Sorteert zoekertjes voor de weergave. Een gewone
/// <c>SortDescription</c> volstaat hier niet: zonder prijs of zonder datum zou
/// een zoekertje anders bovenaan belanden, en dat is precies waar je niet naar
/// wil kijken. Wat leeg is, gaat daarom altijd achteraan — in beide richtingen.
/// </summary>
public class ListingComparer : IComparer
{
    private readonly ListingSort _sort;

    public ListingComparer(ListingSort sort) => _sort = sort;

    public int Compare(object? x, object? y)
    {
        if (x is not Listing a || y is not Listing b) return 0;

        return _sort switch
        {
            ListingSort.PriceAscending => OpPrijs(a, b, omgekeerd: false),
            ListingSort.PriceDescending => OpPrijs(a, b, omgekeerd: true),
            ListingSort.Newest => OpDatum(a, b),
            _ => 0
        };
    }

    /// <summary>
    /// Heeft dit zoekertje een prijs om op te sorteren? Een nul telt niet mee.
    /// Bij 2dehands staat er nul in het prijsveld wanneer er "bieden" of "zie
    /// beschrijving" bedoeld wordt — van de honderd zoekertjes voor "iphone 13"
    /// hadden er negen een nul, waarvan er maar twee echt gratis waren. Zonder
    /// deze regel staat die hele hoop bovenaan zodra je op prijs sorteert, en
    /// dat is precies waar je niet naar wil kijken.
    /// </summary>
    private static bool HeeftPrijs(Listing l) => l.Price is > 0;

    private static int OpPrijs(Listing a, Listing b, bool omgekeerd)
    {
        // Zonder prijs achteraan, ongeacht de richting.
        if (!HeeftPrijs(a) && !HeeftPrijs(b)) return Terugval(a, b);
        if (!HeeftPrijs(a)) return 1;
        if (!HeeftPrijs(b)) return -1;

        var uitkomst = a.Price!.Value.CompareTo(b.Price!.Value);
        if (omgekeerd) uitkomst = -uitkomst;

        return uitkomst != 0 ? uitkomst : Terugval(a, b);
    }

    private static int OpDatum(Listing a, Listing b)
    {
        // Wat je nog niet gezien hebt, staat vooraan. Wie een zoekopdracht opvolgt,
        // bedoelt met "nieuwste" in de eerste plaats dát. Zonder deze regel
        // belandden de nieuwe kavels van Catawiki — die geen datum hebben —
        // helemaal achteraan, onder alles met een datum.
        if (a.IsNew != b.IsNew) return a.IsNew ? -1 : 1;

        if (a.Date is null && b.Date is null) return Terugval(a, b);
        if (a.Date is null) return 1;
        if (b.Date is null) return -1;

        var uitkomst = b.Date.Value.CompareTo(a.Date.Value);   // nieuwste eerst
        return uitkomst != 0 ? uitkomst : Terugval(a, b);
    }

    /// <summary>
    /// Gelijke waarden op de titel uit elkaar houden. Zonder deze terugval zet
    /// de weergave gelijke zoekertjes bij elke verversing in een andere volgorde,
    /// en dan lijkt de lijst te dansen.
    /// </summary>
    private static int Terugval(Listing a, Listing b) =>
        string.Compare(a.Key, b.Key, StringComparison.Ordinal);
}

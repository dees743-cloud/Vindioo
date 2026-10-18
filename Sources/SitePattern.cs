using System.Text.RegularExpressions;

namespace Vindioo.Sources;

/// <summary>
/// Reguliere expressies die uit een <b>sitebestand</b> komen, met een tijdslimiet erop.
///
/// <para>Waarom dit bestaat: een sitebestand is bedoeld om te delen, en zo'n bestand mag een
/// patroon meebrengen (<c>IdPattern</c>, <c>LocationPattern</c>, <c>::match(...)</c>). Een
/// patroon met geneste herhaling kan op een ongelukkige invoer exponentieel lang doorzoeken -
/// "catastrophic backtracking". Zonder limiet loopt zo'n match niet vast met een fout maar
/// draait hij gewoon door, en bij <c>HistoryStore.ApplyIdPatterns</c> gebeurt dat op de
/// UI-draad bij élke start. Eén bestand legt dan het venster plat, telkens opnieuw.</para>
///
/// <para>Een patroon dat de app <b>zelf</b> opbouwt hoort hier niet thuis; dat is geen
/// invoer van buiten. Vandaar dat <c>PriceIndicator</c>, <c>SiteAnalyzer</c> en de
/// cookieknoppen in <c>BrowserFetcher</c> hun eigen <c>Regex</c> houden.</para>
/// </summary>
public static class SitePattern
{
    /// <summary>
    /// Hoe lang één match hoogstens mag duren.
    ///
    /// Ruim genoeg voor elk echt patroon - de zwaarste in de dertien sitebestanden blijft ver
    /// onder een milliseconde - en kort genoeg om niet als vastloper te voelen.
    /// </summary>
    public static readonly TimeSpan Grens = TimeSpan.FromMilliseconds(250);

    /// <summary>Wat er al gemeld is, zodat het logboek niet volloopt met dezelfde regel.</summary>
    private static readonly HashSet<string> Gemeld = new();

    /// <summary>
    /// Bouwt een patroon uit een sitebestand, met de tijdslimiet erop. Geeft null bij een leeg
    /// of ongeldig patroon - de beller valt dan terug op zijn gewone gedrag.
    /// </summary>
    /// <param name="patroon">Het patroon zoals het in het sitebestand staat.</param>
    /// <param name="waar">Naam van de site en het veld, voor in het logboek.</param>
    /// <param name="opties">Extra opties; de tijdslimiet komt er altijd bij.</param>
    public static Regex? Bouw(string? patroon, string waar, RegexOptions opties = RegexOptions.None)
    {
        if (string.IsNullOrWhiteSpace(patroon)) return null;

        try
        {
            return new Regex(patroon, opties, Grens);
        }
        catch (ArgumentException ex)
        {
            Meld($"{waar}: '{patroon}' is geen geldig patroon - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Eén keer per patroon in het logboek. Een ongeldig of traag patroon zit in élk zoekertje
    /// van die site, dus zonder dit staat er honderd keer dezelfde regel.
    /// </summary>
    public static void Meld(string regel)
    {
        lock (Gemeld)
            if (Gemeld.Add(regel))
                Services.Log.Write(regel);
    }
}

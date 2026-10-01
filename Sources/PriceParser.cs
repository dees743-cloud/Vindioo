using System.Globalization;
using System.Text.RegularExpressions;

namespace Zentrix.Sources;

/// <summary>
/// Leest een prijs uit de tekst die een site toont: "€ 1.499,95", "1 499 €", "12.50", "€ 175".
///
/// Dit stond tot 1 oktober 2026 <b>twee keer</b> in de code - één keer in de gewone motor en
/// één keer in de linkmotor - en die twee waren niet gelijk. Dat is precies hoe stille fouten
/// ontstaan: een zoekertje dat op de ene site een prijs heeft en op de andere niet, zonder dat
/// er iets in het logboek komt. Nu is er één lezer, en beide motoren roepen die aan.
/// </summary>
internal static class PriceParser
{
    /// <summary>Het eerste stuk dat op een bedrag lijkt, met zijn scheidingstekens.</summary>
    private static readonly Regex Bedrag = new(@"\d[\d.,\s]*", RegexOptions.Compiled);

    /// <summary>
    /// Elke soort witruimte, en dat is de kern van de zaak.
    ///
    /// <c>\s</c> van .NET dekt de hele Unicode-categorie <c>\p{Z}</c>, dus ook de <b>harde</b>
    /// spatie (U+00A0) en de smalle harde spatie (U+202F). Franse en Spaanse sites scheiden hun
    /// duizendtallen daarmee: "1 499 €". De regex hierboven pikte die dus netjes op, maar het
    /// opschonen deed <c>Replace(" ", "")</c> en dat raakt enkel de gewone spatie U+0020. Wat
    /// overbleef was "1<U+00A0>499", daar kon <c>decimal.TryParse</c> niets mee, en de prijs
    /// werd <b>null</b>. Gevolg: zo'n zoekertje glipt door de prijsgrens heen en valt uit de
    /// prijsindicatie - zonder fout, gewoon zonder prijs.
    /// </summary>
    private static readonly Regex Witruimte = new(@"\s", RegexOptions.Compiled);

    public static decimal? Parse(string? tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return null;

        var raak = Bedrag.Match(tekst);
        if (!raak.Success) return null;

        var kaal = Witruimte.Replace(raak.Value, "").TrimEnd('.', ',');
        if (kaal.Length == 0) return null;

        var punten = kaal.Count(c => c == '.');

        if (kaal.Contains(','))
        {
            // Nederlandse notatie: de punt is een duizendtal, de komma is decimaal.
            kaal = kaal.Replace(".", "").Replace(',', '.');
        }
        else if (punten > 1 || (punten == 1 && kaal[(kaal.IndexOf('.') + 1)..].Length == 3))
        {
            // Geen komma. Dan is een punt enkel een duizendtal wanneer er precies drie cijfers
            // achter staan ("1.499"), of wanneer er meerdere punten staan ("1.234.567"). Anders
            // is het een decimaalteken, en is "12.50" twaalf euro vijftig - niet twaalfduizend
            // vijfhonderd. Die regel stond enkel in de gewone motor; de linkmotor haalde de punt
            // er altijd uit, en maakte van elke prijs met centen een bedrag van honderd keer te
            // veel.
            kaal = kaal.Replace(".", "");
        }

        return decimal.TryParse(kaal, NumberStyles.Any, CultureInfo.InvariantCulture, out var waarde)
            ? waarde
            : null;
    }
}

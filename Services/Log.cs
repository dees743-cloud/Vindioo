using System.IO;
using System.Text.RegularExpressions;

namespace Vindioo.Services;

/// <summary>
/// Eenvoudig logboek in een tekstbestand, zodat achteraf te zien is waar iets
/// misliep — vooral handig bij de brug, waar de helft in Chrome gebeurt en dus
/// niet in de debugger te volgen is. Eén regel per gebeurtenis, met tijdstempel.
/// </summary>
public static class Log
{
    private static readonly object Lock = new();

    /// <summary>Het logbestand; ligt naast de andere gebruikersgegevens.</summary>
    public static string FilePath { get; } = AppPaths.LogFile;

    /// <summary>Boven deze grootte begint het logboek opnieuw.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) File.Delete(FilePath);

                File.AppendAllText(FilePath,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Loggen mag de app nooit tegenhouden.
        }
    }

    /// <summary>
    /// Een URL zoals hij in het logboek mag staan: met de plaatsgebonden stukken eruit.
    ///
    /// <para>Waarom dit bestaat: de volledige zoek-URL in het logboek is goud bij het zoeken
    /// naar een fout - je ziet precies welke pagina gevraagd werd, en je kan ze plakken in een
    /// browser. Maar de filters staan erin, en daar zit een postcode tussen. De app wijst de
    /// gebruiker bij een fout zélf naar dit logboek, en Vindioo is een openbare repo die
    /// issues uitnodigt. De eerste die zijn logboek in een issue plakt, geeft bij benadering
    /// zijn woonplaats weg - precies het gegeven waarvoor vindioo-sites privé staat.</para>
    ///
    /// <para>Nagemeten op 4 oktober 2026 in een logboek van 12 105 regels: negen regels
    /// droegen een postcode of een Duitse zip, en alle negen kwamen van de twee
    /// "pagina N -&gt; " regels in <c>GenericSources</c>.</para>
    ///
    /// <para>Enkel de WAARDE gaat weg, niet de naam van de parameter: dát de zoek-URL een
    /// postcode droeg, is zelf nuttig om te weten.</para>
    /// </summary>
    /// <param name="sjabloon">
    /// De <c>SearchUrlTemplate</c> van de site, als die bij de hand is. Wat daarin als lang
    /// getal staat, is een INSTELLING en geen zoekertje: Facebook draagt zijn regionummer in
    /// het pad, en dat benadert een woonplaats. Van de dertien sitebestanden is Facebook het
    /// enige met zo'n getal in zijn sjabloon (gemeten 4 oktober 2026), dus deze regel raakt
    /// verder niets.
    ///
    /// Zonder sjabloon blijven lange getallen staan, en dat hoort ook: van de 22 zulke URL's
    /// in het logboek waren het er 22 een zoekertje-id (/itm/128096739486, /nl/l/104894297-…),
    /// en zonder dat nummer is zo'n regel niet meer na te spelen.
    /// </param>
    public static string Url(string url, string? sjabloon = null)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var uit = PlaatsFilter.Replace(url, m => m.Groups[1].Value + "…");

        if (!string.IsNullOrEmpty(sjabloon))
            foreach (Match getal in LangGetal.Matches(sjabloon))
                uit = uit.Replace(getal.Value, "…");

        return uit;
    }

    /// <summary>
    /// De parameters die een plaats verraden. De eerste vijf komen echt voor in de
    /// sitebestanden (postcode en distanceMeters bij 2dehands en Marktplaats, zip en zipr bij
    /// kleinanzeigen, radius bij AutoScout24); de rest staat erbij voor een volgende site, want
    /// een maskeerder die pas werkt nadat er iets gelekt is, komt te laat.
    /// </summary>
    private static readonly Regex PlaatsFilter = new(
        @"([?&](?:postcode|zip|zipr|plz|radius|radiusMeters|distance|distanceMeters|" +
        @"location|locatie|plaats|place|lat|lon|latitude|longitude)=)[^&#]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LangGetal = new(@"[0-9]{4,}", RegexOptions.Compiled);
}

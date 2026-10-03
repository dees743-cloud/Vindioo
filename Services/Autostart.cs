using System.IO;
using Microsoft.Win32;

namespace Vindioo.Services;

/// <summary>
/// Zet de app mee in de opstart van Windows. Dat gebeurt met een waarde onder
/// HKEY_CURRENT_USER — dus enkel voor deze gebruiker, zonder beheerdersrechten
/// en zonder iets aan het systeem te veranderen dat een andere gebruiker raakt.
///
/// Dit hoort bij het automatisch zoeken: een schema van "elk uur" heeft weinig
/// zin wanneer de app pas draait nadat je hem zelf opent.
/// </summary>
public static class Autostart
{
    private const string Sleutel = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Naam = "Vindioo";

    /// <summary>
    /// Hoe deze waarde vroeger heette, nieuwste eerst. <b>Niet weghalen.</b>
    ///
    /// Een hernoeming zonder dit zou twee dingen tegelijk fout doen: het vinkje "opstarten met
    /// Windows" staat ineens uit terwijl de gebruiker het aanzette, én de oude waarde blijft in
    /// het register staan en wijst naar de oude exe. Windows start die dan elke keer gewoon mee
    /// op - een app die je dacht hernoemd te hebben, die bij elke aanmelding terugkomt onder
    /// zijn oude naam en naar een lege gegevensmap kijkt. Zie <see cref="NeemOudeOver"/>.
    /// </summary>
    private static readonly string[] OudeNamen = { "Zentrix", "Zoekhulp" };

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Sleutel);
                return key?.GetValue(Naam) is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Zet het pad in het register gelijk met de exe die nu draait, als opstarten met
    /// Windows aanstaat. Het register bewaart het volledige pad, en sinds de exe
    /// Vindioo.exe heet in plaats van Zentrix.exe wees dat naar een bestand dat niet
    /// meer bestaat: Windows start dan stilletjes niets. Hetzelfde gebeurt wanneer je
    /// de app naar een andere map verplaatst.
    /// </summary>
    public static void RefreshPath()
    {
        NeemOudeOver();

        var exe = ExePad();
        if (exe is null) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Sleutel);
            if (key?.GetValue(Naam) is not string huidig) return;             // staat uit
            if (huidig.Contains(exe, StringComparison.OrdinalIgnoreCase)) return;  // klopt al
        }
        catch
        {
            return;
        }

        if (Set(true))
            Log.Write("opstarten met Windows: pad bijgewerkt naar " + exe);
    }

    /// <summary>
    /// Stond de app onder een oude naam in de opstart van Windows, neem dat dan over en haal
    /// de oude waarde weg.
    ///
    /// <para>Het weghalen is het belangrijkste deel, en niet het overnemen. Een waarde die
    /// blijft staan, wijst naar de oude exe: Windows start die bij elke aanmelding mee op,
    /// naast de nieuwe. Je krijgt dan twee apps die allebei denken dat ze de enige zijn - ze
    /// hebben immers elk hun eigen slot - waarvan er één naar een gegevensmap kijkt die
    /// intussen verhuisd is.</para>
    ///
    /// Gebeurt bij elke start, niet één keer: wie een oude versie nog eens opent nadat hij al
    /// overgestapt was, zet die waarde zo weer terug.
    /// </summary>
    private static void NeemOudeOver()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Sleutel, writable: true);
            if (key is null) return;

            var stondAan = key.GetValue(Naam) is not null;

            foreach (var oud in OudeNamen)
            {
                if (key.GetValue(oud) is null) continue;

                key.DeleteValue(oud, throwOnMissingValue: false);
                Log.Write($"opstarten met Windows: de oude registerwaarde '{oud}' is weggehaald");

                // Enkel wanneer het nog niet onder de nieuwe naam stond. Anders zou een
                // achtergebleven oude waarde het vinkje weer kunnen aanzetten.
                if (!stondAan)
                {
                    stondAan = true;
                    Set(true);
                    Log.Write("opstarten met Windows stond aan onder de oude naam en is overgenomen");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("de oude opstartwaarde kon niet nagekeken worden - " + ex.Message);
        }
    }

    /// <summary>
    /// Het pad van Vindioo.exe. Wordt de app gestart als "dotnet Vindioo.dll" - zo doen
    /// testprogramma's en scripts het - dan is het proces dotnet.exe, en dan stond er in het
    /// register "dotnet.exe --systeemvak": Windows startte daarmee niets. Dan nemen we de
    /// Vindioo.exe naast de dll, en als die er niet is, niets (en laten we het register staan).
    /// </summary>
    internal static string? ExePad()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return null;

        if (!Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return exe;

        var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var naast = string.IsNullOrEmpty(dll) ? null : Path.ChangeExtension(dll, ".exe");

        return naast is not null && File.Exists(naast) &&
               Path.GetFileNameWithoutExtension(naast).Equals("Vindioo", StringComparison.OrdinalIgnoreCase)
            ? naast
            : null;
    }

    /// <summary>Zet het aan of uit; geeft terug of dat gelukt is.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Sleutel, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(Naam, throwOnMissingValue: false);
                return true;
            }

            var exe = ExePad();
            if (exe is null) return false;

            // Met een vlag erachter, zodat de app weet dat hij door Windows
            // gestart is en meteen naar het systeemvak mag.
            key.SetValue(Naam, $"\"{exe}\" --systeemvak");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("opstarten met Windows kon niet gezet worden - " + ex.Message);
            return false;
        }
    }
}

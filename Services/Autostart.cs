using System.IO;
using Microsoft.Win32;

namespace Zentrix.Services;

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
    private const string Naam = "Zentrix";

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
    /// Zentrix.exe heet in plaats van zoekhulp.exe wees dat naar een bestand dat niet
    /// meer bestaat: Windows start dan stilletjes niets. Hetzelfde gebeurt wanneer je
    /// de app naar een andere map verplaatst.
    /// </summary>
    public static void RefreshPath()
    {
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
    /// Het pad van Zentrix.exe. Wordt de app gestart als "dotnet Zentrix.dll" - zo doen
    /// testprogramma's en scripts het - dan is het proces dotnet.exe, en dan stond er in het
    /// register "dotnet.exe --systeemvak": Windows startte daarmee niets. Dan nemen we de
    /// Zentrix.exe naast de dll, en als die er niet is, niets (en laten we het register staan).
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
               Path.GetFileNameWithoutExtension(naast).Equals("Zentrix", StringComparison.OrdinalIgnoreCase)
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

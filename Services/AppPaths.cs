using System.IO;

namespace Zentrix.Services;

/// <summary>
/// Waar de app haar gegevens bewaart: sites, databank, instellingen, logboek,
/// brug-code en het browserprofiel. Eén plaats, zodat er ook maar één plaats is
/// om te wijzigen.
///
/// Waarom dit er is: tot september 2026 heette de app in de code "zoekhulp", en
/// zeven klassen schreven elk zelf "%APPDATA%\Zoekhulp" uit. Bij het hernoemen naar
/// Zentrix moest die map mee. Zomaar een nieuwe naam gebruiken had betekend dat de
/// app leeg opstart - geen sites, geen zoekopdrachten, geen favorieten - terwijl
/// alles nog gewoon in de oude map staat.
///
/// Deze klasse gebruikt het logboek bewust NIET. Het logboek vraagt zijn eigen pad
/// hier op; een verhuis die zelf wil loggen, zou dus op zichzelf wachten. Wat er
/// gebeurde staat in <see cref="MigrationNote"/>, en App schrijft dat weg zodra
/// het kan.
/// </summary>
public static class AppPaths
{
    private const string LegacyName = "Zoekhulp";
    private const string CurrentName = "Zentrix";

    // Lazy: de map wordt pas bepaald - en zo nodig verhuisd - bij de eerste vraag,
    // en daarna nooit meer. Wie het eerst vraagt maakt niet uit, zolang niemand de
    // map buiten deze klasse om opent.
    private static readonly Lazy<string> Resolved = new(ResolveFolder);

    /// <summary>De map met alle gegevens van deze gebruiker.</summary>
    public static string Folder => Resolved.Value;

    /// <summary>Wat er bij de eerste vraag met de oude map gebeurde; leeg als er niets te doen was.</summary>
    public static string MigrationNote { get; private set; } = "";

    public static string SitesFolder => Path.Combine(Folder, "sites");
    public static string SettingsFile => Path.Combine(Folder, "instellingen.json");
    public static string BridgeCodeFile => Path.Combine(Folder, "brug-code.txt");
    public static string BrowserProfile => Path.Combine(Folder, "browser-profiel");

    // Bij deze twee kan het hernoemen in de map mislukt zijn terwijl de map zelf wel
    // verhuisde. Dan de oude naam blijven gebruiken, anders begint de app met een
    // lege databank naast de volle.
    public static string DatabaseFile => CurrentOrLegacy("zentrix.db", "zoekhulp.db");
    public static string LogFile => CurrentOrLegacy("zentrix-log.txt", "zoekhulp-log.txt");

    private static string CurrentOrLegacy(string current, string legacy)
    {
        var nieuw = Path.Combine(Folder, current);
        var oud = Path.Combine(Folder, legacy);

        return !File.Exists(nieuw) && File.Exists(oud) ? oud : nieuw;
    }

    private static string ResolveFolder()
    {
        // Een andere gegevensmap, zonder verhuis. Handig om een lege eerste start na te
        // bootsen zonder aan je eigen sites en zoekopdrachten te komen:
        //     set ZENTRIX_DATA=C:\ergens\leeg
        var eigen = Environment.GetEnvironmentVariable("ZENTRIX_DATA");
        if (!string.IsNullOrWhiteSpace(eigen))
        {
            Directory.CreateDirectory(eigen);
            return eigen;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nieuw = Path.Combine(root, CurrentName);
        var oud = Path.Combine(root, LegacyName);

        // Al verhuisd, of een nieuwe installatie zonder oude gegevens.
        if (Directory.Exists(nieuw) || !Directory.Exists(oud))
        {
            Directory.CreateDirectory(nieuw);
            return nieuw;
        }

        try
        {
            // Op dezelfde schijf is dit een naamswijziging, geen kopie: de 540 MB van
            // het browserprofiel gaan mee in een oogwenk, en er staat nooit iets half.
            Directory.Move(oud, nieuw);

            RenameInside(nieuw, "zoekhulp.db", "zentrix.db");
            RenameInside(nieuw, "zoekhulp-log.txt", "zentrix-log.txt");

            MigrationNote = $"gegevensmap verhuisd van {oud} naar {nieuw}";
            return nieuw;
        }
        catch (Exception ex)
        {
            // Meestal een bestand in gebruik, bv. door een achtergebleven Chrome van
            // Playwright. Dan niets forceren: gewoon de oude map blijven gebruiken en
            // het bij de volgende start opnieuw proberen.
            MigrationNote = $"gegevensmap kon niet verhuizen ({ex.Message}); " +
                            $"de app gebruikt voorlopig {oud} en probeert het bij de volgende start opnieuw";
            return oud;
        }
    }

    private static void RenameInside(string folder, string from, string to)
    {
        try
        {
            var van = Path.Combine(folder, from);
            var naar = Path.Combine(folder, to);

            if (File.Exists(van) && !File.Exists(naar)) File.Move(van, naar);
        }
        catch
        {
            // Geen ramp: CurrentOrLegacy valt dan terug op de oude bestandsnaam.
        }
    }
}

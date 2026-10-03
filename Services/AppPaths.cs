using System.IO;

namespace Vindioo.Services;

/// <summary>
/// Waar de app haar gegevens bewaart: sites, databank, instellingen, logboek,
/// brug-code en het browserprofiel. Eén plaats, zodat er ook maar één plaats is
/// om te wijzigen.
///
/// Waarom dit er is: deze app is al <b>twee keer</b> hernoemd. Tot september 2026 heette ze
/// "Zoekhulp", daarna "Zentrix", en sinds 3 oktober 2026 "Vindioo". Bij elke hernoeming moet de
/// gegevensmap mee. Zomaar een nieuwe naam gebruiken zou betekenen dat de app leeg opstart -
/// geen sites, geen zoekopdrachten, geen favorieten - terwijl alles nog gewoon in de oude map
/// staat. Dat is het soort fout waar niemand een foutmelding van ziet.
///
/// <para><b>De namen hieronder zijn geen rommel die weg mag.</b> Elke oude naam die hier
/// geschrapt wordt, is een gebruiker die zijn gegevens kwijt is. Ze staan nieuwste eerst, want
/// wie van Zentrix komt heeft daar zijn verse gegevens staan; een map "Zoekhulp" is dan hoogstens
/// een leeg restant van jaren terug.</para>
///
/// Deze klasse gebruikt het logboek bewust NIET. Het logboek vraagt zijn eigen pad
/// hier op; een verhuis die zelf wil loggen, zou dus op zichzelf wachten. Wat er
/// gebeurde staat in <see cref="MigrationNote"/>, en App schrijft dat weg zodra
/// het kan.
/// </summary>
public static class AppPaths
{
    /// <summary>De naam van nu. Hier staat de mapnaam, niet de naam van het product.</summary>
    private const string CurrentName = "Vindioo";

    /// <summary>
    /// Hoe de map vroeger heette, <b>nieuwste eerst</b>. De eerste die bestaat, verhuist mee.
    /// </summary>
    private static readonly string[] LegacyNames = { "Zentrix", "Zoekhulp" };

    /// <summary>De databank en het logboek, met hun oude namen erachter - zelfde volgorde.</summary>
    private static readonly string[] DatabaseNames = { "vindioo.db", "zentrix.db", "zoekhulp.db" };
    private static readonly string[] LogNames = { "vindioo-log.txt", "zentrix-log.txt", "zoekhulp-log.txt" };

    /// <summary>
    /// Een andere gegevensmap aanwijzen, zonder verhuis. Handig om een lege eerste start na te
    /// bootsen zonder aan je eigen sites en zoekopdrachten te komen, en het is wat de controles
    /// gebruiken: <c>set VINDIOO_DATA=C:\ergens\leeg</c>.
    /// </summary>
    public const string DataVariable = "VINDIOO_DATA";

    /// <summary>
    /// Hoe die variabele vroeger heette. <b>Niet weghalen</b>, en wel hierom: wie hem nog ergens
    /// gezet heeft - in een snelkoppeling, een script, een omgeving van een testopstelling -
    /// zou er bij het hernoemen stilletjes náást vallen, en dan wijst de app naar de ECHTE
    /// gegevensmap terwijl hij juist bedoeld was om daar vanaf te blijven. Dat is geen klein
    /// ongemak: dan draait een proef op je eigen favorieten en zoekopdrachten.
    /// </summary>
    private static readonly string[] OudeDataVariabelen = { "ZENTRIX_DATA", "ZOEKHULP_DATA" };

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
    public static string DatabaseFile => EersteBestaande(DatabaseNames);
    public static string LogFile => EersteBestaande(LogNames);

    /// <summary>
    /// Het eerste bestand uit de rij dat bestaat, en anders het eerste van de rij - de naam van
    /// nu. Zo wint een bestaand <c>zentrix.db</c> van een <c>vindioo.db</c> dat er nog niet is.
    /// </summary>
    private static string EersteBestaande(string[] namen)
    {
        foreach (var naam in namen)
        {
            var pad = Path.Combine(Folder, naam);
            if (File.Exists(pad)) return pad;
        }

        return Path.Combine(Folder, namen[0]);
    }

    private static string ResolveFolder()
    {
        var eigen = Environment.GetEnvironmentVariable(DataVariable);

        // De oude naam telt ook mee. Niet als gelijke: de nieuwe wint wanneer allebei gezet
        // zijn, zodat er geen twijfel is welke het doet.
        if (string.IsNullOrWhiteSpace(eigen))
            eigen = OudeDataVariabelen
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(waarde => !string.IsNullOrWhiteSpace(waarde));

        if (!string.IsNullOrWhiteSpace(eigen))
        {
            Directory.CreateDirectory(eigen);
            return eigen;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var map = Kies(root, out var verslag);

        MigrationNote = verslag;
        return map;
    }

    /// <summary>
    /// Welke map het wordt, en wat er onderweg met een oude map gebeurde.
    ///
    /// Apart van <see cref="ResolveFolder"/> en met de wortel als invoer, zodat dit <b>na te
    /// meten</b> is zonder aan <c>%APPDATA%</c> te komen. Dat was het niet toen de app van
    /// Zoekhulp naar Zentrix ging, en bij een verhuizing van iemands favorieten en
    /// zoekopdrachten is "het zal wel kloppen" niet goed genoeg.
    /// </summary>
    internal static string Kies(string root, out string verslag)
    {
        verslag = "";

        var nieuw = Path.Combine(root, CurrentName);

        // Al verhuisd: klaar. Dit staat vooraan, zodat een achtergebleven oude map nooit
        // een tweede keer over een bestaande nieuwe heen gaat.
        if (Directory.Exists(nieuw)) return nieuw;

        var oud = LegacyNames
            .Select(naam => Path.Combine(root, naam))
            .FirstOrDefault(Directory.Exists);

        // Een nieuwe installatie, zonder oude gegevens.
        if (oud is null)
        {
            Directory.CreateDirectory(nieuw);
            return nieuw;
        }

        try
        {
            // Op dezelfde schijf is dit een naamswijziging, geen kopie: de 540 MB van
            // het browserprofiel gaan mee in een oogwenk, en er staat nooit iets half.
            Directory.Move(oud, nieuw);

            HernoemBinnen(nieuw, DatabaseNames);
            HernoemBinnen(nieuw, LogNames);

            verslag = $"gegevensmap verhuisd van {oud} naar {nieuw}";
            return nieuw;
        }
        catch (Exception ex)
        {
            // Meestal een bestand in gebruik, bv. door een achtergebleven Chrome van
            // Playwright. Dan niets forceren: gewoon de oude map blijven gebruiken en
            // het bij de volgende start opnieuw proberen.
            verslag = $"gegevensmap kon niet verhuizen ({ex.Message}); " +
                      $"de app gebruikt voorlopig {oud} en probeert het bij de volgende start opnieuw";
            return oud;
        }
    }

    /// <summary>
    /// Geeft het eerste bestand uit de rij dat nog een oude naam draagt, de naam van nu.
    /// Bestaat die naam al, dan blijft alles staan: twee databanken samenvoegen kan deze
    /// klasse niet, en de verkeerde overschrijven is erger dan er een laten staan.
    /// </summary>
    private static void HernoemBinnen(string folder, string[] namen)
    {
        try
        {
            var naar = Path.Combine(folder, namen[0]);
            if (File.Exists(naar)) return;

            foreach (var oude in namen.Skip(1))
            {
                var van = Path.Combine(folder, oude);

                if (File.Exists(van))
                {
                    File.Move(van, naar);
                    return;
                }
            }
        }
        catch
        {
            // Geen ramp: EersteBestaande valt dan terug op de oude bestandsnaam.
        }
    }
}

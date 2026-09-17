using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zentrix.Services;

/// <summary>Waar een melding heen gaat wanneer de planner iets nieuws vindt.</summary>
public class NotifySettings
{
    /// <summary>Een ballon bij het pictogram in het systeemvak. Vraagt geen instellingen.</summary>
    public bool Tray { get; set; } = true;

    // ---------- Telegram ----------

    public bool Telegram { get; set; }

    /// <summary>Het token dat @BotFather geeft, in de vorm 1234:AA...</summary>
    public string TelegramToken { get; set; } = "";

    /// <summary>Het nummer van de chat waar de bot naartoe schrijft.</summary>
    public string TelegramChatId { get; set; } = "";

    /// <summary>
    /// De foto meesturen in plaats van enkel tekst. Op een telefoon is het de
    /// foto die bepaalt of je erop klikt. Kost wel één bericht per zoekertje,
    /// dus wie het rustig wil houden zet dit uit.
    /// </summary>
    public bool TelegramPhotos { get; set; } = true;

    // ---------- e-mail ----------

    public bool Email { get; set; }

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;

    /// <summary>Gebruikersnaam bij de mailserver; meestal het e-mailadres zelf.</summary>
    public string SmtpUser { get; set; } = "";

    /// <summary>
    /// Het wachtwoord bij de mailserver. Bij Gmail is dat een app-wachtwoord en
    /// niet het gewone wachtwoord. Staat versluierd in het instellingenbestand
    /// (zie <see cref="AppSettings"/>); dat is geen echte beveiliging, maar het
    /// houdt het wel uit het zicht.
    /// </summary>
    public string SmtpPassword { get; set; } = "";

    public string MailTo { get; set; } = "";

    /// <summary>Is er minstens één kanaal dat echt kan versturen?</summary>
    [JsonIgnore]
    public bool AnyConfigured =>
        Tray ||
        (Telegram && TelegramToken.Length > 0 && TelegramChatId.Length > 0) ||
        (Email && SmtpHost.Length > 0 && MailTo.Length > 0);
}

/// <summary>
/// Instellingen van de app zelf, los van de sites en de zoekopdrachten: hoe het
/// venster zich gedraagt, welke weergave je koos en waar meldingen heen gaan.
///
/// Tot nu toe was er nergens een plaats voor dit soort dingen — de gekozen
/// weergave ging bij elke herstart verloren. Eén JSON-bestand naast de rest van
/// de gebruikersgegevens lost dat op.
/// </summary>
public class AppSettings
{
    private static readonly string Path_ = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>De enige instantie; de hele app leest en schrijft hier.</summary>
    public static AppSettings Current { get; private set; } = new();

    // ---------- venster ----------

    /// <summary>
    /// Bij minimaliseren naar het systeemvak in plaats van de taakbalk.
    ///
    /// Staat sinds september 2026 standaard UIT. Het kruisje verbergt de app al
    /// (zie <see cref="CloseToTray"/>), dus voor de planner is een tweede verberg-knop
    /// niet nodig - en wie op minimaliseren klikt, verwacht de app in de taakbalk terug
    /// te vinden, niet verstopt achter het pijltje bij de klok.
    /// </summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>
    /// Het kruisje verbergt het venster in plaats van de app af te sluiten.
    /// Dat is wat je wil zodra er zoekopdrachten op een schema staan: anders
    /// stopt met het venster ook de planner.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Is de ballon "Zentrix draait verder" al eens getoond? Die legt uit waar de
    /// app gebleven is, en dat hoef je maar één keer te lezen. Hij kwam vroeger bij
    /// élke keer sluiten.
    /// </summary>
    public bool CloseToTrayExplained { get; set; }

    /// <summary>Meteen opstarten met enkel het pictogram in het systeemvak.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Mee opstarten met Windows (zet een sleutel in het register).</summary>
    public bool RunAtLogin { get; set; }

    // ---------- weergave ----------

    /// <summary>0 = lijst, 1 = raster. Wordt nu wel onthouden tussen twee starts.</summary>
    public int ResultView { get; set; }

    /// <summary>De volgorde van de resultaten; de waarden van <c>ListingSort</c>.</summary>
    public int Sort { get; set; }

    /// <summary>
    /// Hoeveel zoekertjes er op één pagina staan. Dit verving het oude "maximum
    /// aantal resultaten": dat was een rem op wat er BINNENKWAM, en daardoor zag je
    /// nooit meer dan honderd zoekertjes per site. Nu haalt de app op wat er is en
    /// bepaalt dit enkel hoeveel je er tegelijk te zien krijgt.
    /// </summary>
    public int PageSize { get; set; } = 100;

    // ---------- meldingen ----------

    public NotifySettings Notify { get; set; } = new();

    // ---------- lezen en schrijven ----------

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path_)) return;

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_));
            if (settings is null) return;

            settings.Notify.SmtpPassword = Ontsluier(settings.Notify.SmtpPassword);
            Current = settings;
        }
        catch (Exception ex)
        {
            Log.Write("instellingen konden niet gelezen worden - " + ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);

            // Het wachtwoord versluierd wegschrijven en daarna meteen herstellen,
            // zodat het in het geheugen gewoon bruikbaar blijft.
            var klaar = Notify.SmtpPassword;
            Notify.SmtpPassword = Versluier(klaar);

            File.WriteAllText(Path_, JsonSerializer.Serialize(this, Options));

            Notify.SmtpPassword = klaar;
        }
        catch (Exception ex)
        {
            Log.Write("instellingen konden niet bewaard worden - " + ex.Message);
        }
    }

    /// <summary>
    /// Zet het wachtwoord om naar base64 met een merkteken ervoor. Dit is
    /// bewust geen versleuteling en doet ook niet alsof: het houdt het enkel uit
    /// het zicht van wie toevallig het bestand openslaat.
    /// </summary>
    private const string Merk = "b64:";

    private static string Versluier(string tekst) =>
        string.IsNullOrEmpty(tekst)
            ? ""
            : Merk + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tekst));

    private static string Ontsluier(string tekst)
    {
        if (!tekst.StartsWith(Merk, StringComparison.Ordinal)) return tekst;

        try
        {
            return System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(tekst[Merk.Length..]));
        }
        catch
        {
            return "";
        }
    }
}

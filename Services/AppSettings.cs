using System.IO;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Het token dat @BotFather geeft, in de vorm 1234:AA... Evengoed een geheim als een
    /// wachtwoord: wie het heeft, schrijft als jouw bot en leest wat jij hem stuurt. Staat
    /// daarom beschermd in het instellingenbestand, net als <see cref="SmtpPassword"/>.
    /// </summary>
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
    /// niet het gewone wachtwoord. Staat in het instellingenbestand beschermd door
    /// Windows, enkel leesbaar voor jouw account op deze pc (zie <see cref="AppSettings"/>).
    /// In het geheugen staat het gewoon leesbaar: het moet naar de mailserver.
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

    // ---------- AI-controle op een foto ----------

    /// <summary>
    /// Welk model van Ollama een foto bekijkt. Moet "vision" kunnen; qwen3.5:9b heeft dat, en
    /// staat hier omdat het op deze pc al geïnstalleerd was. Het draait lokaal op de grafische
    /// kaart, dus er gaat geen foto de deur uit. Zie <see cref="PhotoAnalyzer"/>.
    /// </summary>
    public string AiModel { get; set; } = "qwen3.5:9b";

    /// <summary>
    /// Waar Ollama luistert. Enkel op deze pc; een adres buiten 127.0.0.1 zou betekenen dat de
    /// foto's naar een andere machine gaan, en daar is deze functie niet voor bedoeld.
    /// </summary>
    public string AiUrl { get; set; } = "http://127.0.0.1:11434";

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

            var oudeVorm = false;
            settings.Notify.SmtpPassword = Vrijgeven(settings.Notify.SmtpPassword, "het mailwachtwoord",
                ref oudeVorm, out settings._onleesbaarWachtwoord);
            settings.Notify.TelegramToken = Vrijgeven(settings.Notify.TelegramToken, "het Telegram-token",
                ref oudeVorm, out settings._onleesbaarToken);

            Current = settings;

            // Meteen herschrijven, en niet pas bij de volgende keer bewaren: anders blijft de
            // leesbare vorm staan tot je toevallig een instelling wijzigt.
            if (oudeVorm && settings.Save())
                Log.Write("instellingen: wachtwoord en token (wat ingevuld was) staan nu beschermd door Windows");
        }
        catch (Exception ex)
        {
            Log.Write("instellingen konden niet gelezen worden - " + ex.Message);
        }
    }

    /// <returns>Of het bestand geschreven is; wat misliep, staat in het logboek.</returns>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);

            // Eerst naar een JSON-boom, en daarin de twee geheimen vervangen door hun
            // beschermde vorm. Vroeger werd het wachtwoord even in het object zelf
            // vervangen en daarna teruggezet; een melding die op dat moment op een andere
            // draad een mail verstuurde, meldde zich dan aan met de versluierde vorm.
            var boom = JsonSerializer.SerializeToNode(this, Options)!.AsObject();
            var melden = boom[nameof(Notify)]!.AsObject();

            melden[nameof(NotifySettings.SmtpPassword)] = Bescherm(Notify.SmtpPassword, _onleesbaarWachtwoord);
            melden[nameof(NotifySettings.TelegramToken)] = Bescherm(Notify.TelegramToken, _onleesbaarToken);

            File.WriteAllText(Path_, boom.ToJsonString(Options));

            // Een nieuw ingevulde waarde vervangt de onleesbare voorgoed: wie ze daarna leegmaakt,
            // wil ze echt weg.
            if (Notify.SmtpPassword.Length > 0) _onleesbaarWachtwoord = null;
            if (Notify.TelegramToken.Length > 0) _onleesbaarToken = null;

            return true;
        }
        catch (Exception ex)
        {
            Log.Write("instellingen konden niet bewaard worden - " + ex.Message);
            return false;
        }
    }

    // ---------- de geheimen: wachtwoord en token ----------
    //
    // Sinds 22 september 2026 beschermd met DPAPI, de bescherming van Windows zelf
    // (ProtectedData, voor de huidige gebruiker). Het bestand is dan enkel leesbaar voor
    // jouw Windows-account op deze pc: een kopie in een back-up, op OneDrive of op een
    // andere pc is waardeloos. Een programma dat onder jouw account draait, kan het wel
    // lezen - daartegen helpt geen enkele bescherming die Zentrix zelf kan openen.
    //
    // Daarvoor stond het wachtwoord er als base64 in ("b64:"), en dat is geen bescherming:
    // wie het bestand opende, had het wachtwoord. Het token stond er gewoon leesbaar in.
    // Beide worden bij het inlezen omgezet.

    private const string Merk = "dpapi:";
    private const string OudMerk = "b64:";

    /// <summary>
    /// Een vast extraatje bij het beschermen. Een ander programma dat DPAPI gebruikt, kan
    /// onze waarden zo niet per ongeluk openen. Nooit wijzigen: dan is wat al bewaard staat,
    /// niet meer te lezen.
    /// </summary>
    private static readonly byte[] Extra = "Zentrix-instellingen"u8.ToArray();

    /// <summary>
    /// Een beschermde waarde die niet te openen was: een instellingenbestand van een andere
    /// pc of een ander Windows-account. Die blijft ongewijzigd in het bestand staan zolang je
    /// niets nieuws invult. Mocht het een tijdelijke hapering van Windows geweest zijn, dan
    /// lukt het bij de volgende start alsnog, in plaats van dat een ander opgeslagen vinkje
    /// het wachtwoord gewist heeft.
    /// </summary>
    private string? _onleesbaarWachtwoord, _onleesbaarToken;

    private static string Bescherm(string tekst, string? onleesbaar)
    {
        if (string.IsNullOrEmpty(tekst)) return onleesbaar ?? "";

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(tekst), Extra, DataProtectionScope.CurrentUser);
        return Merk + Convert.ToBase64String(bytes);
    }

    /// <param name="naam">Voor het logboek; de waarde zelf komt daar nooit in.</param>
    /// <param name="oudeVorm">Wordt waar als de waarde nog niet beschermd was.</param>
    /// <param name="onleesbaar">De beschermde waarde als ze niet te openen was, anders null.</param>
    private static string Vrijgeven(string tekst, string naam, ref bool oudeVorm, out string? onleesbaar)
    {
        onleesbaar = null;
        if (string.IsNullOrEmpty(tekst)) return "";

        if (tekst.StartsWith(Merk, StringComparison.Ordinal))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(tekst[Merk.Length..]), Extra,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                Log.Write($"instellingen: {naam} kon niet gelezen worden. Het is beschermd voor een ander " +
                          "Windows-account of een andere pc; vul het opnieuw in bij Meldingen en achtergrond.");
                onleesbaar = tekst;
                return "";
            }
        }

        oudeVorm = true;

        if (!tekst.StartsWith(OudMerk, StringComparison.Ordinal)) return tekst;

        string oud;
        try
        {
            oud = Encoding.UTF8.GetString(Convert.FromBase64String(tekst[OudMerk.Length..]));
        }
        catch (FormatException)
        {
            return "";
        }

        // Een oudere Zentrix kent "dpapi:" niet: ze leest de beschermde vorm als het wachtwoord
        // zelf en zet er bij het bewaren nog eens "b64:" rond. Dan zit de beschermde vorm erin.
        return oud.StartsWith(Merk, StringComparison.Ordinal)
            ? Vrijgeven(oud, naam, ref oudeVorm, out onleesbaar)
            : oud;
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vindioo.Services;

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

    /// <summary>
    /// Dezelfde instellingen, maar enkel met de kanalen die déze melding mag gebruiken.
    ///
    /// Zo blijft er één plaats waar de gegevens staan - het Telegram-token, de mailserver -
    /// terwijl de beller kiest welke weg een bericht neemt. Een kanaal dat hier gevraagd wordt
    /// maar centraal uit staat, gaat niet alsnog aan: het is een zeef, geen schakelaar.
    ///
    /// Erbij op 4 oktober 2026, toen de waarschuwing voor een aflopende veiling van één globale
    /// instelling naar een keuze per favoriet ging.
    /// </summary>
    public NotifySettings Alleen(Vindioo.Models.AlertChannels kanalen)
    {
        var kopie = (NotifySettings)MemberwiseClone();

        kopie.Tray = Tray && kanalen.HasFlag(Vindioo.Models.AlertChannels.Tray);
        kopie.Telegram = Telegram && kanalen.HasFlag(Vindioo.Models.AlertChannels.Telegram);
        kopie.Email = Email && kanalen.HasFlag(Vindioo.Models.AlertChannels.Mail);

        return kopie;
    }

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
    /// Is de ballon "Vindioo draait verder" al eens getoond? Die legt uit waar de
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

    /// <summary>
    /// De naam van de omgevingsvariabele waarin de sleutel vroeger stond - en waarin hij nog
    /// altijd mág staan, want wie hem daar zelf zet, verwacht niet dat de app hem negeert.
    /// </summary>
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>
    /// De sleutel voor de Claude-API, waarmee <see cref="SiteAnalyzer"/> een onbekende site
    /// uitzoekt.
    ///
    /// Beschermd met DPAPI in het instellingenbestand, net als het mailwachtwoord en het
    /// Telegram-token. Tot 1 oktober 2026 stond hij in de omgevingsvariabelen van het
    /// Windows-account, en dat is geen bescherming: elk programma dat onder jouw account draait
    /// leest hem, hij staat zichtbaar in het systeemscherm van Windows, en hij reist mee naar
    /// élk proces dat de app start - ook naar de Chrome die Playwright opent. Een sleutel die
    /// per analyse een halve euro kost, hoort daar niet.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// De sleutel waarmee er gewerkt wordt: de bewaarde, en anders die uit de
    /// omgevingsvariabele. Die terugval blijft met opzet bestaan - wie hem daar zelf zet (of een
    /// wegwerpprojectje dat hem meegeeft) werkt gewoon verder.
    /// </summary>
    public static string ApiKeyInUse =>
        Current.ApiKey.Length > 0
            ? Current.ApiKey
            : Environment.GetEnvironmentVariable(ApiKeyVariable)?.Trim() ?? "";

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
            settings.ApiKey = Vrijgeven(settings.ApiKey, "de API-sleutel",
                ref oudeVorm, out settings._onleesbaarSleutel);

            Current = settings;

            // Stond de sleutel nog in de omgevingsvariabele en nergens anders, dan verhuist hij
            // hierheen. De variabele zelf laten we staan: ze weghalen is een wijziging aan het
            // Windows-account van de gebruiker, en andere hulpmiddelen kunnen ze gebruiken. Het
            // logboek zegt wel hoe je ze kwijtraakt.
            var uitOmgeving = Environment.GetEnvironmentVariable(ApiKeyVariable)?.Trim() ?? "";

            if (settings.ApiKey.Length == 0 && uitOmgeving.Length > 0)
            {
                settings.ApiKey = uitOmgeving;
                oudeVorm = true;

                Log.Write("instellingen: de API-sleutel stond in de omgevingsvariabele " +
                          $"{ApiKeyVariable} en is nu beschermd door Windows bewaard. Je mag die " +
                          "variabele verwijderen; de app heeft ze niet meer nodig.");
            }

            // Meteen herschrijven, en niet pas bij de volgende keer bewaren: anders blijft de
            // leesbare vorm staan tot je toevallig een instelling wijzigt.
            if (oudeVorm && settings.Save())
                Log.Write("instellingen: wachtwoord, token en API-sleutel (wat ingevuld was) staan nu beschermd door Windows");
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

            boom[nameof(ApiKey)] = Bescherm(ApiKey, _onleesbaarSleutel);

            File.WriteAllText(Path_, boom.ToJsonString(Options));

            // Een nieuw ingevulde waarde vervangt de onleesbare voorgoed: wie ze daarna leegmaakt,
            // wil ze echt weg.
            if (Notify.SmtpPassword.Length > 0) _onleesbaarWachtwoord = null;
            if (Notify.TelegramToken.Length > 0) _onleesbaarToken = null;
            if (ApiKey.Length > 0) _onleesbaarSleutel = null;

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
    // lezen - daartegen helpt geen enkele bescherming die Vindioo zelf kan openen.
    //
    // Daarvoor stond het wachtwoord er als base64 in ("b64:"), en dat is geen bescherming:
    // wie het bestand opende, had het wachtwoord. Het token stond er gewoon leesbaar in.
    // Beide worden bij het inlezen omgezet.

    private const string Merk = "dpapi:";
    private const string OudMerk = "b64:";

    /// <summary>
    /// Een vast extraatje bij het beschermen. Een ander programma dat DPAPI gebruikt, kan
    /// onze waarden zo niet per ongeluk openen.
    ///
    /// <para><b>Hier staat met opzet de oude naam, en die blijft staan.</b> Deze bytes zijn
    /// geen tekst die iemand leest maar sleutelmateriaal: ze gingen mee in het versleutelen
    /// van wat er nu in instellingen.json staat. Wijzig je ze - bijvoorbeeld door de app te
    /// hernoemen en overal "Zentrix" te vervangen - dan is de bewaarde API-sleutel niet meer
    /// te openen. Er komt geen foutmelding; er staat gewoon ineens geen sleutel meer. Bij de
    /// hernoeming naar Vindioo op 3 oktober 2026 is dit daarom bewust blijven staan.</para>
    /// </summary>
    private static readonly byte[] Extra = "Zentrix-instellingen"u8.ToArray();

    /// <summary>
    /// Een beschermde waarde die niet te openen was: een instellingenbestand van een andere
    /// pc of een ander Windows-account. Die blijft ongewijzigd in het bestand staan zolang je
    /// niets nieuws invult. Mocht het een tijdelijke hapering van Windows geweest zijn, dan
    /// lukt het bij de volgende start alsnog, in plaats van dat een ander opgeslagen vinkje
    /// het wachtwoord gewist heeft.
    /// </summary>
    private string? _onleesbaarWachtwoord, _onleesbaarToken, _onleesbaarSleutel;

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

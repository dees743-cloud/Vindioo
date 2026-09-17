using System.Globalization;
using System.Windows;
using Zentrix.Services;

namespace Zentrix;

/// <summary>
/// Waar meldingen heen gaan, en hoe de app zich op de achtergrond gedraagt.
/// Dit staat los van de zoekopdrachten zelf: je stelt het één keer in en elke
/// zoekopdracht met "melding sturen" gebruikt het.
///
/// Elk kanaal heeft een testknop. Wachten tot er 's nachts iets gevonden wordt
/// om te ontdekken dat je token niet klopt, is geen manier van werken.
/// </summary>
public partial class NotifySettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    public NotifySettingsWindow()
    {
        InitializeComponent();
        Vul();
    }

    private void Vul()
    {
        var app = AppSettings.Current;
        var melden = app.Notify;

        MinimizeBox.IsChecked = app.MinimizeToTray;
        CloseBox.IsChecked = app.CloseToTray;
        StartMinBox.IsChecked = app.StartMinimized;

        // De echte stand uit het register lezen en niet uit onze eigen
        // instellingen: de gebruiker kan die sleutel ook elders weggehaald hebben.
        AutostartBox.IsChecked = Autostart.IsEnabled;

        TrayNotifyBox.IsChecked = melden.Tray;

        TelegramBox.IsChecked = melden.Telegram;
        TelegramTokenBox.Text = melden.TelegramToken;
        TelegramChatBox.Text = melden.TelegramChatId;
        TelegramPhotosBox.IsChecked = melden.TelegramPhotos;

        EmailBox.IsChecked = melden.Email;
        SmtpHostBox.Text = melden.SmtpHost;
        SmtpPortBox.Text = melden.SmtpPort.ToString(CultureInfo.InvariantCulture);
        SmtpSslBox.IsChecked = melden.SmtpSsl;
        SmtpUserBox.Text = melden.SmtpUser;
        SmtpPasswordBox.Password = melden.SmtpPassword;
        MailToBox.Text = melden.MailTo;
    }

    /// <summary>Leest het scherm uit naar een los instellingenblok.</summary>
    private NotifySettings Lees() => new()
    {
        Tray = TrayNotifyBox.IsChecked == true,

        Telegram = TelegramBox.IsChecked == true,
        TelegramToken = TelegramTokenBox.Text.Trim(),
        TelegramChatId = TelegramChatBox.Text.Trim(),
        TelegramPhotos = TelegramPhotosBox.IsChecked == true,

        Email = EmailBox.IsChecked == true,
        SmtpHost = SmtpHostBox.Text.Trim(),
        SmtpPort = int.TryParse(SmtpPortBox.Text.Trim(), out var poort) ? poort : 587,
        SmtpSsl = SmtpSslBox.IsChecked == true,
        SmtpUser = SmtpUserBox.Text.Trim(),
        SmtpPassword = SmtpPasswordBox.Password,
        MailTo = MailToBox.Text.Trim()
    };

    // ---------- testen ----------

    private void TestTray_Click(object sender, RoutedEventArgs e) =>
        _ = Notifier.TestAsync(Lees(), "tray");

    private async void TestTelegram_Click(object sender, RoutedEventArgs e)
    {
        TelegramResult.Text = "Bezig...";
        var fout = await Notifier.TestAsync(Lees(), "telegram");

        TelegramResult.Text = fout is null
            ? "Gelukt — kijk in Telegram."
            : "Mislukt: " + fout;
    }

    /// <summary>
    /// Haalt het chatnummer op uit het laatste bericht dat iemand naar de bot
    /// stuurde. Zo hoeft de gebruiker dat nummer nergens zelf te zoeken.
    /// </summary>
    private async void FetchChatId_Click(object sender, RoutedEventArgs e)
    {
        var token = TelegramTokenBox.Text.Trim();

        if (token.Length == 0)
        {
            TelegramResult.Text = "Vul eerst het token in.";
            return;
        }

        TelegramResult.Text = "Bezig...";

        try
        {
            var id = await Notifier.FindTelegramChatIdAsync(token);

            if (id is null)
            {
                TelegramResult.Text = "Geen bericht gevonden. Stuur eerst zelf iets naar je bot in Telegram, " +
                                      "en probeer dan opnieuw.";
                return;
            }

            TelegramChatBox.Text = id;
            TelegramResult.Text = "Chat-id gevonden en ingevuld.";
        }
        catch (Exception ex)
        {
            TelegramResult.Text = "Mislukt: " + ex.Message;
        }
    }

    private async void TestEmail_Click(object sender, RoutedEventArgs e)
    {
        EmailResult.Text = "Bezig...";
        var fout = await Notifier.TestAsync(Lees(), "email");

        EmailResult.Text = fout is null
            ? "Verstuurd — kijk in je postvak."
            : "Mislukt: " + fout;
    }

    // ---------- bewaren ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var app = AppSettings.Current;

        app.MinimizeToTray = MinimizeBox.IsChecked == true;
        app.CloseToTray = CloseBox.IsChecked == true;
        app.StartMinimized = StartMinBox.IsChecked == true;
        app.Notify = Lees();

        var wilAutostart = AutostartBox.IsChecked == true;

        if (wilAutostart != Autostart.IsEnabled) Autostart.Set(wilAutostart);
        app.RunAtLogin = Autostart.IsEnabled;

        app.Save();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Zentrix.Services;

/// <summary>
/// Het pictogram naast de klok, rechtsonder. Daardoor kan de app dichtstaan en
/// toch blijven zoeken: het venster verdwijnt, het proces niet.
///
/// Dit is het enige stuk WinForms in de app. WPF heeft geen eigen pictogram voor
/// het systeemvak en WPF-UI 4.3 levert er ook geen; <c>NotifyIcon</c> doet het
/// werk en geeft er meteen de ballonmelding bij. Er wordt geen WinForms-venster
/// getoond — enkel dit pictogram en zijn menu, en dat menu hoort er in de stijl
/// van Windows uit te zien, niet in die van de app.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;

    /// <summary>De vorige toestand van het venster, om het juist terug te zetten.</summary>
    private WindowState _lastState = WindowState.Normal;

    /// <summary>Is de app echt aan het afsluiten, of gaat het venster enkel weg?</summary>
    public bool ReallyClosing { get; set; }

    /// <summary>Wordt aangeroepen wanneer de gebruiker in het menu "Eerstvolgende zoekopdracht nu uitvoeren" kiest.</summary>
    public Action? SearchNowRequested { get; set; }

    public TrayIcon(Window window)
    {
        _window = window;

        _icon = new NotifyIcon
        {
            Icon = LaadIcoon(),
            Visible = true,
            Text = "Zentrix"
        };

        _icon.DoubleClick += (_, _) => Show();
        _icon.BalloonTipClicked += (_, _) => Show();

        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };

        // Het menu hoort bij Windows, niet bij de app, dus het volgt de stand
        // van Windows en niet ons eigen designsysteem. WinForms doet dat niet
        // vanzelf: zonder deze kleuren staat er een spierwit menu midden op een
        // donker bureaublad.
        if (!WindowsGebruiktLichtThema())
        {
            menu.BackColor = System.Drawing.Color.FromArgb(43, 43, 43);
            menu.ForeColor = System.Drawing.Color.White;
        }

        menu.Items.Add("Zentrix openen", null, (_, _) => Show());
        // Zegt wat het doet: de eerste zoekopdracht met een schema nu laten draaien.
        // "Nu zoeken" deed vermoeden dat alles opnieuw gezocht werd.
        menu.Items.Add("Eerstvolgende zoekopdracht nu uitvoeren", null, (_, _) => SearchNowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) =>
        {
            ReallyClosing = true;
            Application.Current.Shutdown();
        });

        _icon.ContextMenuStrip = menu;

        _window.StateChanged += Window_StateChanged;

        // De haak waar Notifier zijn ballon door stuurt.
        Notifier.ShowTrayBalloon = ShowBalloon;
    }

    /// <summary>
    /// Staat Windows zelf in het lichte thema? Uit het register, want de
    /// gebruiker kan dat wisselen terwijl de app draait; we lezen het bij het
    /// opbouwen van het menu en niet eerder.
    /// </summary>
    private static bool WindowsGebruiktLichtThema()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("SystemUsesLightTheme") is not int waarde || waarde != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Het icoon van de app. Het zit als Resource in de exe, dus we halen het
    /// via de pack-URI op; lukt dat niet, dan nemen we het icoon van het
    /// uitvoerbare bestand zelf. Zonder icoon toont Windows een leeg vakje, en
    /// dan is het pictogram niet meer terug te vinden.
    /// </summary>
    private static System.Drawing.Icon LaadIcoon()
    {
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/zentrix.ico"))?.Stream;

            if (stream is not null) return new System.Drawing.Icon(stream);
        }
        catch (Exception ex)
        {
            Log.Write("systeemvak: eigen icoon kon niet geladen worden - " + ex.Message);
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null && File.Exists(exe))
            {
                var uit = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (uit is not null) return uit;
            }
        }
        catch
        {
            // Laatste redmiddel hieronder.
        }

        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>
    /// Bij minimaliseren het venster uit de taakbalk halen. Enkel wanneer de
    /// gebruiker dat wil: wie de app gewoon als venster gebruikt, verwacht hem
    /// in de taakbalk te vinden.
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            if (AppSettings.Current.MinimizeToTray) _window.Hide();
            return;
        }

        _lastState = _window.WindowState;
    }

    /// <summary>Haalt het venster terug naar voren.</summary>
    public void Show()
    {
        _window.Show();
        _window.WindowState = _lastState == WindowState.Minimized ? WindowState.Normal : _lastState;
        _window.Activate();
    }

    /// <summary>Verbergt het venster; de app blijft in het systeemvak draaien.</summary>
    public void HideToTray() => _window.Hide();

    public void ShowBalloon(string titel, string tekst)
    {
        // Een ballon met een lege tekst toont Windows niet.
        if (string.IsNullOrWhiteSpace(tekst)) tekst = " ";

        _icon.BalloonTipTitle = titel;
        _icon.BalloonTipText = tekst;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(10_000);
    }

    /// <summary>
    /// De tekst die verschijnt als je over het pictogram zweeft. Windows kapt
    /// die af op 63 tekens, vandaar de beperking.
    /// </summary>
    public void SetTooltip(string tekst)
    {
        _icon.Text = tekst.Length > 63 ? tekst[..60] + "..." : tekst;
    }

    public void Dispose()
    {
        Notifier.ShowTrayBalloon = null;

        _window.StateChanged -= Window_StateChanged;

        _icon.Visible = false;
        _icon.Dispose();
    }
}

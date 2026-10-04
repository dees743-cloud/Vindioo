using System.Windows;
using System.Windows.Controls;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo;

/// <summary>
/// Wat één favoriet moet melden, en waarheen.
///
/// <para>Tot 4 oktober 2026 stond dit als één schakelaar in <i>Meldingen en achtergrond</i>, voor
/// alle favorieten samen. Zo kijkt niemand ernaar: bij het ene kavel wil je een dag en twee uur
/// vooraf gewaarschuwd worden omdat je echt gaat bieden, bij het andere volstaat een uur, en bij
/// een gewone advertentie slaat het hele idee niet. Daarom per favoriet, via rechtsklik.</para>
///
/// <para>De momenten zijn invulbaar in plaats van vier vaste vinkjes. Standaard staan er twee
/// rijen klaar; met <c>+</c> komen er bij tot <see cref="AlertMoments.Hoogstens"/>. De eenheid
/// staat ernaast, zodat je "24 uur" typt en niet 1440 - en een kwartier vooraf nog altijd kan.</para>
/// </summary>
public partial class FavoriteAlertWindow
{
    private readonly Listing _favoriet;
    private readonly bool _isVeiling;

    /// <summary>De momenten zoals ze bij Bewaren uit de rijen komen.</summary>
    public List<int> Momenten { get; private set; } = new();

    /// <summary>De gekozen kanalen zoals ze bij Bewaren uit de vinkjes komen.</summary>
    public AlertChannels Kanalen { get; private set; }

    /// <summary>Of deze favoriet bericht wil bij een prijswijziging.</summary>
    public bool Prijs { get; private set; }

    /// <param name="favoriet">De favoriet waar dit over gaat.</param>
    /// <param name="isVeiling">
    /// Komt deze favoriet van een veilingsite (<see cref="SiteDefinition.IsAuction"/>)? Dat is
    /// een betrouwbaarder vraag dan "kennen we een einddatum": die kan nog ontbreken zolang er
    /// geen ronde Nakijken geweest is, en dan zou het vak onterecht verdwijnen.
    /// </param>
    public FavoriteAlertWindow(Listing favoriet, bool isVeiling)
    {
        InitializeComponent();

        _favoriet = favoriet;
        _isVeiling = isVeiling;

        TitelTekst.Text = favoriet.Title;
        BronTekst.Text = favoriet.Source;

        if (isVeiling) VulMomenten();
        else ToonGeenVeiling();

        PrijsBox.IsChecked = favoriet.AlertPrice;

        VulKanalen();
    }

    // ---------- de momenten ----------

    private void VulMomenten()
    {
        // Wat er al stond, of anders de twee waarmee een nieuw venster begint.
        var start = _favoriet.AlertLeads.Count > 0
            ? _favoriet.AlertLeads
            : AlertMoments.Standaard.ToList();

        foreach (var minuten in start.Take(AlertMoments.Hoogstens)) VoegRijToe(minuten);

        WerkMomentenBij();
    }

    /// <summary>Eén rij: een getal, een eenheid, en een kruisje om hem weer weg te halen.</summary>
    private void VoegRijToe(int? minuten)
    {
        var rij = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var getal = new Wpf.Ui.Controls.TextBox { Margin = new Thickness(0, 0, 6, 0) };

        var eenheid = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var naam in new[] { "minuten", "uur", "dagen" }) eenheid.Items.Add(naam);

        if (minuten is { } m)
        {
            var (aantal, soort) = AlertMoments.Toon(m);
            getal.Text = aantal.ToString();
            eenheid.SelectedItem = soort;
        }
        else
        {
            eenheid.SelectedItem = "uur";
        }

        var weg = new Wpf.Ui.Controls.Button
        {
            Content = "✕",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Dit moment weghalen"
        };

        weg.Click += (_, _) =>
        {
            MomentenLijst.Children.Remove(rij);
            WerkMomentenBij();
        };

        Grid.SetColumn(getal, 0);
        Grid.SetColumn(eenheid, 1);
        Grid.SetColumn(weg, 2);

        rij.Children.Add(getal);
        rij.Children.Add(eenheid);
        rij.Children.Add(weg);

        MomentenLijst.Children.Add(rij);
    }

    private void Extra_Click(object sender, RoutedEventArgs e)
    {
        if (MomentenLijst.Children.Count >= AlertMoments.Hoogstens) return;

        VoegRijToe(null);
        WerkMomentenBij();
    }

    /// <summary>
    /// De plus uitzetten op tien, en eronder zeggen wat er nu geldt. Een knop die niets doet
    /// zonder te zeggen waarom, laat je denken dat de app hapert.
    /// </summary>
    private void WerkMomentenBij()
    {
        var aantal = MomentenLijst.Children.Count;

        ExtraKnop.IsEnabled = aantal < AlertMoments.Hoogstens;

        MomentenUitleg.Text = aantal == 0
            ? "Geen enkel moment: er wordt niets gestuurd over het einde van deze veiling."
            : aantal >= AlertMoments.Hoogstens
                ? $"Tien momenten is het maximum. Haal er een weg om er een ander bij te zetten."
                : "Je krijgt één bericht per moment. Stond de app uit, dan gaat enkel het "
                  + "dichtstbijzijnde moment nog af - niet alle gemiste achter elkaar.";
    }

    private void ToonGeenVeiling()
    {
        VeilingVak.Visibility = Visibility.Collapsed;
        GeenVeilingVak.Visibility = Visibility.Visible;

        GeenVeilingTekst.Text =
            $"{_favoriet.Source} is geen veilingsite, dus er is geen sluitingstijd om naartoe te "
            + "tellen. Deze favoriet kan wel bericht geven wanneer zijn prijs verandert.";
    }

    // ---------- de kanalen ----------

    private void VulKanalen()
    {
        var melden = AppSettings.Current.Notify;

        TrayBox.IsChecked = _favoriet.AlertChannels.HasFlag(AlertChannels.Tray);
        TelegramBox.IsChecked = _favoriet.AlertChannels.HasFlag(AlertChannels.Telegram);
        MailBox.IsChecked = _favoriet.AlertChannels.HasFlag(AlertChannels.Mail);

        // Een kanaal dat centraal uit staat of niet ingevuld is, kan hier niet aan. Het vinkje
        // blijft wel staan met de reden erbij: onzichtbaar maken zou de vraag oproepen waar
        // Telegram gebleven is.
        Zet(TrayBox, melden.Tray, "staat uit bij Meldingen en achtergrond");

        Zet(TelegramBox,
            melden.Telegram && melden.TelegramToken.Length > 0 && melden.TelegramChatId.Length > 0,
            "nog geen token of chat-id ingevuld");

        Zet(MailBox,
            melden.Email && melden.SmtpHost.Length > 0 && melden.MailTo.Length > 0,
            "nog geen mailserver of ontvanger ingevuld");

        static void Zet(CheckBox vinkje, bool kan, string reden)
        {
            if (kan) return;

            vinkje.IsEnabled = false;
            vinkje.IsChecked = false;
            vinkje.Content += $"  ({reden})";
        }
    }

    // ---------- bewaren ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Kanalen = AlertChannels.Geen;
        if (TrayBox.IsChecked == true) Kanalen |= AlertChannels.Tray;
        if (TelegramBox.IsChecked == true) Kanalen |= AlertChannels.Telegram;
        if (MailBox.IsChecked == true) Kanalen |= AlertChannels.Mail;

        Momenten = _isVeiling ? LeesMomenten() : new List<int>();
        Prijs = PrijsBox.IsChecked == true;

        // Iets aangezet zonder kanaal is een val: je stelt het in, het ziet er goed uit, en er
        // komt nooit iets. Liever hier tegengehouden dan stil nergens aankomen.
        if ((Momenten.Count > 0 || Prijs) && Kanalen == AlertChannels.Geen)
        {
            KanaalWaarschuwing.Text =
                "Kies minstens één kanaal, anders komt er nergens iets aan.";
            KanaalWaarschuwing.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    /// <summary>
    /// De rijen uitlezen. Een leeg of onzinnig veld telt gewoon niet mee: dat is een rij die je
    /// erbij zette en niet invulde, en daar hoort geen foutmelding bij.
    /// </summary>
    private List<int> LeesMomenten()
    {
        var uit = new List<int>();

        foreach (var rij in MomentenLijst.Children.OfType<Grid>())
        {
            var getal = rij.Children.OfType<Wpf.Ui.Controls.TextBox>().FirstOrDefault();
            var eenheid = rij.Children.OfType<ComboBox>().FirstOrDefault();

            if (getal is null || eenheid is null) continue;
            if (!int.TryParse(getal.Text.Trim(), out var aantal) || aantal <= 0) continue;

            var minuten = AlertMoments.NaarMinuten(aantal, eenheid.SelectedItem as string ?? "uur");

            if (minuten > 0 && !uit.Contains(minuten)) uit.Add(minuten);
        }

        uit.Sort((a, b) => b.CompareTo(a));
        return uit;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Vindioo.Models;

namespace Vindioo.Controls;

/// <summary>
/// De timer rechtsonder op een veilingkaart: een klokje en hoelang er nog geboden kan worden.
/// Met een exact sluitingstijdstip (<see cref="EndsAt"/>) telt hij echt af, elke seconde;
/// zonder - Catawiki voor zijn API geantwoord heeft - toont hij de tekst van de site
/// (<see cref="SiteText"/>) en telt hij niet. Wat er precies staat, bepaalt
/// <see cref="Listing.TimerTekst"/>. Bij een gewoon zoekertje is hij er niet.
///
/// Alle timers delen één klok. Een timer luistert enkel zolang hij op het scherm staat
/// (Loaded/Unloaded): het raster bouwt enkel de kaarten in beeld op, dus er tikken er
/// hoogstens een paar tientallen, en staat er geen enkele, dan staat de klok stil.
/// </summary>
public class CountdownBadge : Border
{
    private static readonly DispatcherTimer Klok = new() { Interval = TimeSpan.FromSeconds(1) };
    private static event EventHandler? Tik;
    private static int _luisteraars;

    static CountdownBadge()
    {
        Klok.Tick += (_, _) => Tik?.Invoke(null, EventArgs.Empty);
    }

    private readonly TextBlock _tekst = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SymbolIcon _klokje = new()
    {
        Symbol = SymbolRegular.Timer20,
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private bool _luistert;

    public static readonly DependencyProperty EndsAtProperty =
        DependencyProperty.Register(nameof(EndsAt), typeof(DateTime?), typeof(CountdownBadge),
            new PropertyMetadata(null, (d, _) => ((CountdownBadge)d).Bijwerken()));

    /// <summary>Het exacte sluitingstijdstip, als het bekend is.</summary>
    public DateTime? EndsAt
    {
        get => (DateTime?)GetValue(EndsAtProperty);
        set => SetValue(EndsAtProperty, value);
    }

    public static readonly DependencyProperty SiteTextProperty =
        DependencyProperty.Register(nameof(SiteText), typeof(string), typeof(CountdownBadge),
            new PropertyMetadata("", (d, _) => ((CountdownBadge)d).Bijwerken()));

    /// <summary>De aftelklok zoals de site hem schrijft ("Nog 3 dagen").</summary>
    public string SiteText
    {
        get => (string)GetValue(SiteTextProperty);
        set => SetValue(SiteTextProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(CountdownBadge));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public CountdownBadge()
    {
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(6, 2, 7, 2);

        var rij = new StackPanel { Orientation = Orientation.Horizontal };
        rij.Children.Add(_klokje);
        rij.Children.Add(_tekst);
        Child = rij;

        Loaded += (_, _) => { Luister(true); Bijwerken(); };
        Unloaded += (_, _) => Luister(false);
    }

    /// <summary>Aan- en afmelden bij de gedeelde klok, en die enkel laten lopen als iemand luistert.</summary>
    private void Luister(bool aan)
    {
        if (aan == _luistert) return;
        _luistert = aan;

        if (aan)
        {
            Tik += OpTik;
            if (++_luisteraars == 1) Klok.Start();
        }
        else
        {
            Tik -= OpTik;
            if (--_luisteraars == 0) Klok.Stop();
        }
    }

    private void OpTik(object? sender, EventArgs e)
    {
        // Zonder exact tijdstip valt er niets af te tellen.
        if (EndsAt is not null) Bijwerken();
    }

    /// <summary>Een penseel uit App.xaml, of de terugval als het er (nog) niet is.</summary>
    private Brush Penseel(string sleutel, Brush terugval) => TryFindResource(sleutel) as Brush ?? terugval;

    private void Bijwerken()
    {
        var nu = DateTime.Now;
        var tekst = Listing.TimerTekst(EndsAt, SiteText, nu);

        Visibility = tekst.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (tekst.Length == 0) return;

        if (_tekst.Text != tekst) _tekst.Text = tekst;
        _klokje.FontSize = FontSize + 2;

        var dringend = Listing.IsDringend(EndsAt, nu);
        var voorgrond = Penseel(dringend ? "WarningOnCardBrush" : "CardTextBrush", Brushes.Black);

        Background = Penseel(dringend ? "TimerUrgentBrush" : "TimerBrush", Brushes.Transparent);
        _tekst.Foreground = voorgrond;
        _klokje.Foreground = voorgrond;
        _tekst.FontWeight = dringend ? FontWeights.SemiBold : FontWeights.Normal;

        // Het exacte uur als uitleg, want "3d 04u" zegt niet op welke dag. Zonder exact
        // tijdstip zeggen we waar de tekst vandaan komt.
        ToolTip = EndsAt is { } einde
            ? "Sluit " + einde.ToString("dddd d MMMM 'om' HH:mm", CultureInfo.GetCultureInfo("nl-BE"))
            : "Zoals de site het toont: " + SiteText.Trim();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Vindioo.Controls;

/// <summary>
/// Laat een lijst met het muiswiel vloeiend schuiven in plaats van met sprongen.
///
/// Twee dingen maken schuiven schokkerig, en allebei zitten ze hier.
///
/// <b>De stap is geen goede maat.</b> WPF neemt de Windows-instelling "hoeveel
/// regels per klik van het wiel" en vermenigvuldigt die met een tekstregel van
/// zestien beeldpunten. Op deze pc staat die instelling op 1, dus schoof een lijst
/// met kaarten van tweehonderd beeldpunten er zestien op — acht klikjes per kaart.
/// Een regel tekst is nu eenmaal geen zinnige maat voor een fotokaart. Hier is een
/// klik een vijfde van wat je ziet, wat de maat van de kaarten ook is.
///
/// <b>En een klik verzet de inhoud ineens.</b> WPF schuift in één sprong naar de
/// nieuwe plaats. Hier schuift de inhoud er in een paar frames naartoe: elk frame
/// wordt een kwart van de resterende afstand weggenomen, dus het vertrekt vlot en
/// loopt zacht uit. Dat is wat het oog als vloeiend leest.
///
/// Aanzetten met <c>controls:SmoothScroll.Enabled="True"</c> op de lijst, of vanuit
/// code met <see cref="SetEnabled"/>.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PreviewMouseWheel -= Wiel;
        if ((bool)e.NewValue) element.PreviewMouseWheel += Wiel;
    }

    /// <summary>Waar elke lijst naartoe onderweg is, zolang dat duurt.</summary>
    private static readonly Dictionary<ScrollViewer, double> Doelen = new();

    private static void Wiel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject wortel) return;
        if (ZoekSchuifvak(wortel) is not { } vak) return;

        // Een vijfde van wat er te zien is. Dat werkt voor een lijst met brede
        // kaarten net zo goed als voor een raster met foto's, want het volgt de
        // hoogte van het venster en niet de maat van een tekstregel.
        var stap = Math.Max(48, vak.ViewportHeight / 5);

        var vanaf = Doelen.TryGetValue(vak, out var lopend) ? lopend : vak.VerticalOffset;
        var doel = Begrens(vak, vanaf - Math.Sign(e.Delta) * stap);

        Doelen[vak] = doel;
        Start(vak);

        e.Handled = true;
    }

    private static double Begrens(ScrollViewer vak, double y) =>
        Math.Max(0, Math.Min(y, Math.Max(0, vak.ExtentHeight - vak.ViewportHeight)));

    // ---------- de glijbeweging ----------

    private static bool _loopt;

    private static void Start(ScrollViewer vak)
    {
        if (_loopt) return;

        _loopt = true;
        CompositionTarget.Rendering += Frame;
    }

    private static void Frame(object? sender, EventArgs e)
    {
        foreach (var (vak, doel) in Doelen.ToList())
        {
            var rest = doel - vak.VerticalOffset;

            if (Math.Abs(rest) < 0.5)
            {
                vak.ScrollToVerticalOffset(doel);
                Doelen.Remove(vak);
                continue;
            }

            vak.ScrollToVerticalOffset(vak.VerticalOffset + rest * 0.25);
        }

        if (Doelen.Count > 0) return;

        _loopt = false;
        CompositionTarget.Rendering -= Frame;
    }

    /// <summary>Het schuifvak dat in deze lijst zit.</summary>
    private static ScrollViewer? ZoekSchuifvak(DependencyObject wortel)
    {
        if (wortel is ScrollViewer gevonden) return gevonden;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wortel); i++)
        {
            if (ZoekSchuifvak(VisualTreeHelper.GetChild(wortel, i)) is { } dieper) return dieper;
        }

        return null;
    }
}

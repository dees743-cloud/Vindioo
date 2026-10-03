using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Vindioo.Controls;

/// <summary>
/// Eén regel tekst in een vak dat te smal kan zijn. In rust staat ze stil, met een
/// zachte vervaging aan de rechterkant als er meer is dan er past; zodra de muis erop
/// staat, schuift ze heen en weer zodat je het einde kan lezen.
///
/// Waarvoor: naast de prijs staat de plaats, en bij een veiling erachter hoelang er nog
/// geboden kan worden ("Kasterlee · Nog 3 dagen"). In het raster is een kaart maar 264
/// breed, dus dat past er niet altijd naast elkaar. Een beletselteken laat je dan niet
/// zien wát er weg is, en een tooltip springt over de kaart heen.
///
/// Waarom een <see cref="Decorator"/> en geen gewone TextBlock met een animatie: een
/// TextBlock krijgt van zijn ouder nooit meer breedte dan er beschikbaar is, dus valt er
/// ook niets te schuiven. Hier wordt hij gemeten met oneindige breedte en op zijn
/// natuurlijke breedte geplaatst; het vak eromheen knipt af (<see cref="UIElement.ClipToBounds"/>).
/// </summary>
public class ScrollingText : Decorator
{
    private readonly TextBlock _regel = new();
    private readonly TranslateTransform _schuif = new();
    private double _natuurlijk;

    /// <summary>Hoeveel beeldpunten per seconde de tekst opschuift.</summary>
    private const double SnelheidPerSeconde = 45;

    /// <summary>
    /// Ook een paar beeldpunten te veel doen er zo lang over. Zonder die ondergrens
    /// wipt een regel die net niet past een paar keer per seconde heen en weer.
    /// </summary>
    private static readonly TimeSpan MinimumDuur = TimeSpan.FromSeconds(1.2);

    /// <summary>Even stilstaan voor het schuiven begint: een muis die passeert zet niets in gang.</summary>
    private static readonly TimeSpan Aanloop = TimeSpan.FromMilliseconds(400);

    /// <summary>Over hoeveel beeldpunten het einde uitdooft.</summary>
    private const double Vervaagbreedte = 22;

    private Brush? _vervaging;
    private double _vervaagdBij;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ScrollingText),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, TekstGewijzigd));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // Kleur en lettergrootte erven al van de ouder; met deze twee zijn ze ook
    // rechtstreeks op het element te zetten, zoals bij een TextBlock.
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(ScrollingText));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(ScrollingText));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public ScrollingText()
    {
        ClipToBounds = true;

        _regel.TextWrapping = TextWrapping.NoWrap;
        _regel.RenderTransform = _schuif;
        Child = _regel;

        // In het raster worden de kaarten hergebruikt terwijl je scrolt. Een animatie die
        // op zo'n kaart bleef lopen, schuift daarna de tekst van een ánder zoekertje.
        Unloaded += (_, _) => Stop();
    }

    private static void TekstGewijzigd(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ScrollingText)d;
        self._regel.Text = (string)e.NewValue;
        self.Stop();
    }

    /// <summary>De tekst mag zo breed worden als ze wil; het vak eromheen knipt af.</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        _regel.Measure(new Size(double.PositiveInfinity, constraint.Height));
        _natuurlijk = _regel.DesiredSize.Width;

        return new Size(Math.Min(_natuurlijk, constraint.Width), _regel.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        _regel.Arrange(new Rect(0, 0, _natuurlijk, size.Height));

        // Past het niet, dan vervaagt het einde. Dat zegt "er staat meer" zonder een
        // beletselteken, en het is meteen de uitnodiging om er met de muis op te gaan.
        OpacityMask = Overschot(size.Width) > 1 ? Vervaging(size.Width) : null;

        return size;
    }

    /// <summary>Hoeveel er buiten het vak valt.</summary>
    private double Overschot(double breedte) => _natuurlijk - breedte;

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Start();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Stop();
    }

    private void Start()
    {
        var overschot = Overschot(ActualWidth);
        if (overschot <= 1) return;

        // Tijdens het schuiven geen vervaging: het einde lezen is net de bedoeling.
        OpacityMask = null;

        var duur = TimeSpan.FromSeconds(Math.Max(MinimumDuur.TotalSeconds, overschot / SnelheidPerSeconde));

        var animatie = new DoubleAnimation(0, -overschot, duur)
        {
            BeginTime = Aanloop,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        _schuif.BeginAnimation(TranslateTransform.XProperty, animatie);
    }

    private void Stop()
    {
        _schuif.BeginAnimation(TranslateTransform.XProperty, null);
        _schuif.X = 0;
        OpacityMask = Overschot(ActualWidth) > 1 ? Vervaging(ActualWidth) : null;
    }

    /// <summary>
    /// Het hele vak moet de muis aannemen, ook de witruimte tussen de letters. Zonder dit
    /// reageert enkel de tekst zelf, en dan moet je precies op een letter mikken.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
    }

    /// <summary>
    /// Het verloop dat het einde laat uitdoven, over de breedte van het vák.
    ///
    /// Let op de <see cref="BrushMappingMode.Absolute"/>: een verloop van 0 tot 1 wordt
    /// uitgerekt over de omhullende van het element én zijn kinderen, en de tekst is hier
    /// juist breder dan het vak. Het verloop viel dan volledig in het stuk dat toch al
    /// weggeknipt was, en er was niets te zien.
    /// </summary>
    private Brush Vervaging(double breedte)
    {
        if (_vervaging is not null && Math.Abs(_vervaagdBij - breedte) < 0.5) return _vervaging;

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(breedte, 0)
        };

        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.White, Math.Max(0, 1 - Vervaagbreedte / Math.Max(1, breedte))));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();

        _vervaging = brush;
        _vervaagdBij = breedte;

        return brush;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Vindioo.Controls;

/// <summary>
/// Een <see cref="WrapPanel"/> die enkel de kaarten opbouwt die in beeld staan.
///
/// WPF levert die niet zelf: <see cref="VirtualizingStackPanel"/> kan enkel op een
/// rij of een kolom, en een gewone <see cref="WrapPanel"/> bouwt alles op. Bij
/// zeshonderd zoekertjes zijn dat zeshonderd kaarten met elk een foto, een schaduw
/// en een afgeronde uitsnede, terwijl er een stuk of tien te zien zijn.
///
/// Dit paneel gaat ervan uit dat alle kaarten **even groot** zijn. Dat is hier zo -
/// de rasterkaart heeft een vaste breedte en hoogte - en het scheelt een hoop: met
/// gelijke maten is uit te rekenen welke kaarten bij een bepaalde schuifstand in
/// beeld vallen, zonder ze eerst allemaal te moeten meten. Een paneel dat met
/// wisselende maten overweg kan, moet dat wél en wordt een veelvoud ingewikkelder.
///
/// De maat komt van de eerste kaart: die wordt één keer opgebouwd en gemeten, en
/// daarna gelden zijn maten voor de rest.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size _kaart = Size.Empty;   // maat van één kaart, inclusief marge
    private int _kolommen = 1;

    /// <summary>Hoeveel kaarten er nu naast elkaar passen.</summary>
    public int Kolommen => _kolommen;

    /// <summary>
    /// Gaat af wanneer dat aantal verandert, bijvoorbeeld doordat het venster versleept wordt.
    /// Borrelt omhoog, zodat het hoofdscherm één keer kan luisteren op de lijst zelf in plaats
    /// van naar dit paneel te moeten zoeken in de visuele boom.
    /// </summary>
    public static readonly RoutedEvent KolommenGewijzigdEvent =
        EventManager.RegisterRoutedEvent(nameof(KolommenGewijzigd), RoutingStrategy.Bubble,
                                         typeof(RoutedEventHandler), typeof(VirtualizingWrapPanel));

    public event RoutedEventHandler KolommenGewijzigd
    {
        add => AddHandler(KolommenGewijzigdEvent, value);
        remove => RemoveHandler(KolommenGewijzigdEvent, value);
    }
    private Size _bereik;               // hoe groot het geheel is
    private Size _kijkvenster;          // hoeveel daarvan te zien is
    private Point _positie;             // waar we staan in dat geheel

    // ---------- meten en plaatsen ----------

    protected override Size MeasureOverride(Size beschikbaar)
    {
        var aantal = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

        if (aantal == 0)
        {
            _herkansingen = 0;
            _bereik = new Size(0, 0);
            ScrollOwner?.InvalidateScrollInfo();
            return new Size(0, 0);
        }

        // Bij het wisselen van weergave wordt dit paneel al gemeten voor het aan
        // zijn lijst hangt; dan is er nog geen generator en valt er niets op te
        // bouwen.
        if (ItemContainerGenerator is null) return Herkansing();

        // De maat van een kaart kennen we pas als er een bestaat. Bouw er dus een
        // op, meet hem, en reken daarna met die maat verder.
        if (_kaart.IsEmpty) _kaart = MeetEersteKaart(beschikbaar);

        // Nog geen bruikbare maat: ook dan een herkansing vragen.
        if (_kaart.IsEmpty) return Herkansing();

        _herkansingen = 0;

        var vorigeKolommen = _kolommen;
        _kolommen = Math.Max(1, (int)(beschikbaar.Width / _kaart.Width));

        // Het scherm bepaalt hoeveel er naast elkaar passen, en dus ook hoeveel er op een
        // pagina horen: anders blijft de laatste rij half leeg terwijl er nog pagina's volgen.
        // Niet hier meteen melden - we zitten midden in een meting, en wie daarop reageert zou
        // die meting opnieuw aftrappen. Vandaar via de dispatcher.
        if (_kolommen != vorigeKolommen)
            Dispatcher.BeginInvoke(
                () => RaiseEvent(new RoutedEventArgs(KolommenGewijzigdEvent, this)),
                DispatcherPriority.Background);

        var rijen = (int)Math.Ceiling(aantal / (double)_kolommen);

        _kijkvenster = beschikbaar;
        _bereik = new Size(_kolommen * _kaart.Width, rijen * _kaart.Height);

        // Niet verder scrollen dan er inhoud is (bv. nadat een filter kaarten verbergt).
        _positie.Y = Math.Max(0, Math.Min(_positie.Y, _bereik.Height - _kijkvenster.Height));

        BouwZichtbareOp(aantal, beschikbaar);
        ScrollOwner?.InvalidateScrollInfo();

        return new Size(
            double.IsInfinity(beschikbaar.Width) ? _bereik.Width : beschikbaar.Width,
            double.IsInfinity(beschikbaar.Height) ? _bereik.Height : beschikbaar.Height);
    }

    /// <summary>Hoe vaak we al om een nieuwe meting gevraagd hebben.</summary>
    private int _herkansingen;

    /// <summary>
    /// Vraagt om nog een meetronde wanneer er nu niets op te bouwen valt.
    ///
    /// Dit is niet overbodig: enkel overslaan is te weinig. Wisselde je van raster
    /// naar lijst en terug, dan viel de eerste meting van het nieuwe paneel nog
    /// voor het aan zijn lijst hing. Zonder herkansing bleef het paneel op nul
    /// staan en keek je naar een leeg scherm, terwijl de statusregel en de pager
    /// gewoon resultaten meldden. Er komt dan namelijk niets meer dat vanzelf een
    /// nieuwe meting uitlokt.
    ///
    /// De teller is de rem: blijft het paneel onbereikbaar, dan houdt het na tien
    /// pogingen op in plaats van de processor bezig te houden.
    /// </summary>
    private Size Herkansing()
    {
        if (_herkansingen++ < 10)
        {
            Dispatcher.BeginInvoke(new Action(InvalidateMeasure),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        return new Size(0, 0);
    }

    private Size MeetEersteKaart(Size beschikbaar)
    {
        var generator = ItemContainerGenerator;
        if (generator is null) return Size.Empty;

        var plek = generator.GeneratorPositionFromIndex(0);

        using (generator.StartAt(plek, GeneratorDirection.Forward, true))
        {
            if (generator.GenerateNext(out var nieuw) is not UIElement kind) return new Size(1, 1);

            if (nieuw) AddInternalChild(kind);
            generator.PrepareItemContainer(kind);

            kind.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var maat = kind.DesiredSize;
            return new Size(Math.Max(1, maat.Width), Math.Max(1, maat.Height));
        }
    }

    /// <summary>
    /// Bouwt de kaarten op die bij de huidige schuifstand in beeld vallen, en ruimt
    /// op wat eruit geschoven is. Er gaat bewust één rij extra boven en onder mee:
    /// dan staat de volgende rij al klaar voor ze te zien is.
    /// </summary>
    private void BouwZichtbareOp(int aantal, Size beschikbaar)
    {
        if (ItemContainerGenerator is null) return;

        // Twee rijen extra boven en onder. Hoe ruimer die voorraad, hoe minder vaak
        // er tijdens het schuiven kaarten bij moeten - en dat scheelt haperingen.
        var eersteRij = Math.Max(0, (int)(_positie.Y / _kaart.Height) - 2);
        var laatsteRij = (int)((_positie.Y + beschikbaar.Height) / _kaart.Height) + 2;

        var van = Math.Min(aantal - 1, eersteRij * _kolommen);
        var tot = Math.Min(aantal - 1, (laatsteRij + 1) * _kolommen - 1);

        var generator = ItemContainerGenerator;
        var start = generator.GeneratorPositionFromIndex(van);

        // Altijd vooruit genereren. En de kaart moet op ZIJN plaats tussen de
        // kinderen komen, niet achteraan: ArrangeOverride leidt uit de plaats van
        // een kind af welk zoekertje het is. Voegde je alles achteraan toe, dan
        // klopte die koppeling niet meer en bleef er één kaart over.
        var kindIndex = start.Offset == 0 ? start.Index : start.Index + 1;

        using (generator.StartAt(start, GeneratorDirection.Forward, true))
        {
            for (var i = van; i <= tot; i++, kindIndex++)
            {
                if (generator.GenerateNext(out var nieuw) is not UIElement kind) break;

                // LET OP: `nieuw` betekent "vers gemaakt", niet "moet nog geplaatst
                // worden". Een HERGEBRUIKTE kaart komt uit de voorraad terug met
                // nieuw = false, en die hebben wij bij het opruimen net uit de boom
                // gehaald. Sla je hem dan over, dan bestaat hij wel maar staat hij
                // nergens - en dat is precies wat er gebeurde: onderaan de lijst
                // bleef het scherm leeg terwijl de schuifbalk gewoon doorliep.
                //
                // Dus: kijken of hij op zijn plaats staat, en zo niet, hem er zetten.
                var staatGoed = kindIndex < InternalChildren.Count
                                && ReferenceEquals(InternalChildren[kindIndex], kind);

                if (!staatGoed)
                {
                    if (kindIndex >= InternalChildren.Count) AddInternalChild(kind);
                    else InsertInternalChild(kindIndex, kind);

                    generator.PrepareItemContainer(kind);
                }

                kind.Measure(new Size(_kaart.Width, _kaart.Height));
            }
        }

        RuimOp(van, tot);
    }

    /// <summary>
    /// Haalt de kaarten weg die buiten het opgebouwde bereik vallen.
    ///
    /// Waar het kan worden ze HERGEBRUIKT in plaats van weggegooid. Een kaart
    /// opbouwen kost werk - een foto, een schaduw, een afgeronde uitsnede - en dat
    /// bij elke schuifstap opnieuw doen is precies wat het scrollen deed haperen.
    /// Met hergebruik komt dezelfde omhulling terug met andere inhoud erin.
    /// </summary>
    private void RuimOp(int van, int tot)
    {
        var eigenaar = ItemsControl.GetItemsOwner(this);
        var generator = ItemContainerGenerator;
        if (generator is null) return;

        // Hergebruiken kan enkel als de lijst daarom vraagt en de generator het kan;
        // die mogelijkheid zit op een aparte interface.
        var hergebruiker = eigenaar is not null
                           && GetVirtualizationMode(eigenaar) == VirtualizationMode.Recycling
            ? generator as IRecyclingItemContainerGenerator
            : null;

        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var plek = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(plek);

            if (index >= van && index <= tot) continue;

            if (hergebruiker is not null) hergebruiker.Recycle(plek, 1);
            else generator.Remove(plek, 1);

            RemoveInternalChildRange(i, 1);
        }
    }

    protected override Size ArrangeOverride(Size vak)
    {
        var generator = ItemContainerGenerator;
        if (generator is null) return vak;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var kind = InternalChildren[i];
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));

            if (index < 0) continue;

            var rij = index / _kolommen;
            var kolom = index % _kolommen;

            kind.Arrange(new Rect(
                kolom * _kaart.Width,
                rij * _kaart.Height - _positie.Y,
                _kaart.Width,
                _kaart.Height));
        }

        return vak;
    }

    /// <summary>
    /// Bij een verandering in de lijst mogen de opgebouwde kaarten weg: hun plaats
    /// klopt dan niet meer. De maat van een kaart blijft wel gelden.
    /// </summary>
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;

            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                _positie.Y = 0;
                break;
        }

        InvalidateMeasure();
    }

    // ---------- IScrollInfo ----------
    //
    // Hierdoor schuift de ScrollViewer per beeldpunt in plaats van per kaart, en
    // weet hij hoe groot het geheel is zonder dat alles opgebouwd is.

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => _bereik.Width;
    public double ExtentHeight => _bereik.Height;
    public double ViewportWidth => _kijkvenster.Width;
    public double ViewportHeight => _kijkvenster.Height;
    public double HorizontalOffset => _positie.X;
    public double VerticalOffset => _positie.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    /// <summary>Eén regel: een zesde van een kaart.</summary>
    private double Stap => _kaart.IsEmpty ? 16 : _kaart.Height / 6;

    public void LineUp() => ZetVerticaal(_positie.Y - Stap);
    public void LineDown() => ZetVerticaal(_positie.Y + Stap);
    public void PageUp() => ZetVerticaal(_positie.Y - _kijkvenster.Height);
    public void PageDown() => ZetVerticaal(_positie.Y + _kijkvenster.Height);

    // Het muiswiel wordt hier niet meer afgehandeld: SmoothScroll vangt het af bij
    // de lijst en laat het schuifvak er in een paar frames naartoe glijden. Zo zit
    // het gevoel van schuiven op één plaats, voor allebei de weergaven.
    public void MouseWheelUp() => ZetVerticaal(_positie.Y - Stap * 3);
    public void MouseWheelDown() => ZetVerticaal(_positie.Y + Stap * 3);

    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset) => ZetVerticaal(offset);

    private double Begrens(double y) =>
        Math.Max(0, Math.Min(y, Math.Max(0, _bereik.Height - _kijkvenster.Height)));

    /// <summary>
    /// Zet de schuifstand en werkt bij wat er moet. Enkel opnieuw MÉTEN wanneer er
    /// echt andere kaarten in beeld komen; anders volstaat opnieuw plaatsen, en dat
    /// is een fractie van het werk.
    /// </summary>
    private void ZetVerticaal(double y)
    {
        y = Begrens(y);

        if (Math.Abs(y - _positie.Y) < 0.1) return;

        var vorigeRij = _kaart.IsEmpty ? -1 : (int)(_positie.Y / _kaart.Height);
        _positie.Y = y;
        var nieuweRij = _kaart.IsEmpty ? -1 : (int)(_positie.Y / _kaart.Height);

        if (nieuweRij != vorigeRij) InvalidateMeasure();
        else InvalidateArrange();

        ScrollOwner?.InvalidateScrollInfo();
    }

    /// <summary>Zorgt dat een kaart in beeld komt, bv. bij navigeren met het toetsenbord.</summary>
    public Rect MakeVisible(Visual visual, Rect rechthoek)
    {
        if (_kaart.IsEmpty) return rechthoek;

        var kind = visual as UIElement;
        if (kind is null) return rechthoek;

        var index = ItemContainerGenerator.IndexFromGeneratorPosition(
            new GeneratorPosition(InternalChildren.IndexOf(kind), 0));

        if (index < 0) return rechthoek;

        var boven = index / _kolommen * _kaart.Height;
        var onder = boven + _kaart.Height;

        if (boven < _positie.Y) ZetVerticaal(boven);
        else if (onder > _positie.Y + _kijkvenster.Height) ZetVerticaal(onder - _kijkvenster.Height);

        return rechthoek;
    }
}

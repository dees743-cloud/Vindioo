using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vindioo.Controls;
using Vindioo.Models;
using Vindioo.Services;
using Vindioo.Sources;

namespace Vindioo;

/// <summary>
/// De tabstrip: een tab per site die meezoekt, en de doorlopende omtrek eromheen.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- sites als tabbladen ----------

    private void BuildSiteTabs()
    {
        var previous = _active?.Def?.Id;

        // Wat er op de tabs stond, per Id van de site (dat blijft gelijk als je een site
        // hernoemt). Na Sites beheren, Site toevoegen of een import werden de tabs vroeger
        // leeg herbouwd: alle sites weer uitgevinkt, en je postcode en prijzen weg.
        var vorige = _tabs
            .Where(t => t.Def is not null)
            .GroupBy(t => t.Def!.Id)
            .ToDictionary(g => g.Key, g => g.First());

        _tabs.Clear();

        // Eerst het overzicht: één tabblad met alles samen, zodat je niet zeven
        // keer moet klikken om te zien wat er binnengekomen is.
        _tabs.Add(SiteTab.Alles());

        // Daarna elke bron uit de sitesmap; de fabriek kiest per site de motor.
        // Alles staat uitgevinkt: je kiest zelf welke sites meezoeken, want elke
        // extra site kost tijd - zeker die via de brug.
        foreach (var site in _store.Sites)
        {
            var tab = new SiteTab(site, SourceFactory.Create(site)) { IsEnabled = false };

            if (vorige.TryGetValue(site.Id, out var oud))
            {
                SiteSetting.FromTab(oud).ApplyTo(tab);
                tab.ResultCount = oud.ResultCount;
                tab.ErrorText = oud.ErrorText;
            }

            // De strip volgt het vinkje, waar dat ook omgezet wordt: in de popup,
            // of wanneer een bewaarde zoekopdracht zijn sites oplegt. Daarom hangt
            // dit aan de SiteTab zelf en niet aan het vinkje in de popup - anders
            // moet elke plek die IsEnabled aanraakt eraan denken.
            tab.PropertyChanged += SiteTab_PropertyChanged;
            _tabs.Add(tab);
        }

        // De lijst in de popup toont ze allemaal, ook de uitgevinkte: dat is juist
        // waar je ze aanzet.
        SitesChoiceList.ItemsSource = _tabs.Where(t => !t.IsAll).ToList();

        _tabsView.Refresh();
        UpdateChooserLabel();

        var restored = previous is null ? null : _tabs.FirstOrDefault(t => t.Def?.Id == previous);
        SetActiveTab(restored ?? _tabs.FirstOrDefault());
    }

    /// <summary>Opent het tabblad van één site en toont enkel zijn resultaten.</summary>
    private void SetActiveTab(SiteTab? tab)
    {
        // Een andere site heeft zijn eigen reeks resultaten; op pagina vier van de
        // vorige beginnen slaat nergens op.
        if (!ReferenceEquals(_active, tab)) _pagina = 0;

        _active = tab;

        foreach (var entry in _tabs) entry.IsActive = ReferenceEquals(entry, tab);

        // De filters in de popups horen bij deze site.
        LocationPopup.DataContext = tab;
        SpecialPopup.DataContext = tab;
        PricePopup.DataContext = tab;
        CountPopup.DataContext = tab;

        UpdateFilterAvailability();

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();
        UpdateTabFrame();
    }

    // ---------- de omtrek rond tabs en pagina ----------

    /// <summary>De maten waarmee de omtrek het laatst getekend is.</summary>
    private (double A, double B, double Top, double Strip, double W, double H) _frame;

    /// <summary>
    /// Tekent de tabstrip en de pagina als EEN doorlopende figuur: de bovenrand
    /// van de pagina, de open tab die daaruit omhoog steekt, en de uitlopen die
    /// links en rechts van die tab naar buiten in de lijn buigen.
    ///
    /// Waarom in een figuur en niet in losse stukken: waar twee vormen tegen
    /// elkaar aan moeten sluiten krijg je altijd een sprongetje of een puntje van
    /// een lijnuiteinde, hoe nauwkeurig je de coordinaten ook kiest. Binnen een
    /// figuur bestaat die naad niet.
    /// </summary>
    private void UpdateTabFrame()
    {
        if (!_ready || TabFrame is null) return;

        var w = SearchPanel.ActualWidth;
        var h = SearchPanel.ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double r = 12;    // straal van de hoeken
        const double f = 10;    // straal van de uitloop naast de open tab

        var inset = TabFrame.StrokeThickness / 2;   // de lijn valt binnen de vorm
        double left = inset, right = w - inset, bottom = h - inset;
        var strip = SiteTabsList.ActualHeight;      // onderkant van de tabstrip

        // Waar staat de open tab?
        double a = 0, b = 0, top = 0;
        var notch = false;

        if (ActiveTabElement() is { } pill && pill.ActualWidth > 0)
        {
            var punt = pill.TransformToAncestor(SearchPanel).Transform(new Point(0, 0));
            a = punt.X;
            b = punt.X + pill.ActualWidth;
            top = punt.Y;

            // Enkel inkepen wanneer de tab echt op de onderste rij staat en er
            // rechts genoeg plaats is voor de uitloop.
            notch = Math.Abs(punt.Y + pill.ActualHeight - strip) < 2
                    && b + f < right - r
                    && a >= left - 2;
        }

        // Niets herberekenen wanneer er niets bewoog: Data toekennen zet een
        // nieuwe lay-outronde in gang, en die roept dit weer aan.
        var nu = (a, b, top, strip, w, h);
        if (nu == _frame) return;
        _frame = nu;

        var vorm = new StreamGeometry();

        using (var g = vorm.Open())
        {
            void Lijn(double x, double y) => g.LineTo(new Point(x, y), true, false);

            void Boog(double x, double y, double straal, SweepDirection kant) =>
                g.ArcTo(new Point(x, y), new Size(straal, straal), 0, false, kant, true, false);

            // De onderkant en de zijkanten van de pagina; overal gelijk.
            void Pagina()
            {
                Lijn(right - r, strip);
                Boog(right, strip + r, r, SweepDirection.Clockwise);
                Lijn(right, bottom - r);
                Boog(right - r, bottom, r, SweepDirection.Clockwise);
                Lijn(left + r, bottom);
                Boog(left, bottom - r, r, SweepDirection.Clockwise);
            }

            if (!notch)
            {
                // Geen open tab op de onderste rij: gewoon een afgerond vlak.
                g.BeginFigure(new Point(left + r, strip), true, true);
                Pagina();
                Lijn(left, strip + r);
                Boog(left + r, strip, r, SweepDirection.Clockwise);
            }
            else if (a <= left + 2)
            {
                // De open tab staat helemaal links. Dan is er geen uitloop nodig:
                // de linkerlijn van de pagina loopt gewoon door tot boven de tab
                // en rondt daar af.
                g.BeginFigure(new Point(left, top + r), true, true);
                Boog(left + r, top, r, SweepDirection.Clockwise);
                Lijn(b - r, top);
                Boog(b, top + r, r, SweepDirection.Clockwise);
                Lijn(b, strip - f);
                Boog(b + f, strip, f, SweepDirection.Counterclockwise);
                Pagina();
                // De sluiting trekt de linkerlijn omhoog naar het beginpunt.
            }
            else
            {
                // De open tab staat ergens in het midden: links en rechts een uitloop.
                g.BeginFigure(new Point(left + r, strip), true, true);
                Lijn(a - f, strip);
                Boog(a, strip - f, f, SweepDirection.Counterclockwise);
                Lijn(a, top + r);
                Boog(a + r, top, r, SweepDirection.Clockwise);
                Lijn(b - r, top);
                Boog(b, top + r, r, SweepDirection.Clockwise);
                Lijn(b, strip - f);
                Boog(b + f, strip, f, SweepDirection.Counterclockwise);
                Pagina();
                Lijn(left, strip + r);
                Boog(left + r, strip, r, SweepDirection.Clockwise);
            }
        }

        vorm.Freeze();
        TabFrame.Data = vorm;
    }

    /// <summary>Het randje van de tab die open staat, of null.</summary>
    private FrameworkElement? ActiveTabElement()
    {
        if (_active is null) return null;

        var container = SiteTabsList.ItemContainerGenerator.ContainerFromItem(_active);
        return container is null ? null : EersteRand(container);
    }

    /// <summary>De eerste Border in de boom: dat is het kadertje van de tab zelf.</summary>
    private static FrameworkElement? EersteRand(DependencyObject wortel)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wortel); i++)
        {
            var kind = VisualTreeHelper.GetChild(wortel, i);
            if (kind is Border rand) return rand;

            if (EersteRand(kind) is { } dieper) return dieper;
        }

        return null;
    }

    private void SiteTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SiteTab tab) SetActiveTab(tab);
    }

    /// <summary>
    /// Opent de lijst met sites. Het chipje zit in een ItemsControl en heeft dus
    /// geen naam om in de XAML naar te verwijzen; de PlacementTarget wijst daarom
    /// naar het randje waarop geklikt is.
    /// </summary>
    private void SitesChip_Click(object sender, MouseButtonEventArgs e)
    {
        SitesPopup.PlacementTarget = sender as UIElement;
        SitesPopup.IsOpen = true;
    }

    /// <summary>
    /// Een site aan- of uitgevinkt: de tabstrip toont enkel wat meedoet, dus de
    /// weergave moet opnieuw beslissen wie er in staat.
    /// </summary>
    private void SiteTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SiteTab.IsEnabled)) return;

        _tabsView.Refresh();
        UpdateChooserLabel();

        // Stond de tab open van de site die je net uitvinkte, dan verdwijnt hij
        // onder je handen. Terugvallen op "Alles" in plaats van op een lege pagina.
        if (_active is { IsAll: false, IsEnabled: false })
            SetActiveTab(_tabs.FirstOrDefault(t => t.IsAll));

        UpdateTabFrame();
    }

    private void SiteEnabled_Changed(object sender, RoutedEventArgs e)
    {
        var count = _tabs.Count(t => t.IsEnabled);
        StatusText.Text = count == 0
            ? "Geen enkele site aangevinkt."
            : $"{count} site(s) zoeken mee.";
    }

    /// <summary>Zet op het chipje hoeveel sites er meezoeken.</summary>
    private void UpdateChooserLabel()
    {
        var aan = _tabs.Count(t => !t.IsAll && t.IsEnabled);
        var totaal = _tabs.Count(t => !t.IsAll);

        _chooser.IsEmpty = aan == 0;
        _chooser.Label = aan == 0 ? "Sites kiezen" : $"{aan} van {totaal} sites";
    }

    /// <summary>
    /// Sluiten van de sitelijst. De tabstrip is intussen al bijgewerkt; wat hier
    /// nog moet gebeuren is de omtrek opnieuw tekenen, want de rij is van lengte
    /// veranderd.
    /// </summary>
    private void SitesPopup_Closed(object sender, EventArgs e) => UpdateTabFrame();

    /// <summary>
    /// Dimt de filterknoppen waar deze site niets mee kan. Wat een site aankan
    /// staat in zijn eigen Filters-mapping, dus dit hoeft nergens hardgecodeerd.
    /// De prijs blijft altijd bruikbaar, want die dwingt de app zelf af.
    /// </summary>
    private void UpdateFilterAvailability()
    {
        if (_active is null)
        {
            LocationButton.IsEnabled = false;
            CardButton.IsEnabled = false;
            return;
        }

        // Op het tabblad "Alles" horen de zoekinstellingen bij geen enkele site,
        // dus staan ze uit. De volgorde en de weergave gelden wel voor alles en
        // blijven dus gewoon bruikbaar.
        if (_active.IsAll)
        {
            // CountButton hoort hier niet meer bij: die zet het aantal per pagina,
            // en dat geldt voor de hele app - ook op "Alles".
            foreach (var knop in new[] { LocationButton, PriceButton,
                                         SpecialButton, CardButton })
            {
                knop.IsEnabled = false;
                knop.ToolTip = "Kies eerst een site; deze instellingen horen bij één site.";
            }

            SpecialPanel.Children.Clear();
            LocationCustomPanel.Children.Clear();
            return;
        }

        PriceButton.IsEnabled = true;
        PriceButton.ToolTip = "Prijs";

        var def = _active.Def!;

        var hasPostcode = SearchUrlBuilder.Supports(def, "postcode")
                          || SearchUrlBuilder.Supports(def, "location");
        var hasRadius = SearchUrlBuilder.Supports(def, "radius")
                        || SearchUrlBuilder.Supports(def, "radiusMeters");
        // Filters uit het sitebestand die over de plaats gaan, zoals de provincies
        // van AlleVeilingen. Die staan in deze popup en niet bij de andere filters,
        // en een site zonder postcode of straal kan er dan toch mee op locatie zoeken.
        var hasLocationFilters = def.CustomFilters.Any(f => f.Section == CustomFilterSection.Location);

        LocationButton.IsEnabled = hasPostcode || hasRadius || hasLocationFilters;
        LocationButton.ToolTip = LocationButton.IsEnabled
            ? "Locatie en afstand"
            : $"{_active.Name} kan niet op locatie zoeken.";

        // Wat een site niet kent, verbergen we in plaats van te dimmen: een
        // uitgegrijsd invoerveld ziet eruit als iets dat stuk is.
        PostcodePanel.Visibility = hasPostcode || hasRadius ? Visibility.Visible : Visibility.Collapsed;
        PostcodeBox.IsEnabled = hasPostcode;
        RadiusBox.IsEnabled = hasRadius;
        BuildLocationFilters();

        LocationNote.Text = hasPostcode && !hasRadius
            ? "Deze site kent wel een postcode, maar geen straal."
            : "";

        CardButton.IsEnabled = true;
        CardButton.ToolTip = $"{_active.Name} beheren";

        // Enkel de filters die in deze popup horen; die over de plaats staan bij de locatie.
        var hasSpecial = def.CustomFilters.Any(f => f.Section == CustomFilterSection.Site);

        SpecialButton.IsEnabled = hasSpecial;
        SpecialButton.ToolTip = hasSpecial
            ? $"Filters van {_active.Name}"
            : $"{_active.Name} heeft geen eigen filters.";

        BuildSpecialFilters();
    }

    // ==================== filters die maar op één site bestaan ====================

    /// <summary>
    /// Bouwt de popup met de sitegebonden filters op uit de sitebeschrijving.
    /// Dit gebeurt in code en niet in de XAML omdat elke site iets anders heeft:
    /// AutoScout24 kent brandstof en kilometerstand, een veilingsite niet. Wie er
    /// een filter bij wil, zet een regel in het JSON-bestand van die site — er
    /// hoeft niets aan de app te veranderen.
    /// </summary>
    private void BuildSpecialFilters()
    {
        SpecialPanel.Children.Clear();

        if (_active?.Def is not { } def) return;

        SpecialTitle.Text = $"Filters van {_active.Name}";

        var label = (Style)FindResource("FilterLabel");
        var uitleg = (Brush)FindResource("TextSubtleBrush");

        // De invoer zelf bouwt CustomFilterControls, dezelfde als in het
        // instellingenvenster van een zoekopdracht. De waarden gaan rechtstreeks
        // naar de filters van het open tabblad.
        foreach (var filter in def.CustomFilters.Where(f => f.Section == CustomFilterSection.Site))
            SpecialPanel.Children.Add(CustomFilterControls.Build(filter, _active.Filters.Custom, null, label, uitleg));
    }

    /// <summary>
    /// De sitegebonden filters die over de plaats gaan, in de locatie-popup. Bij
    /// AlleVeilingen zijn dat de provincies: die site kent geen postcode of straal
    /// maar wel regio's, en die zoek je waar je naar een plaats zoekt. Ze stonden daar
    /// al toen ze nog vast in de code zaten; nu komen ze uit het sitebestand.
    /// </summary>
    private void BuildLocationFilters()
    {
        LocationCustomPanel.Children.Clear();

        if (_active?.Def is not { } def) return;

        var label = (Style)FindResource("FilterLabel");
        var uitleg = (Brush)FindResource("TextSubtleBrush");

        // Eén kolom: de popup is smal, en "Oost-Vlaanderen" past niet in de helft.
        foreach (var filter in def.CustomFilters.Where(f => f.Section == CustomFilterSection.Location))
            LocationCustomPanel.Children.Add(
                CustomFilterControls.Build(filter, _active.Filters.Custom, null, label, uitleg, maxColumns: 1));
    }

    private void ClearSpecial_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;

        // Enkel de filters van deze popup: de provincies in de locatie-popup blijven staan.
        foreach (var filter in _active.Def?.CustomFilters.Where(f => f.Section == CustomFilterSection.Site)
                               ?? Enumerable.Empty<CustomFilter>())
            _active.Filters.Custom.Remove(filter.Key);

        BuildSpecialFilters();

        StatusText.Text = $"Filters van {_active.Name} gewist.";
    }
}

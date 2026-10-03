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
/// De filters boven de resultaten, de volgorde en de weergave.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- filters en weergave ----------

    /// <summary>
    /// De filters die enkel de site zelf kan toepassen, als één tekst. Wijzigt
    /// die na het sluiten van een popup, dan moet er opnieuw gezocht worden: die
    /// waarden zitten in de zoek-URL en niet in de resultaten.
    /// </summary>
    private string SiteFilterFingerprint()
    {
        if (_active is null) return "";

        var f = _active.Filters;

        // Ook de sitegebonden filters tellen mee: wijzig je de brandstof, dan
        // moet de app opnieuw gaan zoeken en niet enkel de lijst herschikken.
        var eigen = string.Join(",", f.Custom.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        return string.Join("|", f.Postcode, f.RadiusKm, eigen);
    }

    /// <summary>Hoe die filters stonden toen de popup openging.</summary>
    private string _filtersBijOpenen = "";

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;

        _filtersBijOpenen = SiteFilterFingerprint();

        if (ReferenceEquals(sender, SortButton)) SortPopup.IsOpen = true;
        else if (ReferenceEquals(sender, SpecialButton))
        {
            // Opnieuw opbouwen: een bewaarde zoekopdracht kan de waarden intussen
            // gewijzigd hebben, en de vinkjes moeten tonen wat er nu geldt.
            BuildSpecialFilters();
            SpecialPopup.IsOpen = true;
        }
        else if (ReferenceEquals(sender, LocationButton))
        {
            BuildLocationFilters();
            LocationPopup.IsOpen = true;
        }
        else if (ReferenceEquals(sender, PriceButton)) PricePopup.IsOpen = true;
        else if (ReferenceEquals(sender, CountButton))
        {
            // Aanvinken wat er nu geldt; de keuze staat in de instellingen van de
            // app en niet bij een site, dus er is niets om aan te binden.
            foreach (var kind in PageSizeKeuze.Children)
            {
                if (kind is RadioButton knop && knop.Tag is string tag)
                    knop.IsChecked = tag == AppSettings.Current.PageSize.ToString();
            }

            CountPopup.IsOpen = true;
        }
    }

    /// <summary>
    /// De prijs kan de app zelf afdwingen op wat er al staat, dus die werkt
    /// meteen door terwijl je typt.
    /// </summary>
    /// <summary>
    /// De volgorde is gewisseld. Dat gebeurt op de weergave en niet op de lijst
    /// zelf: de resultaten blijven staan zoals ze binnenkwamen, er wordt enkel
    /// anders naar gekeken.
    /// </summary>
    private void Sort_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _sort = ReferenceEquals(sender, SortPriceUp) ? ListingSort.PriceAscending
              : ReferenceEquals(sender, SortPriceDown) ? ListingSort.PriceDescending
              : ReferenceEquals(sender, SortNewest) ? ListingSort.Newest
              : ReferenceEquals(sender, SortEnding) ? ListingSort.EndingSoonest
              : ListingSort.Default;

        AppSettings.Current.Sort = (int)_sort;
        AppSettings.Current.Save();

        PasSorteringToe();
        ToonGefilterd();
    }

    /// <summary>
    /// Legt de gekozen volgorde op aan de weergave. Een gewone SortDescription
    /// kan hier niet: zoekertjes zonder prijs of zonder datum moeten achteraan,
    /// en dat vraagt een eigen vergelijking.
    /// </summary>
    private void PasSorteringToe()
    {
        if (_resultsView is not ListCollectionView weergave) return;

        weergave.CustomSort = _sort == ListingSort.Default ? null : new ListingComparer(_sort);
    }

    private void PriceFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        ToonGefilterd();

    /// <summary>
    /// Bij het sluiten van een filterpopup: locatie, straal, provincie en het
    /// maximum zitten in de zoek-URL, dus daarvoor is een nieuwe zoekopdracht
    /// nodig. Die starten we vanzelf, in plaats van de gebruiker terug naar het
    /// vergrootglas te sturen.
    /// </summary>
    private void FilterPopup_Closed(object sender, EventArgs e)
    {
        if (!_ready || _active is null) return;

        ToonGefilterd();

        if (SiteFilterFingerprint() == _filtersBijOpenen) return;
        _filtersBijOpenen = SiteFilterFingerprint();

        // Enkel opnieuw zoeken wanneer er al iets te zien was.
        if (_results.Count > 0) _ = RunSearchAsync();
    }

    /// <summary>Werkt de lijst en de teller bij nadat een filter gewijzigd is.</summary>
    private void ToonGefilterd()
    {
        if (!_ready || _active is null) return;

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();

        var zichtbaar = _resultsView.Cast<object>().Count();
        var gevonden = _results.Count(r => r.Source == _active.Name);

        if (gevonden == 0) return;

        StatusText.Text = zichtbaar == gevonden
            ? $"{gevonden} resultaten van {_active.Name}."
            : $"{zichtbaar} van {gevonden} resultaten van {_active.Name} binnen de filter.";
    }

    /// <summary>De twee manieren waarop de resultatenlijst getoond kan worden.</summary>
    private enum ResultView
    {
        /// <summary>Een brede kaart per zoekertje: foto, titel, prijs.</summary>
        List,

        /// <summary>Foto's naast en onder elkaar, zoals Facebook Marketplace.</summary>
        Grid
    }

    private ResultView _view = ResultView.List;

    private void LayoutButton_Click(object sender, RoutedEventArgs e) =>
        SetResultView(_view == ResultView.List ? ResultView.Grid : ResultView.List);

    /// <summary>De twee menu-items gedragen zich als keuzerondjes.</summary>
    private void ViewModeMenu_Click(object sender, RoutedEventArgs e) =>
        SetResultView(ReferenceEquals(sender, ViewGridMenu) ? ResultView.Grid : ResultView.List);

    /// <summary>
    /// Wisselt de resultatenlijst van vorm. Alleen het sjabloon en het paneel
    /// veranderen; de resultaten blijven staan, dus je kan tijdens het bekijken
    /// van een zoekopdracht van weergave wisselen.
    /// </summary>
    private void SetResultView(ResultView view)
    {
        // De keuze geldt voor de hele app en wordt onthouden tot de volgende start.
        if (_ready && AppSettings.Current.ResultView != (int)view)
        {
            AppSettings.Current.ResultView = (int)view;
            AppSettings.Current.Save();
        }

        _view = view;
        var grid = view == ResultView.Grid;

        ViewListMenu.IsChecked = !grid;
        ViewGridMenu.IsChecked = grid;

        // Het pictogram toont waar je naartoe gaat, niet waar je staat.
        LayoutIcon.Symbol = grid
            ? Wpf.Ui.Controls.SymbolRegular.TextBulletListLtr24
            : Wpf.Ui.Controls.SymbolRegular.Grid24;

        var template = (DataTemplate)FindResource(grid ? "GridItemTemplate" : "ListItemTemplate");
        var panel = (ItemsPanelTemplate)FindResource(grid ? "GridItemsPanel" : "ListItemsPanel");

        foreach (var list in new[] { ResultsList, FavoritesList })
        {
            list.ItemTemplate = template;
            list.ItemsPanel = panel;

            // In het raster moet de omhulling van een kaart precies zo groot zijn
            // als de kaart: het paneel rekent met die maat uit hoeveel kolommen er
            // passen. In de lijst blijft de standaardstijl staan.
            list.ItemContainerStyle = grid
                ? (Style)FindResource("GridItemContainerStyle")
                : null;

            // Allebei de panelen schuiven zelf, per beeldpunt: de lijst met een
            // VirtualizingStackPanel, het raster met ons eigen VirtualizingWrapPanel.
            // Dat laatste regelt zijn schuiven via IScrollInfo, en daarvoor MOET
            // CanContentScroll aan staan - anders wikkelt de ScrollViewer er zijn
            // eigen laag omheen, geeft hij het paneel oneindige hoogte, en bouwt het
            // alsnog alles op.
            ScrollViewer.SetCanContentScroll(list, true);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);

            // Per beeldpunt schuiven, niet per kaart. Met CanContentScroll aan doet
            // een VirtualizingStackPanel standaard het laatste: elke klik van het
            // muiswiel springt dan een hele kaart, en dat leest als schokkerig.
            // ScrollUnit.Pixel geeft vloeiend schuiven en houdt de virtualisatie.
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);

            // En het muiswiel zelf: een zinnige stap, met een glijbeweging.
            Controls.SmoothScroll.SetEnabled(list, true);
        }
    }
}

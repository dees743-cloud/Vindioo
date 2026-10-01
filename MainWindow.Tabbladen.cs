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
using Zentrix.Controls;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix;

/// <summary>
/// De balk onderaan, de favorieten en de recente zoektermen.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- de balk onderaan ----------

    private void BottomTab_Checked(object sender, RoutedEventArgs e)
    {
        // Bij het opbouwen van het venster bestaan de panelen nog niet.
        if (!_ready) return;

        SearchPanel.Visibility = Collapse(ReferenceEquals(sender, TabSearch));
        FavoritesPanel.Visibility = Collapse(ReferenceEquals(sender, TabFavorites));
        RecentPanel.Visibility = Collapse(ReferenceEquals(sender, TabRecent));
        SavedPanel.Visibility = Collapse(ReferenceEquals(sender, TabSaved));

        if (ReferenceEquals(sender, TabFavorites)) LoadFavorites();
        if (ReferenceEquals(sender, TabRecent)) LoadRecent();

        UpdateEmptyHints();

        static Visibility Collapse(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Toont de uitlegregel wanneer een lijst leeg is.</summary>
    private void UpdateEmptyHints()
    {
        if (!_ready) return;

        // Zonder sites valt er niets te zoeken: dan een uitnodiging om ze te importeren
        // of toe te voegen, in plaats van de gewone "nog geen resultaten".
        var geenSites = _store.Sites.Count == 0;
        NoSitesPanel.Visibility = geenSites ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = !geenSites && _resultsView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        FavoritesEmpty.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedEmpty.Visibility = _saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyHint.Text = LeegTekst();

        FavoritesCount.Text = (_favorites.Count == 1 ? "1 bewaard" : $"{_favorites.Count} bewaard")
                              + _watchSamenvatting;
    }

    /// <summary>
    /// Wat er in een lege resultatenlijst staat. Vroeger was dat altijd "Nog geen
    /// resultaten van ...", of er nu nog niets gezocht was, niets gevonden, alles
    /// weggefilterd, of de site mislukte - situaties die elk iets anders van je vragen.
    /// </summary>
    private string LeegTekst()
    {
        if (_active is null) return "Voeg eerst een site toe via het tandwiel.";

        if (!_tabs.Any(t => !t.IsAll && t.IsEnabled))
            return "Kies eerst op welke sites je wilt zoeken, met 'Sites kiezen' hierboven.";

        if (_zoektHandmatig) return "Zoeken...";
        if (!_heeftGezocht) return "Typ hierboven wat je zoekt en druk op Enter.";

        if (_active is { IsAll: false, HasError: true })
            return $"{_active.Name} mislukte: {_active.ErrorText}";

        // De schakelaar staat aan, en op deze tab is er niets wat je nog niet bekeek.
        if (_enkelNieuw)
            return (_active.IsAll ? "Niets nieuws." : $"Niets nieuws op {_active.Name}.") +
                   " Zet 'Enkel nieuwe' uit om alles te zien.";

        var gevonden = _results.Count(r => _active.IsAll || r.Source == _active.Name);

        // Op "Alles" stond hier "Niets gevonden.", ook als een site mislukte: de reden stond
        // enkel op de tab van die site, en daar keek je niet.
        if (gevonden == 0 && _active.IsAll)
        {
            var mislukt = _tabs.Where(t => !t.IsAll && t.IsEnabled && t.HasError).Select(t => t.TabName).ToList();

            if (mislukt.Count > 0)
                return $"Niets gevonden. {string.Join(", ", mislukt)} mislukte; " +
                       "wijs het waarschuwingsteken op de tab aan voor de reden.";
        }

        if (gevonden > 0)
            return gevonden == 1
                ? "Er is 1 resultaat, maar het valt buiten je filters."
                : $"Er zijn {gevonden} resultaten, maar ze vallen buiten je filters.";

        return _active.IsAll ? "Niets gevonden." : $"Niets gevonden op {_active.Name}.";
    }

    // ---------- favorieten ----------

    private void LoadFavorites()
    {
        _favorites.Clear();
        foreach (var listing in _history.GetFavorites()) _favorites.Add(listing);

        _favoriteKeys = _history.GetFavoriteKeys();

        // Wat er bij het vorige nakijken uitkwam, gaat mee weg: de lijst is opnieuw
        // ingelezen, dus die regels horen bij zoekertjes die er niet meer staan.
        _watchSamenvatting = "";
    }

    /// <summary>Loopt er een controle, dan is dit haar stopknop.</summary>
    private CancellationTokenSource? _watchStop;

    /// <summary>"· 1 weg, 2 afgelopen", achter het aantal bewaarde favorieten.</summary>
    private string _watchSamenvatting = "";

    /// <summary>
    /// Kijkt elke favoriet na: staat het zoekertje er nog, en wat kost het nu?
    ///
    /// Een favoriet is een kopie, dus de prijs erop is die van de dag dat je hem bewaarde.
    /// <see cref="FavoriteWatch"/> haalt de advertentiepagina op en zegt wat ze vandaag doet;
    /// hier komt enkel het tonen bij.
    ///
    /// Eén voor een, en niet allemaal tegelijk: een brugsite heeft één wachtrij en de
    /// browsersites delen één Chrome, dus tegelijk zou daar toch op elkaar staan wachten.
    /// Daarom is de knop intussen een stopknop - dezelfde vorm als het vergrootglas op het
    /// zoekscherm en de AI-controle, en om dezelfde reden: daar staat je muis al.
    /// </summary>
    private async void WatchFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (_watchStop is not null)
        {
            _watchStop.Cancel();
            return;
        }

        if (_favorites.Count == 0) return;

        using var stop = new CancellationTokenSource();
        _watchStop = stop;
        ZetNakijkknop(true);

        int weg = 0, afgelopen = 0, gewijzigd = 0, gedaan = 0;

        try
        {
            // Een kopie van de lijst: het sterretje kan er intussen een afhalen.
            foreach (var favoriet in _favorites.ToList())
            {
                stop.Token.ThrowIfCancellationRequested();
                FavoritesCount.Text = $"nakijken... {gedaan + 1} van {_favorites.Count}";

                var status = await FavoriteWatch.CheckAsync(favoriet, _store.Sites, stop.Token);

                favoriet.WatchText = FavoriteWatch.Tekst(status, favoriet.Price);
                favoriet.WatchIsWarning = status.Staat is FavoriteState.Weg or FavoriteState.Afgelopen;

                if (status.Staat == FavoriteState.Weg) weg++;
                else if (status.Staat == FavoriteState.Afgelopen) afgelopen++;
                else if (status.PrijsNu is > 0 && favoriet.Price is > 0 &&
                         status.PrijsNu != favoriet.Price) gewijzigd++;

                gedaan++;
            }
        }
        catch (OperationCanceledException)
        {
            // Gestopt. Wat al nagekeken was, blijft op de kaarten staan.
        }
        finally
        {
            _watchStop = null;
            ZetNakijkknop(false);

            _watchSamenvatting = Samenvatting(gedaan, weg, afgelopen, gewijzigd);
            UpdateEmptyHints();
        }
    }

    /// <summary>Wat er achter "3 bewaard" komt te staan.</summary>
    private static string Samenvatting(int gedaan, int weg, int afgelopen, int gewijzigd)
    {
        if (gedaan == 0) return "";

        var delen = new List<string>();
        if (weg > 0) delen.Add($"{weg} weg");
        if (afgelopen > 0) delen.Add($"{afgelopen} afgelopen");
        if (gewijzigd > 0) delen.Add($"{gewijzigd} met een andere prijs");

        return delen.Count == 0 ? " · alles staat er nog" : " · " + string.Join(", ", delen);
    }

    private void ZetNakijkknop(bool bezig)
    {
        WatchButton.Content = bezig ? "Stoppen" : "Nakijken";
        WatchButton.Icon = new Wpf.Ui.Controls.SymbolIcon(
            bezig ? Wpf.Ui.Controls.SymbolRegular.Stop24 : Wpf.Ui.Controls.SymbolRegular.ArrowSync24);

        WatchButton.Appearance = bezig
            ? Wpf.Ui.Controls.ControlAppearance.Caution
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    /// <summary>Zet een zoekertje bij de favorieten, of haalt het er weer af.</summary>
    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        if (listing.IsFavorite)
        {
            _history.RemoveFavorite(listing.Key);
            _favoriteKeys.Remove(listing.Key);
            listing.IsFavorite = false;

            // Op de favorietenpagina verdwijnt hij meteen uit de lijst.
            var stored = _favorites.FirstOrDefault(f => f.Key == listing.Key);
            if (stored is not null) _favorites.Remove(stored);

            StatusText.Text = "Uit de favorieten gehaald.";
        }
        else
        {
            _history.AddFavorite(listing);
            _favoriteKeys.Add(listing.Key);
            listing.IsFavorite = true;

            StatusText.Text = "Bij de favorieten gezet.";
        }

        // Hetzelfde zoekertje kan in beide lijsten staan; die andere moet volgen.
        foreach (var other in _results.Where(r => r.Key == listing.Key))
            other.IsFavorite = listing.IsFavorite;

        UpdateEmptyHints();
    }

    // ---------- recente zoekopdrachten ----------

    private void LoadRecent()
    {
        _recent.Clear();
        foreach (var entry in _history.GetRecent()) _recent.Add(entry);
    }

    private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenRecent();

    /// <summary>Enter doet hetzelfde als dubbelklikken; daarvoor moest je de muis gebruiken.</summary>
    private void RecentList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        OpenRecent();
        e.Handled = true;
    }

    private void OpenRecent()
    {
        if (RecentList.SelectedItem is not RecentSearch entry) return;

        QueryBox.Text = entry.Query;
        _activeSearch = null;

        TabSearch.IsChecked = true;
        _ = RunSearchAsync();
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        _history.ClearRecent();
        LoadRecent();
        UpdateEmptyHints();
    }
}

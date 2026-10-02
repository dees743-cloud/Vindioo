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

        ZetOpruimknop();
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

                // Apart, want de kaart toont er twee verschillende dingen mee: een kruis over
                // wat weg is, en een stempel "AFGELOPEN" over een veiling die voorbij is.
                favoriet.WatchIsGone = status.Staat == FavoriteState.Weg;
                favoriet.WatchIsEnded = status.Staat == FavoriteState.Afgelopen;

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

        // Terug naar Primary, niet naar Secondary: dat laatste stond hier nog van toen de knop
        // rechts in de hoek hing, en dan was Nakijken na één keer gebruiken stilletjes weer
        // onopvallend geworden - precies wat we op 2 oktober 2026 wilden verhelpen.
        WatchButton.Appearance = bezig
            ? Wpf.Ui.Controls.ControlAppearance.Caution
            : Wpf.Ui.Controls.ControlAppearance.Primary;

        ZetOpruimknop();
    }

    /// <summary>
    /// Opruimen kan enkel wanneer er iets te ruimen is, en niet terwijl het nakijken loopt - dan
    /// verandert de lijst onder de lus.
    ///
    /// Op één plaats bepaald, en van daaruit ook door <see cref="UpdateEmptyHints"/> aangeroepen.
    /// Wissel je van tabblad, dan haalt <c>LoadFavorites</c> de lijst opnieuw uit de databank en
    /// zijn de merktekens van de vorige ronde weg; stond het ergens anders, dan bleef de knop
    /// aanstaan voor iets wat er niet meer was.
    /// </summary>
    private void ZetOpruimknop() =>
        CleanButton.IsEnabled = _watchStop is null && _favorites.Any(f => f.WatchIsWarning);

    /// <summary>
    /// Gooit de favorieten weg die weg of afgelopen zijn.
    ///
    /// Enkel wat <see cref="FavoriteWatch"/> echt zo gevonden heeft, dus deze knop doet pas iets
    /// ná Nakijken. Daarom staat hij uit zolang er niets te ruimen valt: een knop die niets doet
    /// en niet zegt waarom, laat je twijfelen of je hem wel goed aanklikte.
    ///
    /// Met een bevestiging, zoals bij het verwijderen van een zoekopdracht of een site: een
    /// favoriet is iets wat je zelf bewaarde en er is geen weg terug.
    /// </summary>
    private void CleanFavorites_Click(object sender, RoutedEventArgs e)
    {
        var opruimen = _favorites.Where(f => f.WatchIsWarning).ToList();
        if (opruimen.Count == 0) return;

        var weg = opruimen.Count(f => f.WatchIsGone);
        var afgelopen = opruimen.Count(f => f.WatchIsEnded);

        var wat = string.Join(" en ", new[]
        {
            weg > 0 ? (weg == 1 ? "1 zoekertje dat weg is" : $"{weg} zoekertjes die weg zijn") : "",
            afgelopen > 0 ? (afgelopen == 1 ? "1 afgelopen veiling" : $"{afgelopen} afgelopen veilingen") : ""
        }.Where(d => d.Length > 0));

        var bevestig = MessageBox.Show(this,
            $"{wat} uit je favorieten halen?\n\nDat kan niet ongedaan gemaakt worden.",
            "Favorieten opruimen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (bevestig != MessageBoxResult.Yes) return;

        foreach (var favoriet in opruimen)
        {
            _history.RemoveFavorite(favoriet.Key);
            _favoriteKeys.Remove(favoriet.Key);
            favoriet.IsFavorite = false;
            _favorites.Remove(favoriet);
        }

        // De samenvatting sloeg op wat er stond; die klopt nu niet meer.
        _watchSamenvatting = opruimen.Count == 1
            ? " · 1 opgeruimd"
            : $" · {opruimen.Count} opgeruimd";

        // UpdateEmptyHints zet de knop zelf weer uit: er staat nu niets meer te ruimen.
        UpdateEmptyHints();

        StatusText.Text = opruimen.Count == 1
            ? "1 favoriet opgeruimd."
            : $"{opruimen.Count} favorieten opgeruimd.";
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

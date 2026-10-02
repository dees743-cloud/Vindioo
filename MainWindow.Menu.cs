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
/// Het tandwielmenu, en een zoekertje openen.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- menu achter het tandwiel ----------

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        GearMenu.PlacementTarget = SearchSettingsButton;
        GearMenu.Placement = PlacementMode.Bottom;
        GearMenu.IsOpen = true;
    }

    /// <summary>De instellingen van deze zoekterm, nu als eerste regel in het menu.</summary>
    private void SearchSettingsMenu_Click(object sender, RoutedEventArgs e) =>
        SearchSettingsButton_Click(sender, e);

    /// <summary>
    /// Tandwiel > Sites beheren: meteen het venster met een tab per site, open op de site
    /// waarvan de tab nu openstaat (op "Alles": de eerste). Tot september 2026 hing hier een
    /// submenu met elke site apart, dat meegroeide met het aantal sites; in het venster
    /// staan ze toch al als tabs.
    /// </summary>
    private void ManageSitesMenu_Click(object sender, RoutedEventArgs e) => OpenSettings(_active?.Name);

    private void CardButton_Click(object sender, RoutedEventArgs e) => OpenSettings(_active?.Name);

    /// <summary>
    /// Opent de instellingen met een kaart per site, eventueel meteen op de
    /// kaart van een bepaalde site.
    /// </summary>
    private void OpenSettings(string? site = null)
    {
        // De naam van elke site voor het venster opengaat, per Id: zo is na het sluiten te
        // zien welke site hernoemd werd.
        var namenVoor = _store.Sites.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Name);

        var window = new SettingsWindow(_store, site).Boven(this);
        window.ShowDialog();

        // De sites kunnen bewerkt, toegevoegd of verwijderd zijn: opnieuw inlezen
        // en de tabs herbouwen zodat het hoofdscherm klopt.
        _store.Load();

        var hernoemd = _store.Sites
            .Where(s => namenVoor.TryGetValue(s.Id, out var oud) && oud != s.Name)
            .Select(s => (Oud: namenVoor[s.Id], Nieuw: s.Name))
            .ToList();

        if (hernoemd.Count > 0) NeemHernoemenMee(hernoemd);
        if (_history.ApplyIdPatterns(_store.Sites) > 0) HerlaadSleutels();

        BuildSiteTabs();
    }

    /// <summary>
    /// Een site kreeg een andere naam: de databank neemt alles mee wat aan die naam hing
    /// (<see cref="HistoryStore.RenameSource"/>), en wat er nu op het scherm en in het geheugen
    /// staat, gaat mee. Zonder dat viel de site stil uit elke bewaarde zoekopdracht.
    /// </summary>
    private void NeemHernoemenMee(List<(string Oud, string Nieuw)> hernoemd)
    {
        foreach (var (oud, nieuw) in hernoemd)
        {
            _history.RenameSource(oud, nieuw);

            foreach (var listing in _results.Concat(_lastOutcomes.Values.SelectMany(l => l)).Where(l => l.Source == oud))
                listing.Source = nieuw;
        }

        var actief = _activeSearch?.Id;
        LoadSavedSearches();
        _activeSearch = actief is null ? null : _saved.FirstOrDefault(s => s.Id == actief);

        HerlaadSleutels();

        StatusText.Text = string.Join(" ", hernoemd.Select(h => $"'{h.Oud}' heet nu '{h.Nieuw}'.")) +
                          " Bewaarde zoekopdrachten, favorieten en wat je al zag, zijn meegenomen.";
    }

    /// <summary>De favorieten opnieuw inlezen, nadat hun sleutels veranderden.</summary>
    private void HerlaadSleutels()
    {
        _favoriteKeys = _history.GetFavoriteKeys();
        LoadFavorites();
    }

    private void AddSiteMenu_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddSiteWindow().Boven(this);

        if (window.ShowDialog() == true && window.Result is not null)
        {
            _store.Add(window.Result);
            BuildSiteTabs();
            StatusText.Text = $"Site '{window.Result.Name}' toegevoegd.";
        }
    }

    /// <summary>
    /// Importeert alle sitebestanden uit een map in één keer. De app komt zonder sites,
    /// dus dit is de eerste stap na het installeren. De map van de sites-repository zelf
    /// kiezen mag ook: <see cref="SiteStore.ImportFolder"/> kijkt dan in zijn submap "sites".
    /// </summary>
    private void ImportSitesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Map met sitebestanden kiezen" };
        if (dialog.ShowDialog(this) != true) return;

        var (aantal, mislukt, vervangen) = _store.ImportFolder(dialog.FolderName);

        // Opnieuw inlezen en de tabs herbouwen, net als na "Site toevoegen". Dat werkt
        // meteen ook de lege toestand bij.
        _store.Load();
        if (_history.ApplyIdPatterns(_store.Sites) > 0) HerlaadSleutels();
        BuildSiteTabs();

        StatusText.Text = aantal == 0 && mislukt.Count == 0
            ? "In die map staan geen sitebestanden."
            : $"{aantal} site(s) geïmporteerd" +
              (vervangen.Count > 0 ? $", waarvan {vervangen.Count} vervangen ({string.Join(", ", vervangen)})" : "") +
              (mislukt.Count > 0 ? $"; niet geïmporteerd: {string.Join(", ", mislukt)}." : ".");
    }

    /// <summary>
    /// Opent het logboek. Stond enkel in Sites beheren, en dat venster is met nul sites
    /// niet te openen - net wanneer een nieuwe gebruiker het nodig heeft.
    /// </summary>
    private void OpenLogMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(Log.FilePath))
            {
                StatusText.Text = "Nog geen logboek: doe eerst een zoekopdracht.";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Log.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon het logboek niet openen: " + ex.Message;
        }
    }

    /// <summary>Toont de koppelcode en zet hem op het klembord voor de extensie.</summary>
    private void BridgeCodeMenu_Click(object sender, RoutedEventArgs e)
    {
        var bridge = BridgeServer.Instance;
        bridge.Start();

        try
        {
            Clipboard.SetText(bridge.Token);

            StatusText.Text = bridge.ExtensionAlive
                ? "De Zentrix Brug is verbonden. De koppelcode staat op je klembord."
                : "Koppelcode gekopieerd. Klik in Chrome op het pictogram van de Zentrix Brug, plak de code en kies 'Code opslaan'.";
        }
        catch
        {
            StatusText.Text = "Koppelcode: " + bridge.Token;
        }
    }

    // ---------- een zoekertje openen ----------

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenSelected(sender as ListBox);

    /// <summary>Enter opent het geselecteerde zoekertje, net als dubbelklikken.</summary>
    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        OpenSelected(sender as ListBox);
        e.Handled = true;
    }

    /// <summary>
    /// De link als webadres (http of https), of null als het iets anders is.
    ///
    /// Met UseShellExecute opent Windows alles wat je het geeft: een webadres, maar ook een
    /// programma op schijf, een bestand op een netwerkmap (\\server\map\iets.exe) of een
    /// ander protocol. De link van een zoekertje komt uit de pagina of uit het sitebestand
    /// (UrlTemplate bij de linkmotor), en een sitebestand is bedoeld om te delen - het kan
    /// dus van een vreemde komen. Tot september 2026 ging elke link ongezien naar Windows.
    /// </summary>
    internal static Uri? AlsWebadres(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var adres) &&
        (adres.Scheme == Uri.UriSchemeHttp || adres.Scheme == Uri.UriSchemeHttps)
            ? adres
            : null;

    /// <summary>
    /// Rechtsklik op een foto: wat is dit ongeveer waard? Opent een eigen venster, zodat je
    /// verder kan kijken terwijl er gezocht wordt, en er meerdere naast elkaar kunnen staan.
    /// </summary>
    private void PriceIndicationMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        new PriceIndicationWindow(listing, _store.Sites).Boven(this).Show();
    }

    /// <summary>
    /// Rechtsklik op een foto: wat staat erop dat je zelf niet ziet? Eigen venster, om dezelfde
    /// reden als de prijsindicatie - het kijken duurt een halve minuut, en intussen wil je verder
    /// kunnen. Het rekenwerk gebeurt op je eigen grafische kaart; zie <see cref="PhotoAnalyzer"/>.
    /// </summary>
    private void PhotoInsightMenu_Click(object sender, RoutedEventArgs e) => OpenAiControle(sender, false);

    /// <summary>
    /// Hetzelfde, maar dan ook de andere foto's van die advertentie. De zoekpagina geeft er één;
    /// op de pagina van het zoekertje staan er vijf of tien, en juist daarop staat vaak wat je
    /// wil zien - het label achteraan, de doos van binnen.
    /// </summary>
    private void PhotoInsightAllMenu_Click(object sender, RoutedEventArgs e) => OpenAiControle(sender, true);

    private void OpenAiControle(object sender, bool alleFotos)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        new PhotoInsightWindow(listing, _store.Sites, alleFotos).Boven(this).Show();
    }

    /// <summary>
    /// Opent het geselecteerde zoekertje in een eigen venster: de foto's, de verkoper en
    /// hoelang het online staat. Tot september 2026 ging hier meteen de browser open; die
    /// staat nu als knop in dat venster. De reden staat bij <see cref="ListingDetailWindow"/>.
    /// </summary>
    private void OpenSelected(ListBox? lijst)
    {
        if (lijst?.SelectedItem is not Listing listing) return;

        new ListingDetailWindow(listing, _store.Sites).Boven(this).Show();
    }
}

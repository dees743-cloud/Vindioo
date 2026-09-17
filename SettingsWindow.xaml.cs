using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix;

/// <summary>
/// Instellingen: één kaart per site. Generieke sites zijn volledig bewerkbaar,
/// ingebouwde bronnen (2dehands, Facebook) tonen alleen informatie.
/// </summary>
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SiteStore _store;
    private readonly ObservableCollection<SiteCard> _cards = new();

    /// <summary>
    /// Opent "Sites beheren". Met <paramref name="openSite"/> springt het scherm
    /// meteen naar de kaart van die site - zo komt "tandwiel > Sites beheren > 2dehands"
    /// in het hoofdscherm rechtstreeks op de juiste tab uit.
    /// </summary>
    public SettingsWindow(SiteStore store, string? openSite = null)
    {
        InitializeComponent();
        _store = store;
        SiteTabs.ItemsSource = _cards;
        BuildCards();

        if (!string.IsNullOrWhiteSpace(openSite)) SelectCardByName(openSite);
    }

    /// <summary>Zet de kaarten opnieuw op: eerst de ingebouwde, dan de generieke.</summary>
    private void BuildCards()
    {
        _cards.Clear();

        foreach (var site in _store.Sites)
            _cards.Add(new SiteCard(site));
    }

    private SiteCard? CardOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as SiteCard;

    // ---------- knoppen per kaart ----------

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { IsEditable: true } card) return;

        card.Preview.Clear();
        card.Status = "Bezig met testen...";

        try
        {
            // De gedeelde Chrome lenen, zoals een zoekopdracht dat doet. Zonder lening bleef
            // die na het testen openstaan, want enkel de laatste lener sluit hem - in het
            // systeemvak soms uren.
            using var lease = BrowserPool.Lease();

            // Een brugsite heeft een draaiende extensie nodig; anders wachtte de test
            // anderhalve minuut op een antwoord dat niet kon komen.
            if (card.Def.UseBridge)
            {
                var brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30),
                    new Progress<string>(tekst => card.Status = tekst));

                if (brug != BridgeStatus.Ready)
                {
                    card.Status = "Testen gaat niet: " + ChromeLauncher.Describe(brug);
                    return;
                }
            }

            var source = SourceFactory.Create(card.Def);
            var found = await source.SearchAsync(TestWordBox.Text.Trim(), 20);

            foreach (var listing in found) card.Preview.Add(listing);

            card.Status = found.Count == 0
                ? "Geen resultaten. Pas de selectors aan."
                : $"{found.Count} resultaten gevonden.";
        }
        catch (Exception ex)
        {
            Log.Write($"testen van {card.Def.Name} mislukt - {ex.Message}");
            card.Status = "Fout: deze site " + FriendlyError.Describe(ex);
        }
    }

    /// <summary>
    /// Opent de site zichtbaar in de browser van de app, zodat je je kan aanmelden. De
    /// aanmelding blijft daarna bewaard in het browserprofiel van Zentrix.
    /// </summary>
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { IsEditable: true } card) return;

        if (string.IsNullOrWhiteSpace(card.Def.BaseUrl))
        {
            card.Status = "Vul eerst de basis-URL in.";
            return;
        }

        card.Status = "De browser gaat open. Meld je aan en sluit daarna het venster.";

        try
        {
            await using var browser = new BrowserFetcher();
            await browser.OpenForLoginAsync(card.Def.BaseUrl);

            // Of je echt aangemeld was, kan de app niet zien: enkel dat het venster dicht is.
            card.Status = "Browser gesloten. Als je aangemeld was, onthoudt Zentrix dat.";
        }
        catch (Exception ex)
        {
            card.Status = "Kon de browser niet openen: " + ex.Message;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { IsEditable: true } card) return;

        if (string.IsNullOrWhiteSpace(card.Def.Name))
        {
            card.Status = "Geef de site eerst een naam.";
            return;
        }

        _store.Save(card.Def);
        card.Status = "Opgeslagen.";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { IsEditable: true } card) return;

        var dialog = new SaveFileDialog
        {
            Title = "Site exporteren",
            Filter = "Sitebestand (*.json)|*.json",
            FileName = (string.IsNullOrWhiteSpace(card.Def.Id) ? "site" : card.Def.Id) + ".json"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _store.Export(card.Def, dialog.FileName);
            card.Status = "Geëxporteerd.";
        }
        catch (Exception ex)
        {
            card.Status = "Kon niet exporteren: " + ex.Message;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { IsEditable: true } card) return;

        var confirm = MessageBox.Show(
            this, $"'{card.Def.Name}' verwijderen?", "Bevestigen",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _store.Remove(card.Def.Name);
        _cards.Remove(card);
        StatusText.Text = $"'{card.Def.Name}' verwijderd.";
    }

    // ---------- algemene knoppen ----------

    private void AddSiteButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddSiteWindow { Owner = this };

        if (window.ShowDialog() == true && window.Result is not null)
        {
            _store.Add(window.Result);
            BuildCards();
            SelectCardByName(window.Result.Name);
            StatusText.Text = $"Site '{window.Result.Name}' toegevoegd.";
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sitebestand importeren",
            Filter = "Sitebestand (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true) return;

        var imported = _store.Import(dialog.FileName);
        if (imported is null)
        {
            StatusText.Text = "Dit bestand is geen geldige sitebeschrijving.";
            return;
        }

        BuildCards();
        SelectCardByName(imported.Name);
        StatusText.Text = $"Site '{imported.Name}' geïmporteerd.";
    }

    /// <summary>Opent de tab van een site op naam, bv. na toevoegen of importeren.</summary>
    private void SelectCardByName(string name)
    {
        var card = _cards.FirstOrDefault(c =>
            c.Def.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (card is not null) SiteTabs.SelectedItem = card;
    }

    /// <summary>
    /// Opent het logboek in de standaard teksteditor. Daar staat per zoekopdracht
    /// wat er gebeurde en hoe lang het duurde — de snelste weg naar de oorzaak
    /// wanneer een site traag is of niets teruggeeft.
    /// </summary>
    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(Log.FilePath))
            {
                StatusText.Text = "Nog geen logboek: doe eerst een zoekopdracht.";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = Log.FilePath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon het logboek niet openen: " + ex.Message;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SiteStore.Location);
            Process.Start(new ProcessStartInfo
            {
                FileName = SiteStore.Location,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon de map niet openen: " + ex.Message;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// Wat één kaart in het instellingen-scherm nodig heeft: de sitebeschrijving,
/// een testvoorbeeld en een statusregel. Voor ingebouwde bronnen is er geen
/// bewerkbare beschrijving, alleen uitleg.
/// </summary>
public class SiteCard : ObservableObject
{
    /// <summary>De onderliggende beschrijving (bij ingebouwde bronnen enkel de naam).</summary>
    public SiteDefinition Def { get; }

    /// <summary>Ingebouwde bron: code, niet bewerkbaar of verwijderbaar.</summary>
    public bool IsBuiltin { get; }

    /// <summary>Het tegenovergestelde: een generieke, bewerkbare site.</summary>
    public bool IsEditable => !IsBuiltin;

    /// <summary>
    /// Gebruikt deze site de generieke motor? Enkel dan hebben de selector-velden
    /// zin; een site met de linkmotor leest de pagina op een andere manier uit.
    /// </summary>
    public bool UsesSelectors => IsEditable && Def.Engine == SiteEngine.Generic;

    /// <summary>Site met een eigen motor: toon uitleg in plaats van de selectors.</summary>
    public bool UsesOwnEngine => IsEditable && Def.Engine != SiteEngine.Generic;

    /// <summary>
    /// Heeft aanmelden zin? Enkel bij sites die de browser van de app gebruiken: die
    /// heeft een eigen profiel waarin de aanmelding bewaard wordt. Een brugsite loopt
    /// via je eigen Chrome, en daar ben je al aangemeld of niet.
    /// </summary>
    public bool CanLogin => IsEditable && !Def.UseBridge &&
                            (Def.NeedsBrowser || Def.Engine == SiteEngine.LinkText);

    /// <summary>Uitlegtekst voor een ingebouwde bron.</summary>
    public string Info { get; } = "";

    public ObservableCollection<Listing> Preview { get; } = new();

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string KindLabel => Def.Kind == SiteKind.Json ? "JSON" : "HTML";

    public SiteCard(SiteDefinition def)
    {
        Def = def;
        IsBuiltin = false;
    }

    private SiteCard(string name, string info)
    {
        Def = new SiteDefinition { Name = name };
        IsBuiltin = true;
        Info = info;
    }

    public static SiteCard Builtin(string name, string info) => new(name, info);

    /// <summary>
    /// De zoekfilters als bewerkbare tekst: één regel per filter, "naam = stukje".
    /// Wijzigingen worden meteen teruggeschreven naar <see cref="SiteDefinition.Filters"/>.
    /// </summary>
    public string FiltersText
    {
        get => string.Join(Environment.NewLine, (Def.Filters ?? new()).Select(kv => $"{kv.Key} = {kv.Value}"));
        set
        {
            var parsed = new Dictionary<string, string>();

            foreach (var line in value.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                var eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                var key = trimmed[..eq].Trim();
                var val = trimmed[(eq + 1)..].Trim();
                if (key.Length > 0) parsed[key] = val;
            }

            Def.Filters = parsed;
        }
    }

    /// <summary>De veilinghuizen die overgeslagen worden, als bewerkbare tekst met komma's ertussen.</summary>
    public string AuctionSellersText
    {
        get => string.Join(", ", Def.AuctionSellers ?? new());
        set => Def.AuctionSellers = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

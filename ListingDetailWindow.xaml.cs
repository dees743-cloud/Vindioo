using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix;

/// <summary>Eén foto in de rij miniaturen, en of ze op dit moment groot staat.</summary>
public class FotoView : ObservableObject
{
    public string Url { get; init; } = "";

    private bool _isActief;
    public bool IsActief
    {
        get => _isActief;
        set => SetProperty(ref _isActief, value);
    }
}

/// <summary>
/// Alles van één zoekertje bij elkaar, geopend met een dubbelklik. Tot september 2026 opende
/// een dubbelklik meteen de webpagina; dat is nu een knop in dit venster. De reden: om te zien
/// of een zoekertje iets voor je is, wil je vooral de foto's, wie het verkoopt en hoelang het
/// er al staat - en daarvoor een browser openen, de cookiemelding wegklikken en de pagina laten
/// laden is een omweg van tien seconden per zoekertje.
///
/// De opbouw volgt hoe de eigenaar het vroeg: de miniaturen boven elkaar op een rij, één grote
/// foto eronder, en de muis over een miniatuur wisselt die grote foto. Klikken hoeft niet - er
/// valt niets te kiezen dat blijft staan, en zo blader je met één beweging door alle foto's.
///
/// **Wat er meteen staat, en wat wordt opgehaald.** De titel, de prijs, de plaats en de site
/// komen uit het zoekertje zelf: die zijn er al. De andere foto's, de verkoper en "online
/// sinds" staan op de pagina van het zoekertje, en die wordt opgehaald zodra het venster
/// opengaat (<see cref="DetailFetcher.DetailsAsync"/>, één verzoek voor alle drie). Zolang dat
/// loopt staat de foto die we al hadden er groot, zodat het venster nooit leeg is.
///
/// Wat een site geeft, zegt haar sitebestand. Heeft ze geen van de drie velden ingevuld, dan
/// blijft het bij die ene foto en zegt het venster dat ook - beter dan een leeg vak waarvan
/// niemand weet of het aan het laden is.
/// </summary>
public partial class ListingDetailWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Listing _listing;
    private readonly IReadOnlyList<SiteDefinition> _sites;
    private readonly ObservableCollection<FotoView> _fotos = new();

    private CancellationTokenSource? _cts;

    public ListingDetailWindow(Listing listing, IReadOnlyList<SiteDefinition> sites)
    {
        InitializeComponent();

        _listing = listing;
        _sites = sites;

        Thumbs.ItemsSource = _fotos;

        ToonWatWeHebben();

        Loaded += async (_, _) => await HaalPaginaAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    /// <summary>
    /// Wat het zoekertje zelf al weet. Dat staat er meteen, zodat het venster niet leeg is
    /// terwijl de pagina opgehaald wordt.
    /// </summary>
    private void ToonWatWeHebben()
    {
        Title = _listing.Title;
        TitleText.Text = _listing.Title;

        // Dezelfde opmaak als op de kaart in de lijst: dat is één plaats die beslist hoe een
        // prijs eruitziet. Deed dit venster het zelf, dan stond hetzelfde zoekertje hier op
        // "€ 40" en in de lijst op "€ 39,95".
        var prijs = _listing.Price is { } p && p > 0
            ? (string)new Converters.PriceTextConverter()
                .Convert(p, typeof(string), null!, CultureInfo.CurrentCulture)
            : _listing.PriceLabel.Length > 0 ? _listing.PriceLabel : "geen prijs";

        var plaats = _listing.PlaceLine;
        SubText.Text = plaats.Length > 0 ? $"{prijs} · {plaats}" : prijs;

        SellerText.Text = _listing.Seller.Length > 0 ? _listing.Seller : "-";
        SourceText.Text = _listing.Source;
        UrlText.Text = _listing.Url.Length > 0 ? _listing.Url : "-";

        // De zoekpagina van sommige sites geeft al een datum; die is beter dan niets terwijl
        // de pagina nog opgehaald wordt.
        PostedText.Text = _listing.Date is { } datum ? datum.ToString("d MMMM yyyy") : "-";

        OpenButton.IsEnabled = MainWindow.AlsWebadres(_listing.Url) is not null;

        if (!string.IsNullOrWhiteSpace(_listing.LargeImage))
        {
            _fotos.Add(new FotoView { Url = _listing.LargeImage });
            ZetGroot(_fotos[0]);
        }
    }

    /// <summary>
    /// De pagina van het zoekertje: de andere foto's, de verkoper en sinds wanneer het online
    /// staat. Eén verzoek voor alle drie; zie <see cref="DetailFetcher.DetailsAsync"/>.
    /// </summary>
    private async Task HaalPaginaAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        StatusText.Text = "De pagina van dit zoekertje ophalen...";

        try
        {
            var klok = System.Diagnostics.Stopwatch.StartNew();
            var details = await DetailFetcher.DetailsAsync(_listing, _sites, cts.Token);
            klok.Stop();

            if (cts.IsCancellationRequested) return;

            // De foto die we al hadden staat vooraan en is er al; de rest komt erachter.
            foreach (var url in details.Fotos)
                if (!_fotos.Any(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase)))
                    _fotos.Add(new FotoView { Url = url });

            if (_fotos.Count > 0 && !_fotos.Any(f => f.IsActief)) ZetGroot(_fotos[0]);

            if (details.Verkoper.Length > 0) SellerText.Text = details.Verkoper;
            if (details.Sinds.Length > 0) PostedText.Text = details.Sinds;

            PhotoStatus.Text = _fotos.Count switch
            {
                0 => "Dit zoekertje heeft geen foto.",
                1 => "Eén foto; deze site geeft er niet meer, of het sitebestand zegt nog niet waar ze staan.",
                var n => $"{n} foto's. Ga met de muis over een miniatuur om ze groot te zien."
            };

            StatusText.Text = details.Fout ?? $"Opgehaald in {klok.ElapsedMilliseconds} ms.";
        }
        catch (OperationCanceledException)
        {
            // Het venster ging dicht.
        }
        catch (Exception ex)
        {
            StatusText.Text = FriendlyError.Describe(ex);
            Log.Write($"detailvenster: '{_listing.Title}' - {ex.Message}");
        }
    }

    /// <summary>
    /// De muis over een miniatuur zet die foto groot. Geen klik: zo blader je met één beweging
    /// door alle foto's, en er valt hier niets te kiezen dat daarna blijft staan.
    /// </summary>
    private void Miniatuur_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FotoView foto) ZetGroot(foto);
    }

    /// <summary>
    /// Deze foto groot tonen. Een lege of kapotte link mag het venster niet laten vallen: de
    /// link komt uit een sitebestand, en dat is bedoeld om te delen.
    /// </summary>
    private void ZetGroot(FotoView foto)
    {
        foreach (var f in _fotos) f.IsActief = ReferenceEquals(f, foto);

        try
        {
            BigPhoto.Source = string.IsNullOrWhiteSpace(foto.Url)
                ? null
                : new BitmapImage(new Uri(foto.Url));
        }
        catch (Exception ex)
        {
            BigPhoto.Source = null;
            Log.Write($"detailvenster: foto niet te tonen - {ex.Message}");
        }
    }

    /// <summary>Het zoekertje alsnog op de site openen - wat een dubbelklik vroeger meteen deed.</summary>
    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var adres = MainWindow.AlsWebadres(_listing.Url);

        if (adres is null)
        {
            StatusText.Text = "Deze link is geen webadres en wordt daarom niet geopend.";
            Log.Write($"zoekertje niet geopend, geen webadres: {_listing.Url}");
            return;
        }

        try
        {
            // UseShellExecute laat Windows zelf de standaardbrowser kiezen.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = adres.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon de link niet openen: " + ex.Message;
        }
    }

    /// <summary>Doorsturen naar de AI-controle, met alle foto's die we hier al kennen.</summary>
    private void AiButton_Click(object sender, RoutedEventArgs e) =>
        new PhotoInsightWindow(_listing, _sites, alleFotos: true) { Owner = this }.Show();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

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
/// foto eronder, en een klik op een miniatuur wisselt die grote foto. Eerst wisselde hij al bij
/// het zweven met de muis; dat ging te vaak per ongeluk, want je muis passeert die rij ook op
/// weg naar iets anders.
///
/// **De grote foto krijgt de vrije ruimte** en groeit dus mee wanneer je het venster groter
/// maakt. Een klik erop legt hem schermvullend over het venster (Esc of nog een klik sluit dat
/// weer), want anders is "groot" nog altijd de helft van je scherm.
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

    /// <summary>Welke foto er schermvullend staat, als plaats in <see cref="_fotos"/>.</summary>
    private int _zoomPlaats;

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

        VolgVensterhoogte();
        SizeChanged += (_, _) => VolgVensterhoogte();
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
        // Is er een prijs, dan altijd via de converter - ook een nul. De kaart doet dat ook, en
        // anders staat hetzelfde zoekertje hier op "0" en in de lijst op "€ 0". De eigen tekst
        // van de site is enkel de terugval wanneer er helemaal geen prijs is.
        var prijs = _listing.Price is { } p
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

        // Idem voor de beschrijving: de zoek-API van 2dehands kapt af op 200 tekens, maar die
        // 200 zijn er meteen. De volledige komt van de pagina.
        ZetBeschrijving(_listing.Description);

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

        // Een site die een aangemelde browser vraagt (Facebook) kost seconden in plaats van
        // tienden: Chrome moet mogelijk eerst starten en de pagina moet echt gebouwd worden.
        // Dat hoort er te staan vóór het wachten, niet erna.
        var viaBrowser = _sites.FirstOrDefault(s =>
            string.Equals(s.Name, _listing.Source, StringComparison.OrdinalIgnoreCase))
            is { NeedsBrowser: true, UseBridge: false };

        StatusText.Text = viaBrowser
            ? "De pagina van dit zoekertje ophalen in de aangemelde browser; dat duurt een paar seconden..."
            : "De pagina van dit zoekertje ophalen...";

        // Zichtbaar maken dat er nog foto's onderweg zijn. Zonder dit zag je één miniatuur en
        // één grote foto, en niets dat zei dat er nog iets kwam - bij Facebook vier seconden
        // lang, en dan verscheen er ineens een rij bij.
        LoadingTile.Visibility = Visibility.Visible;
        PhotoStatus.Text = "De andere foto's van deze advertentie ophalen...";

        try
        {
            var klok = System.Diagnostics.Stopwatch.StartNew();
            var details = await DetailFetcher.DetailsAsync(_listing, _sites, cts.Token);
            klok.Stop();

            if (cts.IsCancellationRequested) return;

            ZetFotos(details.PaginaFotos);

            if (details.Verkoper.Length > 0) SellerText.Text = details.Verkoper;
            if (details.Sinds.Length > 0) PostedText.Text = details.Sinds;

            // Enkel als de pagina meer geeft dan wat we al hadden: de zoekpagina kapt af, maar
            // een site die niets extra's geeft mag de tekst niet wissen.
            if (details.Beschrijving.Length > DescriptionText.Text.Length)
                ZetBeschrijving(details.Beschrijving);

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
        finally
        {
            // Ook wanneer het misging: een wieltje dat blijft draaien belooft iets dat niet
            // meer komt, en "aan het ophalen" blijven zeggen is dan even onwaar.
            if (!cts.IsCancellationRequested)
            {
                LoadingTile.Visibility = Visibility.Collapsed;

                PhotoStatus.Text = _fotos.Count switch
                {
                    0 => "Dit zoekertje heeft geen foto.",
                    1 => "Eén foto; deze site geeft er niet meer, of het sitebestand zegt nog " +
                         "niet waar ze staan.",
                    var n => $"{n} foto's. Klik op een miniatuur om ze groot te zien, en op de " +
                             "grote foto om ze schermvullend te bekijken."
                };
            }
        }
    }

    /// <summary>
    /// De foto's die de pagina gaf in de plaats van wat er stond.
    ///
    /// **De miniatuur van de zoekpagina verdwijnt zodra de pagina foto's geeft**, en dat is met
    /// opzet: het is dezelfde foto, alleen kleiner. Bij Facebook stond ze zo twee keer in de rij
    /// - eerst als 260x260 en meteen erna als 960x720 - en de grote foto eronder toonde dan de
    /// slechtste van de twee. Geeft de pagina niets, dan blijft ze natuurlijk staan: dan is ze
    /// het enige wat we hebben.
    ///
    /// Bij de meeste sites valt er niets te verwijderen: daar is het adres van de zoekpagina
    /// letterlijk hetzelfde als op de pagina, en stond ze er dus al maar één keer in.
    /// </summary>
    private void ZetFotos(IReadOnlyList<string> vanDePagina)
    {
        if (vanDePagina.Count == 0) return;

        var stondGroot = _fotos.FirstOrDefault(f => f.IsActief)?.Url ?? "";

        _fotos.Clear();
        foreach (var url in vanDePagina)
            if (!_fotos.Any(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase)))
                _fotos.Add(new FotoView { Url = url });

        if (_fotos.Count == 0) return;

        // Stond er een foto groot die er nog is, dan blijft die staan; anders de eerste. Zo
        // springt het beeld niet weg onder iemand die net op een miniatuur geklikt had.
        var houden = _fotos.FirstOrDefault(f => string.Equals(f.Url, stondGroot, StringComparison.OrdinalIgnoreCase));
        ZetGroot(houden ?? _fotos[0]);

        // De rij begint vooraan: er stond er net één, en nu staan er vijftien.
        ThumbScroll.ScrollToHorizontalOffset(0);
    }

    /// <summary>
    /// De beschrijving, of niets. Een leeg blok met een kopje erboven zegt niets; dan blijft
    /// het hele stuk weg.
    /// </summary>
    private void ZetBeschrijving(string tekst)
    {
        var schoon = (tekst ?? "").Trim();

        DescriptionText.Text = schoon;

        var zichtbaar = schoon.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DescriptionText.Visibility = zichtbaar;
        DescriptionHeader.Visibility = zichtbaar;
    }

    /// <summary>
    /// Hoe hoog het onderste blok hoogstens mag worden: iets minder dan de helft van het venster.
    /// De rij eronder staat op Auto, dus een korte beschrijving krijgt geen half leeg vak; een
    /// lange loopt tot deze grens en schuift daarbinnen. Zonder die grens duwt een advertentie
    /// met algemene voorwaarden de foto het venster uit.
    ///
    /// De grens staat op het blok en niet op de rij, en dat is precies het punt: een rij op Auto
    /// meet haar kind met oneindige hoogte. De ScrollViewer besluit dan dat er niets te schuiven
    /// valt, en daarna knipt de rij de tekst af - zichtbaar afgeknipt, zonder schuifbalk.
    /// </summary>
    private void VolgVensterhoogte()
    {
        InfoBlock.MaxHeight = Math.Max(200, ActualHeight * 0.45);
    }

    /// <summary>
    /// Het muiswiel schuift de rij miniaturen opzij. Die rij schuift horizontaal, en een wiel
    /// doet daar uit zichzelf niets: het verzoek gaat omhoog naar iets dat verticaal schuift,
    /// en dat is er hier niet. Bij vijftien foto's blijft er dan enkel de schuifbalk over.
    /// </summary>
    private void ThumbScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ThumbScroll.ScrollableWidth <= 0) return;

        ThumbScroll.ScrollToHorizontalOffset(ThumbScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>Een klik op een miniatuur zet die foto groot.</summary>
    private void Miniatuur_Klik(object sender, MouseButtonEventArgs e)
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

    /// <summary>
    /// De foto schermvullend over het venster, en nog eens klikken sluit hem. Zo groot als het
    /// venster is, dus maximaliseren geeft hem op volle grootte. Erbij staat hoeveel
    /// beeldpunten de foto werkelijk heeft: dan weet je meteen of de site meer te bieden had.
    /// </summary>
    private void BigPhoto_Klik(object sender, MouseButtonEventArgs e)
    {
        if (BigPhoto.Source is null) return;

        var actief = _fotos.FirstOrDefault(f => f.IsActief);
        ToonZoom(actief is null ? 0 : _fotos.IndexOf(actief));

        ZoomLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// De foto op deze plaats schermvullend tonen, met de pijlen en de teller erbij.
    ///
    /// Zet ook de grote foto eronder en het vinkje op de miniatuur mee: sluit je de vergroting,
    /// dan sta je op de foto die je als laatste bekeek en niet terug op die van daarvoor.
    /// </summary>
    private void ToonZoom(int plaats)
    {
        if (_fotos.Count == 0) return;

        _zoomPlaats = Math.Clamp(plaats, 0, _fotos.Count - 1);

        ZetGroot(_fotos[_zoomPlaats]);
        ZoomPhoto.Source = BigPhoto.Source;

        ZoomTeller.Text = _fotos.Count > 1 ? $"{_zoomPlaats + 1} van {_fotos.Count}" : "";

        // Hidden en niet Collapsed: zo blijft de andere pijl op zijn plaats staan.
        ZoomVorige.Visibility = _zoomPlaats > 0 ? Visibility.Visible : Visibility.Hidden;
        ZoomVolgende.Visibility = _zoomPlaats < _fotos.Count - 1 ? Visibility.Visible : Visibility.Hidden;

        ZetZoomTekst(BigPhoto.Source as BitmapImage);
    }

    /// <summary>
    /// Hoeveel beeldpunten de foto werkelijk heeft: dan weet je meteen of de site meer te bieden
    /// had.
    ///
    /// Bij het bladeren is die foto nog niet binnen - een <see cref="BitmapImage"/> van een
    /// webadres haalt zichzelf op de achtergrond op, en dan staat het formaat nog op nul. Daarom
    /// wordt de tekst ook nog eens gezet zodra ze er is; zonder dat stond er bij elke volgende
    /// foto enkel "Klik of Esc om te sluiten".
    /// </summary>
    private void ZetZoomTekst(BitmapImage? beeld)
    {
        Zet();

        if (beeld is { IsDownloading: true })
            beeld.DownloadCompleted += (_, _) => Zet();

        void Zet() =>
            ZoomHint.Text = beeld is { PixelWidth: > 0 }
                ? $"{beeld.PixelWidth} × {beeld.PixelHeight} beeldpunten — klik of Esc om te sluiten"
                : "Klik of Esc om te sluiten";
    }

    private void ZoomVorige_Click(object sender, RoutedEventArgs e) => ToonZoom(_zoomPlaats - 1);

    private void ZoomVolgende_Click(object sender, RoutedEventArgs e) => ToonZoom(_zoomPlaats + 1);

    private void Zoom_Klik(object sender, MouseButtonEventArgs e) => SluitZoom();

    private void SluitZoom()
    {
        ZoomLayer.Visibility = Visibility.Collapsed;
        ZoomPhoto.Source = null;
    }

    /// <summary>
    /// Esc sluit eerst de schermvullende foto en pas daarna het venster. Anders sluit één druk
    /// allebei, en dan lijkt het alsof het venster zomaar wegvalt.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (ZoomLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                SluitZoom();
                e.Handled = true;
                return;
            }

            // De pijltjestoetsen doen hetzelfde als de knoppen. Wie met de muis bladert, houdt
            // zijn hand daar; wie net Esc leerde, bladert liever met het toetsenbord.
            if (e.Key is Key.Left or Key.Right)
            {
                ToonZoom(_zoomPlaats + (e.Key == Key.Left ? -1 : 1));
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
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

    /// <summary>
    /// Wat is dit ongeveer waard? Hetzelfde venster als de rechtsklik op een foto in de
    /// resultatenlijst; het zoekt zijn eigen vergelijkingen op, dus het krijgt enkel het
    /// zoekertje en de sites mee.
    /// </summary>
    private void PriceButton_Click(object sender, RoutedEventArgs e) =>
        new PriceIndicationWindow(_listing, _sites).Boven(this).Show();

    /// <summary>Doorsturen naar de AI-controle, met alle foto's die we hier al kennen.</summary>
    private void AiButton_Click(object sender, RoutedEventArgs e) =>
        new PhotoInsightWindow(_listing, _sites, alleFotos: true).Boven(this).Show();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

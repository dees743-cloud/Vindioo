using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo;

/// <summary>Eén groep vergelijkingen in het venster, bv. "Lopende veilingen (3)".</summary>
public class PriceGroupView
{
    public string Header { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<PriceComparable> Items { get; init; } = new();
}

/// <summary>
/// Wat een zoekertje ongeveer waard is, geopend met een rechtsklik op zijn foto. Stelt een
/// zoekterm voor (merk en model uit de titel), zoekt meteen, en toont de marktwaarde met alle
/// vergelijkingen eronder, zodat je zelf kan nagaan waar het getal vandaan komt. Het rekenwerk
/// zit in <see cref="PriceIndicator"/>; dit venster toont enkel.
/// </summary>
public partial class PriceIndicationWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Listing _listing;
    private readonly IReadOnlyList<SiteDefinition> _sites;
    private CancellationTokenSource? _cts;

    /// <param name="vanDeFotos">
    /// Wat de AI-controle van de foto's las. Die namen komen er als chipjes bij te staan om aan
    /// te klikken; de zoekterm blijft die uit de titel. Waarom de app er zelf geen kiest, staat
    /// bij <see cref="PriceIndicator.AlsZoektermen"/> - kort: op een foto staat meer dan het
    /// voorwerp, en de enige marktwaarde die er bij het meten uitkwam ging over een Xbox op de
    /// achtergrond van een stereoset.
    /// </param>
    /// <param name="term">
    /// De zoekterm, wanneer die niet uit de titel van het zoekertje hoort te komen. Zo opent
    /// <see cref="LotPriceWindow"/> de vergelijkingen van één gelezen titel uit een partij.
    /// </param>
    public PriceIndicationWindow(Listing listing, IReadOnlyList<SiteDefinition> sites,
                                 IReadOnlyList<string>? vanDeFotos = null, string? term = null)
    {
        InitializeComponent();

        _listing = listing;
        _sites = sites;

        ToonZoekertje();

        var gekozen = (term ?? "").Trim();
        TermBox.Text = gekozen.Length > 0 ? gekozen : PriceIndicator.SuggestTerm(listing.Title);

        // Waarmee het venster begon. Zolang die er nog staat, mag het opzoeken bij Tweakers
        // de volledige titel gebruiken; heeft de gebruiker zelf iets getypt, dan wint dat.
        _termBijOpenen = TermBox.Text;

        if (gekozen.Length > 0)
            TermUitleg.Text =
                "Deze titel is van de foto's gelezen, niet uit de titel van het zoekertje. " +
                "Klopt het niet, pas het dan aan: elk woord moet in de titel van een " +
                "vergelijking staan.";

        var voorstellen = PriceIndicator.AlsZoektermen(vanDeFotos ?? Array.Empty<string>());

        if (voorstellen.Count > 0)
        {
            GelezenNamen.ItemsSource = voorstellen;
            GelezenBlok.Visibility = Visibility.Visible;

            GelezenKop.Text = $"Van de foto's gelezen ({voorstellen.Count}) - klik erop om het bij " +
                              "de zoekterm te zetten. Wat op een typenummer lijkt, staat vooraan.";
        }

        Loaded += async (_, _) => await ZoekAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    /// <summary>
    /// Een gelezen naam bij de zoekterm zetten. Erbij en niet in de plaats van: meestal is het
    /// merk uit de titel juist en ontbreekt enkel het typenummer, en dan wil je "Sony" plus
    /// "C77ES". Wie het anders wil, tikt het vak gewoon leeg - het is een tekstvak.
    /// </summary>
    private void GelezenNaam_Klik(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not string naam) return;

        var nu = TermBox.Text.Trim();

        if (nu.Contains(naam, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = $"'{naam}' staat al in de zoekterm.";
            return;
        }

        TermBox.Text = nu.Length > 0 ? nu + " " + naam : naam;
        TermBox.CaretIndex = TermBox.Text.Length;

        StatusText.Text = $"'{naam}' erbij gezet. Klik op Zoeken om het te proberen.";
    }

    private void ToonZoekertje()
    {
        OriginTitle.Text = _listing.Title;

        var def = _sites.FirstOrDefault(s => s.Name == _listing.Source);
        var prijs = _listing.Price is { } p && p > 0 ? PriceRange.Euro(p) : "geen prijs";

        OriginPrice.Text = PriceIndicator.IsVeiling(_listing, def)
            ? $"Huidig bod {prijs} · {_listing.Source}" +
              (string.IsNullOrWhiteSpace(_listing.Seller) ? "" : $" · veiling van {_listing.Seller}")
            : $"{prijs} · {_listing.Source}";

        // De foto in code: een lege of kapotte link mag het venster niet laten vallen.
        if (string.IsNullOrWhiteSpace(_listing.Thumbnail)) return;
        try
        {
            OriginPhoto.Background = new ImageBrush(new BitmapImage(new Uri(_listing.Thumbnail)))
            {
                Stretch = Stretch.UniformToFill
            };
        }
        catch (Exception ex)
        {
            Log.Write($"prijsindicatie: foto niet geladen - {ex.Message}");
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await ZoekAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ZoekAsync()
    {
        var term = TermBox.Text.Trim();
        if (term.Length == 0)
        {
            StatusText.Text = "Typ eerst waarop gezocht moet worden, bv. het merk en het model.";
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        SearchButton.IsEnabled = false;
        MarketPanel.Visibility = Visibility.Collapsed;
        Groups.ItemsSource = null;

        // Een melding van de zoektocht kan nog binnenkomen nadat de uitkomst er staat
        // (Progress stuurt ze door via de schermdraad). Die mag de uitkomst niet overschrijven.
        var bezig = true;
        var status = new Progress<string>(tekst => { if (bezig) StatusText.Text = tekst; });
        StatusText.Text = "Zoeken...";

        try
        {
            var uitkomst = await PriceIndicator.DetermineAsync(term, _listing, _sites, status, cts.Token);
            bezig = false;
            if (!cts.IsCancellationRequested) Toon(uitkomst);
        }
        catch (OperationCanceledException)
        {
            // Venster gesloten of opnieuw gezocht: niets meer te tonen.
        }
        catch (Exception ex)
        {
            bezig = false;
            StatusText.Text = "De prijsindicatie mislukte: " + FriendlyError.Describe(ex);
            Log.Write("prijsindicatie mislukte: " + ex);
        }
        finally
        {
            if (_cts == cts) SearchButton.IsEnabled = true;
        }
    }

    private void Toon(PriceIndication ind)
    {
        if (ind.NoSites)
        {
            StatusText.Text = "Geen enkele site telt mee voor de prijsindicatie. Zet in Sites beheren het vinkje " +
                              "'Telt mee voor prijsindicatie' aan bij een site met vraagprijzen, zoals een " +
                              "zoekertjessite, of importeer de sites opnieuw.";
            return;
        }

        StatusText.Text = StatusRegel(ind);

        ToonMarktwaarde(ind);
        Groups.ItemsSource = Groepen(ind);
    }

    private static string StatusRegel(PriceIndication ind)
    {
        var delen = new List<string>
        {
            $"Gezocht op {string.Join(", ", ind.SitesSearched)} om {ind.CheckedAt:HH:mm}."
        };

        if (ind.OtherCount > 0)
            delen.Add($"{ind.OtherCount} treffer(s) gingen over iets anders en staan er niet bij.");

        delen.AddRange(ind.SiteErrors.Select(f => $"{f.Key}: {f.Value}"));
        return string.Join(" ", delen);
    }

    private void ToonMarktwaarde(PriceIndication ind)
    {
        MarketPanel.Visibility = Visibility.Visible;
        Comparison.Text = "";

        var naam = ind.Model.Length > 0 ? ind.Model : $"'{ind.Term}'";
        var markt = ind.Market;

        if (markt is null)
        {
            MarketValue.Text = "Geen vraagprijzen";
            MarketDetail.Text = $"Voor {naam} vond geen enkele site een vraagprijs die meetelt.";
        }
        else if (markt.Count < PriceIndicator.MinimumVoorMarktwaarde)
        {
            var prijzen = ind.Items
                .Where(i => i.Kind == ComparableKind.Counted && i.Listing.Price is > 0)
                .Select(i => PriceRange.Euro(i.Listing.Price!.Value));

            MarketValue.Text = "Te weinig gegevens";
            MarketDetail.Text = $"Voor {naam} {(markt.Count == 1 ? "is er één vraagprijs" : $"zijn er {markt.Count} vraagprijzen")}: " +
                                $"{string.Join(" en ", prijzen)}. Vanaf {PriceIndicator.MinimumVoorMarktwaarde} geeft de app een marktwaarde.";
        }
        else
        {
            MarketValue.Text = "± " + PriceRange.Euro(markt.Median);
            MarketDetail.Text = $"{naam}: meestal {markt.Vork}, uit {markt.Count} vraagprijzen" +
                                (markt.Count < 8 ? ". Nog niet veel gegevens, dus met een korreltje zout." : ".");

            if (_listing.Price is { } prijs && prijs > 0 && markt.Median > 0)
            {
                var procent = (int)Math.Round((1 - prijs / markt.Median) * 100);
                var wie = ind.OriginIsAuction ? "Het huidige bod" : "De prijs van dit zoekertje";

                Comparison.Text = procent switch
                {
                    >= 5 => $"{wie} ({PriceRange.Euro(prijs)}) ligt {procent} % onder de marktwaarde.",
                    <= -5 => $"{wie} ({PriceRange.Euro(prijs)}) ligt {-procent} % boven de marktwaarde.",
                    _ => $"{wie} ({PriceRange.Euro(prijs)}) ligt ongeveer op de marktwaarde."
                } + (ind.OriginIsAuction ? " Een bod kan nog stijgen." : "");
            }
        }

        BroadText.Text = ind.Broad is { } breed
            ? $"Andere toestellen uit de reeks {ind.BroadTerm} (geen exact model): meestal {breed.Vork}, " +
              $"mediaan {PriceRange.Euro(breed.Median)}, uit {breed.Count} vraagprijzen. Enkel als richting."
            : ind.BroadTerm.Length > 0
                ? $"Ook bij {ind.BroadTerm} geen bruikbare vraagprijzen."
                : ind.BroadNote;
    }

    /// <summary>De vergelijkingen in groepen, telkens van goedkoop naar duur.</summary>
    private static List<PriceGroupView> Groepen(PriceIndication ind)
    {
        static List<PriceComparable> Sorteer(IEnumerable<PriceComparable> items) =>
            items.OrderBy(i => i.Listing.Price is > 0 ? 0 : 1).ThenBy(i => i.Listing.Price).ToList();

        var groepen = new List<PriceGroupView>();

        void Voeg(string kop, string samenvatting, IEnumerable<PriceComparable> items)
        {
            var lijst = Sorteer(items);
            if (lijst.Count > 0)
                groepen.Add(new PriceGroupView { Header = $"{kop} ({lijst.Count})", Summary = samenvatting, Items = lijst });
        }

        var uitschieters = ind.Items.Any(i => i.Kind == ComparableKind.Outlier && i.Model == ind.Model);
        Voeg("Meegeteld voor de marktwaarde", uitschieters ? "Een uitschieter staat erbij, maar telt niet mee." : "",
             ind.Items.Where(i => i.Kind == ComparableKind.Counted ||
                                  (i.Kind == ComparableKind.Outlier && i.Model == ind.Model)));

        foreach (var (variant, reeks) in ind.Variants.OrderBy(v => v.Key))
            Voeg($"Ander model: {variant}", $"Een ander toestel met hetzelfde nummer: meestal {reeks.Vork}, " +
                                           $"mediaan {PriceRange.Euro(reeks.Median)}. Telt niet mee.",
                 ind.Items.Where(i => i.Model == variant &&
                                      i.Kind is ComparableKind.Variant or ComparableKind.Outlier));

        Voeg("Lopende veilingen", "Een bod kan nog stijgen, dus het telt niet mee.",
             ind.Items.Where(i => i.Kind == ComparableKind.Auction));

        Voeg("Niet meegeteld", "Sets, toebehoren, defecte toestellen, zoekertjes zonder prijs en mensen die zelf zoeken.",
             ind.Items.Where(i => i.Kind is ComparableKind.Set or ComparableKind.Accessory or ComparableKind.Defect or
                                           ComparableKind.NoPrice or ComparableKind.Wanted));

        // Bij het verbreden komt er veel rommel mee (bij "Denon DCD" 49 van de 100: veilingen,
        // afstandsbedieningen, sets). Die staan er niet bij; enkel hoeveel het er waren.
        var weggelaten = ind.BroadItems.Count(i => i.Kind is not (ComparableKind.Related or ComparableKind.Outlier));
        Voeg($"Uit dezelfde reeks: {ind.BroadTerm}",
             "Geen exact model, enkel als richting." +
             (weggelaten > 0 ? $" {weggelaten} andere (veilingen, sets, toebehoren, defect of zonder prijs) staan er niet bij." : ""),
             ind.BroadItems.Where(i => i.Kind is ComparableKind.Related or ComparableKind.Outlier));

        return groepen;
    }

    // ==================== nieuw bij Tweakers ====================

    /// <summary>Het product waarvan de prijs nu getoond wordt, voor de link eronder.</summary>
    private Pricewatch.Voorstel? _tweakers;

    /// <summary>
    /// Zoekt de zoekterm op in de productlijst van Tweakers. Dat gebeurt hier op de pc, in een
    /// kopie van hun eigen sitemap: hun zoekpagina is in robots.txt verboden. De eerste keer
    /// wordt die lijst opgehaald (303 122 producten, zo'n twee seconden); daarna kost het een
    /// tiende seconde.
    /// </summary>
    private async void TweakersButton_Click(object sender, RoutedEventArgs e)
    {
        var term = TermBox.Text.Trim();
        if (term.Length == 0)
        {
            TweakersStatus.Text = "Typ eerst een zoekterm hierboven.";
            return;
        }

        TweakersButton.IsEnabled = false;
        TweakersVoorstellen.Visibility = Visibility.Collapsed;
        TweakersBeeld.Visibility = Visibility.Collapsed;
        TweakersStatus.Text = "Opzoeken in de productlijst van Tweakers...";

        var cts = _tweakersCts = new CancellationTokenSource();
        var bezig = true;
        var status = new Progress<string>(t => { if (bezig) TweakersStatus.Text = t; });

        try
        {
            // Staat de zoekterm nog zoals het venster hem voorstelde, dan zoeken we ook op de
            // volledige titel: die term is met opzet kort gehouden om vergelijkingen te vinden
            // ("Sony PlayStation Slim"), en dan komt de PS3 boven de PS5 te staan. Heeft de
            // gebruiker zelf iets getypt, dan telt enkel dat - wie "Commodore 64" invult, wil
            // geen PlayStations zien.
            var eigenTerm = !string.Equals(term, _termBijOpenen.Trim(), StringComparison.OrdinalIgnoreCase);

            var vragen = new List<string>();
            if (!eigenTerm && _listing.Title.Trim().Length > 0) vragen.Add(_listing.Title);
            if (!vragen.Any(v => string.Equals(v, term, StringComparison.OrdinalIgnoreCase))) vragen.Add(term);

            var samen = new List<Pricewatch.Voorstel>();

            foreach (var vraag in vragen)
            {
                foreach (var v in await Pricewatch.VoorstellenAsync(vraag, 6, status, cts.Token))
                    if (samen.All(x => x.Id != v.Id)) samen.Add(v);
            }

            var voorstellen = samen.Take(6).ToList();

            bezig = false;
            if (cts.IsCancellationRequested) return;

            TweakersNamen.ItemsSource = voorstellen;
            TweakersVoorstellen.Visibility = voorstellen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Eerlijk zwijgen is beter dan een verkeerd product: Tweakers heeft geen Commodore 64
            // en geen Marantz CD5003, en dan hoort daar niets te staan.
            TweakersStatus.Text = voorstellen.Count > 0
                ? ""
                : $"Tweakers heeft geen product dat op '{term}' lijkt. Dat gebeurt vaak bij ouder " +
                  "spul; hun lijst gaat over wat er nieuw te koop is of was.";
        }
        catch (OperationCanceledException)
        {
            // Venster gesloten of opnieuw gevraagd.
        }
        catch (Exception ex)
        {
            bezig = false;
            TweakersStatus.Text = "Opzoeken bij Tweakers mislukte: " + FriendlyError.Describe(ex);
            Log.Write("pricewatch: opzoeken mislukte: " + ex);
        }
        finally
        {
            if (_tweakersCts == cts) TweakersButton.IsEnabled = true;
        }
    }

    private CancellationTokenSource? _tweakersCts;

    /// <summary>De zoekterm zoals het venster hem voorstelde, om te zien of jij hem wijzigde.</summary>
    private readonly string _termBijOpenen;

    /// <summary>Eén product aangeklikt: zijn pagina ophalen en de prijs tonen.</summary>
    private async void TweakersProduct_Klik(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Pricewatch.Voorstel product) return;

        TweakersStatus.Text = "De prijs van dit product ophalen...";
        TweakersBeeld.Visibility = Visibility.Collapsed;

        var cts = _tweakersCts = new CancellationTokenSource();

        try
        {
            var beeld = await Pricewatch.HaalAsync(product, cts.Token);
            if (cts.IsCancellationRequested) return;

            _tweakers = product;

            TweakersNaam.Text = beeld.Naam;
            TweakersPrijs.Text = beeld.Regel;
            TweakersTweedehands.Text = beeld.TweedehandsRegel;
            TweakersBeeld.Visibility = Visibility.Visible;
            TweakersStatus.Text = "";
        }
        catch (OperationCanceledException)
        {
            // Venster gesloten of een ander product aangeklikt.
        }
        catch (Exception ex)
        {
            TweakersStatus.Text = "De prijs ophalen mislukte: " + FriendlyError.Describe(ex);
            Log.Write("pricewatch: prijs ophalen mislukte: " + ex);
        }
    }

    private void TweakersLink_Klik(object sender, MouseButtonEventArgs e)
    {
        if (_tweakers is null) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _tweakers.Url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TweakersStatus.Text = "De pagina kon niet geopend worden: " + ex.Message;
        }
    }

    private void Rij_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PriceComparable item) return;

        var adres = MainWindow.AlsWebadres(item.Listing.Url);
        if (adres is null)
        {
            StatusText.Text = "Deze link is geen webadres en wordt daarom niet geopend: " + item.Listing.Url;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = adres.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Het zoekertje kon niet geopend worden: " + ex.Message;
        }
    }
}

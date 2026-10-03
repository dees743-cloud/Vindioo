using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo;

/// <summary>Eén gelezen titel met wat ze ongeveer waard is, klaar om te tonen.</summary>
public class LotRowView
{
    public string Titel { get; init; } = "";
    public string Bedrag { get; init; } = "";
    public string Onder { get; init; } = "";

    /// <summary>De mediaan, enkel om op te sorteren; 0 wanneer er geen marktwaarde was.</summary>
    public decimal Waarde { get; init; }

    public bool Gevonden => Waarde > 0;
}

/// <summary>
/// Wat zit er in deze partij? Bij een doos spellen of een stapel platen is de vraag niet wat de
/// doos waard is, maar of er iets waardevols bij zit. De AI-controle leest de titels van de
/// foto's; dit venster zoekt ze <b>één voor één</b> op met dezelfde prijsindicatie als elders,
/// en zet ze op volgorde van duur naar goedkoop.
///
/// **Waarom dit werkt waar de automatische zoekterm faalde** (zie
/// <see cref="PriceIndicator.AlsZoektermen"/>): bij een partij ís elke gelezen naam een titel
/// die op zichzelf te koop staat, en hoeft er niets gekozen te worden. Gemeten op 26 september
/// 2026: een set van 160 PSP-spellen gaf 108 namen op drie foto's, en van de eerste vijftien
/// kregen er **tien** een marktwaarde (van € 3,99 voor FIFA 12 tot € 25 voor Yu-Gi-Oh! GX Tag
/// Force). Een verzameling pop-cd's: 109 namen, **negen van de twaalf**.
///
/// **Er staat met opzet geen totaal bij.** Bij die cd-verzameling gaf het gelezen getal "25000"
/// een marktwaarde van € 550 uit vijf dure treffers - meer dan alle echte titels samen. Eén
/// verkeerd gelezen naam maakt een totaal dus waardeloos, terwijl een lijst op volgorde precies
/// laat zien wat de vraag was: zit er iets waardevols bij. (Een naam zonder één letter erin komt
/// sindsdien niet meer in de lijst, maar dat vangt niet alles.)
///
/// **Per keer vijfentwintig.** Een prijsindicatie kost ongeveer een seconde en vier verzoeken
/// aan de sites; alle 98 bruikbare namen in één klik zou bijna vierhonderd verzoeken in twee
/// minuten zijn. Dus vijfentwintig, met een knop voor de volgende vijfentwintig - en de knop is
/// intussen een stopknop, zoals overal in de app.
/// </summary>
public partial class LotPriceWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>Hoeveel titels er per klik opgezocht worden; zie de klasse hierboven.</summary>
    private const int PerKeer = 25;

    private readonly Listing _listing;
    private readonly IReadOnlyList<SiteDefinition> _sites;
    private readonly List<string> _tezoeken;
    private readonly ObservableCollection<LotRowView> _rijen = new();

    private CancellationTokenSource? _cts;
    private bool _bezig;
    private bool _gestopt;

    /// <summary>Hoeveel titels er al opgezocht zijn; de volgende klik gaat verder waar dit staat.</summary>
    private int _gedaan;

    public LotPriceWindow(Listing listing, IReadOnlyList<SiteDefinition> sites,
                          IReadOnlyList<string> gelezen)
    {
        InitializeComponent();

        _listing = listing;
        _sites = sites;
        _tezoeken = PriceIndicator.AlsZoektermen(gelezen, 200);

        Titels.ItemsSource = _rijen;

        ToonZoekertje();

        UitlegText.Text =
            $"De AI las {gelezen.Count} namen op de foto's van dit zoekertje; " +
            $"{_tezoeken.Count} daarvan zijn bruikbaar als zoekterm. Elke titel wordt apart " +
            "opgezocht op de sites die meetellen voor de prijsindicatie, en komt hieronder te " +
            $"staan van duur naar goedkoop. Dat gaat per {PerKeer}: één titel kost ongeveer een " +
            "seconde en vier verzoeken aan die sites.";

        StatusText.Text = _tezoeken.Count == 0
            ? "Er is niets gelezen dat als zoekterm kan dienen."
            : $"Klaar om de eerste {Math.Min(PerKeer, _tezoeken.Count)} op te zoeken.";

        RunButton.IsEnabled = _tezoeken.Count > 0;

        Closed += (_, _) => _cts?.Cancel();
    }

    private void ToonZoekertje()
    {
        OriginTitle.Text = _listing.Title;

        var prijs = _listing.Price is { } p
            ? (string)new Converters.PriceTextConverter()
                .Convert(p, typeof(string), null!, System.Globalization.CultureInfo.CurrentCulture)
            : _listing.PriceLabel.Length > 0 ? _listing.PriceLabel : "geen prijs";

        OriginPrice.Text = $"{prijs} · {_listing.Source}";

        try
        {
            if (_listing.Thumbnail.Length > 0)
                OriginPhoto.Background = new ImageBrush(new BitmapImage(new Uri(_listing.Thumbnail)))
                { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            // Een kapotte link mag het venster niet laten vallen.
            Log.Write($"partijprijzen: foto niet geladen - {ex.Message}");
        }
    }

    /// <summary>
    /// Dezelfde knop zoekt en stopt, zoals het vergrootglas op het hoofdscherm en de knop in de
    /// AI-controle: daar staat je muis al.
    /// </summary>
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bezig)
        {
            _gestopt = true;
            _cts?.Cancel();
            StatusText.Text = "Stoppen...";
            RunButton.IsEnabled = false;
            return;
        }

        await ZoekAsync();
    }

    private async Task ZoekAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        _bezig = true;
        _gestopt = false;

        RunButton.Content = "Stoppen";
        RunButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Caution;

        var tot = Math.Min(_gedaan + PerKeer, _tezoeken.Count);
        var klok = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            while (_gedaan < tot)
            {
                var naam = _tezoeken[_gedaan];

                StatusText.Text = $"{_gedaan + 1} van {_tezoeken.Count}: '{naam}' opzoeken...";

                var ind = await PriceIndicator.DetermineAsync(naam, null, _sites, null, cts.Token);

                if (cts.IsCancellationRequested) return;

                Voeg(naam, ind);
                _gedaan++;
            }

            klok.Stop();

            var metWaarde = _rijen.Count(r => r.Gevonden);

            StatusText.Text = $"{_gedaan} titels opgezocht in {klok.Elapsed.TotalSeconds:F0} s; " +
                              $"{metWaarde} daarvan gaven een marktwaarde." +
                              (_gedaan < _tezoeken.Count
                                  ? $" Er staan er nog {_tezoeken.Count - _gedaan} klaar."
                                  : "");
        }
        catch (OperationCanceledException)
        {
            // Op Stoppen gedrukt, of het venster ging dicht.
            if (_gestopt)
                StatusText.Text = $"Gestopt na {_gedaan} van de {_tezoeken.Count} titels; " +
                                  "die staan hieronder.";
        }
        catch (Exception ex)
        {
            StatusText.Text = FriendlyError.Describe(ex);
            Log.Write($"partijprijzen: mislukt - {ex.Message}");
        }
        finally
        {
            _bezig = false;

            // Ook na Stoppen weer bruikbaar: wat al opgezocht is blijft staan, en de knop gaat
            // verder waar ze gebleven was.
            if (_gestopt || !cts.IsCancellationRequested)
            {
                RunButton.Content = _gedaan >= _tezoeken.Count
                    ? "Alles opgezocht"
                    : $"Nog {Math.Min(PerKeer, _tezoeken.Count - _gedaan)} opzoeken";

                RunButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                RunButton.IsEnabled = _gedaan < _tezoeken.Count;
            }
        }
    }

    /// <summary>
    /// Een uitkomst op zijn plaats in de lijst zetten: duurste eerst, en wat geen marktwaarde
    /// had onderaan. Invoegen en niet opnieuw opbouwen - een lijst wissen geeft een Reset, en
    /// dan springt het venster naar boven terwijl je aan het lezen bent.
    /// </summary>
    private void Voeg(string naam, PriceIndication ind)
    {
        var rij = ind.Market is { } m
            ? new LotRowView
            {
                Titel = naam,
                Bedrag = PriceRange.Euro(m.Median),
                Onder = $"{PriceRange.Euro(m.Low)} tot {PriceRange.Euro(m.High)}, uit {m.Count} vraagprijzen",
                Waarde = m.Median
            }
            : new LotRowView
            {
                Titel = naam,
                Bedrag = "-",
                Onder = ind.Items.Count > 0
                    ? $"te weinig gegevens: {ind.Items.Count} vergelijking(en)"
                    : $"niets gevonden dat hierover ging ({ind.OtherCount} treffers gingen over iets anders)"
            };

        var plaats = _rijen.Count;

        if (rij.Gevonden)
            for (var i = 0; i < _rijen.Count; i++)
                if (!_rijen[i].Gevonden || _rijen[i].Waarde < rij.Waarde) { plaats = i; break; }

        _rijen.Insert(plaats, rij);

        LijstBlok.Visibility = Visibility.Visible;
        LijstKop.Text = $"{_rijen.Count(r => r.Gevonden)} van de {_rijen.Count} opgezochte titels " +
                        "gaven een marktwaarde";
    }

    /// <summary>De gewone prijsindicatie voor die ene titel, met alle vergelijkingen eronder.</summary>
    private void Rij_Klik(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LotRowView rij) return;

        new PriceIndicationWindow(_listing, _sites, term: rij.Titel).Boven(this).Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

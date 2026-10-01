using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix;

/// <summary>
/// Wat er van één foto gelezen is, klaar om te tonen. Het verhaal staat hier niet in: dat is er
/// één, over alle foto's samen, en het staat bovenaan het venster.
/// </summary>
public class PhotoResultView
{
    public string Kop { get; init; } = "";
    public List<string> Gelezen { get; init; } = new();
    public Brush? Penseel { get; init; }

    public string GelezenKop => Gelezen.Count == 0
        ? ""
        : $"Gelezen op de foto ({Gelezen.Count}) - ongeveer vijf op de zes klopt:";

    /// <summary>Een foto zonder leesbare tekst zegt dat zelf; anders staat er een leeg blok.</summary>
    public string NietsGelezen => Gelezen.Count == 0 ? "Niets leesbaars op deze foto." : "";

    public Visibility NamenZichtbaar => Gelezen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FotoZichtbaar => Penseel is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// Wat staat er op deze foto dat je zelf niet ziet? Geopend met een rechtsklik op de foto van een
/// zoekertje. Een model op deze pc leest wat er op de voorwerpen staat en vertelt erover in gewone
/// taal; het werk zit in <see cref="PhotoAnalyzer"/>, dit venster toont enkel.
///
/// Twee dingen die het venster eerlijk moet zeggen, want anders lijkt het stuk:
///
/// - **Het model laden kost de eerste keer zo'n veertig seconden.** Staat het nog niet in de
///   grafische kaart (<see cref="PhotoAnalyzer.ModelStaatKlaarAsync"/>), dan staat dat er vóór het
///   wachten begint, niet erna.
/// - **Grondig lezen duurt langer.** In stukken lezen is wat kleine tekst leesbaar maakt - de
///   titels op een doos vol dvd's - maar het kost een halve minuut in plaats van een paar
///   seconden. Het vinkje staat aan, en zegt in zijn tooltip waarom je het zou uitzetten.
///
/// **Alle foto's van het zoekertje** (het tweede vinkje) haalt ook de andere foto's van de
/// advertentie op, van de pagina van het zoekertje zelf - daar staat vaak wat je zoekt.
///
/// **Daarvan komt één verhaal, niet één per foto** (26 september 2026). Bovenaan staat wat de AI
/// ziet, en dat wordt na elke foto opnieuw geschreven met alles wat er tot dan gelezen is; per
/// foto blijft enkel zijn miniatuur en zijn gelezen namen staan. Vier alinea's over dezelfde
/// DVD-speler zijn viermaal hetzelfde, en elk ervan kent maar een stuk van het toestel; één
/// alinea over alle gelezen namen samen zet bovendien leesfouten recht. Zie
/// <see cref="PhotoAnalyzer.LeesAsync"/> voor de meting.
///
/// Het groeit mee terwijl je kijkt: bij vijf foto's duurt het geheel meer dan een minuut, en dan
/// wil je niet naar een leeg venster kijken.
///
/// De grote foto gaat voor op de miniatuur (<see cref="Listing.LargeImage"/>): hoe meer
/// beeldpunten, hoe meer er te lezen valt. En is zelfs díe te klein - bij Facebook is de foto
/// van de zoekpagina 261 × 261 - dan wordt de grotere van de advertentiepagina gehaald
/// (<see cref="DetailFetcher.GroteVersieAsync"/>). Het venster zegt dat dan ook, en toont die
/// foto erbij: anders kijkt de AI naar iets anders dan waarop je klikte.
/// </summary>
public partial class PhotoInsightWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Listing _listing;
    private readonly IReadOnlyList<SiteDefinition> _sites;
    private readonly ObservableCollection<PhotoResultView> _uitkomsten = new();

    private CancellationTokenSource? _cts;

    /// <summary>Er wordt gekeken; de knop is dan een stopknop.</summary>
    private bool _bezig;

    /// <summary>
    /// Wat er over alle foto's samen gelezen is. Blijft staan na het kijken: de knop
    /// Prijsindicatie geeft die namen mee als voorstellen om aan te klikken.
    /// </summary>
    private readonly List<string> _gelezenSamen = new();

    /// <summary>
    /// Er is op Stoppen gedrukt. Nodig naast de token: die staat ook op "geannuleerd" wanneer
    /// het venster dichtgaat of wanneer er opnieuw gekeken wordt, en dan hoort er geen
    /// "Gestopt" in de statusregel te komen.
    /// </summary>
    private bool _gestopt;

    public PhotoInsightWindow(Listing listing, IReadOnlyList<SiteDefinition> sites, bool alleFotos = false)
    {
        InitializeComponent();

        _listing = listing;
        _sites = sites;

        Results.ItemsSource = _uitkomsten;
        AllPhotosBox.IsChecked = alleFotos;

        ToonZoekertje();
        ModelText.Text = $"{AppSettings.Current.AiModel}, op je eigen grafische kaart via Ollama.";

        Loaded += async (_, _) => await KijkAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private void ToonZoekertje()
    {
        OriginTitle.Text = _listing.Title;

        var prijs = _listing.Price is { } p && p > 0 ? PriceRange.Euro(p) : "geen prijs";
        OriginPrice.Text = $"{prijs} · {_listing.Source}";

        OriginPhoto.Background = Penseel(_listing.Thumbnail);
    }

    /// <summary>
    /// Dezelfde knop start en stopt, net als het vergrootglas op het hoofdscherm. Eén knop op
    /// één plaats: daar staat je muis al, en een tweede knop ernaast zou twee zichtbaarheden
    /// overal gelijk moeten houden.
    /// </summary>
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bezig)
        {
            _gestopt = true;
            _cts?.Cancel();
            StatusText.Text = "Stoppen...";
            RunButton.IsEnabled = false;   // niet twee keer
            return;
        }

        await KijkAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Voor een partij: elke gelezen titel apart opzoeken. Daar is de vraag niet wat de doos
    /// waard is maar of er iets waardevols bij zit, en dan ís elke gelezen naam een titel die
    /// op zichzelf te koop staat. Zie <see cref="LotPriceWindow"/>.
    /// </summary>
    private void LotButton_Click(object sender, RoutedEventArgs e) =>
        new LotPriceWindow(_listing, _sites, _gelezenSamen) { Owner = this }.Show();

    /// <summary>
    /// Wat is dit ongeveer waard? Hetzelfde venster als elders, maar met een betere zoekterm:
    /// de prijsindicatie leidt die anders af uit de <i>titel</i>, en daar staat meestal geen
    /// modelnummer in (op 2dehands 78 tot 98% van de titels niet). Op het toestel staat het wel,
    /// en dat is net wat er hier gelezen is.
    ///
    /// De gelezen namen gaan mee als <b>voorstellen om aan te klikken</b>. De app kiest er zelf
    /// geen zoekterm uit, en dat is gemeten: zie <see cref="PriceIndicator.AlsZoektermen"/>.
    /// </summary>
    private void PriceButton_Click(object sender, RoutedEventArgs e) =>
        new PriceIndicationWindow(_listing, _sites, _gelezenSamen) { Owner = this }.Show();

    /// <summary>
    /// Alles wat er staat naar het klembord: bij een doos vol dvd's is die namenlijst juist het
    /// ding dat je ergens anders wil plakken.
    /// </summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uitkomsten.Count == 0) return;

        var tekst = new System.Text.StringBuilder();
        tekst.AppendLine(_listing.Title);

        if (SamenvattingTekst.Text.Length > 0)
        {
            tekst.AppendLine();
            tekst.AppendLine(SamenvattingTekst.Text);
        }

        foreach (var uitkomst in _uitkomsten)
        {
            if (uitkomst.Gelezen.Count == 0) continue;

            tekst.AppendLine();
            tekst.AppendLine(uitkomst.Kop + ", gelezen:");
            foreach (var naam in uitkomst.Gelezen) tekst.AppendLine(naam);
        }

        try
        {
            Clipboard.SetText(tekst.ToString());
            StatusText.Text = "Gekopieerd naar het klembord.";
        }
        catch (Exception ex)
        {
            // Het klembord kan even bezet zijn door een ander programma.
            StatusText.Text = "Kopiëren lukte niet: " + ex.Message;
        }
    }

    private async Task KijkAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        _bezig = true;
        _gestopt = false;

        RunButton.Content = "Stoppen";
        RunButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Caution;
        RunButton.ToolTip = "Het kijken afbreken. Wat al bekeken is, blijft staan.";

        CopyButton.IsEnabled = false;
        PriceButton.IsEnabled = false;
        LotButton.IsEnabled = false;
        _uitkomsten.Clear();

        SamenvattingBlok.Visibility = Visibility.Collapsed;
        SamenvattingTekst.Text = "";

        var grondig = ThoroughBox.IsChecked == true;
        var alle = AllPhotosBox.IsChecked == true;

        Kop.Title = alle ? "AI-controle op alle foto's van dit zoekertje" : "AI-controle op deze foto";

        try
        {
            var fotos = new List<string>();

            // De eerste foto is bij één foto al opgehaald om ze na te meten; zo gaat ze niet
            // twee keer over de lijn. En stond er een grotere op de advertentiepagina, dan moet
            // het venster dat zeggen - anders kijkt de AI naar iets anders dan waarop je klikte.
            byte[]? alGehaald = null;
            string? kleinereWas = null;
            var maat = "";

            if (alle)
            {
                StatusText.Text = "De andere foto's van dit zoekertje opzoeken...";

                var details = await DetailFetcher.DetailsAsync(_listing, _sites, cts.Token);

                // Geeft de pagina foto's, dan kijken we enkel naar díe - niet ook nog naar de
                // foto van de zoekpagina. Bij Facebook is dat namelijk dezelfde foto op 260 px
                // naast dezelfde op 960 px, en dan las het model die kleine niet en verzon het
                // er iets bij: bij een stapel spelletjes "ongeveer twintig cd's". Twee keer
                // wachten op hetzelfde, met een verkeerd antwoord erbovenop.
                fotos = details.PaginaFotos.Count > 0
                    ? details.PaginaFotos.ToList()
                    : details.Fotos.ToList();
            }
            else if (!string.IsNullOrWhiteSpace(_listing.LargeImage))
            {
                // Is de foto van de zoekpagina te klein om iets van te lezen - bij Facebook
                // 261 px - dan haalt dit de grotere van de advertentiepagina. Is ze groot
                // genoeg, dan gebeurt er niets extra.
                var keuze = await DetailFetcher.GroteVersieAsync(
                    _listing, _sites, new Progress<string>(t => StatusText.Text = t), cts.Token);

                fotos.Add(keuze.Url);

                alGehaald = keuze.Beeld;
                kleinereWas = keuze.VorigeMaat;
                maat = keuze.Maat;
            }

            if (fotos.Count == 0)
            {
                StatusText.Text = "Dit zoekertje heeft geen foto om naar te kijken.";
                return;
            }

            if (alle && fotos.Count == 1)
                Log.Write($"AI-controle: geen extra foto's gevonden voor '{_listing.Title}'");

            // Eerst zeggen dat het model geladen moet worden, en pas dan wachten: anders staar je
            // veertig seconden naar een venster dat niets lijkt te doen.
            if (!await PhotoAnalyzer.ModelStaatKlaarAsync(cts.Token))
                StatusText.Text = $"Het model {AppSettings.Current.AiModel} wordt eerst in de " +
                                  "grafische kaart geladen; dat duurt ongeveer 40 seconden. " +
                                  "Daarna gaat elke volgende foto snel.";

            var klok = System.Diagnostics.Stopwatch.StartNew();

            // Alles wat er tot hier gelezen is, over de foto's heen. Daaruit wordt telkens één
            // verhaal geschreven - niet één per foto. En de eerste foto gaat mee naar dat
            // vertellen: het model moet de namen in hun verband zien.
            var samen = _gelezenSamen;
            samen.Clear();

            byte[]? hoofdfoto = null;

            for (var i = 0; i < fotos.Count; i++)
            {
                var nummer = fotos.Count == 1 ? "" : $"Foto {i + 1} van {fotos.Count}: ";
                var melder = new Progress<string>(tekst => StatusText.Text = nummer + tekst);

                var beeld = i == 0 && alGehaald is not null
                    ? alGehaald
                    : await HaalFotoAsync(fotos[i], cts.Token);

                if (beeld is null)
                {
                    Log.Write($"AI-controle: foto {i + 1} niet op te halen");
                    continue;
                }

                hoofdfoto ??= beeld;

                var lezing = await new PhotoAnalyzer().LeesAsync(beeld, grondig, melder, cts.Token);

                if (cts.IsCancellationRequested) return;

                if (lezing.Fout is not null)
                {
                    StatusText.Text = lezing.Fout;
                    return;
                }

                var nieuw = lezing.Gelezen
                    .Where(n => !samen.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                samen.AddRange(nieuw);

                // Meteen tonen: bij vijf foto's duurt het geheel meer dan een minuut.
                _uitkomsten.Add(new PhotoResultView
                {
                    Kop = fotos.Count == 1 ? "Wat er op de foto staat" : $"Foto {i + 1} van {fotos.Count}",
                    Gelezen = lezing.Gelezen.ToList(),
                    // Bij één foto hoeft er geen miniatuur: je klikte er net zelf op. Tenzij het
                    // een grotere versie van de advertentiepagina geworden is - dan hoor je te
                    // zien waar de AI werkelijk naar keek.
                    Penseel = fotos.Count == 1 && kleinereWas is null ? null : Penseel(fotos[i])
                });

                CopyButton.IsEnabled = true;
                PriceButton.IsEnabled = true;
                LotButton.IsEnabled = true;

                // Bracht deze foto geen enkele nieuwe naam, dan kan het verhaal niet veranderen;
                // die vraag aan het model wordt dan overgeslagen. Bij de eerste foto altijd wel,
                // ook als er niets gelezen is - anders blijft het vak leeg.
                if (nieuw.Count == 0 && i > 0) continue;

                var verhaal = await new PhotoAnalyzer()
                    .VertelAsync(samen, hoofdfoto, fotos.Count, melder, cts.Token);

                if (cts.IsCancellationRequested) return;

                if (verhaal.Fout is not null)
                {
                    StatusText.Text = verhaal.Fout;
                    return;
                }

                ToonSamenvatting(verhaal.Beschrijving, fotos.Count);
            }

            klok.Stop();

            // Kwam er van begin tot einde geen zin uit, dan hoort dat er te staan: anders zie
            // je wel de gelezen namen maar nergens waarom er geen verhaal bij staat.
            if (_uitkomsten.Count > 0 && SamenvattingTekst.Text.Length == 0)
                ToonSamenvatting("Het model zag niets waarover het iets kon zeggen.", fotos.Count);

            StatusText.Text = _uitkomsten.Count switch
            {
                0 => "Geen van de foto's was op te halen.",
                1 => $"Klaar in {klok.Elapsed.TotalSeconds:F0} s.",
                var n => $"Klaar in {klok.Elapsed.TotalSeconds:F0} s, {n} foto's bekeken."
            };

            if (kleinereWas is not null && _uitkomsten.Count > 0)
                StatusText.Text += $" Bekeken is de foto van de advertentiepagina ({maat} " +
                                   $"beeldpunten); die op de kaart is maar {kleinereWas}.";
        }
        catch (OperationCanceledException)
        {
            // Op Stoppen gedrukt, het venster ging dicht, of er wordt opnieuw gekeken.
            if (_gestopt)
                StatusText.Text = _uitkomsten.Count switch
                {
                    0 => "Gestopt. Er was nog geen enkele foto klaar.",
                    1 => "Gestopt na één foto; die staat hieronder.",
                    var n => $"Gestopt na {n} foto's; die staan hieronder."
                };
        }
        finally
        {
            _bezig = false;

            // Ook na Stoppen weer bruikbaar: wat al bekeken is blijft staan, en je kan er zo
            // opnieuw aan beginnen. Gaat het venster dicht, of is er intussen een nieuwe beurt
            // begonnen, dan blijft de knop van die ander.
            if (_gestopt || !cts.IsCancellationRequested)
            {
                RunButton.Content = "Opnieuw kijken";
                RunButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                RunButton.ToolTip = null;
                RunButton.IsEnabled = true;
            }

            if (_uitkomsten.Count > 0)
            {
                CopyButton.IsEnabled = true;
                PriceButton.IsEnabled = true;
                LotButton.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// Het verhaal bovenaan, dat na elke foto opnieuw geschreven wordt met alles wat er tot dan
    /// gelezen is. De kop zegt over hoeveel foto's het gaat, want anders lijkt het bij vier
    /// foto's alsof er maar naar één gekeken is.
    /// </summary>
    private void ToonSamenvatting(string tekst, int aantalFotos)
    {
        // Komt er een keer niets terug, dan blijft staan wat er al stond: dat is altijd beter
        // dan een goed verhaal vervangen door een lege regel.
        if (tekst.Length == 0) return;

        SamenvattingKop.Text = aantalFotos <= 1
            ? "Wat de AI ziet"
            : $"Wat de AI ziet - {aantalFotos} foto's samen";

        SamenvattingTekst.Text = tekst;
        SamenvattingBlok.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Een foto als penseel, om ze in een vlak te tonen. Een lege of kapotte link mag het venster
    /// niet laten vallen - een sitebestand kan van een vreemde komen.
    /// </summary>
    private static Brush? Penseel(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            return new ImageBrush(new BitmapImage(new Uri(url))) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log.Write($"AI-controle: foto niet geladen - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// De foto ophalen zoals een browser dat doet. Sommige sites weigeren een verzoek zonder
    /// herkenbare afzender, en dan komt er een foutcode in plaats van een foto.
    /// </summary>
    private static async Task<byte[]?> HaalFotoAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            using var vraag = new HttpRequestMessage(HttpMethod.Get, url);
            vraag.Headers.TryAddWithoutValidation("User-Agent", Services.HttpFactory.UserAgent);

            using var antwoord = await Http.SendAsync(vraag, ct);
            antwoord.EnsureSuccessStatusCode();

            return await antwoord.Content.ReadAsByteArrayAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write($"AI-controle: foto niet op te halen - {ex.Message}");
            return null;
        }
    }
}

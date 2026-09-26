using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix;

/// <summary>Wat de AI van één foto maakte, klaar om te tonen.</summary>
public class PhotoResultView
{
    public string Kop { get; init; } = "";
    public string Beschrijving { get; init; } = "";
    public List<string> Gelezen { get; init; } = new();
    public Brush? Penseel { get; init; }

    public string GelezenKop => Gelezen.Count == 0
        ? ""
        : $"Gelezen op de foto ({Gelezen.Count}) - ongeveer vijf op de zes klopt:";

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
/// advertentie op, van de pagina van het zoekertje zelf - daar staat vaak wat je zoekt. Elke foto
/// krijgt zijn eigen blok, en dat blok verschijnt zodra die foto klaar is: bij vijf foto's duurt
/// het geheel meer dan een minuut, en dan wil je niet naar een leeg venster kijken.
///
/// De grote foto gaat voor op de miniatuur (<see cref="Listing.LargeImage"/>): hoe meer
/// beeldpunten, hoe meer er te lezen valt.
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
    /// Alles wat er staat naar het klembord: bij een doos vol dvd's is die namenlijst juist het
    /// ding dat je ergens anders wil plakken.
    /// </summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uitkomsten.Count == 0) return;

        var tekst = new System.Text.StringBuilder();
        tekst.AppendLine(_listing.Title);

        foreach (var uitkomst in _uitkomsten)
        {
            tekst.AppendLine();
            tekst.AppendLine(uitkomst.Kop);
            tekst.AppendLine(uitkomst.Beschrijving);

            if (uitkomst.Gelezen.Count == 0) continue;

            tekst.AppendLine();
            tekst.AppendLine("Gelezen op de foto:");
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
        _uitkomsten.Clear();

        var grondig = ThoroughBox.IsChecked == true;
        var alle = AllPhotosBox.IsChecked == true;

        Kop.Title = alle ? "AI-controle op alle foto's van dit zoekertje" : "AI-controle op deze foto";

        try
        {
            var fotos = new List<string>();

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
                fotos.Add(_listing.LargeImage);
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

            for (var i = 0; i < fotos.Count; i++)
            {
                var nummer = fotos.Count == 1 ? "" : $"Foto {i + 1} van {fotos.Count}: ";
                var melder = new Progress<string>(tekst => StatusText.Text = nummer + tekst);

                var beeld = await HaalFotoAsync(fotos[i], cts.Token);

                if (beeld is null)
                {
                    Log.Write($"AI-controle: foto {i + 1} niet op te halen");
                    continue;
                }

                var uitkomst = await new PhotoAnalyzer().AnalyseerAsync(beeld, grondig, melder, cts.Token);

                if (cts.IsCancellationRequested) return;

                if (uitkomst.Fout is not null)
                {
                    StatusText.Text = uitkomst.Fout;
                    return;
                }

                // Meteen tonen: bij vijf foto's duurt het geheel meer dan een minuut.
                _uitkomsten.Add(new PhotoResultView
                {
                    Kop = fotos.Count == 1 ? "Wat de AI ziet" : $"Foto {i + 1} van {fotos.Count}",
                    Beschrijving = uitkomst.Beschrijving.Length > 0
                        ? uitkomst.Beschrijving
                        : "Het model zag niets waarover het iets kon zeggen.",
                    Gelezen = uitkomst.Gelezen.ToList(),
                    Penseel = fotos.Count == 1 ? null : Penseel(fotos[i])
                });

                CopyButton.IsEnabled = true;
            }

            klok.Stop();

            StatusText.Text = _uitkomsten.Count switch
            {
                0 => "Geen van de foto's was op te halen.",
                1 => $"Klaar in {klok.Elapsed.TotalSeconds:F0} s.",
                var n => $"Klaar in {klok.Elapsed.TotalSeconds:F0} s, {n} foto's bekeken."
            };
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

            if (_uitkomsten.Count > 0) CopyButton.IsEnabled = true;
        }
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
            vraag.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36");

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

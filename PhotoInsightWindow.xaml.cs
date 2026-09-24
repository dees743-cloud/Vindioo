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

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await KijkAsync();

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

        RunButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        _uitkomsten.Clear();

        var grondig = ThoroughBox.IsChecked == true;
        var alle = AllPhotosBox.IsChecked == true;

        try
        {
            var fotos = new List<string>();

            if (alle)
            {
                StatusText.Text = "De andere foto's van dit zoekertje opzoeken...";
                fotos = await DetailFetcher.FotosAsync(_listing, _sites, cts.Token);
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
            // Het venster ging dicht, of er werd opnieuw gekeken.
        }
        finally
        {
            if (!cts.IsCancellationRequested) RunButton.IsEnabled = true;
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

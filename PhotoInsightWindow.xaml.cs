using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix;

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
/// De grote foto gaat voor op de miniatuur (<see cref="Listing.LargeImage"/>): hoe meer
/// beeldpunten, hoe meer er te lezen valt.
/// </summary>
public partial class PhotoInsightWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Listing _listing;
    private CancellationTokenSource? _cts;
    private PhotoInsight? _laatste;

    public PhotoInsightWindow(Listing listing)
    {
        InitializeComponent();

        _listing = listing;

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
            Log.Write($"AI-controle: foto niet geladen - {ex.Message}");
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await KijkAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// De beschrijving én de gelezen namen naar het klembord: bij een doos vol dvd's is die lijst
    /// juist het ding dat je ergens anders wil plakken.
    /// </summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_laatste is null) return;

        var tekst = _laatste.Beschrijving;

        if (_laatste.Gelezen.Count > 0)
            tekst += Environment.NewLine + Environment.NewLine +
                     "Gelezen op de foto:" + Environment.NewLine +
                     string.Join(Environment.NewLine, _laatste.Gelezen);

        try
        {
            Clipboard.SetText(tekst);
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
        ResultPanel.Visibility = Visibility.Collapsed;
        ReadPanel.Visibility = Visibility.Collapsed;

        var grondig = ThoroughBox.IsChecked == true;

        try
        {
            StatusText.Text = "De foto ophalen...";

            var foto = await HaalFotoAsync(_listing.LargeImage, cts.Token);

            if (foto is null)
            {
                StatusText.Text = "De foto van dit zoekertje is niet op te halen.";
                return;
            }

            // Eerst zeggen dat het model geladen moet worden, en pas dan wachten: anders staar je
            // veertig seconden naar een venster dat niets lijkt te doen.
            if (!await PhotoAnalyzer.ModelStaatKlaarAsync(cts.Token))
                StatusText.Text = $"Het model {AppSettings.Current.AiModel} wordt eerst in de " +
                                  "grafische kaart geladen; dat duurt ongeveer 40 seconden. " +
                                  "Daarna gaat elke volgende foto snel.";

            var melder = new Progress<string>(tekst => StatusText.Text = tekst);
            var uitkomst = await new PhotoAnalyzer().AnalyseerAsync(foto, grondig, melder, cts.Token);

            if (cts.IsCancellationRequested) return;

            Toon(uitkomst);
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

    private void Toon(PhotoInsight uitkomst)
    {
        _laatste = uitkomst;

        if (uitkomst.Fout is not null)
        {
            StatusText.Text = uitkomst.Fout;
            return;
        }

        DescriptionText.Text = uitkomst.Beschrijving.Length > 0
            ? uitkomst.Beschrijving
            : "Het model zag niets waarover het iets kon zeggen.";

        ResultPanel.Visibility = Visibility.Visible;
        CopyButton.IsEnabled = true;

        if (uitkomst.Gelezen.Count > 0)
        {
            ReadTitle.Text = $"Gelezen op de foto ({uitkomst.Gelezen.Count})";
            ReadNames.ItemsSource = uitkomst.Gelezen;
            ReadPanel.Visibility = Visibility.Visible;
        }

        StatusText.Text = uitkomst.Stukken > 1
            ? $"Klaar in {uitkomst.Duur.TotalSeconds:F0} s, de foto in {uitkomst.Stukken} stukken gelezen."
            : $"Klaar in {uitkomst.Duur.TotalSeconds:F0} s." +
              (uitkomst.Gelezen.Count == 0
                  ? " Zet 'Grondig' aan om ook kleine tekst te lezen."
                  : "");
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

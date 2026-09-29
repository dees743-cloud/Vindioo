using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix;

public partial class AddSiteWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ObservableCollection<Listing> _preview = new();
    private readonly SiteAnalyzer _analyzer = new();

    // Elk invoerveld gekoppeld aan zijn eigenschap in de definitie.
    private readonly Dictionary<string, TextBox> _fields = new();

    /// <summary>De definitie zoals ze bij Opslaan bewaard wordt.</summary>
    public SiteDefinition? Result { get; private set; }

    public AddSiteWindow()
    {
        InitializeComponent();
        PreviewList.ItemsSource = _preview;

        // Meteen zeggen dat er een sleutel nodig is, en niet pas nadat alles ingevuld is.
        ApiKeyPanel.Visibility = HeeftApiSleutel() ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool HeeftApiSleutel() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    /// <summary>
    /// Bewaart de API-sleutel als omgevingsvariabele van het Windows-account, zoals
    /// CLAUDE.md wil: de sleutel staat nooit in de code of in een bestand van de app.
    /// Hij wordt ook meteen voor dit proces gezet, zodat de app niet herstart hoeft te
    /// worden - de analyse leest hem bij elke aanvraag opnieuw.
    /// </summary>
    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var sleutel = ApiKeyBox.Password.Trim();

        if (sleutel.Length == 0)
        {
            StatusText.Text = "Plak eerst je sleutel in het veld.";
            return;
        }

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", sleutel);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", sleutel, EnvironmentVariableTarget.User);

            ApiKeyBox.Clear();
            ApiKeyPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Sleutel bewaard. Je kan nu analyseren.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon de sleutel niet bewaren: " + ex.Message;
        }
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        var word = TestQueryBox.Text.Trim();

        // Ook als {query} er al staat: de analyse moet iets invullen om een echte
        // resultatenpagina te krijgen.
        if (string.IsNullOrEmpty(word))
        {
            StatusText.Text = "Vul in welk woord je gezocht hebt.";
            return;
        }

        if (!url.Contains("{query}"))
        {
            url = MakeTemplate(url, word);

            if (!url.Contains("{query}"))
            {
                StatusText.Text = $"'{word}' staat niet in de URL. Controleer het woord, of zet zelf {{query}} op de juiste plaats.";
                return;
            }

            UrlBox.Text = url;   // toon wat de app ervan gemaakt heeft
        }

        AnalyzeButton.IsEnabled = false;
        Spinner.Visibility = Visibility.Visible;

        try
        {
            // De analyse meldt zelf in welke stap ze zit: de pagina ophalen kan via
            // drie wegen gaan, en de AI kan een tweede keer nodig hebben.
            var status = new Progress<string>(text => StatusText.Text = text);
            var analysis = await _analyzer.AnalyzeAsync(url, word, BridgeCheck.IsChecked == true, status);

            ShowFields(analysis);

            StatusText.Text = analysis.Check.IsGood
                ? "Klaar. Klik op Testen om het met een echte zoekopdracht te proberen."
                : "Niet alles klopt; zie de telling links. Pas aan wat nodig is en klik op Testen.";

            TestButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex.Message.StartsWith("API-fout (401)"))
        {
            // Een verkeerde of ingetrokken sleutel. Het veld om te plakken verscheen enkel
            // zolang er geen sleutel was; daarna kon je hem enkel nog in de omgevingsvariabelen
            // van Windows vervangen.
            Log.Write("analyse: de API weigerde de sleutel (401)");
            ApiKeyPanel.Visibility = Visibility.Visible;
            StatusText.Text = "De Claude-API weigert deze sleutel. Plak hierboven een geldige sleutel en probeer opnieuw.";
        }
        catch (Exception ex)
        {
            Log.Write($"analyse mislukt - {ex.Message}");
            StatusText.Text = "Fout: " + ex.Message;
        }
        finally
        {
            Spinner.Visibility = Visibility.Collapsed;
            AnalyzeButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Zet de gevonden selectors in bewerkbare velden, zodat je kan bijsturen, met
    /// bovenaan wat de app ervan nagemeten heeft.
    /// </summary>
    private void ShowFields(SiteAnalysis analysis)
    {
        var d = analysis.Definition;

        FieldsPanel.Children.Clear();
        _fields.Clear();

        // Eerst de telling: die zegt of je de velden hieronder kan vertrouwen.
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = MeasurementText(analysis),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        Add("Naam", d.Name);
        Add("Korte naam (tab)", d.ShortName);
        Add("Basis-URL", d.BaseUrl);
        Add("Resultaat", d.ItemSelector);
        Add("Titel", d.TitleSelector);
        Add("Beschrijving", d.DescriptionSelector);
        Add("Prijs", d.PriceSelector);
        Add("Plaats", d.LocationSelector);
        Add("Datum", d.DateSelector);
        Add("Link", d.UrlSelector);
        Add("Foto", d.ImageSelector);
        Add("Grote foto", d.LargeImageSelector);
        Add("Volgende pagina", d.PageTemplate);

        // De vier velden van de pagina van een zoekertje zelf. Ze stonden enkel in Sites
        // beheren, terwijl de analyse ze nu meteen invult - en dan wil je ze ook hier
        // kunnen nakijken, naast de telling die erbij hoort.
        Add("Foto's (op de pagina zelf)", d.DetailImagesSelector);
        Add("Verkoper (op de pagina zelf)", d.DetailSellerSelector);
        Add("Online sinds (op de pagina zelf)", d.DetailPostedSelector);
        Add("Beschrijving (op de pagina zelf)", d.DetailDescriptionSelector);

        // Welke weg de site nodig heeft, is gemeten tijdens de analyse. Aanpasbaar,
        // want een site kan bij de volgende keer strenger zijn.
        _browserCheck = new CheckBox
        {
            Content = "Browser gebruiken (JavaScript-site)",
            IsChecked = d.NeedsBrowser,
            Margin = new Thickness(0, 12, 0, 0)
        };
        FieldsPanel.Children.Add(_browserCheck);

        _bridgeCheck = new CheckBox
        {
            Content = "Via mijn eigen Chrome (brug)",
            IsChecked = d.UseBridge,
            Margin = new Thickness(0, 4, 0, 0)
        };
        FieldsPanel.Children.Add(_bridgeCheck);

        if (!string.IsNullOrWhiteSpace(d.Notes))
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = d.Notes,
                Opacity = 0.6,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
        }

        _current = d;

        void Add(string label, string value)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Opacity = 0.6,
                Margin = new Thickness(0, 8, 0, 2)
            });

            var box = new TextBox { Text = value };
            FieldsPanel.Children.Add(box);
            _fields[label] = box;
        }
    }

    /// <summary>De telling in gewone woorden, met de weg die werkte en wat er schort.</summary>
    private static string MeasurementText(SiteAnalysis analysis)
    {
        var lines = new List<string>
        {
            analysis.Route switch
            {
                FetchRoute.Direct => "Werkt zonder browser: de snelste weg.",
                FetchRoute.Browser => "Deze site heeft een browser nodig.",
                _ => "Deze site blokkeert de app en loopt via je eigen Chrome."
            },
            analysis.Check.Summary
        };

        if (analysis.Rounds > 1)
            lines.Add($"De AI had {analysis.Rounds} pogingen nodig.");

        lines.AddRange(analysis.Check.Problems.Select(p => "• " + p));

        // De advertentiepagina is een tweede meting op een andere pagina, dus die krijgt
        // een eigen regel in plaats van dat de getallen door elkaar lopen.
        if (analysis.Detail is { } detail)
        {
            lines.Add(detail.Summary);
            lines.AddRange(detail.Problems.Select(p => "• " + p));
        }
        else
        {
            lines.Add("De pagina van een zoekertje is niet bekeken; de velden daarvoor staan leeg.");
        }

        return string.Join("\n", lines);
    }

    private SiteDefinition? _current;
    private CheckBox? _browserCheck;
    private CheckBox? _bridgeCheck;

    /// <summary>Leest de (eventueel aangepaste) velden terug in de definitie.</summary>
    private SiteDefinition CollectDefinition()
    {
        var d = _current ?? new SiteDefinition();

        d.Name = _fields["Naam"].Text.Trim();
        d.ShortName = _fields["Korte naam (tab)"].Text.Trim();
        d.BaseUrl = _fields["Basis-URL"].Text.Trim();
        d.ItemSelector = _fields["Resultaat"].Text.Trim();
        d.TitleSelector = _fields["Titel"].Text.Trim();
        d.DescriptionSelector = _fields["Beschrijving"].Text.Trim();
        d.PriceSelector = _fields["Prijs"].Text.Trim();
        d.LocationSelector = _fields["Plaats"].Text.Trim();
        d.DateSelector = _fields["Datum"].Text.Trim();
        d.UrlSelector = _fields["Link"].Text.Trim();
        d.ImageSelector = _fields["Foto"].Text.Trim();
        d.LargeImageSelector = _fields["Grote foto"].Text.Trim();
        d.PageTemplate = _fields["Volgende pagina"].Text.Trim();
        d.DetailImagesSelector = _fields["Foto's (op de pagina zelf)"].Text.Trim();
        d.DetailSellerSelector = _fields["Verkoper (op de pagina zelf)"].Text.Trim();
        d.DetailPostedSelector = _fields["Online sinds (op de pagina zelf)"].Text.Trim();
        d.DetailDescriptionSelector = _fields["Beschrijving (op de pagina zelf)"].Text.Trim();
        d.SearchUrlTemplate = UrlBox.Text.Trim();
        d.NeedsBrowser = _browserCheck?.IsChecked == true;
        d.UseBridge = _bridgeCheck?.IsChecked == true;

        return d;
    }

    /// <summary>
    /// Opent de site zichtbaar in de browser van de app, zodat je je kan aanmelden.
    /// De login blijft daarna bewaard in het profiel van de app.
    /// </summary>
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            StatusText.Text = "Vul eerst een URL in.";
            return;
        }

        // Zonder zoekterm openen: gewoon de startpagina van de site.
        var word = TestQueryBox.Text.Trim();
        var openUrl = url.Contains("{query}")
            ? url.Replace("{query}", Uri.EscapeDataString(string.IsNullOrEmpty(word) ? "test" : word))
            : url;

        LoginButton.IsEnabled = false;
        StatusText.Text = "Browser wordt geopend. Meld je aan en sluit dan het venster.";

        try
        {
            await using var browser = new BrowserFetcher();
            await browser.OpenForLoginAsync(openUrl);

            // Of je echt aangemeld was, kan de app niet zien: enkel dat het venster dicht is.
            StatusText.Text = "Browser gesloten. Als je aangemeld was, onthoudt Zentrix dat. Klik nu op Analyseren met AI.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fout: " + ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        _preview.Clear();
        StatusText.Text = "Bezig met testen...";

        try
        {
            var definition = CollectDefinition();

            // Een browsersite leent de gedeelde Chrome. Zonder lening bleef die na
            // het testen openstaan, want enkel de laatste lener sluit hem.
            using var lease = BrowserPool.Lease();

            if (definition.UseBridge)
            {
                var brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30),
                    new Progress<string>(text => StatusText.Text = text));

                if (brug != BridgeStatus.Ready)
                {
                    StatusText.Text = "Testen gaat niet: " + ChromeLauncher.Describe(brug);
                    return;
                }
            }

            var found = await new GenericSource(definition).SearchAsync(TestQueryBox.Text.Trim(), 20);

            foreach (var listing in found) _preview.Add(listing);

            StatusText.Text = found.Count == 0
                ? "Geen resultaten. Pas de selectors aan of analyseer opnieuw."
                : $"{found.Count} resultaten gevonden, {found.Count(l => l.Price.HasValue)} met prijs.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fout: " + ex.Message;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CollectDefinition();

        if (string.IsNullOrWhiteSpace(Result.Name))
        {
            StatusText.Text = "Geef de site een naam.";
            return;
        }

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Zet het gezochte woord in de URL om naar {query}. Houdt rekening met
    /// de manieren waarop een browser dat woord kan schrijven.
    /// </summary>
    private static string MakeTemplate(string url, string word)
    {
        // Meest specifieke vorm eerst, anders vervangt hij het verkeerde stuk.
        var variants = new[]
        {
            Uri.EscapeDataString(word),   // spaties als %20
            word.Replace(" ", "+"),       // spaties als +
            word
        };

        foreach (var variant in variants.Distinct())
        {
            var index = url.IndexOf(variant, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                return url[..index] + "{query}" + url[(index + variant.Length)..];
        }

        return url;
    }
}

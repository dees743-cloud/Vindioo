using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zentrix.Controls;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix;

/// <summary>
/// Zoeken vanaf het scherm: klaarzetten, de runner aanroepen, en tonen.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- zoeken ----------

    /// <summary>
    /// Het venster verslepen door de kopbalk, en maximaliseren bij een dubbelklik.
    /// De titelbalk van WPF-UI staat enkel nog rechts bij de knoppen: over de
    /// volle breedte eist hij die hele strook op als sleepgebied, waardoor de
    /// zoekbalk eronder geen invoer meer aanneemt.
    /// </summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = RunSearchAsync();
    }

    /// <summary>
    /// Hoort deze zoekterm bij de bewaarde zoekopdracht die openstaat? Zo niet, dan staat
    /// wat je doet daar los van: de resultaten horen er niet in, en het tandwiel hoort haar
    /// niet te openen.
    /// </summary>
    private bool IsActieveZoekterm(string query) =>
        _activeSearch is not null &&
        string.Equals(query, _activeSearch.Query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dubbelklikken selecteert de hele zoekterm, niet enkel het woord waarop je
    /// klikt. Zo wis je met een druk op Backspace alles en begin je opnieuw. WPF
    /// heeft op dat moment zelf al een woord geselecteerd; die selectie
    /// overschrijven we hier.
    /// </summary>
    private void QueryBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        QueryBox.SelectAll();

    /// <summary>
    /// Het tandwieltje naast de zoekbalk: de instellingen van deze zoekterm.
    /// Staat er al een zoekopdracht met precies die term, dan bewerken we die;
    /// anders wordt er een nieuwe gemaakt met wat er nu op het scherm staat —
    /// de aangevinkte sites en hun filters gaan dus mee.
    /// </summary>
    private void SearchSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();

        // De geopende zoekopdracht enkel als de zoekterm nog klopt. Wie na een bewaarde
        // zoekopdracht een ander woord typte, kreeg anders de instellingen van de vorige,
        // en na Bewaren stond dat oude woord weer in de balk.
        var bestaand = IsActieveZoekterm(query)
            ? _activeSearch
            : _saved.FirstOrDefault(sv => string.Equals(sv.Query, query, StringComparison.OrdinalIgnoreCase));

        if (bestaand is not null)
        {
            OpenSearchSettings(bestaand);
            return;
        }

        var nieuw = new SavedSearch
        {
            Query = query,
            SiteSettings = _tabs.Where(t => !t.IsAll).Select(SiteSetting.FromTab).ToList()
        };

        OpenSearchSettings(nieuw);
    }

    private void NotifyMenu_Click(object sender, RoutedEventArgs e)
    {
        var window = new NotifySettingsWindow().Boven(this);
        window.ShowDialog();
    }

    /// <summary>
    /// Het vergrootglas naast de zoekbalk - of de stopknop, wanneer er al een zoekopdracht van
    /// het scherm loopt. Eén knop op één plaats, want daar staat je muis al, en het pictogram
    /// zegt welke van de twee het nu is.
    /// </summary>
    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stoppen is null)
        {
            _ = RunSearchAsync();
            return;
        }

        // De sites moeten hun lopende verzoek nog afmaken; bij Facebook kan dat een paar
        // seconden scrollen zijn. Daarom zegt de statusregel meteen dat het onderweg is, en
        // gaat de knop uit - twee keer stoppen bestaat niet.
        StatusText.Text = "Stoppen...";
        SearchButton.IsEnabled = false;
        _stoppen.Cancel();
    }

    /// <summary>
    /// Het scherm weer in rust na een zoekopdracht, hoe ze ook afliep: geen wieltje meer, en
    /// het vergrootglas in plaats van de stopknop.
    /// </summary>
    private void ZoekenGedaan()
    {
        _zoektHandmatig = false;
        Spinner.Visibility = Visibility.Collapsed;

        _stoppen?.Dispose();
        _stoppen = null;

        SearchButton.Tag = null;
        SearchButton.IsEnabled = true;
    }

    // ==================== resultaten over pagina's verdelen ====================

    /// <summary>Hoeveel zoekertjes er op één pagina passen.</summary>
    private int PaginaGrootte => Math.Max(10, AppSettings.Current.PageSize);

    /// <summary>
    /// Zet de juiste schijf van de resultaten op het scherm en werkt de pager bij.
    ///
    /// Wordt aangeroepen na elke verversing van de weergave: er kunnen zoekertjes
    /// bijgekomen zijn terwijl je naar pagina één kijkt, en dan groeit het aantal
    /// pagina's mee zonder dat je iets merkt.
    /// </summary>
    private void ToonPagina()
    {
        if (!_ready) return;

        var alles = _resultsView.Cast<Listing>().ToList();
        var paginas = Math.Max(1, (int)Math.Ceiling(alles.Count / (double)PaginaGrootte));

        // Blijf op een bestaande pagina. Klik je iets weg op de laatste pagina,
        // dan kan die verdwijnen.
        _pagina = Math.Max(0, Math.Min(_pagina, paginas - 1));

        var schijf = alles.Skip(_pagina * PaginaGrootte).Take(PaginaGrootte).ToList();

        // Enkel aanpassen wat er veranderd is, in plaats van alles te wissen en opnieuw
        // toe te voegen. Wissen geeft een Reset, en daarop gooide het raster al zijn
        // kaarten weg en sprong het terug naar boven - bij elke levering van een site,
        // dus ook terwijl je al door pagina één aan het scrollen was. Staat een
        // zoekertje verderop al, dan zijn de tussenliggende van de pagina verdwenen;
        // anders is het nieuw en komt het op zijn plaats. Hoogstens tweehonderd
        // zoekertjes, dus dit zoeken kost niets.
        for (var i = 0; i < schijf.Count; i++)
        {
            if (i < _zichtbaar.Count && ReferenceEquals(_zichtbaar[i], schijf[i])) continue;

            var verderop = -1;
            for (var j = i + 1; j < _zichtbaar.Count; j++)
            {
                if (!ReferenceEquals(_zichtbaar[j], schijf[i])) continue;
                verderop = j;
                break;
            }

            if (verderop > 0)
            {
                for (var k = verderop - 1; k >= i; k--) _zichtbaar.RemoveAt(k);
            }
            else
            {
                _zichtbaar.Insert(i, schijf[i]);
            }
        }

        while (_zichtbaar.Count > schijf.Count) _zichtbaar.RemoveAt(_zichtbaar.Count - 1);

        BouwPager(paginas);
        UpdateNieuwSchakelaar();
        VulEinddatumsAan();
    }

    /// <summary>
    /// De schakelaar "Enkel nieuwe": zichtbaar zodra er op deze tab iets staat wat je nog niet
    /// bekeek, of zolang hij aan staat - anders kan je hem niet meer uitzetten op een tab
    /// zonder nieuwe. Het getal telt binnen de tab en de prijs, net als de lijst eronder.
    /// </summary>
    private void UpdateNieuwSchakelaar()
    {
        var aantal = _results.Count(l => l.IsNew && HoortInHuidigeTab(l));

        NewOnlyButton.Visibility = aantal > 0 || _enkelNieuw ? Visibility.Visible : Visibility.Collapsed;
        NewOnlyButton.IsChecked = _enkelNieuw;
        NewOnlyText.Text = $"Enkel nieuwe ({aantal})";
    }

    /// <summary>De schakelaar "Enkel nieuwe" aan- of uitgezet: opnieuw filteren, vanaf pagina één.</summary>
    private void NewOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        _enkelNieuw = NewOnlyButton.IsChecked == true;
        _pagina = 0;

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();
    }

    /// <summary>
    /// Haalt op de achtergrond de sluitingsdatum op van de veilingkavels die nu op het
    /// scherm staan. Enkel van wat je ziet: die datum staat bij AlleVeilingen op de pagina
    /// van het kavel zelf, dus het is één verzoek per kavel. Voor alle vijfhonderd
    /// zoekertjes van een zoekopdracht zou dat vijfhonderd verzoeken zijn. Behalve bij de
    /// volgorde "Veiling die het eerst afloopt": die kan niet zonder het einde van elk kavel.
    ///
    /// Wat al opgehaald is, onthoudt <see cref="DetailFetcher"/>, dus heen en weer bladeren
    /// kost niets. Bij elke nieuwe pagina wordt het vorige stilgelegd: die kavels staan dan
    /// niet meer in beeld.
    /// </summary>
    private void VulEinddatumsAan()
    {
        _einddatums?.Cancel();
        _einddatums?.Dispose();
        _einddatums = new CancellationTokenSource();

        var token = _einddatums.Token;
        var sites = _store.Sites;

        // Bij "Veiling die het eerst afloopt" heeft de volgorde het einde van élk kavel in
        // de lijst nodig, niet enkel van wat nu in beeld staat. Anders komen de kavels zonder
        // datum achteraan, raken ze nooit in beeld, en krijgen ze dus nooit een datum.
        var opEinde = _sort == ListingSort.EndingSoonest;
        var alles = _resultsView.Cast<Listing>().ToList();
        var kavels = opEinde ? alles : _zichtbaar.ToList();

        // De API van een site (Catawiki: één verzoek per 24 kavels) is goedkoop genoeg voor
        // álle zoekertjes, maar ze loopt via de brug. Zolang er gezocht wordt, heeft de
        // zoekopdracht die brug nodig; na het zoeken komt hier vanzelf nog een beurt
        // (ToonPagina in RunSearchAsync), en dan is de slotbeurt vrij.
        var apiMag = SearchRunner.Gate.CurrentCount > 0;

        _ = Task.Run(async () =>
        {
            try
            {
                var api = apiMag ? DetailFetcher.FillFromApiAsync(alles, sites, token) : Task.FromResult(0);
                var paginas = DetailFetcher.FillAsync(kavels, sites, token);
                var aangevuld = (await Task.WhenAll(api, paginas)).Sum();

                // Zijn er datums bijgekomen, dan staan die kavels nog op de verkeerde plaats:
                // één keer opnieuw op volgorde, als alles binnen is, en niet bij elke datum -
                // anders springt de lijst voortdurend. De volgende ronde vindt alles in het
                // geheugen en vult niets meer aan, dus dit loopt niet rond.
                if (opEinde && aangevuld > 0 && !token.IsCancellationRequested)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_sort != ListingSort.EndingSoonest) return;

                        PasSorteringToe();   // een nieuwe vergelijking, met het uur van nu
                        ToonPagina();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Volgende pagina, andere zoekopdracht: dit hoeft niet af.
            }
            catch (Exception ex)
            {
                Log.Write("einddatums ophalen mislukt - " + ex.Message);
            }
        }, token);
    }

    private CancellationTokenSource? _einddatums;

    /// <summary>Hoeveel paginanummers er hoogstens naast elkaar staan.</summary>
    private const int PagerBreedte = 7;

    /// <summary>
    /// Vult de pager met een venster van hoogstens zeven nummers rond de open
    /// pagina. Ga je vooruit, dan schuift dat venster mee: eerst 1 tot 7, daarna
    /// komt 8 erbij en valt 1 weg. Alle pagina's tonen zou bij vijftien pagina's
    /// al een lint dwars over de kopbalk geven.
    /// </summary>
    private void BouwPager(int paginas)
    {
        // Twee keer dezelfde pager: een bij de filterknoppen en een onder de
        // resultaten. Wie gescrold heeft, hoeft dan niet terug naar boven.
        foreach (var balk in new[] { Pager, PagerOnder })
        {
            balk.Children.Clear();
            balk.Visibility = paginas > 1 ? Visibility.Visible : Visibility.Collapsed;

            if (paginas <= 1) continue;

            balk.Children.Add(PagerKnop("\u2039", _pagina - 1, _pagina > 0));

            var eerste = Math.Max(0, Math.Min(_pagina - PagerBreedte / 2, paginas - PagerBreedte));
            var laatste = Math.Min(paginas - 1, eerste + PagerBreedte - 1);

            for (var p = eerste; p <= laatste; p++)
                balk.Children.Add(PagerKnop((p + 1).ToString(), p, true, p == _pagina));

            balk.Children.Add(PagerKnop("\u203a", _pagina + 1, _pagina < paginas - 1));
        }
    }

    private Button PagerKnop(string tekst, int naar, bool bruikbaar, bool open = false)
    {
        var knop = new Button
        {
            Content = tekst,
            Style = (Style)FindResource("PagerButton"),
            IsEnabled = bruikbaar,
            Tag = open ? "open" : null
        };

        knop.Click += (_, _) => GaNaarPagina(naar);
        return knop;
    }

    private void GaNaarPagina(int pagina)
    {
        if (pagina == _pagina) return;

        _pagina = pagina;
        ToonPagina();

        // Bovenaan beginnen: je verwacht de eerste van de nieuwe pagina te zien,
        // niet de plek waar je op de vorige stond.
        if (ResultsList.Items.Count > 0) ResultsList.ScrollIntoView(ResultsList.Items[0]);
    }

    /// <summary>Het aantal per pagina wijzigen; we springen dan terug naar pagina één.</summary>
    private void PageSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton knop || knop.Tag is not string tag) return;
        if (!int.TryParse(tag, out var grootte)) return;

        AppSettings.Current.PageSize = grootte;
        AppSettings.Current.Save();

        _pagina = 0;
        ToonPagina();
    }

    /// <summary>
    /// Hoort dit zoekertje op het scherm? Enkel dat van de open tab, en enkel
    /// binnen de prijsgrenzen van díe tab. Zo werkt de prijsfilter meteen op wat
    /// er al staat: niets wordt weggegooid, het wordt enkel niet getoond.
    /// </summary>
    private bool ZichtbaarInHuidigeTab(object item) =>
        item is Listing listing && HoortInHuidigeTab(listing) && (!_enkelNieuw || listing.IsNew);

    /// <summary>
    /// Staat de schakelaar "Enkel nieuwe" aan? Dan toont de lijst enkel wat je nog niet
    /// bekeek (<see cref="Listing.IsNew"/>). Aan na een klik op de teller van een
    /// zoekopdracht, uit bij elke nieuwe zoekopdracht.
    /// </summary>
    private bool _enkelNieuw;

    /// <summary>
    /// De tab en de prijs, zonder de schakelaar "Enkel nieuwe": daarmee telt de
    /// schakelaar hoeveel nieuwe er op deze tab staan.
    /// </summary>
    private bool HoortInHuidigeTab(Listing listing)
    {
        if (_active is null) return false;

        // Op "Alles" tellen alle sites mee; op een sitetab enkel die ene.
        if (!_active.IsAll && listing.Source != _active.Name) return false;

        return HoortBijZoekopdracht(listing);
    }

    /// <summary>
    /// Hoort dit zoekertje bij de zoekopdracht zelf: binnen de prijsgrens van zijn eigen site, en
    /// door de verfijning? Wat hier buiten valt, telt niet mee in de teller, wordt niet bewaard en
    /// wordt niet als gezien onthouden - precies zoals bij de planner
    /// (<see cref="SearchRunner.RunAsync"/>, <c>BinnenPrijs</c>).
    ///
    /// Tot 22 september 2026 deed het scherm dat anders: het telde en bewaarde ook wat buiten de
    /// prijs viel, en toonde het enkel niet. Bij Zoekopdrachten stond dan "12 nieuw" terwijl de
    /// schakelaar erna "Enkel nieuwe (8)" zei, en wat je nooit te zien kreeg, gold toch als
    /// bekeken. Zo koos de eigenaar het: buiten je prijsgrens bestaat niet voor de zoekopdracht.
    /// Dit speelt enkel bij sites die zelf niet op prijs filteren (AlleVeilingen, Facebook,
    /// Kleinanzeigen); bij de rest komt zo'n zoekertje niet eens binnen.
    ///
    /// De prijsgrens hoort bij de site die hem opgaf, dus die wordt per zoekertje opgezocht bij
    /// zijn eigen tab - anders zou een grens van de ene site die van de andere overschrijven.
    /// </summary>
    private bool HoortBijZoekopdracht(Listing listing)
    {
        if (_activeSearch is not null && !_activeSearch.Matches(listing)) return false;

        var bron = _tabs.FirstOrDefault(t => !t.IsAll && t.Name == listing.Source);
        if (bron is null) return true;

        var filters = bron.Filters;

        // Een zoekertje zonder prijs blijft staan: bij 2dehands betekent een lege prijs
        // "bieden" of "zie beschrijving", en dat sluit je niet uit met een grens.
        if (filters.PriceMin is { } min && listing.Price < min) return false;
        if (filters.PriceMax is { } max && listing.Price > max) return false;

        return true;
    }

    /// <summary>
    /// Wat er nodig is om te kunnen zoeken. Null wanneer het niet kan - de reden staat dan
    /// al in de statusregel, want die zegt wat je eraan doet ("Kies eerst welke sites
    /// meezoeken") en niet enkel dat het misging.
    /// </summary>
    private sealed record Zoekklaar(string Query, List<SiteTab> Sites, string ZonderTerm);

    /// <summary>
    /// Het eerste van de drie stukken van <see cref="RunSearchAsync"/>: voorbereiden.
    /// Hier staat enkel wat er nog NIET gebeurd is - geen slot, geen token, geen stopknop -
    /// zodat elke uitgang hier gewoon mag teruggeven zonder iets op te ruimen.
    /// </summary>
    private Zoekklaar? ZoekenVoorbereiden()
    {
        var query = QueryBox.Text.Trim();

        // Een andere zoekterm dan die van de geopende bewaarde zoekopdracht: dan staat dit
        // zoeken daar los van. Die controle stond enkel bij het vergrootglas, dus met Enter
        // kwamen de resultaten van "fiets" in de zoekopdracht "marantz" terecht - als "al
        // gezien", met een nieuw tijdstip dat de planner verzette, en met de fouten van
        // "fiets" over die van "marantz" heen. Hier geldt het voor elke weg naar zoeken.
        if (_activeSearch is not null && !IsActieveZoekterm(query)) _activeSearch = null;

        var searching = _tabs.Where(t => t.IsEnabled && !t.IsAll).ToList();
        if (searching.Count == 0)
        {
            StatusText.Text = "Kies eerst welke sites meezoeken, met 'Sites kiezen' boven de resultaten.";
            return null;
        }

        _heeftGezocht = true;

        // Zoeken zonder zoekterm kan enkel op sites die het aankunnen. Bij AutoScout24 is dat
        // de gewone gang van zaken: daar is het zoekwoord het merk, en wie niet merkgebonden
        // zoekt zet enkel filters. De rest zou van een lege term hun hele catalogus maken, dus
        // die slaan we over.
        //
        // De runner heeft dezelfde regel, maar het scherm beslist het hier al. Twee redenen: de
        // tekst mag zeggen wat je eraan doet, en een zoekopdracht die bij de runner niet kan
        // draaien krijgt wél een tijdstip - dat verzet de volgende geplande beurt, terwijl er
        // met de zoekopdracht zelf niets mis is.
        var zonderTerm = "";

        if (query.Length == 0)
        {
            var kunnen = searching.Where(t => t.Def!.AllowsEmptyQuery).ToList();

            if (kunnen.Count == 0)
            {
                StatusText.Text = searching.Count == 1
                    ? $"{searching[0].Name} heeft een zoekterm nodig."
                    : "Typ een zoekterm. Geen van de aangevinkte sites kan zoeken op filters alleen.";
                return null;
            }

            // Wel zeggen wat er overgeslagen wordt, anders lijkt het alsof die
            // sites niets gevonden hebben.
            if (kunnen.Count < searching.Count)
                zonderTerm = $" Alleen {string.Join(", ", kunnen.Select(t => t.Name))} " +
                             "kan zoeken zonder zoekterm; de rest is overgeslagen.";

            searching = kunnen;
        }

        return new Zoekklaar(query, searching, zonderTerm);
    }

    /// <summary>
    /// Zelf zoeken: het vergrootglas, Enter, een filterpopup die sluit, of een bewaarde
    /// zoekopdracht openen waarvan de vorige resultaten niet meer te vinden zijn.
    ///
    /// Sinds 23 september 2026 zoekt het scherm niet meer zelf. Het zet klaar waarmee gezocht
    /// wordt - de geopende bewaarde zoekopdracht, of een tijdelijke uit de tabs - en laat
    /// <see cref="SearchRunner.RunAsync"/> het werk doen, net als de planner. Wat hier overblijft
    /// is tonen: de tabs, de statusregel, <em>Recent</em> en het bewaren van de uitkomst.
    ///
    /// Daarvoor stond dezelfde zoeklus twee keer in de app, en moest elke regel dus twee keer
    /// geschreven worden. Dat kostte in september 2026 al twee keer werk ("nieuw tot je kijkt",
    /// de nieuwe rem op het aantal) en gaf stille verschillen: de prijsgrens die op het scherm
    /// anders telde dan bij de planner, en filters die je wijzigde en die de planner niet kende.
    /// Zie punt 9 in docs/volgende-stappen.md.
    /// </summary>
    private async Task RunSearchAsync()
    {
        // Er loopt er al een van het scherm. Enter, een filterpopup die sluit en het openen van
        // een zoekopdracht komen hier allemaal binnen, en tot nu startten die gewoon een tweede
        // beurt die dan op het slot van de eerste bleef wachten - dezelfde sites nog eens af,
        // zonder dat je erom vroeg. Nu is er er één tegelijk, en dat moet ook: er is één
        // stopknop en één CancellationTokenSource.
        if (_stoppen is not null) return;

        if (ZoekenVoorbereiden() is not { } gereed) return;

        var (query, searching, zonderTerm) = gereed;

        TabSearch.IsChecked = true;

        _zoektHandmatig = true;
        Spinner.Visibility = Visibility.Visible;
        StatusText.Text = "Bezig met zoeken...";

        // Vanaf hier kan je de beurt afbreken, en wordt het vergrootglas dus een stopknop.
        // Het wachten op het slot hoort er mee bij: daar kan je het langst staan kijken.
        _stoppen = new CancellationTokenSource();
        var stop = _stoppen.Token;
        SearchButton.Tag = "stop";

        // Hetzelfde slot als de planner. Draait er op de achtergrond net een zoekopdracht, dan
        // wachten we die af: de brug heeft één wachtrij en twee Playwright-sessies op hetzelfde
        // profiel botsen. Het scherm neemt het slot zelf (slotGenomen), want het wil dit kunnen
        // zeggen, en het houdt het vast tot de resultaten bewaard zijn - zie finally.
        if (SearchRunner.Gate.CurrentCount == 0)
            StatusText.Text = "Wachten tot de zoekopdracht op de achtergrond klaar is...";

        try
        {
            await SearchRunner.Gate.WaitAsync(stop);
        }
        catch (OperationCanceledException)
        {
            // Gestopt terwijl we nog op de beurt op de achtergrond wachtten. Er is niets
            // gebeurd, en het slot is nooit van ons geweest: dus ook niet vrijgeven.
            StatusText.Text = "Gestopt; er was nog niets gezocht.";
            ZoekenGedaan();
            return;
        }

        // Waarmee er gezocht wordt. Staat er een bewaarde zoekopdracht open, dan is zij het, met
        // de sites en filters van het scherm erin: anders zoekt de planner straks met de oude
        // waarden verder (NeemSchermfiltersOver). Anders een tijdelijke zoekopdracht zonder Id,
        // en die schrijft niets weg en markeert niets als nieuw.
        var overgenomen = _activeSearch is null ? "" : NeemSchermfiltersOver(_activeSearch);

        var zoekopdracht = _activeSearch ?? new SavedSearch
        {
            Query = query,
            SiteSettings = searching.Select(SiteSetting.FromTab).ToList()
        };

        bool HeeftTab(string site) =>
            _tabs.Any(t => !t.IsAll && string.Equals(t.Name, site, StringComparison.OrdinalIgnoreCase));

        // Aangevinkt in de zoekopdracht, maar niet meer in Sites beheren. Die hebben geen tab om
        // een waarschuwingsteken op te zetten, dus ze horen in de statusregel. Tot deze
        // verbouwing zweeg het scherm erover en meldde enkel de planner het.
        var verdwenen = zoekopdracht.SiteSettings
            .Where(s => s.Enabled && !HeeftTab(s.Site))
            .Select(s => s.Site)
            .ToList();

        // Een nieuwe zoekopdracht toont alles, ook als je daarnet enkel de nieuwe bekeek.
        _enkelNieuw = false;

        _results.Clear();
        _zichtbaar.Clear();
        _pagina = 0;
        _nieuwBovenaan = 0;

        foreach (var tab in _tabs)
        {
            tab.ResultCount = 0;
            tab.ErrorText = "";
        }

        if (query.Length > 0)
        {
            _history.AddRecent(query);
            LoadRecent();
        }

        // Van hoeveel sites er een melding komt: de aangevinkte tabs, plus de verdwenen sites -
        // ook die meldt de runner.
        var verwacht = searching.Count + verdwenen.Count;
        var klaar = 0;

        // Eén site is klaar. Haar teller staat er al (die loopt mee met de leveringen); hier
        // komt haar tijd in de statusregel en haar fout op het waarschuwingsteken van haar tab.
        void SiteIsKlaar(SiteKlaar melding)
        {
            klaar++;

            var tab = _tabs.FirstOrDefault(t => !t.IsAll && t.Name == melding.Site);
            if (tab is not null && melding.Fout is not null) tab.ErrorText = melding.Fout;

            StatusText.Text = melding.Fout is null
                ? $"({klaar}/{verwacht}) {melding.Site}: {melding.Aantal} gevonden in {melding.Duur.TotalSeconds:F1}s"
                : $"({klaar}/{verwacht}) {melding.Site}: {melding.Fout}";
        }

        // Het bewaren van de resultaten loopt op de achtergrond; in finally wordt erop gewacht.
        var bewaren = Task.CompletedTask;

        try
        {
            StatusText.Text = searching.Count == 1
                ? $"{searching[0].Name} doorzoeken..."
                : $"{searching.Count} sites doorzoeken...";

            var outcome = await _runner.RunAsync(
                zoekopdracht,
                markSeen: true,
                status: new DirecteMelder(tekst => StatusText.Text = tekst),
                delivered: ToonLading,
                tussentijds: true,
                siteKlaar: SiteIsKlaar,
                slotGenomen: true,
                logNaam: "zoeken",
                ct: stop);

            // De teller, het tijdstip en wat er per site misliep staan al in de zoekopdracht:
            // dat deed de runner, precies zoals bij een geplande beurt.
            if (_activeSearch is not null)
            {
                // Werd er niet gezocht, dan is de lege lijst geen uitkomst en blijven de vorige
                // resultaten staan.
                if (outcome.NotRunReason is null)
                {
                    _lastOutcomes[_activeSearch.Id] = outcome.All;
                    bewaren = BewaarUitkomstAsync(_activeSearch.Id, outcome.All.ToList());
                }

                UpdateSchedulerHint();
            }

            if (outcome.NotRunReason is not null)
            {
                StatusText.Text = $"Er is niet gezocht: {outcome.NotRunReason}.";
                return;
            }

            // Kort houden: dit is één regel onderaan het scherm. Wat er per site misliep, staat
            // bij die site, als waarschuwingsteken op zijn tab.
            var brugOvergeslagen = outcome.Bridge == BridgeStatus.Ready
                ? new List<SiteTab>()
                : searching.Where(t => t.Def!.UseBridge).ToList();

            // Wat er overblijft aan echte fouten: de brugsites, de verdwenen sites en de sites
            // die dit zoekwoord niet kennen staan ook in SiteErrors, maar die krijgen hieronder
            // elk hun eigen zin.
            var mislukt = outcome.SiteErrors.Count - brugOvergeslagen.Count - verdwenen.Count
                          - outcome.QueryNotSupported.Count;

            var message = $"{_results.Count} resultaten van {searching.Count} site(s)";

            if (_activeSearch is not null) message += $" · {_activeSearch.NewCount} nieuw";
            message += ".";

            if (brugOvergeslagen.Count > 0)
                message += $" {string.Join(" en ", brugOvergeslagen.Select(t => t.Name))} overgeslagen: " +
                           ChromeLauncher.Describe(outcome.Bridge);

            if (verdwenen.Count > 0)
                message += $" {string.Join(" en ", verdwenen)} " +
                           $"{(verdwenen.Count == 1 ? "bestaat" : "bestaan")} niet meer in Sites beheren.";

            // Geen mislukking maar een antwoord: AutoScout24 zoekt op automerk, dus "cd speler"
            // bestaat daar niet. Zonder deze zin stond er "1 site mislukte" bij elke zoekopdracht
            // die niet over auto's gaat.
            if (outcome.QueryNotSupported.Count > 0)
                message += $" {string.Join(" en ", outcome.QueryNotSupported)} " +
                           $"{(outcome.QueryNotSupported.Count == 1 ? "kent" : "kennen")} dit zoekwoord niet.";

            if (mislukt > 0)
                message += mislukt == 1
                    ? " 1 site mislukte; zie het waarschuwingsteken op de tab."
                    : $" {mislukt} sites mislukten; zie het waarschuwingsteken op de tabs.";

            message += zonderTerm;

            // Stil bewaren is even verwarrend als stil vergeten, dus het staat erbij.
            if (overgenomen.Length > 0 && _activeSearch is not null)
                message += $" De gewijzigde {overgenomen} zijn bewaard in '{_activeSearch.Name}'.";

            StatusText.Text = message;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Je drukte op de stopknop. Wat al binnen was, blijft gewoon staan: de sites die
            // klaar waren, leverden hun zoekertjes al af. De zoekopdracht zelf blijft
            // onaangeroerd - de runner komt na het afbreken niet meer aan het wegschrijven toe,
            // dus haar tijdstip, haar teller en "al gezien" blijven die van de vorige beurt.
            // Dat is ook de bedoeling: een halve beurt is geen beurt.
            Log.Write($"zoeken: '{zoekopdracht.Name}' gestopt door de gebruiker");

            StatusText.Text = _results.Count == 0
                ? "Gestopt; er was nog niets binnen."
                : $"Gestopt. {_results.Count} resultaten van de sites die wel klaar waren.";
        }
        finally
        {
            await ZoekenAfrondenAsync(zoekopdracht, bewaren, overgenomen);
        }
    }

    /// <summary>
    /// Het laatste van de drie stukken van <see cref="RunSearchAsync"/>: afronden. Draait
    /// altijd, ook na een fout of na de stopknop - vandaar dat de aanroeper het in een
    /// finally zet.
    /// </summary>
    private async Task ZoekenAfrondenAsync(SavedSearch zoekopdracht, Task bewaren, string overgenomen)
    {
        // HET SLOT MOET ALTIJD TERUG, en daarom staat het bewaren in een eigen try. Het is
        // een semafoor van één, gedeeld met de planner en de prijsindicatie: komt
        // Gate.Release() niet aan de beurt, dan wacht vanaf dat moment ELKE zoekopdracht
        // voor altijd, en lijkt de app gewoon stuk tot ze herstart wordt. Er staan hier twee
        // dingen die kunnen falen - een UPDATE op een bezette databank, en het bewaren op de
        // achtergrond (een volle schijf) - en die mogen het slot niet meenemen.
        try
        {
            // Wat we van het scherm overnamen hoort in de databank, ook als de beurt gestopt
            // of niet uitgevoerd werd: je wijzigde die filters, en dat staat los van of er
            // resultaten kwamen. Bij een gelukte beurt schreef de runner ze al weg; nog eens
            // schrijven is één UPDATE te veel en verder onschuldig.
            if (overgenomen.Length > 0 && _activeSearch is not null)
            {
                _history.Update(_activeSearch);
                Log.Write($"zoeken: de gewijzigde {overgenomen} zijn bewaard in '{_activeSearch.Name}'");
            }

            // Pas het slot vrijgeven als de resultaten bewaard zijn: zo schrijft de volgende
            // zoekopdracht nooit tegelijk. Het scherm blijft intussen gewoon reageren, en wat
            // hierboven nog op _activeSearch werkte, liep al voor deze wachttijd.
            await bewaren;
        }
        catch (Exception fout)
        {
            // Mislukt bewaren is erg genoeg, maar het is geen reden om de app te laten
            // hangen. Het staat in het logboek en achter de statusregel, en de volgende
            // zoekopdracht kan gewoon draaien.
            Log.Write($"zoeken: het bewaren van '{zoekopdracht.Name}' mislukte - {fout.Message}");
            StatusText.Text += $" (het bewaren mislukte: {FriendlyError.Describe(fout)})";
        }
        finally
        {
            SearchRunner.Gate.Release();
        }

        ZoekenGedaan();

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();
    }

    /// <summary>
    /// Een melder die rechtstreeks doorgeeft. <see cref="Progress{T}"/> post zijn oproepen naar
    /// de schermdraad, en dan kan "Catawiki doorzoeken..." achteraf over "(2/3) Catawiki: 100
    /// gevonden in 6,2s" heen vallen. De runner meldt al vanaf de schermdraad, dus er valt hier
    /// niets te posten.
    /// </summary>
    private sealed class DirecteMelder : IProgress<string>
    {
        private readonly Action<string> _toon;

        public DirecteMelder(Action<string> toon) => _toon = toon;

        public void Report(string value) => _toon(value);
    }
}

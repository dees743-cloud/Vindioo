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

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>Alle gevonden zoekertjes van alle sites samen.</summary>
    private readonly ObservableCollection<Listing> _results = new();

    /// <summary>Wat er van _results door de filters komt: enkel de open tab.</summary>
    private ICollectionView _resultsView = null!;

    /// <summary>
    /// Wat er werkelijk op het scherm staat: één pagina uit _resultsView.
    ///
    /// Waarom een eigen lijst en niet nog een filter: een filter beslist per
    /// zoekertje los van de rest, en "zit dit op pagina drie" is juist een vraag
    /// over de VOLGORDE van alles samen. Die is pas te beantwoorden als de filters
    /// en de sortering al gedraaid hebben. Vandaar: eerst filteren en sorteren zoals
    /// altijd, en daarna er een schijf uit nemen.
    /// </summary>
    private readonly ObservableCollection<Listing> _zichtbaar = new();

    /// <summary>Welke pagina er open staat, geteld vanaf nul.</summary>
    private int _pagina;

    private readonly ObservableCollection<Listing> _favorites = new();
    private readonly ObservableCollection<RecentSearch> _recent = new();
    private readonly ObservableCollection<SavedSearch> _saved = new();

    /// <summary>Eén tabblad per site, met zijn eigen filters.</summary>
    private readonly ObservableCollection<SiteTab> _tabs = new();

    /// <summary>
    /// Wat er van _tabs in de tabstrip staat: "Alles" en de sites die meezoeken.
    /// Een site die uitgevinkt staat heeft geen resultaten en dus niets te tonen;
    /// hem toch een tab geven kost enkel breedte, en die is daar schaars. Zo groeit
    /// de strip mee met wat je gebruikt en niet met hoeveel sites er bestaan.
    /// </summary>
    private ICollectionView _tabsView = null!;

    /// <summary>Het chipje achteraan de tabstrip om sites aan of uit te vinken.</summary>
    private readonly SiteChooser _chooser = new();

    private readonly SiteStore _store = new();
    private readonly HistoryStore _history = new();

    /// <summary>Voert een zoekopdracht uit zonder het scherm nodig te hebben.</summary>
    private readonly SearchRunner _runner;

    /// <summary>Kijkt welke zoekopdrachten aan de beurt zijn en voert ze uit.</summary>
    private readonly SearchScheduler _scheduler;

    /// <summary>Het pictogram naast de klok. Pas na het laden van het venster gemaakt.</summary>
    private TrayIcon? _tray;

    /// <summary>
    /// Wat een geplande zoekopdracht als laatste opleverde, per zoekopdracht.
    /// Zo kan je die resultaten bekijken zonder opnieuw te moeten zoeken — en dat
    /// scheelt bij de brug al gauw een halve minuut.
    /// </summary>
    private readonly Dictionary<int, List<Listing>> _lastOutcomes = new();

    /// <summary>
    /// De zoekopdracht van de planner die nu rechtstreeks op het scherm binnenloopt,
    /// of null. Enkel gezet wanneer het venster in beeld stond toen ze begon.
    /// </summary>
    private int? _plannerOpScherm;

    /// <summary>Waar het volgende nieuwe zoekertje van die beurt komt: bovenaan, na de vorige nieuwe.</summary>
    private int _plannerNieuwBovenaan;

    /// <summary>Is de gebruiker zelf aan het zoeken? Dan zwijgt de planner op het scherm.</summary>
    private bool _zoektHandmatig;

    /// <summary>
    /// Is er sinds het opstarten al iets gezocht of getoond? Bepaalt of een lege lijst
    /// "typ hierboven wat je zoekt" zegt of "niets gevonden". Zie LeegTekst.
    /// </summary>
    private bool _heeftGezocht;

    /// <summary>De volgorde waarin de resultaten staan; geldt voor de hele app.</summary>
    private ListingSort _sort = ListingSort.Default;

    /// <summary>De sleutels van alles wat als favoriet staat.</summary>
    private HashSet<string> _favoriteKeys = new();

    /// <summary>Het tabblad dat open staat; bepaalt welke resultaten je ziet.</summary>
    private SiteTab? _active;

    /// <summary>De zoekopdracht die nu open staat, of null bij een losse zoekopdracht.</summary>
    private SavedSearch? _activeSearch;

    /// <summary>
    /// De zoekopdracht waarvan je net zelf op de afspeelknop drukte. Die mag het scherm
    /// overnemen; een beurt die de planner zelf start, enkel als het scherm vrij is.
    /// </summary>
    private int? _meekijkenMet;

    /// <summary>
    /// Staat het venster volledig klaar? De keuzerondjes onderaan sturen hun
    /// Checked al af tijdens het inlezen van de XAML, dus voor de velden hier
    /// bestaan. Zonder deze vlag loopt dat stuk op een lege verwijzing.
    /// </summary>
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        // De tabstrip bevat twee soorten dingen: de tabs zelf (gefilterd, zie
        // _tabsView) en achteraan het chipje om sites te kiezen. Een
        // CompositeCollection zet die in EEN rij, zodat het chipje gewoon meeloopt
        // in het WrapPanel en achter de laatste tab blijft staan - ook als de rij
        // afbreekt. Het chipje bij de tabs in _tabs stoppen zou eenvoudiger lijken,
        // maar dan krijgt elke plek die over "alle tabs behalve Alles" gaat er een
        // uitzondering bij, en dat zijn er een stuk of tien.
        _tabsView = CollectionViewSource.GetDefaultView(_tabs);
        _tabsView.Filter = o => o is SiteTab t && (t.IsAll || t.IsEnabled);

        SiteTabsList.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = _tabsView },
            _chooser
        };
        FavoritesList.ItemsSource = _favorites;
        RecentList.ItemsSource = _recent;
        SavedList.ItemsSource = _saved;

        // De resultatenlijst toont enkel de site waarvan de tab open staat. Dat
        // gebeurt met een filter op de weergave, niet met een tweede lijst: zo
        // blijft er één plaats waar resultaten binnenkomen.
        ResultsList.ItemsSource = _zichtbaar;
        _resultsView = CollectionViewSource.GetDefaultView(_results);
        _resultsView.Filter = ZichtbaarInHuidigeTab;

        _store.Load();
        BuildSiteTabs();

        // Sleutels omzetten voor sites met een IdPattern (zie HistoryStore.ApplyIdPatterns),
        // voor de favorieten hieronder ingelezen worden.
        _history.ApplyIdPatterns(_store.Sites);

        _runner = new SearchRunner(_store, _history);
        _scheduler = new SearchScheduler(_saved, _runner, _history);
        _scheduler.Started += Scheduler_Started;
        _scheduler.Delivered += Scheduler_Delivered;
        _scheduler.Completed += Scheduler_Completed;
        // De planner mag de statusregel enkel gebruiken wanneer de gebruiker zelf
        // niets aan het zoeken is. Anders zie je tijdens je eigen zoekopdracht
        // ineens de voortgang van iets op de achtergrond.
        _scheduler.Status += tekst =>
        {
            if (!_zoektHandmatig) StatusText.Text = tekst;
        };

        _favoriteKeys = _history.GetFavoriteKeys();
        LoadSavedSearches();
        LoadRecent();
        LoadFavorites();

        // Zodra de indeling verandert (venster groter, tab gewisseld, tabs die
        // afbreken naar een tweede rij) moet de omtrek opnieuw berekend worden.
        SearchPanel.LayoutUpdated += (_, _) => UpdateTabFrame();

        ApplyBackgroundPhoto();

        // De gekozen weergave en volgorde overleven nu een herstart.
        SetResultView(AppSettings.Current.ResultView == 1 ? ResultView.Grid : ResultView.List);

        _sort = (ListingSort)AppSettings.Current.Sort;
        PasSorteringToe();

        (_sort switch
        {
            ListingSort.PriceAscending => SortPriceUp,
            ListingSort.PriceDescending => SortPriceDown,
            ListingSort.Newest => SortNewest,
            _ => SortDefault
        }).IsChecked = true;

        _ready = true;
        UpdateEmptyHints();
        UpdateTabFrame();
        UpdateSchedulerHint();

        // Luisteren naar de browserextensie. Is de poort bezet, dan werkt de rest gewoon.
        BridgeServer.Instance.Start();
        if (BridgeServer.Instance.PortBusy)
            StatusText.Text = "Let op: " + ChromeLauncher.Describe(BridgeStatus.PortInUse);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Wie het venster opent, heeft de melding gezien: de tekst bij het pictogram in het
        // systeemvak ("3 nieuw bij ...") bleef anders staan tot de app herstartte.
        Activated += (_, _) => _tray?.SetTooltip("Zentrix");

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Het logo staat gecentreerd in dezelfde rij als de zoekbalk. Onder zo'n 1040
        // beeldpunten breed schoof het over het vergrootglas en het tandwiel - een venster
        // op een half scherm van 1920 is 960 breed. Dan liever geen logo.
        SizeChanged += (_, _) =>
            Logo.Visibility = ActualWidth < 1040 ? Visibility.Collapsed : Visibility.Visible;

        Log.Write("hoofdscherm opgebouwd");
    }

    /// <summary>Ctrl+F: naar de zoekbalk, van waar je ook kwam.</summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;

        TabSearch.IsChecked = true;
        QueryBox.Focus();
        QueryBox.SelectAll();
        e.Handled = true;
    }

    // ==================== op de achtergrond draaien ====================

    /// <summary>
    /// Pas wanneer het venster echt staat: het pictogram in het systeemvak, de
    /// planner, en wat er bij het opstarten moet draaien. Dit in de constructor
    /// doen zou betekenen dat je bij het starten naar een bevroren app kijkt.
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Meteen kunnen typen: de zoekbalk had bij het opstarten geen focus.
        QueryBox.Focus();

        _tray = new TrayIcon(this) { SearchNowRequested = TraySearchNow };

        // Door Windows gestart, of zo ingesteld: meteen naar het systeemvak.
        var stilStarten = AppSettings.Current.StartMinimized ||
                          Environment.GetCommandLineArgs().Any(
                              a => string.Equals(a, "--systeemvak", StringComparison.OrdinalIgnoreCase));

        if (stilStarten) _tray.HideToTray();

        _scheduler.Start();
        UpdateSchedulerHint();

        await _scheduler.RunStartupSearchesAsync();
    }

    /// <summary>
    /// Het kruisje sluit de app niet af maar verbergt haar, zolang dat zo
    /// ingesteld staat. Anders stopt met het venster ook de planner, en dan
    /// heeft een schema van "elk uur" weinig betekenis. Afsluiten doe je via
    /// het menu op het pictogram.
    /// </summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_tray is null) return;

        if (AppSettings.Current.CloseToTray && !_tray.ReallyClosing)
        {
            e.Cancel = true;
            _tray.HideToTray();

            // Eén keer uitleggen waar de app gebleven is, niet bij elke keer sluiten.
            if (!AppSettings.Current.CloseToTrayExplained)
            {
                _tray.ShowBalloon("Zentrix zoekt verder",
                    "Zentrix draait verder op de achtergrond. Je vindt het pictogram rechtsonder bij de klok, " +
                    "soms achter het pijltje. Afsluiten: rechtsklik op het pictogram > Afsluiten.");

                AppSettings.Current.CloseToTrayExplained = true;
                AppSettings.Current.Save();
            }

            return;
        }

        _scheduler.Stop();
        _tray.Dispose();
        _tray = null;
    }

    /// <summary>"Eerstvolgende zoekopdracht nu uitvoeren" uit het menu op het pictogram in het systeemvak.</summary>
    private void TraySearchNow()
    {
        // De zoekopdracht die het eerst aan de beurt is, niet de eerste in de alfabetische lijst.
        var eerste = _saved
            .Where(s => s.Schedule.Mode != ScheduleMode.Off)
            .OrderBy(s => s.Schedule.NextRun(s.LastRun) ?? DateTime.MaxValue)
            .FirstOrDefault() ?? _saved.FirstOrDefault();

        if (eerste is null)
        {
            _tray?.Show();
            StatusText.Text = "Er staan nog geen zoekopdrachten klaar.";
            return;
        }

        _ = _scheduler.RunAsync(eerste);
    }

    /// <summary>
    /// Haalt het venster naar voren, ook vanuit het systeemvak. Een tweede start van de
    /// app roept dit op (zie App.OnStartup): vroeger startte die stilletjes niet, want de
    /// poort van de brug was al bezet, en dan leek het alsof de snelkoppeling niets deed.
    /// </summary>
    public void BrengNaarVoren()
    {
        if (_tray is not null)
        {
            _tray.Show();
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Staat het venster echt in beeld? Verbergen in het systeemvak (Hide) zet
    /// IsVisible op false, maar geminimaliseerd blijft IsVisible true — dat moet
    /// dus apart nagekeken worden.
    /// </summary>
    private bool VensterInBeeld => IsVisible && WindowState != WindowState.Minimized;

    /// <summary>
    /// De planner begint aan een zoekopdracht. Staat het venster in beeld, dan
    /// kijk je mee: het scherm schakelt over naar die zoekopdracht en de
    /// resultaten lopen binnen zoals bij gewoon zoeken. In het systeemvak of
    /// geminimaliseerd blijft het scherm zoals het was; de resultaten worden wel
    /// bewaard en zijn met dubbelklikken op de zoekopdracht op te halen.
    /// </summary>
    private void Scheduler_Started(SavedSearch search)
    {
        // Eerst altijd loslaten. Een vorige beurt die vastliep kwam nooit bij
        // Completed, en zou anders blijven meeschrijven op het scherm.
        _plannerOpScherm = null;

        var gevraagd = _meekijkenMet == search.Id;
        _meekijkenMet = null;

        // Een eigen zoekopdracht gaat voor: die lijst niet overschrijven.
        if (_zoektHandmatig || !VensterInBeeld) return;

        // Het scherm enkel overnemen als het vrij is: je drukte zelf op de afspeelknop van
        // deze zoekopdracht, het scherm toont haar al, of er is nog niets gezocht of
        // ingevuld. Vroeger nam een geplande beurt het scherm altijd over, en dan waren je
        // zoekterm, je vinkjes en je filters weg zonder dat je iets gevraagd had. De
        // resultaten gaan niet verloren: dubbelklikken bij Zoekopdrachten toont ze.
        var toontHaarAl = _activeSearch?.Id == search.Id && IsActieveZoekterm(QueryBox.Text.Trim());
        var leeg = !_heeftGezocht && QueryBox.Text.Trim().Length == 0 &&
                   !_tabs.Any(t => !t.IsAll && t.IsEnabled);

        if (!gevraagd && !toontHaarAl && !leeg) return;

        // Het object uit de lijst nemen: LoadSavedSearches kan de lijst intussen
        // opnieuw ingelezen hebben, en dan is "search" een verouderde kopie.
        var actueel = _saved.FirstOrDefault(s => s.Id == search.Id) ?? search;

        _activeSearch = actueel;
        QueryBox.Text = actueel.Query;
        PasToe(actueel);

        _results.Clear();
        _zichtbaar.Clear();
        _pagina = 0;

        foreach (var tab in _tabs)
        {
            tab.ResultCount = 0;
            tab.ErrorText = "";
        }

        _plannerOpScherm = search.Id;
        _plannerNieuwBovenaan = 0;
        _heeftGezocht = true;

        ToonPagina();
        UpdateEmptyHints();
    }

    /// <summary>
    /// Eén site van de lopende beurt is klaar. Werkt zoals AddBatch bij gewoon
    /// zoeken: nieuwe zoekertjes bovenaan, de rest erachter, en meteen tonen.
    /// </summary>
    private void Scheduler_Delivered(SavedSearch search, IReadOnlyList<Listing> lading)
    {
        if (_plannerOpScherm != search.Id) return;

        // Intussen zelf een zoekopdracht gestart? Dan is het scherm van jou.
        if (_zoektHandmatig)
        {
            _plannerOpScherm = null;
            return;
        }

        foreach (var listing in lading)
        {
            listing.IsFavorite = _favoriteKeys.Contains(listing.Key);

            if (listing.IsNew) _results.Insert(_plannerNieuwBovenaan++, listing);
            else _results.Add(listing);

            var tab = _tabs.FirstOrDefault(t => t.Name == listing.Source);
            if (tab is not null) tab.ResultCount++;
        }

        UpdateEmptyHints();
        ToonPagina();
    }

    /// <summary>
    /// De planner is klaar met een zoekopdracht. De resultaten worden altijd
    /// bewaard, ook als niemand keek; stonden ze op het scherm, dan komt er
    /// nog de laatste hand bij.
    /// </summary>
    private void Scheduler_Completed(SavedSearch search, SearchOutcome outcome)
    {
        // Werd er niet gezocht (geen site aangevinkt, of een site die hernoemd werd), dan
        // is de lege lijst geen uitkomst. De vorige resultaten blijven dan staan.
        if (outcome.NotRunReason is null)
        {
            _lastOutcomes[search.Id] = outcome.All;

            // En op schijf, zodat ze een herstart overleven. Zonder dit krijg je een
            // melding over driehonderd zoekertjes die de app daarna niet meer kan tonen.
            _history.SaveOutcome(search.Id, outcome.All);
        }

        UpdateSchedulerHint();
        UpdateEmptyHints();

        if (_tray is not null && outcome.New.Count > 0)
            _tray.SetTooltip($"Zentrix — {outcome.New.Count} nieuw bij '{search.Name}'");

        if (_plannerOpScherm != search.Id) return;
        _plannerOpScherm = null;

        if (_zoektHandmatig) return;

        foreach (var tab in _tabs.Where(t => !t.IsAll))
            tab.ErrorText = outcome.SiteErrors.GetValueOrDefault(tab.Name, "");

        // Alles is binnen: de weergave opnieuw laten filteren en sorteren.
        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();
    }

    /// <summary>De regel onder "Zoekopdrachten" die zegt wat de planner van plan is.</summary>
    private void UpdateSchedulerHint()
    {
        if (!_ready) return;

        if (!_scheduler.AnyScheduled)
        {
            SchedulerHint.Text = _saved.Count == 0
                ? "Nog niets ingesteld."
                : "Geen enkele zoekopdracht draait vanzelf. Zet een schema met het tandwiel op de kaart van een zoekopdracht.";
            return;
        }

        var volgende = _scheduler.NextDue();

        SchedulerHint.Text = volgende is null
            ? "De planner draait."
            : volgende.Value <= DateTime.Now
                ? "Eerstvolgende beurt: nu meteen."
                : volgende.Value.Date == DateTime.Today
                    ? $"Eerstvolgende beurt vandaag om {volgende:HH:mm}."
                    : $"Eerstvolgende beurt {volgende:dddd d MMMM} om {volgende:HH:mm}.";
    }

    /// <summary>
    /// Legt de achtergrondfoto over het verloop. In code en niet in de XAML,
    /// zodat een ontbrekende of stukke afbeelding de app niet laat vallen: dan
    /// blijft gewoon het verloop uit App.xaml staan.
    /// </summary>
    private void ApplyBackgroundPhoto()
    {
        try
        {
            // OnLoad dwingt af dat de afbeelding hier en nu ingelezen wordt. Zonder
            // dat gebeurt het pas bij het tekenen, en dan valt een fout buiten
            // deze catch en klapt de app alsnog.
            var bron = new BitmapImage();
            bron.BeginInit();
            bron.UriSource = new Uri("pack://application:,,,/Assets/background.png");
            bron.CacheOption = BitmapCacheOption.OnLoad;
            bron.EndInit();
            bron.Freeze();

            BackgroundPhoto.Background = new ImageBrush(bron)
            {
                // Vult het venster zonder te vervormen; wat niet past valt weg.
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
        catch (Exception ex)
        {
            Log.Write("achtergrond: foto niet geladen, verloop blijft staan - " + ex.Message);
        }
    }

    // ---------- sites als tabbladen ----------

    private void BuildSiteTabs()
    {
        var previous = _active?.Def?.Id;

        // Wat er op de tabs stond, per Id van de site (dat blijft gelijk als je een site
        // hernoemt). Na Sites beheren, Site toevoegen of een import werden de tabs vroeger
        // leeg herbouwd: alle sites weer uitgevinkt, en je postcode en prijzen weg.
        var vorige = _tabs
            .Where(t => t.Def is not null)
            .GroupBy(t => t.Def!.Id)
            .ToDictionary(g => g.Key, g => g.First());

        _tabs.Clear();

        // Eerst het overzicht: één tabblad met alles samen, zodat je niet zeven
        // keer moet klikken om te zien wat er binnengekomen is.
        _tabs.Add(SiteTab.Alles());

        // Daarna elke bron uit de sitesmap; de fabriek kiest per site de motor.
        // Alles staat uitgevinkt: je kiest zelf welke sites meezoeken, want elke
        // extra site kost tijd - zeker die via de brug.
        foreach (var site in _store.Sites)
        {
            var tab = new SiteTab(site, SourceFactory.Create(site)) { IsEnabled = false };

            if (vorige.TryGetValue(site.Id, out var oud))
            {
                SiteSetting.FromTab(oud).ApplyTo(tab);
                tab.ResultCount = oud.ResultCount;
                tab.ErrorText = oud.ErrorText;
            }

            // De strip volgt het vinkje, waar dat ook omgezet wordt: in de popup,
            // of wanneer een bewaarde zoekopdracht zijn sites oplegt. Daarom hangt
            // dit aan de SiteTab zelf en niet aan het vinkje in de popup - anders
            // moet elke plek die IsEnabled aanraakt eraan denken.
            tab.PropertyChanged += SiteTab_PropertyChanged;
            _tabs.Add(tab);
        }

        // De lijst in de popup toont ze allemaal, ook de uitgevinkte: dat is juist
        // waar je ze aanzet.
        SitesChoiceList.ItemsSource = _tabs.Where(t => !t.IsAll).ToList();

        _tabsView.Refresh();
        UpdateChooserLabel();

        var restored = previous is null ? null : _tabs.FirstOrDefault(t => t.Def?.Id == previous);
        SetActiveTab(restored ?? _tabs.FirstOrDefault());

        BuildCardsMenu();
    }

    /// <summary>Opent het tabblad van één site en toont enkel zijn resultaten.</summary>
    private void SetActiveTab(SiteTab? tab)
    {
        // Een andere site heeft zijn eigen reeks resultaten; op pagina vier van de
        // vorige beginnen slaat nergens op.
        if (!ReferenceEquals(_active, tab)) _pagina = 0;

        _active = tab;

        foreach (var entry in _tabs) entry.IsActive = ReferenceEquals(entry, tab);

        // De filters in de popups horen bij deze site.
        LocationPopup.DataContext = tab;
        SpecialPopup.DataContext = tab;
        PricePopup.DataContext = tab;
        CountPopup.DataContext = tab;

        UpdateFilterAvailability();

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();
        UpdateTabFrame();
    }

    // ---------- de omtrek rond tabs en pagina ----------

    /// <summary>De maten waarmee de omtrek het laatst getekend is.</summary>
    private (double A, double B, double Top, double Strip, double W, double H) _frame;

    /// <summary>
    /// Tekent de tabstrip en de pagina als EEN doorlopende figuur: de bovenrand
    /// van de pagina, de open tab die daaruit omhoog steekt, en de uitlopen die
    /// links en rechts van die tab naar buiten in de lijn buigen.
    ///
    /// Waarom in een figuur en niet in losse stukken: waar twee vormen tegen
    /// elkaar aan moeten sluiten krijg je altijd een sprongetje of een puntje van
    /// een lijnuiteinde, hoe nauwkeurig je de coordinaten ook kiest. Binnen een
    /// figuur bestaat die naad niet.
    /// </summary>
    private void UpdateTabFrame()
    {
        if (!_ready || TabFrame is null) return;

        var w = SearchPanel.ActualWidth;
        var h = SearchPanel.ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double r = 12;    // straal van de hoeken
        const double f = 10;    // straal van de uitloop naast de open tab

        var inset = TabFrame.StrokeThickness / 2;   // de lijn valt binnen de vorm
        double left = inset, right = w - inset, bottom = h - inset;
        var strip = SiteTabsList.ActualHeight;      // onderkant van de tabstrip

        // Waar staat de open tab?
        double a = 0, b = 0, top = 0;
        var notch = false;

        if (ActiveTabElement() is { } pill && pill.ActualWidth > 0)
        {
            var punt = pill.TransformToAncestor(SearchPanel).Transform(new Point(0, 0));
            a = punt.X;
            b = punt.X + pill.ActualWidth;
            top = punt.Y;

            // Enkel inkepen wanneer de tab echt op de onderste rij staat en er
            // rechts genoeg plaats is voor de uitloop.
            notch = Math.Abs(punt.Y + pill.ActualHeight - strip) < 2
                    && b + f < right - r
                    && a >= left - 2;
        }

        // Niets herberekenen wanneer er niets bewoog: Data toekennen zet een
        // nieuwe lay-outronde in gang, en die roept dit weer aan.
        var nu = (a, b, top, strip, w, h);
        if (nu == _frame) return;
        _frame = nu;

        var vorm = new StreamGeometry();

        using (var g = vorm.Open())
        {
            void Lijn(double x, double y) => g.LineTo(new Point(x, y), true, false);

            void Boog(double x, double y, double straal, SweepDirection kant) =>
                g.ArcTo(new Point(x, y), new Size(straal, straal), 0, false, kant, true, false);

            // De onderkant en de zijkanten van de pagina; overal gelijk.
            void Pagina()
            {
                Lijn(right - r, strip);
                Boog(right, strip + r, r, SweepDirection.Clockwise);
                Lijn(right, bottom - r);
                Boog(right - r, bottom, r, SweepDirection.Clockwise);
                Lijn(left + r, bottom);
                Boog(left, bottom - r, r, SweepDirection.Clockwise);
            }

            if (!notch)
            {
                // Geen open tab op de onderste rij: gewoon een afgerond vlak.
                g.BeginFigure(new Point(left + r, strip), true, true);
                Pagina();
                Lijn(left, strip + r);
                Boog(left + r, strip, r, SweepDirection.Clockwise);
            }
            else if (a <= left + 2)
            {
                // De open tab staat helemaal links. Dan is er geen uitloop nodig:
                // de linkerlijn van de pagina loopt gewoon door tot boven de tab
                // en rondt daar af.
                g.BeginFigure(new Point(left, top + r), true, true);
                Boog(left + r, top, r, SweepDirection.Clockwise);
                Lijn(b - r, top);
                Boog(b, top + r, r, SweepDirection.Clockwise);
                Lijn(b, strip - f);
                Boog(b + f, strip, f, SweepDirection.Counterclockwise);
                Pagina();
                // De sluiting trekt de linkerlijn omhoog naar het beginpunt.
            }
            else
            {
                // De open tab staat ergens in het midden: links en rechts een uitloop.
                g.BeginFigure(new Point(left + r, strip), true, true);
                Lijn(a - f, strip);
                Boog(a, strip - f, f, SweepDirection.Counterclockwise);
                Lijn(a, top + r);
                Boog(a + r, top, r, SweepDirection.Clockwise);
                Lijn(b - r, top);
                Boog(b, top + r, r, SweepDirection.Clockwise);
                Lijn(b, strip - f);
                Boog(b + f, strip, f, SweepDirection.Counterclockwise);
                Pagina();
                Lijn(left, strip + r);
                Boog(left + r, strip, r, SweepDirection.Clockwise);
            }
        }

        vorm.Freeze();
        TabFrame.Data = vorm;
    }

    /// <summary>Het randje van de tab die open staat, of null.</summary>
    private FrameworkElement? ActiveTabElement()
    {
        if (_active is null) return null;

        var container = SiteTabsList.ItemContainerGenerator.ContainerFromItem(_active);
        return container is null ? null : EersteRand(container);
    }

    /// <summary>De eerste Border in de boom: dat is het kadertje van de tab zelf.</summary>
    private static FrameworkElement? EersteRand(DependencyObject wortel)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wortel); i++)
        {
            var kind = VisualTreeHelper.GetChild(wortel, i);
            if (kind is Border rand) return rand;

            if (EersteRand(kind) is { } dieper) return dieper;
        }

        return null;
    }

    private void SiteTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SiteTab tab) SetActiveTab(tab);
    }

    /// <summary>
    /// Opent de lijst met sites. Het chipje zit in een ItemsControl en heeft dus
    /// geen naam om in de XAML naar te verwijzen; de PlacementTarget wijst daarom
    /// naar het randje waarop geklikt is.
    /// </summary>
    private void SitesChip_Click(object sender, MouseButtonEventArgs e)
    {
        SitesPopup.PlacementTarget = sender as UIElement;
        SitesPopup.IsOpen = true;
    }

    /// <summary>
    /// Een site aan- of uitgevinkt: de tabstrip toont enkel wat meedoet, dus de
    /// weergave moet opnieuw beslissen wie er in staat.
    /// </summary>
    private void SiteTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SiteTab.IsEnabled)) return;

        _tabsView.Refresh();
        UpdateChooserLabel();

        // Stond de tab open van de site die je net uitvinkte, dan verdwijnt hij
        // onder je handen. Terugvallen op "Alles" in plaats van op een lege pagina.
        if (_active is { IsAll: false, IsEnabled: false })
            SetActiveTab(_tabs.FirstOrDefault(t => t.IsAll));

        UpdateTabFrame();
    }

    private void SiteEnabled_Changed(object sender, RoutedEventArgs e)
    {
        var count = _tabs.Count(t => t.IsEnabled);
        StatusText.Text = count == 0
            ? "Geen enkele site aangevinkt."
            : $"{count} site(s) zoeken mee.";
    }

    /// <summary>Zet op het chipje hoeveel sites er meezoeken.</summary>
    private void UpdateChooserLabel()
    {
        var aan = _tabs.Count(t => !t.IsAll && t.IsEnabled);
        var totaal = _tabs.Count(t => !t.IsAll);

        _chooser.IsEmpty = aan == 0;
        _chooser.Label = aan == 0 ? "Sites kiezen" : $"{aan} van {totaal} sites";
    }

    /// <summary>
    /// Sluiten van de sitelijst. De tabstrip is intussen al bijgewerkt; wat hier
    /// nog moet gebeuren is de omtrek opnieuw tekenen, want de rij is van lengte
    /// veranderd.
    /// </summary>
    private void SitesPopup_Closed(object sender, EventArgs e) => UpdateTabFrame();

    /// <summary>
    /// Dimt de filterknoppen waar deze site niets mee kan. Wat een site aankan
    /// staat in zijn eigen Filters-mapping, dus dit hoeft nergens hardgecodeerd.
    /// De prijs blijft altijd bruikbaar, want die dwingt de app zelf af.
    /// </summary>
    private void UpdateFilterAvailability()
    {
        if (_active is null)
        {
            LocationButton.IsEnabled = false;
            CardButton.IsEnabled = false;
            return;
        }

        // Op het tabblad "Alles" horen de zoekinstellingen bij geen enkele site,
        // dus staan ze uit. De volgorde en de weergave gelden wel voor alles en
        // blijven dus gewoon bruikbaar.
        if (_active.IsAll)
        {
            // CountButton hoort hier niet meer bij: die zet het aantal per pagina,
            // en dat geldt voor de hele app - ook op "Alles".
            foreach (var knop in new[] { LocationButton, PriceButton,
                                         SpecialButton, CardButton })
            {
                knop.IsEnabled = false;
                knop.ToolTip = "Kies eerst een site; deze instellingen horen bij één site.";
            }

            SpecialPanel.Children.Clear();
            LocationCustomPanel.Children.Clear();
            return;
        }

        PriceButton.IsEnabled = true;
        PriceButton.ToolTip = "Prijs";

        var def = _active.Def!;

        var hasPostcode = SearchUrlBuilder.Supports(def, "postcode")
                          || SearchUrlBuilder.Supports(def, "location");
        var hasRadius = SearchUrlBuilder.Supports(def, "radius")
                        || SearchUrlBuilder.Supports(def, "radiusMeters");
        // Filters uit het sitebestand die over de plaats gaan, zoals de provincies
        // van AlleVeilingen. Die staan in deze popup en niet bij de andere filters,
        // en een site zonder postcode of straal kan er dan toch mee op locatie zoeken.
        var hasLocationFilters = def.CustomFilters.Any(f => f.Section == CustomFilterSection.Location);

        LocationButton.IsEnabled = hasPostcode || hasRadius || hasLocationFilters;
        LocationButton.ToolTip = LocationButton.IsEnabled
            ? "Locatie en afstand"
            : $"{_active.Name} kan niet op locatie zoeken.";

        // Wat een site niet kent, verbergen we in plaats van te dimmen: een
        // uitgegrijsd invoerveld ziet eruit als iets dat stuk is.
        PostcodePanel.Visibility = hasPostcode || hasRadius ? Visibility.Visible : Visibility.Collapsed;
        PostcodeBox.IsEnabled = hasPostcode;
        RadiusBox.IsEnabled = hasRadius;
        BuildLocationFilters();

        LocationNote.Text = hasPostcode && !hasRadius
            ? "Deze site kent wel een postcode, maar geen straal."
            : "";

        CardButton.IsEnabled = true;
        CardButton.ToolTip = $"{_active.Name} beheren";

        // Enkel de filters die in deze popup horen; die over de plaats staan bij de locatie.
        var hasSpecial = def.CustomFilters.Any(f => f.Section == CustomFilterSection.Site);

        SpecialButton.IsEnabled = hasSpecial;
        SpecialButton.ToolTip = hasSpecial
            ? $"Filters van {_active.Name}"
            : $"{_active.Name} heeft geen eigen filters.";

        BuildSpecialFilters();
    }

    // ==================== filters die maar op één site bestaan ====================

    /// <summary>
    /// Bouwt de popup met de sitegebonden filters op uit de sitebeschrijving.
    /// Dit gebeurt in code en niet in de XAML omdat elke site iets anders heeft:
    /// AutoScout24 kent brandstof en kilometerstand, een veilingsite niet. Wie er
    /// een filter bij wil, zet een regel in het JSON-bestand van die site — er
    /// hoeft niets aan de app te veranderen.
    /// </summary>
    private void BuildSpecialFilters()
    {
        SpecialPanel.Children.Clear();

        if (_active?.Def is not { } def) return;

        SpecialTitle.Text = $"Filters van {_active.Name}";

        var label = (Style)FindResource("FilterLabel");
        var uitleg = (Brush)FindResource("TextSubtleBrush");

        // De invoer zelf bouwt CustomFilterControls, dezelfde als in het
        // instellingenvenster van een zoekopdracht. De waarden gaan rechtstreeks
        // naar de filters van het open tabblad.
        foreach (var filter in def.CustomFilters.Where(f => f.Section == CustomFilterSection.Site))
            SpecialPanel.Children.Add(CustomFilterControls.Build(filter, _active.Filters.Custom, null, label, uitleg));
    }

    /// <summary>
    /// De sitegebonden filters die over de plaats gaan, in de locatie-popup. Bij
    /// AlleVeilingen zijn dat de provincies: die site kent geen postcode of straal
    /// maar wel regio's, en die zoek je waar je naar een plaats zoekt. Ze stonden daar
    /// al toen ze nog vast in de code zaten; nu komen ze uit het sitebestand.
    /// </summary>
    private void BuildLocationFilters()
    {
        LocationCustomPanel.Children.Clear();

        if (_active?.Def is not { } def) return;

        var label = (Style)FindResource("FilterLabel");
        var uitleg = (Brush)FindResource("TextSubtleBrush");

        // Eén kolom: de popup is smal, en "Oost-Vlaanderen" past niet in de helft.
        foreach (var filter in def.CustomFilters.Where(f => f.Section == CustomFilterSection.Location))
            LocationCustomPanel.Children.Add(
                CustomFilterControls.Build(filter, _active.Filters.Custom, null, label, uitleg, maxColumns: 1));
    }

    private void ClearSpecial_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;

        // Enkel de filters van deze popup: de provincies in de locatie-popup blijven staan.
        foreach (var filter in _active.Def?.CustomFilters.Where(f => f.Section == CustomFilterSection.Site)
                               ?? Enumerable.Empty<CustomFilter>())
            _active.Filters.Custom.Remove(filter.Key);

        BuildSpecialFilters();

        StatusText.Text = $"Filters van {_active.Name} gewist.";
    }

    // ---------- filters en weergave ----------

    /// <summary>
    /// De filters die enkel de site zelf kan toepassen, als één tekst. Wijzigt
    /// die na het sluiten van een popup, dan moet er opnieuw gezocht worden: die
    /// waarden zitten in de zoek-URL en niet in de resultaten.
    /// </summary>
    private string SiteFilterFingerprint()
    {
        if (_active is null) return "";

        var f = _active.Filters;

        // Ook de sitegebonden filters tellen mee: wijzig je de brandstof, dan
        // moet de app opnieuw gaan zoeken en niet enkel de lijst herschikken.
        var eigen = string.Join(",", f.Custom.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        return string.Join("|", f.Postcode, f.RadiusKm, _active.MaxResults, eigen);
    }

    /// <summary>Hoe die filters stonden toen de popup openging.</summary>
    private string _filtersBijOpenen = "";

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;

        _filtersBijOpenen = SiteFilterFingerprint();

        if (ReferenceEquals(sender, SortButton)) SortPopup.IsOpen = true;
        else if (ReferenceEquals(sender, SpecialButton))
        {
            // Opnieuw opbouwen: een bewaarde zoekopdracht kan de waarden intussen
            // gewijzigd hebben, en de vinkjes moeten tonen wat er nu geldt.
            BuildSpecialFilters();
            SpecialPopup.IsOpen = true;
        }
        else if (ReferenceEquals(sender, LocationButton))
        {
            BuildLocationFilters();
            LocationPopup.IsOpen = true;
        }
        else if (ReferenceEquals(sender, PriceButton)) PricePopup.IsOpen = true;
        else if (ReferenceEquals(sender, CountButton))
        {
            // Aanvinken wat er nu geldt; de keuze staat in de instellingen van de
            // app en niet bij een site, dus er is niets om aan te binden.
            foreach (var kind in PageSizeKeuze.Children)
            {
                if (kind is RadioButton knop && knop.Tag is string tag)
                    knop.IsChecked = tag == AppSettings.Current.PageSize.ToString();
            }

            CountPopup.IsOpen = true;
        }
    }

    /// <summary>
    /// De prijs kan de app zelf afdwingen op wat er al staat, dus die werkt
    /// meteen door terwijl je typt.
    /// </summary>
    /// <summary>
    /// De volgorde is gewisseld. Dat gebeurt op de weergave en niet op de lijst
    /// zelf: de resultaten blijven staan zoals ze binnenkwamen, er wordt enkel
    /// anders naar gekeken.
    /// </summary>
    private void Sort_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _sort = ReferenceEquals(sender, SortPriceUp) ? ListingSort.PriceAscending
              : ReferenceEquals(sender, SortPriceDown) ? ListingSort.PriceDescending
              : ReferenceEquals(sender, SortNewest) ? ListingSort.Newest
              : ListingSort.Default;

        AppSettings.Current.Sort = (int)_sort;
        AppSettings.Current.Save();

        PasSorteringToe();
        ToonGefilterd();
    }

    /// <summary>
    /// Legt de gekozen volgorde op aan de weergave. Een gewone SortDescription
    /// kan hier niet: zoekertjes zonder prijs of zonder datum moeten achteraan,
    /// en dat vraagt een eigen vergelijking.
    /// </summary>
    private void PasSorteringToe()
    {
        if (_resultsView is not ListCollectionView weergave) return;

        weergave.CustomSort = _sort == ListingSort.Default ? null : new ListingComparer(_sort);
    }

    private void PriceFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        ToonGefilterd();

    /// <summary>
    /// Bij het sluiten van een filterpopup: locatie, straal, provincie en het
    /// maximum zitten in de zoek-URL, dus daarvoor is een nieuwe zoekopdracht
    /// nodig. Die starten we vanzelf, in plaats van de gebruiker terug naar het
    /// vergrootglas te sturen.
    /// </summary>
    private void FilterPopup_Closed(object sender, EventArgs e)
    {
        if (!_ready || _active is null) return;

        ToonGefilterd();

        if (SiteFilterFingerprint() == _filtersBijOpenen) return;
        _filtersBijOpenen = SiteFilterFingerprint();

        // Enkel opnieuw zoeken wanneer er al iets te zien was.
        if (_results.Count > 0) _ = RunSearchAsync();
    }

    /// <summary>Werkt de lijst en de teller bij nadat een filter gewijzigd is.</summary>
    private void ToonGefilterd()
    {
        if (!_ready || _active is null) return;

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();

        var zichtbaar = _resultsView.Cast<object>().Count();
        var gevonden = _results.Count(r => r.Source == _active.Name);

        if (gevonden == 0) return;

        StatusText.Text = zichtbaar == gevonden
            ? $"{gevonden} resultaten van {_active.Name}."
            : $"{zichtbaar} van {gevonden} resultaten van {_active.Name} binnen de filter.";
    }

    /// <summary>De twee manieren waarop de resultatenlijst getoond kan worden.</summary>
    private enum ResultView
    {
        /// <summary>Een brede kaart per zoekertje: foto, titel, prijs.</summary>
        List,

        /// <summary>Foto's naast en onder elkaar, zoals Facebook Marketplace.</summary>
        Grid
    }

    private ResultView _view = ResultView.List;

    private void LayoutButton_Click(object sender, RoutedEventArgs e) =>
        SetResultView(_view == ResultView.List ? ResultView.Grid : ResultView.List);

    /// <summary>De twee menu-items gedragen zich als keuzerondjes.</summary>
    private void ViewModeMenu_Click(object sender, RoutedEventArgs e) =>
        SetResultView(ReferenceEquals(sender, ViewGridMenu) ? ResultView.Grid : ResultView.List);

    /// <summary>
    /// Wisselt de resultatenlijst van vorm. Alleen het sjabloon en het paneel
    /// veranderen; de resultaten blijven staan, dus je kan tijdens het bekijken
    /// van een zoekopdracht van weergave wisselen.
    /// </summary>
    private void SetResultView(ResultView view)
    {
        // De keuze geldt voor de hele app en wordt onthouden tot de volgende start.
        if (_ready && AppSettings.Current.ResultView != (int)view)
        {
            AppSettings.Current.ResultView = (int)view;
            AppSettings.Current.Save();
        }

        _view = view;
        var grid = view == ResultView.Grid;

        ViewListMenu.IsChecked = !grid;
        ViewGridMenu.IsChecked = grid;

        // Het pictogram toont waar je naartoe gaat, niet waar je staat.
        LayoutIcon.Symbol = grid
            ? Wpf.Ui.Controls.SymbolRegular.TextBulletListLtr24
            : Wpf.Ui.Controls.SymbolRegular.Grid24;

        var template = (DataTemplate)FindResource(grid ? "GridItemTemplate" : "ListItemTemplate");
        var panel = (ItemsPanelTemplate)FindResource(grid ? "GridItemsPanel" : "ListItemsPanel");

        foreach (var list in new[] { ResultsList, FavoritesList })
        {
            list.ItemTemplate = template;
            list.ItemsPanel = panel;

            // In het raster moet de omhulling van een kaart precies zo groot zijn
            // als de kaart: het paneel rekent met die maat uit hoeveel kolommen er
            // passen. In de lijst blijft de standaardstijl staan.
            list.ItemContainerStyle = grid
                ? (Style)FindResource("GridItemContainerStyle")
                : null;

            // Allebei de panelen schuiven zelf, per beeldpunt: de lijst met een
            // VirtualizingStackPanel, het raster met ons eigen VirtualizingWrapPanel.
            // Dat laatste regelt zijn schuiven via IScrollInfo, en daarvoor MOET
            // CanContentScroll aan staan - anders wikkelt de ScrollViewer er zijn
            // eigen laag omheen, geeft hij het paneel oneindige hoogte, en bouwt het
            // alsnog alles op.
            ScrollViewer.SetCanContentScroll(list, true);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);

            // Per beeldpunt schuiven, niet per kaart. Met CanContentScroll aan doet
            // een VirtualizingStackPanel standaard het laatste: elke klik van het
            // muiswiel springt dan een hele kaart, en dat leest als schokkerig.
            // ScrollUnit.Pixel geeft vloeiend schuiven en houdt de virtualisatie.
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);

            // En het muiswiel zelf: een zinnige stap, met een glijbeweging.
            Controls.SmoothScroll.SetEnabled(list, true);
        }
    }

    // ---------- de balk onderaan ----------

    private void BottomTab_Checked(object sender, RoutedEventArgs e)
    {
        // Bij het opbouwen van het venster bestaan de panelen nog niet.
        if (!_ready) return;

        SearchPanel.Visibility = Collapse(ReferenceEquals(sender, TabSearch));
        FavoritesPanel.Visibility = Collapse(ReferenceEquals(sender, TabFavorites));
        RecentPanel.Visibility = Collapse(ReferenceEquals(sender, TabRecent));
        SavedPanel.Visibility = Collapse(ReferenceEquals(sender, TabSaved));

        if (ReferenceEquals(sender, TabFavorites)) LoadFavorites();
        if (ReferenceEquals(sender, TabRecent)) LoadRecent();

        UpdateEmptyHints();

        static Visibility Collapse(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Toont de uitlegregel wanneer een lijst leeg is.</summary>
    private void UpdateEmptyHints()
    {
        if (!_ready) return;

        // Zonder sites valt er niets te zoeken: dan een uitnodiging om ze te importeren
        // of toe te voegen, in plaats van de gewone "nog geen resultaten".
        var geenSites = _store.Sites.Count == 0;
        NoSitesPanel.Visibility = geenSites ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = !geenSites && _resultsView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        FavoritesEmpty.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedEmpty.Visibility = _saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyHint.Text = LeegTekst();

        FavoritesCount.Text = _favorites.Count == 1 ? "1 bewaard" : $"{_favorites.Count} bewaard";
    }

    /// <summary>
    /// Wat er in een lege resultatenlijst staat. Vroeger was dat altijd "Nog geen
    /// resultaten van ...", of er nu nog niets gezocht was, niets gevonden, alles
    /// weggefilterd, of de site mislukte - situaties die elk iets anders van je vragen.
    /// </summary>
    private string LeegTekst()
    {
        if (_active is null) return "Voeg eerst een site toe via het tandwiel.";

        if (!_tabs.Any(t => !t.IsAll && t.IsEnabled))
            return "Kies eerst op welke sites je wilt zoeken, met 'Sites kiezen' hierboven.";

        if (_zoektHandmatig) return "Zoeken...";
        if (!_heeftGezocht) return "Typ hierboven wat je zoekt en druk op Enter.";

        if (_active is { IsAll: false, HasError: true })
            return $"{_active.Name} mislukte: {_active.ErrorText}";

        var gevonden = _results.Count(r => _active.IsAll || r.Source == _active.Name);

        // Op "Alles" stond hier "Niets gevonden.", ook als een site mislukte: de reden stond
        // enkel op de tab van die site, en daar keek je niet.
        if (gevonden == 0 && _active.IsAll)
        {
            var mislukt = _tabs.Where(t => !t.IsAll && t.IsEnabled && t.HasError).Select(t => t.TabName).ToList();

            if (mislukt.Count > 0)
                return $"Niets gevonden. {string.Join(", ", mislukt)} mislukte; " +
                       "wijs het waarschuwingsteken op de tab aan voor de reden.";
        }

        if (gevonden > 0)
            return gevonden == 1
                ? "Er is 1 resultaat, maar het valt buiten je filters."
                : $"Er zijn {gevonden} resultaten, maar ze vallen buiten je filters.";

        return _active.IsAll ? "Niets gevonden." : $"Niets gevonden op {_active.Name}.";
    }

    // ---------- favorieten ----------

    private void LoadFavorites()
    {
        _favorites.Clear();
        foreach (var listing in _history.GetFavorites()) _favorites.Add(listing);

        _favoriteKeys = _history.GetFavoriteKeys();
    }

    /// <summary>Zet een zoekertje bij de favorieten, of haalt het er weer af.</summary>
    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        if (listing.IsFavorite)
        {
            _history.RemoveFavorite(listing.Key);
            _favoriteKeys.Remove(listing.Key);
            listing.IsFavorite = false;

            // Op de favorietenpagina verdwijnt hij meteen uit de lijst.
            var stored = _favorites.FirstOrDefault(f => f.Key == listing.Key);
            if (stored is not null) _favorites.Remove(stored);

            StatusText.Text = "Uit de favorieten gehaald.";
        }
        else
        {
            _history.AddFavorite(listing);
            _favoriteKeys.Add(listing.Key);
            listing.IsFavorite = true;

            StatusText.Text = "Bij de favorieten gezet.";
        }

        // Hetzelfde zoekertje kan in beide lijsten staan; die andere moet volgen.
        foreach (var other in _results.Where(r => r.Key == listing.Key))
            other.IsFavorite = listing.IsFavorite;

        UpdateEmptyHints();
    }

    // ---------- recente zoekopdrachten ----------

    private void LoadRecent()
    {
        _recent.Clear();
        foreach (var entry in _history.GetRecent()) _recent.Add(entry);
    }

    private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenRecent();

    /// <summary>Enter doet hetzelfde als dubbelklikken; daarvoor moest je de muis gebruiken.</summary>
    private void RecentList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        OpenRecent();
        e.Handled = true;
    }

    private void OpenRecent()
    {
        if (RecentList.SelectedItem is not RecentSearch entry) return;

        QueryBox.Text = entry.Query;
        _activeSearch = null;

        TabSearch.IsChecked = true;
        _ = RunSearchAsync();
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        _history.ClearRecent();
        LoadRecent();
        UpdateEmptyHints();
    }

    // ---------- vastgezette zoekopdrachten ----------

    private void LoadSavedSearches()
    {
        _saved.Clear();
        foreach (var search in _history.GetAll()) _saved.Add(search);
    }

    private void SavedList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteSavedButton.IsEnabled = SavedList.SelectedItem is SavedSearch;

    /// <summary>
    /// Zet de zoekterm en de filters van deze zoekopdracht klaar en toont zijn
    /// resultaten. Heeft de planner die net nog opgehaald, dan tonen we díe in
    /// plaats van opnieuw te gaan zoeken — bij sites via de brug scheelt dat al
    /// gauw een halve minuut wachten.
    /// </summary>
    private void SavedList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSaved();

    /// <summary>Enter doet hetzelfde als dubbelklikken.</summary>
    private void SavedList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        OpenSaved();
        e.Handled = true;
    }

    private void OpenSaved()
    {
        if (SavedList.SelectedItem is not SavedSearch search) return;

        _activeSearch = search;
        QueryBox.Text = search.Query;
        PasToe(search);

        TabSearch.IsChecked = true;

        // Wat de laatste beurt opleverde, uit het geheugen of anders van schijf.
        // Er wordt niet meer op de klok gekeken: een half uur oude lijst is nog
        // altijd beter dan niets, en de statusregel zegt erbij van wanneer ze is.
        // Wie verse resultaten wil, klikt op het vergrootglas om opnieuw te zoeken.
        var bewaard = _lastOutcomes.TryGetValue(search.Id, out var uitGeheugen) && uitGeheugen.Count > 0
            ? uitGeheugen
            : _history.GetOutcome(search.Id);

        if (bewaard.Count > 0)
        {
            ToonBewaard(search, bewaard);
            return;
        }

        _ = RunSearchAsync();
    }

    /// <summary>Zet de resultaten van een eerdere beurt in de lijst.</summary>
    private void ToonBewaard(SavedSearch search, List<Listing> resultaten)
    {
        _heeftGezocht = true;

        _results.Clear();
        _zichtbaar.Clear();
        _pagina = 0;

        // De fouten van die beurt op de tabs, zoals na gewoon zoeken. Vroeger bleven hier de
        // waarschuwingstekens van wat er daarvoor op het scherm stond, en ontbraken die van
        // deze zoekopdracht - net na een melding "kon niet overal zoeken".
        foreach (var tab in _tabs)
        {
            tab.ResultCount = 0;
            tab.ErrorText = tab.IsAll ? "" : search.LastErrors.GetValueOrDefault(tab.Name, "");
        }

        // Nieuwe zoekertjes vooraan, net als bij gewoon zoeken. OrderBy is stabiel:
        // binnen de nieuwe en binnen de rest blijft de volgorde van de sites staan.
        foreach (var listing in resultaten.OrderBy(l => l.IsNew ? 0 : 1))
        {
            listing.IsFavorite = _favoriteKeys.Contains(listing.Key);
            _results.Add(listing);

            var tab = _tabs.FirstOrDefault(t => t.Name == listing.Source);
            if (tab is not null) tab.ResultCount++;
        }

        // De eerste site met resultaten openzetten, anders kijk je naar een
        // lege tab terwijl er wel iets gevonden is. Op "Alles" staat sowieso
        // alles, dus daar hoeft er niets te wisselen.
        if (_active is null || (!_active.IsAll && _active.ResultCount == 0))
        {
            var metResultaat = _tabs.FirstOrDefault(t => t.ResultCount > 0);
            if (metResultaat is not null) SetActiveTab(metResultaat);
        }

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();

        var wanneer = search.LastRun is { } run
            ? (DateTime.Now - run) < TimeSpan.FromHours(12)
                ? $"van {run:HH:mm}"
                : $"van {run:d MMM HH:mm}"
            : "van de laatste beurt";

        var nieuw = resultaten.Count(l => l.IsNew);

        StatusText.Text = $"'{search.Name}': {resultaten.Count} resultaten {wanneer}"
                          + (nieuw > 0 ? $", waarvan {nieuw} nieuw" : "")
                          + ". Klik op het vergrootglas om opnieuw te zoeken.";
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText.Text = "Typ eerst een zoekterm.";
            return;
        }

        // Twee keer vastzetten gaf twee dezelfde zoekopdrachten, en dus dubbele meldingen.
        var bestaand = _saved.FirstOrDefault(s => string.Equals(s.Query, query, StringComparison.OrdinalIgnoreCase));
        if (bestaand is not null)
        {
            _activeSearch = bestaand;
            StatusText.Text = $"'{bestaand.Name}' staat al bij Zoekopdrachten. Aanpassen doe je met het tandwiel naast de zoekbalk.";
            return;
        }

        // Elke tab levert zijn eigen instellingen aan: een zoekopdracht bewaart
        // dus de postcode van de ene site naast de provincies van de andere.
        var search = new SavedSearch
        {
            Query = query,
            SiteSettings = _tabs.Where(t => !t.IsAll).Select(SiteSetting.FromTab).ToList()
        };

        search.Id = _history.Add(search);

        // Wat nu op het scherm staat, geldt als gezien: pas morgen is er iets nieuw.
        if (_results.Count > 0)
            _history.MarkSeen(search.Id, _results.Select(r => r.Key));

        LoadSavedSearches();
        UpdateEmptyHints();
        _activeSearch = search;

        UpdateSchedulerHint();

        StatusText.Text = $"'{search.Name}' vastgezet. Wanneer hij vanzelf draait, stel je in met het tandwiel " +
                          "op zijn kaart bij Zoekopdrachten.";
    }

    private void NewSavedButton_Click(object sender, RoutedEventArgs e)
    {
        var nieuw = new SavedSearch
        {
            Query = QueryBox.Text.Trim(),

            // Met alle sites erin, uitgevinkt: dan staat de keuze er al klaar.
            SiteSettings = _tabs.Where(t => !t.IsAll)
                                .Select(t => new SiteSetting { Site = t.Name, MaxResults = 500 }).ToList()
        };

        OpenSearchSettings(nieuw);
    }

    /// <summary>De knop met het tandwiel op de kaart van een zoekopdracht.</summary>
    private void EditSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SavedSearch search) OpenSearchSettings(search);
    }

    /// <summary>De knop met het driehoekje: deze zoekopdracht nu laten draaien.</summary>
    private void RunSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedSearch search) return;

        // Zelf gevraagd, dus het scherm mag meekijken (zie Scheduler_Started).
        _meekijkenMet = search.Id;
        _ = _scheduler.RunAsync(search);
    }

    /// <summary>
    /// Opent het instellingenscherm van een zoekopdracht en verwerkt het
    /// resultaat. Een nieuwe zoekopdracht krijgt daar zijn id, dus daarna moet
    /// de lijst opnieuw ingelezen worden.
    /// </summary>
    private void OpenSearchSettings(SavedSearch search)
    {
        var window = new SearchSettingsWindow(search, _store, _history) { Owner = this };

        if (window.ShowDialog() != true) return;

        LoadSavedSearches();
        UpdateEmptyHints();
        UpdateSchedulerHint();

        // De bewerkte zoekopdracht opnieuw opzoeken: LoadSavedSearches maakt
        // verse objecten, dus het object dat we meegaven is niet meer het object
        // dat in de lijst staat.
        _activeSearch = _saved.FirstOrDefault(s => s.Id == search.Id);

        if (_activeSearch is not null)
        {
            QueryBox.Text = _activeSearch.Query;
            PasToe(_activeSearch);
        }

        StatusText.Text = $"'{search.Name}' bewaard. {search.Schedule.Describe()}.";
    }

    /// <summary>
    /// Zet de vinkjes en filters van de tabs gelijk met een bewaarde zoekopdracht,
    /// zodat het zoekscherm toont waarmee die zoekopdracht werkt.
    /// </summary>
    private void PasToe(SavedSearch search)
    {
        foreach (var tab in _tabs)
        {
            if (tab.IsAll) continue;

            var setting = search.For(tab.Name);

            if (setting is null)
            {
                tab.IsEnabled = false;
                continue;
            }

            setting.ApplyTo(tab);
        }

        UpdateFilterAvailability();
    }

    private void DeleteSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedList.SelectedItem is not SavedSearch search) return;

        // Eerst vragen. Een site verwijderen deed dat al; een zoekopdracht niet, terwijl
        // daar het schema en de hele "al gezien"-geschiedenis mee weggaan.
        var bevestig = MessageBox.Show(this,
            $"Zoekopdracht '{search.Name}' verwijderen? Het schema en wat al gezien is, gaan ook weg.",
            "Zoekopdracht verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (bevestig != MessageBoxResult.Yes) return;

        _history.Delete(search.Id);
        if (_activeSearch?.Id == search.Id) _activeSearch = null;

        LoadSavedSearches();
        UpdateEmptyHints();
        DeleteSavedButton.IsEnabled = false;
        StatusText.Text = $"'{search.Name}' verwijderd.";
    }

    // ---------- menu achter het tandwiel ----------

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        GearMenu.PlacementTarget = SearchSettingsButton;
        GearMenu.Placement = PlacementMode.Bottom;
        GearMenu.IsOpen = true;
    }

    /// <summary>De instellingen van deze zoekterm, nu als eerste regel in het menu.</summary>
    private void SearchSettingsMenu_Click(object sender, RoutedEventArgs e) =>
        SearchSettingsButton_Click(sender, e);

    /// <summary>
    /// Vult "Sites beheren" met een regel per site. Het menu wordt telkens opnieuw
    /// opgebouwd samen met de tabs, zodat een toegevoegde of verwijderde site
    /// er meteen in staat.
    /// </summary>
    private void BuildCardsMenu()
    {
        CardsMenu.Items.Clear();

        if (_store.Sites.Count == 0)
        {
            CardsMenu.Items.Add(new MenuItem { Header = "Nog geen sites", IsEnabled = false });
            return;
        }

        foreach (var site in _store.Sites)
        {
            // De naam apart bijhouden: de lus-variabele mag niet in de klik terechtkomen.
            var name = site.Name;

            var item = new MenuItem { Header = name };
            item.Click += (_, _) => OpenSettings(name);

            CardsMenu.Items.Add(item);
        }
    }

    private void CardButton_Click(object sender, RoutedEventArgs e) => OpenSettings(_active?.Name);

    /// <summary>
    /// Opent de instellingen met een kaart per site, eventueel meteen op de
    /// kaart van een bepaalde site.
    /// </summary>
    private void OpenSettings(string? site = null)
    {
        // De naam van elke site voor het venster opengaat, per Id: zo is na het sluiten te
        // zien welke site hernoemd werd.
        var namenVoor = _store.Sites.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Name);

        var window = new SettingsWindow(_store, site) { Owner = this };
        window.ShowDialog();

        // De sites kunnen bewerkt, toegevoegd of verwijderd zijn: opnieuw inlezen
        // en de tabs herbouwen zodat het hoofdscherm klopt.
        _store.Load();

        var hernoemd = _store.Sites
            .Where(s => namenVoor.TryGetValue(s.Id, out var oud) && oud != s.Name)
            .Select(s => (Oud: namenVoor[s.Id], Nieuw: s.Name))
            .ToList();

        if (hernoemd.Count > 0) NeemHernoemenMee(hernoemd);
        if (_history.ApplyIdPatterns(_store.Sites) > 0) HerlaadSleutels();

        BuildSiteTabs();
    }

    /// <summary>
    /// Een site kreeg een andere naam: de databank neemt alles mee wat aan die naam hing
    /// (<see cref="HistoryStore.RenameSource"/>), en wat er nu op het scherm en in het geheugen
    /// staat, gaat mee. Zonder dat viel de site stil uit elke bewaarde zoekopdracht.
    /// </summary>
    private void NeemHernoemenMee(List<(string Oud, string Nieuw)> hernoemd)
    {
        foreach (var (oud, nieuw) in hernoemd)
        {
            _history.RenameSource(oud, nieuw);

            foreach (var listing in _results.Concat(_lastOutcomes.Values.SelectMany(l => l)).Where(l => l.Source == oud))
                listing.Source = nieuw;
        }

        var actief = _activeSearch?.Id;
        LoadSavedSearches();
        _activeSearch = actief is null ? null : _saved.FirstOrDefault(s => s.Id == actief);

        HerlaadSleutels();

        StatusText.Text = string.Join(" ", hernoemd.Select(h => $"'{h.Oud}' heet nu '{h.Nieuw}'.")) +
                          " Bewaarde zoekopdrachten, favorieten en wat je al zag, zijn meegenomen.";
    }

    /// <summary>De favorieten opnieuw inlezen, nadat hun sleutels veranderden.</summary>
    private void HerlaadSleutels()
    {
        _favoriteKeys = _history.GetFavoriteKeys();
        LoadFavorites();
    }

    private void AddSiteMenu_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddSiteWindow { Owner = this };

        if (window.ShowDialog() == true && window.Result is not null)
        {
            _store.Add(window.Result);
            BuildSiteTabs();
            StatusText.Text = $"Site '{window.Result.Name}' toegevoegd.";
        }
    }

    /// <summary>
    /// Importeert alle sitebestanden uit een map in één keer. De app komt zonder sites,
    /// dus dit is de eerste stap na het installeren. De map van de sites-repository zelf
    /// kiezen mag ook: <see cref="SiteStore.ImportFolder"/> kijkt dan in zijn submap "sites".
    /// </summary>
    private void ImportSitesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Map met sitebestanden kiezen" };
        if (dialog.ShowDialog(this) != true) return;

        var (aantal, mislukt, vervangen) = _store.ImportFolder(dialog.FolderName);

        // Opnieuw inlezen en de tabs herbouwen, net als na "Site toevoegen". Dat werkt
        // meteen ook de lege toestand bij.
        _store.Load();
        if (_history.ApplyIdPatterns(_store.Sites) > 0) HerlaadSleutels();
        BuildSiteTabs();

        StatusText.Text = aantal == 0 && mislukt.Count == 0
            ? "In die map staan geen sitebestanden."
            : $"{aantal} site(s) geïmporteerd" +
              (vervangen.Count > 0 ? $", waarvan {vervangen.Count} vervangen ({string.Join(", ", vervangen)})" : "") +
              (mislukt.Count > 0 ? $"; geen geldige sitebeschrijving: {string.Join(", ", mislukt)}." : ".");
    }

    /// <summary>
    /// Opent het logboek. Stond enkel in Sites beheren, en dat venster is met nul sites
    /// niet te openen - net wanneer een nieuwe gebruiker het nodig heeft.
    /// </summary>
    private void OpenLogMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(Log.FilePath))
            {
                StatusText.Text = "Nog geen logboek: doe eerst een zoekopdracht.";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Log.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Kon het logboek niet openen: " + ex.Message;
        }
    }

    /// <summary>Toont de koppelcode en zet hem op het klembord voor de extensie.</summary>
    private void BridgeCodeMenu_Click(object sender, RoutedEventArgs e)
    {
        var bridge = BridgeServer.Instance;
        bridge.Start();

        try
        {
            Clipboard.SetText(bridge.Token);

            StatusText.Text = bridge.ExtensionAlive
                ? "De Zentrix Brug is verbonden. De koppelcode staat op je klembord."
                : "Koppelcode gekopieerd. Klik in Chrome op het pictogram van de Zentrix Brug, plak de code en kies 'Code opslaan'.";
        }
        catch
        {
            StatusText.Text = "Koppelcode: " + bridge.Token;
        }
    }

    // ---------- een zoekertje openen ----------

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenSelected(sender as ListBox);

    /// <summary>Enter opent het geselecteerde zoekertje, net als dubbelklikken.</summary>
    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        OpenSelected(sender as ListBox);
        e.Handled = true;
    }

    /// <summary>
    /// De link als webadres (http of https), of null als het iets anders is.
    ///
    /// Met UseShellExecute opent Windows alles wat je het geeft: een webadres, maar ook een
    /// programma op schijf, een bestand op een netwerkmap (\\server\map\iets.exe) of een
    /// ander protocol. De link van een zoekertje komt uit de pagina of uit het sitebestand
    /// (UrlTemplate bij de linkmotor), en een sitebestand is bedoeld om te delen - het kan
    /// dus van een vreemde komen. Tot september 2026 ging elke link ongezien naar Windows.
    /// </summary>
    internal static Uri? AlsWebadres(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var adres) &&
        (adres.Scheme == Uri.UriSchemeHttp || adres.Scheme == Uri.UriSchemeHttps)
            ? adres
            : null;

    /// <summary>
    /// Rechtsklik op een foto: wat is dit ongeveer waard? Opent een eigen venster, zodat je
    /// verder kan kijken terwijl er gezocht wordt, en er meerdere naast elkaar kunnen staan.
    /// </summary>
    private void PriceIndicationMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        new PriceIndicationWindow(listing, _store.Sites) { Owner = this }.Show();
    }

    /// <summary>Opent het geselecteerde zoekertje in de standaardbrowser.</summary>
    private void OpenSelected(ListBox? lijst)
    {
        if (lijst?.SelectedItem is not Listing listing) return;

        if (string.IsNullOrWhiteSpace(listing.Url))
        {
            StatusText.Text = "Dit resultaat heeft geen link.";
            return;
        }

        var adres = AlsWebadres(listing.Url);
        if (adres is null)
        {
            StatusText.Text = "Deze link is geen webadres en wordt daarom niet geopend: " + listing.Url;
            Log.Write($"zoekertje niet geopend, geen webadres: {listing.Url}");
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
        var window = new NotifySettingsWindow { Owner = this };
        window.ShowDialog();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

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
    }

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
    private bool ZichtbaarInHuidigeTab(object item)
    {
        if (item is not Listing listing || _active is null) return false;

        // Op "Alles" tellen alle sites mee; op een sitetab enkel die ene.
        if (!_active.IsAll && listing.Source != _active.Name) return false;

        if (_activeSearch is not null && !_activeSearch.Matches(listing)) return false;

        // De prijsgrens hoort bij de site die hem opgaf. Op "Alles" zoeken we
        // daarom het tabblad van dít zoekertje op, en niet dat van de weergave —
        // anders zou een grens van de ene site die van de andere overschrijven.
        var bron = _active.IsAll
            ? _tabs.FirstOrDefault(t => !t.IsAll && t.Name == listing.Source)
            : _active;

        if (bron is null) return true;

        var filters = bron.Filters;

        // Zonder prijs valt een zoekertje buiten een prijsgrens; zo deed de
        // oude filter het ook.
        if (filters.PriceMin is { } min && listing.Price < min) return false;
        if (filters.PriceMax is { } max && listing.Price > max) return false;

        return true;
    }

    private async Task RunSearchAsync()
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
            return;
        }

        _heeftGezocht = true;

        // Zoeken zonder zoekterm kan enkel op sites die het aankunnen. Bij
        // AutoScout24 is dat de gewone gang van zaken: daar is het zoekwoord het
        // merk, en wie niet merkgebonden zoekt zet enkel filters. De rest zou van
        // een lege term hun hele catalogus maken, dus die slaan we over.
        var zonderTerm = "";

        if (query.Length == 0)
        {
            var kunnen = searching.Where(t => t.Def!.AllowsEmptyQuery).ToList();

            if (kunnen.Count == 0)
            {
                StatusText.Text = searching.Count == 1
                    ? $"{searching[0].Name} heeft een zoekterm nodig."
                    : "Typ een zoekterm. Geen van de aangevinkte sites kan zoeken op filters alleen.";
                return;
            }

            // Wel zeggen wat er overgeslagen wordt, anders lijkt het alsof die
            // sites niets gevonden hebben.
            if (kunnen.Count < searching.Count)
                zonderTerm = $" Alleen {string.Join(", ", kunnen.Select(t => t.Name))} " +
                             "kan zoeken zonder zoekterm; de rest is overgeslagen.";

            searching = kunnen;
        }

        TabSearch.IsChecked = true;

        _zoektHandmatig = true;
        SearchButton.IsEnabled = false;
        Spinner.Visibility = Visibility.Visible;
        StatusText.Text = "Bezig met zoeken...";

        // Hetzelfde slot als de planner. Draait er op de achtergrond net een
        // zoekopdracht, dan wachten we die af: de brug heeft één wachtrij en
        // twee Playwright-sessies op hetzelfde profiel botsen.
        if (SearchRunner.Gate.CurrentCount == 0)
            StatusText.Text = "Wachten tot de zoekopdracht op de achtergrond klaar is...";

        await SearchRunner.Gate.WaitAsync();

        // Sites die via de brug werken hebben een draaiende Chrome nodig: staat
        // die dicht, dan vraagt niemand om werk en blijft de opdracht hangen.
        var brug = BridgeStatus.Ready;
        var brugOvergeslagen = new List<SiteTab>();
        var aantalSites = searching.Count;

        if (searching.Any(t => t.Def!.UseBridge))
        {
            var report = new Progress<string>(text => StatusText.Text = text);
            brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30), report);

            // Werkt de brug niet, dan die sites meteen overslaan. Anders wacht elke
            // brugsite nog anderhalve minuut op een antwoord waarvan al vaststaat dat
            // het niet komt: bij Catawiki en leboncoin samen ruim drie minuten.
            if (brug != BridgeStatus.Ready)
            {
                brugOvergeslagen = searching.Where(t => t.Def!.UseBridge).ToList();
                searching = searching.Except(brugOvergeslagen).ToList();
            }
        }

        _results.Clear();
        _zichtbaar.Clear();
        _pagina = 0;

        foreach (var tab in _tabs)
        {
            tab.ResultCount = 0;
            tab.ErrorText = "";
        }

        foreach (var tab in brugOvergeslagen)
            tab.ErrorText = "Overgeslagen: " + ChromeLauncher.Describe(brug);

        if (query.Length > 0)
        {
            _history.AddRecent(query);
            LoadRecent();
        }

        var errors = new List<string>();

        // Hoeveel elke site gaf, voor de bewaarde zoekopdracht (zie SavedSearch.LastCounts).
        var tellingen = new Dictionary<string, int>();

        try
        {
            // Bij een bewaarde zoekopdracht één keer ophalen wat we al gezien hebben.
            HashSet<string>? seen = _activeSearch is not null
                ? _history.GetSeenKeys(_activeSearch.Id)
                : null;

            var newCount = 0;

            // Nieuwe resultaten horen bovenaan. Omdat we per bron toevoegen in
            // plaats van alles op het einde, schuiven we ze op deze teller in.
            var newTopIndex = 0;

            // Welke resultaten al in de lijst staan. Bronnen die gaandeweg leveren
            // sturen dezelfde zoekertjes meermaals door; die willen we maar één
            // keer. We houden het zoekertje zelf bij en niet enkel zijn sleutel,
            // want een latere levering is soms vollediger - bij Catawiki staat de
            // prijs er in de eerste versie nog niet in.
            var shown = new Dictionary<string, Listing>();
            var aangevuld = false;

            // Voegt een lading resultaten van één site toe aan de lijst.
            void AddBatch(List<Listing> raw, SiteTab tab)
            {
                // Alles wordt bewaard, ook wat buiten de prijsfilter valt. Het
                // filteren gebeurt bij het tonen, zodat een ruimere prijs meteen
                // meer resultaten laat zien in plaats van een nieuwe zoekopdracht
                // te vragen.
                var batch = raw;
                var added = new List<string>();

                foreach (var listing in batch)
                {
                    if (shown.TryGetValue(listing.Key, out var bestaand))
                    {
                        // Stond er al: aanvullen wat toen nog ontbrak.
                        if (bestaand.MergeFrom(listing)) aangevuld = true;
                        continue;
                    }

                    shown[listing.Key] = listing;
                    listing.IsFavorite = _favoriteKeys.Contains(listing.Key);

                    if (seen is not null && _activeSearch is not null)
                    {
                        listing.IsNew = !seen.Contains(listing.Key);
                        if (listing.IsNew) newCount++;
                    }

                    if (listing.IsNew) _results.Insert(newTopIndex++, listing);
                    else _results.Add(listing);

                    tab.ResultCount++;
                    added.Add(listing.Key);
                }

                if (_activeSearch is not null && added.Count > 0)
                    _history.MarkSeen(_activeSearch.Id, added);

                if (added.Count > 0)
                {
                    UpdateEmptyHints();

                    // Meteen tonen wat er binnen is. Zo kijk je al naar pagina één
                    // terwijl de rest nog onderweg is, en groeit het aantal
                    // pagina's onder je handen mee.
                    ToonPagina();
                }
            }

            // Zolang deze zoekopdracht loopt mogen alle browsersites dezelfde Chrome
            // gebruiken. Zodra we hier buiten gaan, sluit hij.
            using var browserLease = BrowserPool.Lease();

            var done = 0;

            // Eén site doorzoeken. Wordt zowel tegelijk als na elkaar gebruikt.
            async Task Doorzoek(SiteTab tab)
            {
                try
                {
                    var start = DateTime.Now;
                    Log.Write($"zoeken: {tab.Name} gestart voor '{query}'");

                    // Progress roept AddBatch op de schermdraad aan, want dit object
                    // wordt hier op die draad gemaakt. Elke site krijgt zijn eigen
                    // melder, zodat de juiste filters meegaan.
                    var progress = new Progress<List<Listing>>(list => AddBatch(list, tab));

                    var fromSource = await tab.Source!.SearchAsync(
                        query, tab.MaxResults, tab.Filters, progress);

                    var seconds = (DateTime.Now - start).TotalSeconds;

                    // Wat via progress al binnenkwam, wordt hier overgeslagen.
                    AddBatch(fromSource, tab);

                    tellingen[tab.Name] = fromSource.Count;

                    // Nul, terwijl deze bewaarde zoekopdracht hier vorige keer tien of meer vond:
                    // dan is de site waarschijnlijk stuk, en dat hoort op de tab te staan.
                    if (_activeSearch?.VerdachtLeeg(tab.Name, fromSource.Count) is { } verdacht)
                    {
                        tab.ErrorText = verdacht;
                        errors.Add($"{tab.Name}: {verdacht}");
                    }

                    Log.Write($"zoeken: {tab.Name} klaar — {fromSource.Count} resultaten in {seconds:F1}s");

                    done++;
                    StatusText.Text = $"({done}/{searching.Count}) {tab.Name}: " +
                                      $"{fromSource.Count} gevonden in {seconds:F1}s";
                }
                catch (Exception ex)
                {
                    done++;
                    Log.Write($"zoeken: {tab.Name} mislukte - {ex.Message}");

                    var melding = FriendlyError.Describe(ex);
                    errors.Add($"{tab.Name}: {melding}");

                    // Bij de site zelf tonen: een waarschuwingsteken op zijn tab, met de
                    // melding als tooltip. De statusregel noemt enkel nog hoeveel sites
                    // mislukten; daar stond vroeger alles achter elkaar, en het einde viel
                    // buiten het venster.
                    tab.ErrorText = melding;
                }
            }

            // Drie rijstroken die tegelijk lopen: de rechtstreekse sites (allemaal samen),
            // de browsersites (na elkaar, ze delen één Chrome-profiel) en de brugsites
            // (na elkaar, één wachtrij). Een browsersite en een brugsite delen niets, dus
            // die hoeven niet op elkaar te wachten. Zie SearchRunner.RunInLanesAsync.
            StatusText.Text = searching.Count == 1
                ? $"{searching[0].Name} doorzoeken..."
                : $"{searching.Count} sites doorzoeken...";

            await SearchRunner.RunInLanesAsync(searching, t => t.Def!, Doorzoek);

            // Teller bij de vastgezette zoekopdracht verversen. Het tijdstip
            // hoort er ook bij: anders denkt de planner dat deze zoekopdracht nog
            // moet draaien en doet hij het meteen nog eens over.
            if (_activeSearch is not null)
            {
                _activeSearch.NewCount = newCount;
                _activeSearch.LastRun = DateTime.Now;

                _history.SetLastRun(_activeSearch.Id, DateTime.Now, newCount);
                _lastOutcomes[_activeSearch.Id] = _results.ToList();
                _history.SaveOutcome(_activeSearch.Id, _results.ToList());

                // Wat er misliep, bij de zoekopdracht bewaren: de lijst met zoekopdrachten
                // toont het dan ook na een herstart.
                var fouten = _tabs.Where(t => !t.IsAll && t.HasError)
                                  .ToDictionary(t => t.Name, t => t.ErrorText);
                _activeSearch.RecordRun(searching.Concat(brugOvergeslagen).Select(t => t.Name), fouten, tellingen);
                _history.Update(_activeSearch);

                UpdateSchedulerHint();
            }

            // Kort houden: dit is één regel onderaan het scherm. Wat er per site misliep,
            // staat bij die site, als waarschuwingsteken op zijn tab.
            var message = $"{_results.Count} resultaten van {aantalSites} site(s)";

            if (_activeSearch is not null) message += $" · {newCount} nieuw";
            message += ".";

            if (brugOvergeslagen.Count > 0)
                message += $" {string.Join(" en ", brugOvergeslagen.Select(t => t.Name))} overgeslagen: " +
                           ChromeLauncher.Describe(brug);

            if (errors.Count > 0)
                message += errors.Count == 1
                    ? " 1 site mislukte; zie het waarschuwingsteken op de tab."
                    : $" {errors.Count} sites mislukten; zie het waarschuwingsteken op de tabs.";

            message += zonderTerm;

            if (aangevuld)
                Log.Write("zoeken: latere leveringen van de brug vulden ontbrekende gegevens aan");

            StatusText.Text = message;
        }
        finally
        {
            SearchRunner.Gate.Release();

            _zoektHandmatig = false;
            Spinner.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;

            _resultsView.Refresh();
        ToonPagina();
            UpdateEmptyHints();
        }
    }
}

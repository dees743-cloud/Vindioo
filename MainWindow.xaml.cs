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

    /// <summary>
    /// Waar het volgende nieuwe zoekertje van deze beurt komt: bovenaan, na de vorige nieuwe.
    /// Geldt voor allebei de wegen naar het scherm - de planner en je eigen zoekopdracht -
    /// want die lopen sinds 23 september 2026 door dezelfde lus (zie <see cref="ToonLading"/>).
    /// </summary>
    private int _nieuwBovenaan;

    /// <summary>Is de gebruiker zelf aan het zoeken? Dan zwijgt de planner op het scherm.</summary>
    private bool _zoektHandmatig;

    /// <summary>
    /// Waarmee de lopende zoekopdracht van het scherm afgebroken wordt, of null wanneer er niets
    /// loopt. Het is meteen de stand van de knop naast de zoekbalk: staat hij er, dan is het
    /// vergrootglas een stopknop (zie <see cref="SearchButton_Click"/>).
    /// </summary>
    private CancellationTokenSource? _stoppen;

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

        // Kijkt het scherm mee, dan mogen de resultaten ook tussentijds binnenlopen, zoals bij
        // zelf zoeken. Scheduler_Started heeft dat net beslist; zit de app in het systeemvak,
        // dan kijkt niemand mee en wordt er niets tussentijds opgehaald.
        _scheduler.WordtGetoond = s => _plannerOpScherm == s.Id;
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
            ListingSort.EndingSoonest => SortEnding,
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

        VersieTekst.Text = Versie.Volledig;

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

        // In het logboek: wanneer het venster getoond wordt en of het daarna echt tekent.
        DisplayDiagnostics.VolgVenster(this, "hoofdvenster");

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
    /// Een gewone start: het venster staat er, en dan pas het pictogram in het systeemvak en
    /// de planner (zie <see cref="StartAchtergrondAsync"/>).
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Meteen kunnen typen: de zoekbalk had bij het opstarten geen focus.
        QueryBox.Focus();

        await StartAchtergrondAsync();
    }

    /// <summary>
    /// Een start door Windows (<c>--systeemvak</c>, of de instelling "meteen in het
    /// systeemvak"): het pictogram en de planner, zonder het venster ooit te tonen. Dat wordt
    /// pas gemaakt en getekend wanneer je het opent.
    ///
    /// Waarom: bij het opstarten van de pc bleef het venster soms spierwit, tot je Zentrix
    /// herstartte (22 september 2026). De app zelf liep gewoon - de planner zocht en stuurde
    /// een melding - enkel het tekenen faalde. Tot dan werd het venster ook bij een start door
    /// Windows eerst getoond en meteen weer verborgen, een minuut na het aanmelden, terwijl
    /// Windows en de grafische kaart nog aan het opstarten waren. Het vlak waarop de kaart
    /// tekent, werd dus op dat moment gemaakt. Nu bestaat dat vlak pas wanneer je kijkt.
    /// Een start via het systeemvak zonder pc-start gaf dat witte venster nooit.
    /// </summary>
    public async void StartOpAchtergrond()
    {
        Log.Write("gestart in het systeemvak, zonder venster: dat wordt pas getekend als je het opent");
        await StartAchtergrondAsync();
    }

    /// <summary>
    /// Zijn het pictogram in het systeemvak en de planner al gestart? Dat mag maar één keer:
    /// bij een start door Windows gebeurt het zonder venster, en wanneer je het venster daarna
    /// voor het eerst opent, komt <see cref="MainWindow_Loaded"/> alsnog.
    /// </summary>
    public bool AchtergrondGestart { get; private set; }

    /// <summary>
    /// Het pictogram in het systeemvak, de planner, en wat er bij het opstarten moet draaien.
    /// Niet in de constructor: dan zou je bij het starten naar een bevroren app kijken.
    /// </summary>
    private async Task StartAchtergrondAsync()
    {
        if (AchtergrondGestart) return;
        AchtergrondGestart = true;

        _tray = new TrayIcon(this) { SearchNowRequested = TraySearchNow };

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
        _nieuwBovenaan = 0;
        _heeftGezocht = true;
        _enkelNieuw = false;

        ToonPagina();
        UpdateEmptyHints();
    }

    /// <summary>
    /// Er is weer een lading zoekertjes binnen, van de planner of van je eigen zoekopdracht.
    /// Ze zijn al ontdubbeld en hun NIEUW-vlag staat al goed (<see cref="SearchRunner"/>); wat
    /// hier gebeurt is enkel tonen: nieuwe bovenaan, de rest erachter, de teller op de tab
    /// bijwerken en de zichtbare pagina verversen.
    ///
    /// Het scherm kijkt niet naar de prijsgrens: alles wat binnenkwam blijft in
    /// <see cref="_results"/> staan, zodat een ruimere prijs meteen meer toont zonder opnieuw
    /// te zoeken. Wat er te zien is, bepaalt <see cref="HoortInHuidigeTab"/>.
    /// </summary>
    private void ToonLading(IReadOnlyList<Listing> lading)
    {
        if (lading.Count == 0) return;

        foreach (var listing in lading)
        {
            listing.IsFavorite = _favoriteKeys.Contains(listing.Key);

            if (listing.IsNew) _results.Insert(_nieuwBovenaan++, listing);
            else _results.Add(listing);

            var tab = _tabs.FirstOrDefault(t => !t.IsAll && t.Name == listing.Source);
            if (tab is not null) tab.ResultCount++;
        }

        UpdateEmptyHints();

        // Meteen tonen wat er binnen is. Zo kijk je al naar pagina één terwijl de rest nog
        // onderweg is, en groeit het aantal pagina's onder je handen mee.
        ToonPagina();
    }

    /// <summary>Eén lading van de planner, wanneer die op het scherm meeloopt.</summary>
    private void Scheduler_Delivered(SavedSearch search, IReadOnlyList<Listing> lading)
    {
        if (_plannerOpScherm != search.Id) return;

        // Intussen zelf een zoekopdracht gestart? Dan is het scherm van jou.
        if (_zoektHandmatig)
        {
            _plannerOpScherm = null;
            return;
        }

        ToonLading(lading);
    }

    /// <summary>
    /// Bewaart wat een beurt opleverde, op een achtergronddraad. Op de schermdraad kostte dat
    /// bij 4000 zoekertjes 50 tot 79 ms (gemeten op 22 september 2026): het scherm haperde
    /// precies wanneer de resultaten klaar waren, en dat eens per zoekopdracht. De lijst is een
    /// kopie, dus wat er daarna in de resultaten verandert, raakt ze niet.
    ///
    /// Mislukt het, dan staat dat in het logboek. De resultaten staan dan nog in het geheugen
    /// (<see cref="_lastOutcomes"/>), alleen niet meer na een herstart.
    /// </summary>
    private Task BewaarUitkomstAsync(int searchId, List<Listing> lijst) => Task.Run(() =>
    {
        try
        {
            _history.SaveOutcome(searchId, lijst);
        }
        catch (Exception ex)
        {
            Log.Write($"de resultaten van zoekopdracht {searchId} konden niet bewaard worden - {ex.Message}");
        }
    });

    /// <summary>
    /// De planner is klaar met een zoekopdracht. De resultaten worden altijd
    /// bewaard, ook als niemand keek; stonden ze op het scherm, dan komt er
    /// nog de laatste hand bij.
    ///
    /// Het bewaren wacht hier niet: het slot van de planner is al vrij. Schrijft de volgende
    /// zoekopdracht intussen iets, dan wacht SQLite tot deze klaar is.
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
            _ = BewaarUitkomstAsync(search.Id, outcome.All.ToList());
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

        return string.Join("|", f.Postcode, f.RadiusKm, eigen);
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
              : ReferenceEquals(sender, SortEnding) ? ListingSort.EndingSoonest
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

        FavoritesCount.Text = (_favorites.Count == 1 ? "1 bewaard" : $"{_favorites.Count} bewaard")
                              + _watchSamenvatting;
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

        // De schakelaar staat aan, en op deze tab is er niets wat je nog niet bekeek.
        if (_enkelNieuw)
            return (_active.IsAll ? "Niets nieuws." : $"Niets nieuws op {_active.Name}.") +
                   " Zet 'Enkel nieuwe' uit om alles te zien.";

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

        // Wat er bij het vorige nakijken uitkwam, gaat mee weg: de lijst is opnieuw
        // ingelezen, dus die regels horen bij zoekertjes die er niet meer staan.
        _watchSamenvatting = "";
    }

    /// <summary>Loopt er een controle, dan is dit haar stopknop.</summary>
    private CancellationTokenSource? _watchStop;

    /// <summary>"· 1 weg, 2 afgelopen", achter het aantal bewaarde favorieten.</summary>
    private string _watchSamenvatting = "";

    /// <summary>
    /// Kijkt elke favoriet na: staat het zoekertje er nog, en wat kost het nu?
    ///
    /// Een favoriet is een kopie, dus de prijs erop is die van de dag dat je hem bewaarde.
    /// <see cref="FavoriteWatch"/> haalt de advertentiepagina op en zegt wat ze vandaag doet;
    /// hier komt enkel het tonen bij.
    ///
    /// Eén voor een, en niet allemaal tegelijk: een brugsite heeft één wachtrij en de
    /// browsersites delen één Chrome, dus tegelijk zou daar toch op elkaar staan wachten.
    /// Daarom is de knop intussen een stopknop - dezelfde vorm als het vergrootglas op het
    /// zoekscherm en de AI-controle, en om dezelfde reden: daar staat je muis al.
    /// </summary>
    private async void WatchFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (_watchStop is not null)
        {
            _watchStop.Cancel();
            return;
        }

        if (_favorites.Count == 0) return;

        using var stop = new CancellationTokenSource();
        _watchStop = stop;
        ZetNakijkknop(true);

        int weg = 0, afgelopen = 0, gewijzigd = 0, gedaan = 0;

        try
        {
            // Een kopie van de lijst: het sterretje kan er intussen een afhalen.
            foreach (var favoriet in _favorites.ToList())
            {
                stop.Token.ThrowIfCancellationRequested();
                FavoritesCount.Text = $"nakijken... {gedaan + 1} van {_favorites.Count}";

                var status = await FavoriteWatch.CheckAsync(favoriet, _store.Sites, stop.Token);

                favoriet.WatchText = FavoriteWatch.Tekst(status, favoriet.Price);
                favoriet.WatchIsWarning = status.Staat is FavoriteState.Weg or FavoriteState.Afgelopen;

                if (status.Staat == FavoriteState.Weg) weg++;
                else if (status.Staat == FavoriteState.Afgelopen) afgelopen++;
                else if (status.PrijsNu is > 0 && favoriet.Price is > 0 &&
                         status.PrijsNu != favoriet.Price) gewijzigd++;

                gedaan++;
            }
        }
        catch (OperationCanceledException)
        {
            // Gestopt. Wat al nagekeken was, blijft op de kaarten staan.
        }
        finally
        {
            _watchStop = null;
            ZetNakijkknop(false);

            _watchSamenvatting = Samenvatting(gedaan, weg, afgelopen, gewijzigd);
            UpdateEmptyHints();
        }
    }

    /// <summary>Wat er achter "3 bewaard" komt te staan.</summary>
    private static string Samenvatting(int gedaan, int weg, int afgelopen, int gewijzigd)
    {
        if (gedaan == 0) return "";

        var delen = new List<string>();
        if (weg > 0) delen.Add($"{weg} weg");
        if (afgelopen > 0) delen.Add($"{afgelopen} afgelopen");
        if (gewijzigd > 0) delen.Add($"{gewijzigd} met een andere prijs");

        return delen.Count == 0 ? " · alles staat er nog" : " · " + string.Join(", ", delen);
    }

    private void ZetNakijkknop(bool bezig)
    {
        WatchButton.Content = bezig ? "Stoppen" : "Nakijken";
        WatchButton.Icon = new Wpf.Ui.Controls.SymbolIcon(
            bezig ? Wpf.Ui.Controls.SymbolRegular.Stop24 : Wpf.Ui.Controls.SymbolRegular.ArrowSync24);

        WatchButton.Appearance = bezig
            ? Wpf.Ui.Controls.ControlAppearance.Caution
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
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

    /// <summary>
    /// Zet de zoekterm en de filters van deze zoekopdracht klaar en toont zijn
    /// resultaten. Heeft de planner die net nog opgehaald, dan tonen we díe in
    /// plaats van opnieuw te gaan zoeken — bij sites via de brug scheelt dat al
    /// gauw een halve minuut wachten.
    /// </summary>
    private void SavedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SavedList.SelectedItem is SavedSearch search) OpenSaved(search, enkelNieuw: false);
    }

    /// <summary>
    /// Enter doet hetzelfde als dubbelklikken; Delete hetzelfde als het vuilbakje. Zo kan je
    /// ook zonder muis verwijderen, nu de knop "Verwijderen" bovenaan weg is.
    /// </summary>
    private void SavedList_KeyDown(object sender, KeyEventArgs e)
    {
        if (SavedList.SelectedItem is not SavedSearch search) return;

        if (e.Key == Key.Enter) OpenSaved(search, enkelNieuw: false);
        else if (e.Key == Key.Delete) DeleteSaved(search);
        else return;

        e.Handled = true;
    }

    /// <summary>
    /// De teller bij een zoekopdracht: opent haar met enkel wat je nog niet bekeek. Met de
    /// schakelaar "Enkel nieuwe" boven de resultaten zie je daarna alles.
    /// </summary>
    private void NewBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedSearch search) return;

        SavedList.SelectedItem = search;
        OpenSaved(search, enkelNieuw: true);
    }

    /// <param name="enkelNieuw">Enkel tonen wat je nog niet bekeek (de schakelaar "Enkel nieuwe" aan).</param>
    private void OpenSaved(SavedSearch search, bool enkelNieuw)
    {
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

        if (bewaard.Count == 0)
        {
            _ = RunSearchAsync();
            return;
        }

        // Wat nieuw is, opnieuw bepalen in plaats van de vlag van die beurt te geloven: die
        // zei "nog niet bekeken" op het moment van de beurt, en misschien heb je de lijst
        // intussen al geopend.
        var gezien = _history.GetSeen(search.Id);
        search.LastViewed = _history.GetLastViewed(search.Id);

        foreach (var listing in bewaard)
            listing.IsNew = search.IsUnviewed(gezien.TryGetValue(listing.Key, out var eerst) ? eerst : null);

        _enkelNieuw = enkelNieuw && bewaard.Any(l => l.IsNew);
        ToonBewaard(search, bewaard);

        // Nu heb je ze gezien. Het NIEUW-label blijft staan zolang deze lijst op het scherm
        // staat; pas bij de volgende keer openen tellen ze als bekeken.
        search.LastViewed = DateTimeOffset.Now;
        search.NewCount = 0;
        _history.SetViewed(search.Id, search.LastViewed.Value);
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

        StatusText.Text = _enkelNieuw
            ? $"'{search.Name}': de {nieuw} die je nog niet zag, van {resultaten.Count} resultaten {wanneer}. " +
              "Zet 'Enkel nieuwe' uit om alles te zien."
            : $"'{search.Name}': {resultaten.Count} resultaten {wanneer}"
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

        // Wat nu op het scherm staat, geldt als gezien én bekeken: pas morgen is er iets
        // nieuw. Bekeken na het markeren, anders telt wat net gemarkeerd werd als nieuwer.
        if (_results.Count > 0)
            _history.MarkSeen(search.Id, _results.Select(r => r.Key));

        search.LastViewed = DateTimeOffset.Now;
        _history.SetViewed(search.Id, search.LastViewed.Value);

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
                                .Select(t => new SiteSetting { Site = t.Name }).ToList()
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
    /// De andere richting van <see cref="PasToe"/>: wat je op het scherm aan de sites en hun
    /// filters wijzigde terwijl een bewaarde zoekopdracht openstond, gaat mee in die
    /// zoekopdracht. Zo zoekt de planner straks met dezelfde waarden.
    ///
    /// Tot 22 september 2026 gebeurde dat niet, en dat gaf een stil verschil: zet je de prijs
    /// of de postcode anders en laat je opnieuw zoeken, dan gebruikte die beurt de nieuwe
    /// waarde - met de resultaten, de teller en het tijdstip onder die zoekopdracht - terwijl
    /// de volgende geplande beurt nog met de oude waarden liep. Zo koos de eigenaar het; de
    /// statusregel zegt wat er bewaard is, want stil bewaren is even verwarrend als stil
    /// vergeten. Gevonden bij het vergelijken van de twee zoeklussen (zie Volgende stappen).
    ///
    /// Een site die in de zoekopdracht staat maar geen tab heeft - verwijderd of hernoemd -
    /// blijft staan: de planner meldt die als fout, en dat mag niet stil verdwijnen.
    /// </summary>
    /// <returns>Wat er overgenomen werd, voor de statusregel; leeg als er niets veranderde.</returns>
    private string NeemSchermfiltersOver(SavedSearch search)
    {
        static string Vorm(SiteSetting s) =>
            $"{s.Enabled}|{s.PriceMin}|{s.PriceMax}|{s.Postcode}|{s.RadiusKm}|" +
            string.Join(",", s.Custom.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        var zonderTab = search.SiteSettings
            .Where(s => !_tabs.Any(t => !t.IsAll && string.Equals(t.Name, s.Site, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var nieuw = _tabs.Where(t => !t.IsAll).Select(SiteSetting.FromTab).Concat(zonderTab).ToList();

        var sites = false;
        var filters = false;

        foreach (var s in nieuw)
        {
            var oud = search.For(s.Site);

            if (oud is null) { sites = true; continue; }
            if (oud.Enabled != s.Enabled) sites = true;
            if (Vorm(oud) != Vorm(s) && oud.Enabled == s.Enabled) filters = true;
        }

        if (!sites && !filters && nieuw.Count == search.SiteSettings.Count) return "";

        search.SiteSettings = nieuw;

        return sites && filters ? "sites en filters" : sites ? "sites" : "filters";
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

    /// <summary>Het vuilbakje op de kaart van een zoekopdracht.</summary>
    private void DeleteSaved_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SavedSearch search) DeleteSaved(search);
    }

    private void DeleteSaved(SavedSearch search)
    {
        // Eerst vragen. Een site verwijderen deed dat al; een zoekopdracht niet, terwijl
        // daar het schema en de hele "al gezien"-geschiedenis mee weggaan. Nu het vuilbakje
        // op elke kaart staat, naast het driehoekje, is die vraag er zeker nodig.
        var bevestig = MessageBox.Show(this,
            $"Zoekopdracht '{search.Name}' verwijderen? Het schema en wat al gezien is, gaan ook weg.",
            "Zoekopdracht verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (bevestig != MessageBoxResult.Yes) return;

        _history.Delete(search.Id);
        _lastOutcomes.Remove(search.Id);
        if (_activeSearch?.Id == search.Id) _activeSearch = null;

        LoadSavedSearches();
        UpdateEmptyHints();
        UpdateSchedulerHint();
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
    /// Tandwiel > Sites beheren: meteen het venster met een tab per site, open op de site
    /// waarvan de tab nu openstaat (op "Alles": de eerste). Tot september 2026 hing hier een
    /// submenu met elke site apart, dat meegroeide met het aantal sites; in het venster
    /// staan ze toch al als tabs.
    /// </summary>
    private void ManageSitesMenu_Click(object sender, RoutedEventArgs e) => OpenSettings(_active?.Name);

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

    /// <summary>
    /// Rechtsklik op een foto: wat staat erop dat je zelf niet ziet? Eigen venster, om dezelfde
    /// reden als de prijsindicatie - het kijken duurt een halve minuut, en intussen wil je verder
    /// kunnen. Het rekenwerk gebeurt op je eigen grafische kaart; zie <see cref="PhotoAnalyzer"/>.
    /// </summary>
    private void PhotoInsightMenu_Click(object sender, RoutedEventArgs e) => OpenAiControle(sender, false);

    /// <summary>
    /// Hetzelfde, maar dan ook de andere foto's van die advertentie. De zoekpagina geeft er één;
    /// op de pagina van het zoekertje staan er vijf of tien, en juist daarop staat vaak wat je
    /// wil zien - het label achteraan, de doos van binnen.
    /// </summary>
    private void PhotoInsightAllMenu_Click(object sender, RoutedEventArgs e) => OpenAiControle(sender, true);

    private void OpenAiControle(object sender, bool alleFotos)
    {
        if ((sender as FrameworkElement)?.DataContext is not Listing listing) return;

        new PhotoInsightWindow(listing, _store.Sites, alleFotos) { Owner = this }.Show();
    }

    /// <summary>
    /// Opent het geselecteerde zoekertje in een eigen venster: de foto's, de verkoper en
    /// hoelang het online staat. Tot september 2026 ging hier meteen de browser open; die
    /// staat nu als knop in dat venster. De reden staat bij <see cref="ListingDetailWindow"/>.
    /// </summary>
    private void OpenSelected(ListBox? lijst)
    {
        if (lijst?.SelectedItem is not Listing listing) return;

        new ListingDetailWindow(listing, _store.Sites) { Owner = this }.Show();
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

            // Wat er overblijft aan echte fouten: de brugsites en de verdwenen sites staan ook
            // in SiteErrors, maar die krijgen hieronder hun eigen zin.
            var mislukt = outcome.SiteErrors.Count - brugOvergeslagen.Count - verdwenen.Count;

            var message = $"{_results.Count} resultaten van {searching.Count} site(s)";

            if (_activeSearch is not null) message += $" · {_activeSearch.NewCount} nieuw";
            message += ".";

            if (brugOvergeslagen.Count > 0)
                message += $" {string.Join(" en ", brugOvergeslagen.Select(t => t.Name))} overgeslagen: " +
                           ChromeLauncher.Describe(outcome.Bridge);

            if (verdwenen.Count > 0)
                message += $" {string.Join(" en ", verdwenen)} " +
                           $"{(verdwenen.Count == 1 ? "bestaat" : "bestaan")} niet meer in Sites beheren.";

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

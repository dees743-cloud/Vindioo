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
using Vindioo.Controls;
using Vindioo.Models;
using Vindioo.Services;
using Vindioo.Sources;

namespace Vindioo;

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

        // Het raster laat weten hoeveel kaarten er naast elkaar passen. Daar hangt de
        // paginagrootte van af, zodat de laatste rij vol staat; zie PaginaGrootte.
        ResultsList.AddHandler(Controls.VirtualizingWrapPanel.KolommenGewijzigdEvent,
                               new RoutedEventHandler(Kolommen_Gewijzigd));

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

        // De favorieten vanzelf nakijken, zodat er meteen een prijs van vandaag staat in
        // plaats van die van de dag dat je ze bewaarde. De kaart toont ondertussen al de
        // laatst bekende prijs uit de databank, dus dit is bijwerken en geen wachten.
        //
        // Op ApplicationIdle en op de achtergrond: dit doet een verzoek per favoriet, en
        // het opstarten van het venster mag daar niet op blijven staan.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await NakijkFavorietenAsync();
            }
            catch (Exception ex)
            {
                Log.Write($"favorieten nakijken bij het opstarten mislukte - {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Luisteren naar de browserextensie. Is de poort bezet, dan werkt de rest gewoon.
        //
        // De extensie heeft sinds 1 oktober 2026 toestemming per site nodig, en ze kan die enkel
        // vragen voor sites die ze kent. Daarom geven we haar de hosts van de brugsites; dan
        // staan ze meteen in haar popup, in plaats van pas nadat een zoekopdracht één keer
        // misliep. Als functie en niet als lijst, want er komen sites bij terwijl de app draait.
        BridgeServer.Instance.BridgeHosts = () => _store.Sites
            .Where(s => s.UseBridge)
            .SelectMany(SiteUrlCheck.Hosts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Rechtsklikken in Chrome op een zoekertje, en het staat bij je favorieten. Dit is de
        // enige weg die van de extensie naar de app loopt in plaats van omgekeerd; FavoriteFromUrl
        // kijkt na wat er binnenkomt voor er iets opgehaald wordt.
        BridgeServer.Instance.FavorietToevoegen = async adres =>
        {
            var uitkomst = await FavoriteFromUrl.VoegToeAsync(adres, _store.Sites, _history);

            // Staat het tabblad Favorieten open, dan hoort het er meteen bij te staan - anders
            // kijk je naar een lijst waar het net bijgekomen zoekertje niet in staat.
            if (uitkomst.Ok)
                await Dispatcher.InvokeAsync(() => { _favoriteKeys.Add(uitkomst.Favoriet!.Key); LoadFavorites(); });

            return uitkomst;
        };

        BridgeServer.Instance.Start();
        if (BridgeServer.Instance.PortBusy)
            StatusText.Text = "Let op: " + ChromeLauncher.Describe(BridgeStatus.PortInUse);

        VersieTekst.Text = Versie.Volledig;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Wie het venster opent, heeft de melding gezien: de tekst bij het pictogram in het
        // systeemvak ("3 nieuw bij ...") bleef anders staan tot de app herstartte.
        Activated += (_, _) => _tray?.SetTooltip("Vindioo");

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
    /// Waarom: bij het opstarten van de pc bleef het venster soms spierwit, tot je Vindioo
    /// herstartte (22 september 2026). De app zelf liep gewoon - de planner zocht en stuurde
    /// een melding - enkel het tekenen faalde. Tot dan werd het venster ook bij een start door
    /// Windows eerst getoond en meteen weer verborgen, een minuut na het aanmelden, terwijl
    /// Windows en de grafische kaart nog aan het opstarten waren. Het vlak waarop de kaart
    /// tekent, werd dus op dat moment gemaakt. Nu bestaat dat vlak pas wanneer je kijkt.
    /// Een start via het systeemvak zonder pc-start gaf dat witte venster nooit.
    /// </summary>
    public async Task StartOpAchtergrondAsync()
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
                _tray.ShowBalloon("Vindioo zoekt verder",
                    "Vindioo draait verder op de achtergrond. Je vindt het pictogram rechtsonder bij de klok, " +
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
            _tray.SetTooltip($"Vindioo — {outcome.New.Count} nieuw bij '{search.Name}'");

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

}

using System.Collections.ObjectModel;
using System.Windows.Threading;
using Vindioo.Models;

namespace Vindioo.Services;

/// <summary>
/// Kijkt elke halve minuut welke zoekopdrachten aan de beurt zijn en voert ze
/// uit. Dit is het stuk dat blijft werken wanneer de app in het systeemvak zit.
///
/// Eén tik, één zoekopdracht tegelijk: er wordt niets parallel gedaan. Twee
/// sites tegelijk doorzoeken botst op de brug (één wachtrij) en op het
/// browserprofiel van Playwright. Het is bovendien de bedoeling dat dit op de
/// achtergrond gebeurt zonder de pc op te eten.
/// </summary>
public class SearchScheduler
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    private readonly ObservableCollection<SavedSearch> _searches;
    private readonly SearchRunner _runner;
    private readonly HistoryStore _history;

    /// <summary>Loopt er nu een beurt? Zo ja, slaan we de tik over.</summary>
    private bool _busy;

    public SearchScheduler(ObservableCollection<SavedSearch> searches,
                           SearchRunner runner, HistoryStore history)
    {
        _searches = searches;
        _runner = runner;
        _history = history;

        _timer.Tick += async (_, _) => await TickAsync();
    }

    /// <summary>Een zoekopdracht begint. Het scherm kan zich klaarzetten om mee te kijken.</summary>
    public event Action<SavedSearch>? Started;

    /// <summary>Eén site van de lopende zoekopdracht is klaar, met wat ze nieuw aanbracht.</summary>
    public event Action<SavedSearch, IReadOnlyList<Listing>>? Delivered;

    /// <summary>Er is een zoekopdracht klaar. Het scherm mag de resultaten tonen.</summary>
    public event Action<SavedSearch, SearchOutcome>? Completed;

    /// <summary>Korte tekst over waar de planner mee bezig is, voor de statusregel.</summary>
    public event Action<string>? Status;

    /// <summary>
    /// Kijkt het scherm mee met deze zoekopdracht? Zo ja, dan worden de resultaten ook
    /// tussentijds doorgegeven (zie <see cref="SearchRunner.RunAsync"/>), zodat de eerste
    /// zoekertjes er staan terwijl een site nog bezig is. Het scherm beslist dat zelf, in
    /// <c>Scheduler_Started</c>: staat het venster in het systeemvak, dan kijkt niemand mee.
    ///
    /// Waarom een vraag en geen gebeurtenis: <see cref="Delivered"/> heeft altijd een
    /// luisteraar (het hoofdscherm), ook wanneer die de lading meteen weggooit.
    /// </summary>
    public Func<SavedSearch, bool>? WordtGetoond { get; set; }

    /// <summary>Staat er minstens één zoekopdracht op een schema?</summary>
    public bool AnyScheduled => _searches.Any(s => s.Schedule.Mode != ScheduleMode.Off);

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>
    /// Draait alles wat "ook bij opstarten" aangevinkt heeft. Wordt met opzet
    /// niet meteen bij het bouwen van het venster aangeroepen: eerst mag het
    /// scherm staan, anders kijk je bij het starten naar een bevroren app.
    /// </summary>
    public async Task RunStartupSearchesAsync()
    {
        var lijst = _searches.Where(s => s.Schedule.RunOnStartup).ToList();
        if (lijst.Count == 0) return;

        Log.Write($"planner: {lijst.Count} zoekopdracht(en) bij het opstarten");

        foreach (var search in lijst) await RunAsync(search);
    }

    /// <summary>Voert één zoekopdracht nu meteen uit, ongeacht zijn schema.</summary>
    public async Task RunAsync(SavedSearch search)
    {
        if (search.IsRunning) return;

        search.IsRunning = true;
        Status?.Invoke($"Zoekopdracht '{search.Name}' draait...");

        try
        {
            // Binnen de try, zodat een fout in wie meekijkt in het logboek belandt.
            Started?.Invoke(search);

            var melder = new Progress<string>(tekst => Status?.Invoke($"'{search.Name}': {tekst}"));
            var outcome = await _runner.RunAsync(search, markSeen: true, status: melder,
                                                 delivered: lading => Delivered?.Invoke(search, lading),
                                                 tussentijds: WordtGetoond?.Invoke(search) == true);

            search.RefreshStatus();

            var meldingOk = true;

            if (outcome.New.Count > 0 && search.Schedule.NotifyOnNew)
                meldingOk &= await Notifier.NotifyNewAsync(search, outcome.New);

            // Mislukt een site voor de tweede keer op rij, dan ook dat melden - via
            // dezelfde kanalen, want wie een melding wil over nieuwe zoekertjes, wil
            // ook weten wanneer er geen meer kunnen komen.
            if (outcome.JustBroken.Count > 0 && search.Schedule.NotifyOnNew)
                meldingOk &= await Notifier.NotifyProblemAsync(search, outcome.JustBroken);

            Completed?.Invoke(search, outcome);

            // Niets gezocht: dat zeggen, en niet "niets nieuws (0 resultaten)".
            if (outcome.NotRunReason is not null)
            {
                Status?.Invoke($"'{search.Name}' is niet uitgevoerd: {outcome.NotRunReason}.");
                return;
            }

            // "Niets nieuws" terwijl er sites mislukten, zou misleiden: dan staat niet
            // vast dat er niets nieuws is. Een site die dit zoekwoord niet kent, hoort daar
            // niet bij: die heeft geantwoord, en haar antwoord is "dit gaat niet over mij".
            var mislukt = SearchRunner.EchteFouten(outcome).Count switch
            {
                0 => "",
                1 => " 1 site mislukte; zie de zoekopdracht.",
                var n => $" {n} sites mislukten; zie de zoekopdracht."
            };

            var nietVanToepassing = outcome.QueryNotSupported.Count == 0
                ? ""
                : $" {string.Join(" en ", outcome.QueryNotSupported)} " +
                  $"{(outcome.QueryNotSupported.Count == 1 ? "kent" : "kennen")} dit zoekwoord niet.";

            // Nieuw bij deze beurt, en daarnaast wat vorige beurten vonden en je nog niet
            // bekeek. Zonder dat tweede stond er "niets nieuws" terwijl de teller 48 zei.
            var nogNiet = search.NewCount - outcome.New.Count;
            var ookNog = nogNiet > 0 ? $" Nog {nogNiet} van eerder niet bekeken; klik op de teller." : "";

            Status?.Invoke((outcome.New.Count > 0
                ? $"'{search.Name}': {outcome.New.Count} nieuw van {outcome.All.Count}."
                : $"'{search.Name}': niets nieuws sinds de vorige beurt ({outcome.All.Count} resultaten).") +
                ookNog + mislukt + nietVanToepassing +
                (meldingOk ? "" : " De melding kon nergens verstuurd worden; zie Meldingen en achtergrond."));
        }
        catch (Exception ex)
        {
            Log.Write($"planner: '{search.Name}' liep vast - {ex.Message}");
            Status?.Invoke($"'{search.Name}' mislukte: {ex.Message}");

            // Toch als gedraaid noteren. Anders blijft deze zoekopdracht aan de beurt, neemt
            // de planner haar bij elke tik opnieuw, en komen de andere nooit meer aan bod.
            try
            {
                search.LastRun = DateTime.Now;
                _history.SetLastRun(search.Id, search.LastRun.Value, search.NewCount);
            }
            catch (Exception fout)
            {
                Log.Write($"planner: tijdstip van '{search.Name}' niet bewaard - {fout.Message}");
            }
        }
        finally
        {
            search.IsRunning = false;
        }
    }

    /// <summary>
    /// De tik. Zoekt de eerste zoekopdracht die aan de beurt is en voert enkel
    /// díe uit; de volgende tik neemt de volgende. Zo blijft er tussen twee
    /// zoekopdrachten ruimte voor iets anders.
    /// </summary>
    internal async Task TickAsync()
    {
        var nu = DateTime.Now;

        // Vóór de rem hieronder, en met opzet: dit kost geen enkel verzoek (het werkt met het
        // einde dat al in de databank staat), dus het hoeft niet te wachten tot een zoekopdracht
        // klaar is. Een veiling die sluit terwijl de planner een trage site afwerkt, zou anders
        // pas achteraf gemeld worden - of niet meer.
        try
        {
            await AuctionWatch.TickAsync(_history, nu);
        }
        catch (Exception ex)
        {
            // Een melding die mislukt, mag de planner niet stilleggen.
            Log.Write($"planner: de veilingwaarschuwing liep vast - {ex.Message}");
        }

        if (_busy) return;

        var aanDeBeurt = _searches.FirstOrDefault(s =>
            !s.IsRunning &&
            s.Schedule.Mode != ScheduleMode.Off &&
            s.Schedule.WithinWindow(nu) &&
            s.Schedule.NextRun(s.LastRun, nu) is { } moment && moment <= nu);

        if (aanDeBeurt is null) return;

        _busy = true;

        try
        {
            await RunAsync(aanDeBeurt);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Wanneer de eerstvolgende zoekopdracht aan de beurt is, of null wanneer er
    /// niets op een schema staat. Voor de tekst onder het pictogram in het
    /// systeemvak.
    /// </summary>
    public DateTime? NextDue()
    {
        DateTime? eerste = null;

        foreach (var search in _searches)
        {
            if (search.Schedule.Mode == ScheduleMode.Off) continue;

            var moment = search.Schedule.NextRun(search.LastRun);
            if (moment is null) continue;

            if (eerste is null || moment < eerste) eerste = moment;
        }

        return eerste;
    }

    /// <summary>Schrijft een gewijzigde zoekopdracht weg en ververst zijn regel.</summary>
    public void Save(SavedSearch search)
    {
        _history.Update(search);
        search.RefreshStatus();
    }
}

using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>Wat één keer draaien van een zoekopdracht opleverde.</summary>
public class SearchOutcome
{
    /// <summary>Alles wat er gevonden is, na de filters en de verfijning.</summary>
    public List<Listing> All { get; } = new();

    /// <summary>Wat er bij deze beurt voor het eerst bij was.</summary>
    public List<Listing> New { get; } = new();

    /// <summary>Wat er misliep, als één regel per probleem: "site: melding".</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Dezelfde fouten per site, voor de zoekopdracht zelf (zie <see cref="SavedSearch.LastErrors"/>).</summary>
    public Dictionary<string, string> SiteErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// De sites die bij deze beurt voor de tweede keer op rij mislukten. Daarover
    /// hoort een melding te komen; zie <see cref="SavedSearch.RecordRun"/>.
    /// </summary>
    public List<string> JustBroken { get; } = new();

    /// <summary>Hoe het klaarzetten van de brug afliep; Ready wanneer er geen brugsite meedeed.</summary>
    public BridgeStatus Bridge { get; set; } = BridgeStatus.Ready;

    /// <summary>Hoeveel zoekertjes elke site gaf, ook 0; zie <see cref="SavedSearch.LastCounts"/>.</summary>
    public Dictionary<string, int> SiteCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Waarom er helemaal niet gezocht is (geen site aangevinkt, geen zoekterm), of null
    /// wanneer er wel gezocht werd. Dan is een lege lijst geen uitkomst, en mag ze de vorige
    /// resultaten niet vervangen.
    /// </summary>
    public string? NotRunReason { get; set; }

}

/// <summary>
/// Voert een <see cref="SavedSearch"/> uit zonder ook maar iets van het scherm
/// nodig te hebben. Dat is de kern van het automatisch zoeken: dezelfde code
/// draait of je nu op het vergrootglas klikt of de app in het systeemvak staat.
///
/// Het hoofdscherm houdt zijn eigen lus, want dat wil resultaten tonen zodra ze
/// binnenkomen. Wat ze delen is <see cref="Gate"/> - twee zoekopdrachten tegelijk
/// gaat niet goed: de brug heeft één wachtrij, en twee Playwright-sessies op
/// hetzelfde browserprofiel botsen - en <see cref="RunInLanesAsync"/>, dat bepaalt
/// welke sites tegelijk mogen.
/// </summary>
public class SearchRunner
{
    /// <summary>
    /// Er mag er maar één tegelijk zoeken. Ook het hoofdscherm neemt dit slot,
    /// zodat een geplande zoekopdracht niet dwars door een handmatige loopt.
    /// </summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly SiteStore _store;
    private readonly HistoryStore _history;

    public SearchRunner(SiteStore store, HistoryStore history)
    {
        _store = store;
        _history = history;
    }

    /// <summary>
    /// Doorzoekt sites in drie rijstroken die tegelijk lopen:
    ///
    ///   - de rechtstreekse sites, allemaal samen: ze delen niets;
    ///   - de browsersites, na elkaar: ze delen één Chrome met één profiel;
    ///   - de brugsites, na elkaar: de brug heeft één wachtrij.
    ///
    /// Binnen een rijstrook blijft het na elkaar, maar een browsersite en een brugsite
    /// hebben niets met elkaar te maken. Tot september 2026 wachtten ze toch op elkaar,
    /// en toen duurde "marantz" over acht sites 53,7 seconden; met rijstroken zo'n 27.
    ///
    /// <paramref name="work"/> moet zijn eigen fouten opvangen: een site die mislukt,
    /// mag de andere rijstroken niet laten vallen.
    /// </summary>
    public static Task RunInLanesAsync<T>(IReadOnlyCollection<T> items, Func<T, SiteDefinition> def,
                                          Func<T, Task> work)
    {
        var rechtstreeks = items.Where(i => !IsBridgeSite(def(i)) && !IsBrowserSite(def(i))).ToList();
        var browser = items.Where(i => IsBrowserSite(def(i))).ToList();
        var brug = items.Where(i => IsBridgeSite(def(i))).ToList();

        return Task.WhenAll(
            Task.WhenAll(rechtstreeks.Select(work)),
            NaElkaarAsync(browser, work),
            NaElkaarAsync(brug, work));
    }

    private static bool IsBridgeSite(SiteDefinition def) => def.UseBridge;

    /// <summary>
    /// Gebruikt deze site de gedeelde Chrome van Playwright? De linkmotor doet dat
    /// altijd, ook als het vinkje "browser" in het bestand vergeten is.
    /// </summary>
    private static bool IsBrowserSite(SiteDefinition def) =>
        !def.UseBridge && (def.NeedsBrowser || def.Engine == SiteEngine.LinkText);

    private static async Task NaElkaarAsync<T>(IEnumerable<T> items, Func<T, Task> work)
    {
        foreach (var item in items) await work(item);
    }

    /// <summary>
    /// Draait de zoekopdracht: elke aangevinkte site af, de resultaten
    /// samenvoegen, de verfijning toepassen en bepalen wat er nieuw is.
    /// </summary>
    /// <param name="markSeen">
    /// Onthouden dat deze resultaten gezien zijn. Bij een echte beurt hoort dat
    /// te gebeuren; bij een proefdraai (de knop "Nu uitvoeren" in de
    /// instellingen) niet, anders is alles de volgende keer plots niet nieuw meer.
    /// </param>
    /// <param name="delivered">
    /// Krijgt na elke site de zoekertjes die die site nieuw aanbracht, met
    /// IsNew al ingevuld. Zo kan het scherm ze tonen terwijl de
    /// volgende site nog bezig is. Wordt aangeroepen op de draad waarop deze
    /// methode loopt — bij de planner is dat de schermdraad.
    /// </param>
    /// <summary>
    /// Een zoekopdracht die niet kan draaien, toch als "gedraaid" noteren. Zonder dat
    /// tijdstip blijft ze voor de planner altijd aan de beurt: hij neemt de eerste die aan
    /// de beurt is, dus elke halve minuut weer deze, en de zoekopdrachten na haar in de
    /// lijst draaiden nooit meer. Zo gaat het nu één keer per schema mis, met de reden in
    /// de statusregel en het logboek.
    /// </summary>
    private SearchOutcome NietUitgevoerd(SavedSearch search, bool markSeen, SearchOutcome outcome, string reden,
                                         IReadOnlyList<string>? verdwenen = null)
    {
        outcome.NotRunReason = reden;
        outcome.Errors.Add(reden);
        Log.Write($"planner: '{search.Name}' niet uitgevoerd - {reden}");

        // Bij "Nu uitvoeren" in het venster van de zoekopdracht (markSeen uit) is het een
        // proefbeurt, en die verzet het schema niet.
        if (markSeen)
        {
            _history.SetLastRun(search.Id, DateTime.Now, 0);
            search.LastRun = DateTime.Now;
            search.NewCount = 0;

            // Verdwenen sites tellen als mislukte beurt: zo komt er na twee keer een melding.
            if (verdwenen is { Count: > 0 })
            {
                outcome.JustBroken.AddRange(search.RecordRun(verdwenen, outcome.SiteErrors));
                _history.Update(search);
            }
        }

        return outcome;
    }

    public async Task<SearchOutcome> RunAsync(SavedSearch search, bool markSeen = true,
                                              IProgress<string>? status = null,
                                              Action<IReadOnlyList<Listing>>? delivered = null,
                                              CancellationToken ct = default)
    {
        var outcome = new SearchOutcome();

        var gekozen = search.SiteSettings
            .Where(s => s.Enabled)
            .Select(s => (Setting: s, Def: _store.Sites.FirstOrDefault(
                d => string.Equals(d.Name, s.Site, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var werk = gekozen
            .Where(p => p.Def is not null)
            .Select(p => (p.Setting, Def: p.Def!))
            .ToList();

        // Aangevinkt, maar niet meer in Sites beheren: verwijderd, of een bestand met een
        // andere naam. Vroeger viel zo'n site stil weg; nu is het een fout van die site, en
        // na twee beurten een melding.
        var verdwenen = gekozen.Where(p => p.Def is null).Select(p => p.Setting.Site).ToList();
        const string verdwenenMelding = "bestaat niet meer in Sites beheren (verwijderd of hernoemd).";

        foreach (var site in verdwenen)
        {
            outcome.SiteErrors[site] = verdwenenMelding;
            outcome.Errors.Add($"{site}: {verdwenenMelding}");
        }

        if (werk.Count == 0)
            return NietUitgevoerd(search, markSeen, outcome, verdwenen.Count > 0
                ? $"de aangevinkte site{(verdwenen.Count > 1 ? "s bestaan" : " bestaat")} niet meer: {string.Join(", ", verdwenen)}"
                : "geen site aangevinkt", verdwenen);

        // Zonder zoekterm blijven enkel de sites over die op filters alleen kunnen
        // zoeken (AutoScout24). Dezelfde regel als in het hoofdscherm, want een
        // zoekopdracht moet hetzelfde doen of je nu kijkt of niet.
        if (string.IsNullOrWhiteSpace(search.Query))
        {
            var overgeslagen = werk.Where(p => !p.Def.AllowsEmptyQuery).Select(p => p.Setting.Site).ToList();
            werk = werk.Where(p => p.Def.AllowsEmptyQuery).ToList();

            if (werk.Count == 0)
                return NietUitgevoerd(search, markSeen, outcome,
                    "geen zoekterm, en geen enkele aangevinkte site kan zoeken op filters alleen");

            if (overgeslagen.Count > 0)
                Log.Write($"planner: geen zoekterm, overgeslagen: {string.Join(", ", overgeslagen)}");
        }

        // Een fout bij een site. De rijstroken lopen tegelijk, dus onder een slot.
        void Fout(string site, string melding)
        {
            lock (outcome)
            {
                outcome.SiteErrors[site] = melding;
                outcome.Errors.Add($"{site}: {melding}");
            }
        }

        await Gate.WaitAsync(ct);

        // Alle browsersites van deze beurt delen één Chrome.
        using var browserLease = BrowserPool.Lease();

        try
        {
            Log.Write($"planner: '{search.Name}' gestart op {werk.Count} site(s)");

            // Sites via de brug hebben een draaiende Chrome nodig. Werkt de brug niet,
            // dan slaan we die sites meteen over: anders wacht elke brugsite nog
            // anderhalve minuut op een antwoord waarvan al vaststaat dat het niet komt.
            if (werk.Any(p => p.Def.UseBridge))
            {
                status?.Report("Chrome klaarzetten voor de brug...");
                outcome.Bridge = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30));

                if (outcome.Bridge != BridgeStatus.Ready)
                {
                    var reden = "overgeslagen, " + ChromeLauncher.Describe(outcome.Bridge);

                    foreach (var (setting, _) in werk.Where(p => p.Def.UseBridge))
                    {
                        Fout(setting.Site, reden);
                        Log.Write($"planner: {setting.Site} {reden}");
                    }
                }
            }

            // Per sleutel één zoekertje. Latere leveringen van dezelfde site
            // vullen aan wat nog ontbrak, net als in het hoofdscherm.
            var gevonden = new Dictionary<string, Listing>();

            // Vooraf ophalen en niet pas op het einde: elke site geeft zijn
            // zoekertjes meteen door, en dan moet al vaststaan wat nieuw is.
            var eerderGezien = _history.GetSeenKeys(search.Id);

            var uitTeVoeren = werk
                .Where(p => outcome.Bridge == BridgeStatus.Ready || !p.Def.UseBridge)
                .ToList();

            await RunInLanesAsync(uitTeVoeren, p => p.Def, async p =>
            {
                var (setting, def) = p;

                ct.ThrowIfCancellationRequested();
                status?.Report($"{setting.Site} doorzoeken...");

                try
                {
                    var bron = SourceFactory.Create(def);
                    var start = DateTime.Now;

                    var resultaten = await bron.SearchAsync(
                        search.Query, setting.MaxResults, setting.ToFilters(), null, ct);

                    lock (outcome) outcome.SiteCounts[setting.Site] = resultaten.Count;

                    if (search.VerdachtLeeg(setting.Site, resultaten.Count) is { } verdacht)
                    {
                        Fout(setting.Site, verdacht);
                        Log.Write($"planner: {setting.Site} {verdacht}");
                    }

                    // Wat deze site nieuw aanbrengt, apart bijhouden om door te geven.
                    var vers = new List<Listing>();

                    // De rijstroken komen hier samen. Bij de planner lopen ze allemaal
                    // verder op de schermdraad, maar een slot kost niets en maakt het
                    // ook veilig wanneer iemand deze methode van elders aanroept.
                    lock (gevonden)
                    {
                        foreach (var listing in resultaten)
                        {
                            if (gevonden.TryGetValue(listing.Key, out var bestaand))
                            {
                                bestaand.MergeFrom(listing);
                                continue;
                            }

                            gevonden[listing.Key] = listing;
                            vers.Add(listing);
                        }
                    }

                    if (delivered is not null && vers.Count > 0)
                    {
                        // De vlaggen nu al zetten, met dezelfde regels als op het
                        // einde. IsNew meldt geen wijziging aan het scherm: een kaart
                        // leest hem één keer, bij het tekenen. Achteraf zetten is dus
                        // te laat — dan verschijnt het NIEUW-label nooit.
                        foreach (var listing in vers)
                        {
                            listing.IsNew = !eerderGezien.Contains(listing.Key) &&
                                            search.Matches(listing) &&
                                            BinnenPrijs(search, listing);
                        }

                        delivered(vers);
                    }

                    Log.Write($"planner: {setting.Site} gaf {resultaten.Count} resultaten " +
                              $"in {(DateTime.Now - start).TotalSeconds:F1}s");
                }
                // Enkel doorgooien als de zoekopdracht zelf gestopt wordt. Een time-out van
                // HttpClient is óók een OperationCanceledException, en die gooide vroeger de hele
                // beurt om: de resultaten van de andere sites gingen verloren, het tijdstip werd
                // niet bewaard, en de planner nam dezelfde zoekopdracht bij elke tik opnieuw.
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Fout(setting.Site, FriendlyError.Describe(ex));
                    Log.Write($"planner: {setting.Site} mislukte - {ex.Message}");
                }
            });

            // Nu pas filteren. De prijsgrens hoort bij de site die hem opgaf, dus
            // die wordt per zoekertje opgezocht; de verfijning met woorden geldt
            // voor de hele zoekopdracht.
            foreach (var listing in gevonden.Values)
            {
                if (!search.Matches(listing)) continue;
                if (!BinnenPrijs(search, listing)) continue;

                outcome.All.Add(listing);
            }

            foreach (var listing in outcome.All)
            {
                if (eerderGezien.Contains(listing.Key)) continue;

                listing.IsNew = true;
                outcome.New.Add(listing);
            }

            if (markSeen)
            {
                if (outcome.All.Count > 0)
                    _history.MarkSeen(search.Id, outcome.All.Select(l => l.Key));

                _history.SetLastRun(search.Id, DateTime.Now, outcome.New.Count);

                search.LastRun = DateTime.Now;
                search.NewCount = outcome.New.Count;

                // Wat er misliep bij de zoekopdracht zelf bijhouden: de lijst toont het,
                // en na twee mislukte beurten op rij komt er een melding (zie SearchScheduler).
                outcome.JustBroken.AddRange(
                    search.RecordRun(werk.Select(p => p.Setting.Site).Concat(verdwenen),
                                     outcome.SiteErrors, outcome.SiteCounts));

                _history.Update(search);
            }

            Log.Write($"planner: '{search.Name}' klaar — {outcome.All.Count} resultaten, " +
                      $"{outcome.New.Count} nieuw, {outcome.SiteErrors.Count} site(s) mislukt");

            return outcome;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Valt dit zoekertje binnen de prijsgrenzen van zíjn eigen site? Zonder
    /// prijs valt het buiten elke grens, net als in het hoofdscherm.
    /// </summary>
    private static bool BinnenPrijs(SavedSearch search, Listing listing)
    {
        var setting = search.For(listing.Source);
        if (setting is null) return true;

        if (setting.PriceMin is { } min && listing.Price < min) return false;
        if (setting.PriceMax is { } max && listing.Price > max) return false;

        return true;
    }
}

using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>Wat één keer draaien van een zoekopdracht opleverde.</summary>
public class SearchOutcome
{
    /// <summary>Alles wat er gevonden is, na de filters en de verfijning.</summary>
    public List<Listing> All { get; } = new();

    /// <summary>
    /// Wat er bij deze beurt voor het eerst bij was: daarover gaat de melding. Niet hetzelfde
    /// als <see cref="Listing.IsNew"/>, dat zegt of je het al bekeken hebt - dat kan ook iets
    /// zijn wat een vorige beurt vond.
    /// </summary>
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

    /// <summary>
    /// De sites die dit zoekwoord niet kennen - AutoScout24 bij elk woord dat geen automerk
    /// is. Zie <see cref="Zentrix.Sources.UnsupportedQueryException"/>.
    ///
    /// Ze staan ook in <see cref="SiteErrors"/>, zodat je op hun tab leest waarom er niets
    /// kwam, maar ze tellen <b>niet</b> als mislukking: niet in de regel onderaan, en niet in
    /// <see cref="SavedSearch.RecordRun"/>. Dezelfde aanpak als bij een brugsite die
    /// overgeslagen werd en bij een site die niet meer bestaat.
    /// </summary>
    public List<string> QueryNotSupported { get; } = new();

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
/// Eén site is klaar (23 september 2026, de derde stap naar één zoeklus). Het scherm zet daarmee
/// de teller op haar tab, de tijd in de statusregel en een eventuele fout op het
/// waarschuwingsteken - en dat meteen, niet pas wanneer de hele beurt klaar is.
///
/// Ook een site die helemaal niet gezocht heeft, komt hier langs: een brugsite die overgeslagen
/// werd omdat de brug niet werkt, en een aangevinkte site die niet meer bestaat. Zonder dat zou
/// het scherm die stil laten vallen, en dat is precies waar de vangnetten van 3c over gaan.
/// </summary>
/// <param name="Site">De naam van de site, zoals ze op haar tab staat.</param>
/// <param name="Aantal">Hoeveel zoekertjes ze gaf; 0 bij een fout.</param>
/// <param name="Duur">Hoelang die site erover deed.</param>
/// <param name="Fout">De melding in gewone taal, of null wanneer het lukte.</param>
public record SiteKlaar(string Site, int Aantal, TimeSpan Duur, string? Fout);

/// <summary>
/// Voert een <see cref="SavedSearch"/> uit zonder ook maar iets van het scherm
/// nodig te hebben. Dat is de kern van het automatisch zoeken: dezelfde code
/// draait of je nu op het vergrootglas klikt of de app in het systeemvak staat.
///
/// Sinds 23 september 2026 is dit de enige plaats waar gezocht wordt. Het hoofdscherm had zijn
/// eigen lus, want het wil resultaten tonen zodra ze binnenkomen, maar dan moest elke regel twee
/// keer geschreven worden - en dat gaf stille verschillen tussen zelf zoeken en een geplande
/// beurt. Wat het scherm nodig had om mee te kunnen, staat in de parameters:
/// <c>delivered</c> met <c>tussentijds</c>, <c>siteKlaar</c>, <c>slotGenomen</c> en
/// <c>logNaam</c>.
///
/// <see cref="Gate"/> laat er maar één tegelijk zoeken: de brug heeft één wachtrij, en twee
/// Playwright-sessies op hetzelfde browserprofiel botsen. <see cref="RunInLanesAsync"/> bepaalt
/// welke sites binnen één beurt tegelijk mogen.
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
                                         string logNaam, IReadOnlyList<string>? verdwenen = null)
    {
        outcome.NotRunReason = reden;
        outcome.Errors.Add(reden);
        Log.Write($"{logNaam}: '{search.Name}' niet uitgevoerd - {reden}");

        // Bij "Nu uitvoeren" in het venster van de zoekopdracht (markSeen uit) is het een
        // proefbeurt, en die verzet het schema niet. Een zoekopdracht zonder Id is niet
        // bewaard en heeft geen rij om in te schrijven (zie RunAsync).
        if (markSeen && search.Id > 0)
        {
            // De teller blijft staan: wat je nog niet bekeek, is door deze mislukte beurt
            // niet minder nieuw geworden.
            _history.SetLastRun(search.Id, DateTime.Now, search.NewCount);
            search.LastRun = DateTime.Now;

            // Verdwenen sites tellen als mislukte beurt: zo komt er na twee keer een melding.
            if (verdwenen is { Count: > 0 })
            {
                outcome.JustBroken.AddRange(search.RecordRun(verdwenen, outcome.SiteErrors));
                _history.Update(search);
            }
        }

        return outcome;
    }

    /// <param name="tussentijds">
    /// Ook doorgeven wat er binnenkomt terwijl een site nog bezig is - per pagina, en bij de brug
    /// zelfs terwijl de pagina laadt - in plaats van pas wanneer die site helemaal klaar is. Enkel
    /// zinvol wanneer iemand meekijkt: het scherm toont dan de eerste zoekertjes na een seconde in
    /// plaats van na een site. Staat het uit, dan vraagt de app de brug ook niet om tussentijdse
    /// versies, en dat scheelde in september 2026 111 van de 159 miljoen gekopieerde tekens.
    /// De planner weet het van het scherm, via <see cref="SearchScheduler.WordtGetoond"/>.
    /// </param>
    /// <param name="siteKlaar">
    /// Krijgt per site te horen dat ze klaar is, met haar aantal, haar tijd en haar fout (zie
    /// <see cref="SiteKlaar"/>). Zo kan het scherm de tab meteen bijwerken in plaats van te
    /// wachten tot de hele beurt klaar is. Wordt, net als <paramref name="delivered"/>,
    /// aangeroepen op de draad waarop deze methode loopt.
    /// </param>
    /// <param name="slotGenomen">
    /// De aanroeper heeft <see cref="Gate"/> al genomen en geeft het zelf weer vrij. Dat doet het
    /// hoofdscherm: het wil de statusregel al op "wachten tot de zoekopdracht op de achtergrond
    /// klaar is" kunnen zetten, en het houdt het slot vast tot de resultaten bewaard zijn, zodat
    /// de volgende zoekopdracht nooit tegelijk in de databank schrijft.
    /// </param>
    /// <param name="logNaam">
    /// Waarmee de regels in het logboek beginnen: "planner" voor een geplande beurt, "zoeken"
    /// wanneer je zelf op het vergrootglas klikt. Dezelfde code, maar in het logboek wil je kunnen
    /// zien wie er aan het werk was.
    /// </param>
    public async Task<SearchOutcome> RunAsync(SavedSearch search, bool markSeen = true,
                                              IProgress<string>? status = null,
                                              Action<IReadOnlyList<Listing>>? delivered = null,
                                              bool tussentijds = false,
                                              Action<SiteKlaar>? siteKlaar = null,
                                              bool slotGenomen = false,
                                              string logNaam = "planner",
                                              CancellationToken ct = default)
    {
        var outcome = new SearchOutcome();

        // Een zoekopdracht zonder Id is niet bewaard: zo zoekt het zoekscherm los, met enkel
        // wat er op dat moment op het scherm staat (23 september 2026, de tweede stap naar één
        // zoeklus). Dan is er geen rij in de databank om in te schrijven, en bestaat "nieuw"
        // niet: dat gaat over wat je bij díe zoekopdracht nog niet bekeek. Zonder deze regel
        // zou alles als nieuw gemarkeerd worden, want wat niet in "al gezien" staat, is nieuw.
        var bewaard = search.Id > 0;
        var onthouden = markSeen && bewaard;

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

            // Ze heeft geen tab meer, maar wie meekijkt mag weten dat ze eruit ligt.
            siteKlaar?.Invoke(new SiteKlaar(site, 0, TimeSpan.Zero, verdwenenMelding));
        }

        if (werk.Count == 0)
            return NietUitgevoerd(search, markSeen, outcome, verdwenen.Count > 0
                ? $"de aangevinkte site{(verdwenen.Count > 1 ? "s bestaan" : " bestaat")} niet meer: {string.Join(", ", verdwenen)}"
                : "geen site aangevinkt", logNaam, verdwenen);

        // Zonder zoekterm blijven enkel de sites over die op filters alleen kunnen
        // zoeken (AutoScout24). Dezelfde regel als in het hoofdscherm, want een
        // zoekopdracht moet hetzelfde doen of je nu kijkt of niet.
        if (string.IsNullOrWhiteSpace(search.Query))
        {
            var overgeslagen = werk.Where(p => !p.Def.AllowsEmptyQuery).Select(p => p.Setting.Site).ToList();
            werk = werk.Where(p => p.Def.AllowsEmptyQuery).ToList();

            if (werk.Count == 0)
                return NietUitgevoerd(search, markSeen, outcome,
                    "geen zoekterm, en geen enkele aangevinkte site kan zoeken op filters alleen", logNaam);

            if (overgeslagen.Count > 0)
                Log.Write($"{logNaam}: geen zoekterm, overgeslagen: {string.Join(", ", overgeslagen)}");
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

        if (!slotGenomen) await Gate.WaitAsync(ct);

        // Alle browsersites van deze beurt delen één Chrome.
        using var browserLease = BrowserPool.Lease();

        try
        {
            Log.Write($"{logNaam}: '{search.Name}' gestart op {werk.Count} site(s)");

            // Sites via de brug hebben een draaiende Chrome nodig. Werkt de brug niet,
            // dan slaan we die sites meteen over: anders wacht elke brugsite nog
            // anderhalve minuut op een antwoord waarvan al vaststaat dat het niet komt.
            if (werk.Any(p => p.Def.UseBridge))
            {
                status?.Report("Chrome klaarzetten voor de brug...");

                // De meldingen onderweg gaan mee naar de statusregel: dit kan dertig seconden
                // duren, en dan hoort er te staan waarop gewacht wordt.
                outcome.Bridge = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30), status);

                if (outcome.Bridge != BridgeStatus.Ready)
                {
                    var reden = "overgeslagen, " + ChromeLauncher.Describe(outcome.Bridge);

                    foreach (var (setting, _) in werk.Where(p => p.Def.UseBridge))
                    {
                        Fout(setting.Site, reden);
                        Log.Write($"{logNaam}: {setting.Site} {reden}");

                        // Ook een site die niet eens gezocht heeft, hoort op het scherm te komen.
                        siteKlaar?.Invoke(new SiteKlaar(setting.Site, 0, TimeSpan.Zero, reden));
                    }
                }
            }

            // Per sleutel één zoekertje. Latere leveringen van dezelfde site
            // vullen aan wat nog ontbrak, net als in het hoofdscherm.
            var gevonden = new Dictionary<string, Listing>();

            // Vooraf ophalen en niet pas op het einde: elke site geeft zijn
            // zoekertjes meteen door, en dan moet al vaststaan wat nieuw is.
            var eerderGezien = bewaard ? _history.GetSeen(search.Id) : new Dictionary<string, DateTimeOffset>();
            if (bewaard) search.LastViewed = _history.GetLastViewed(search.Id);

            DateTimeOffset? EerstGezien(Listing l) =>
                eerderGezien.TryGetValue(l.Key, out var t) ? t : null;

            var uitTeVoeren = werk
                .Where(p => outcome.Bridge == BridgeStatus.Ready || !p.Def.UseBridge)
                .ToList();

            await RunInLanesAsync(uitTeVoeren, p => p.Def, async p =>
            {
                var (setting, def) = p;

                ct.ThrowIfCancellationRequested();
                status?.Report($"{setting.Site} doorzoeken...");

                // Wat er binnenkomt samenvoegen en doorgeven. Wordt tijdens het ophalen
                // aangeroepen (per pagina, als er iemand meekijkt) en op het einde met alles
                // van deze site; wat al binnen was, komt er niet twee keer in.
                void Lever(IReadOnlyList<Listing> binnen)
                {
                    var vers = new List<Listing>();

                    // De rijstroken komen hier samen. Bij de planner lopen ze allemaal
                    // verder op de schermdraad, maar een slot kost niets en maakt het
                    // ook veilig wanneer iemand deze methode van elders aanroept.
                    lock (gevonden)
                    {
                        foreach (var listing in binnen)
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

                    if (delivered is null || vers.Count == 0) return;

                    // De vlaggen nu al zetten, met dezelfde regels als op het einde. IsNew
                    // meldt geen wijziging aan het scherm: een kaart leest hem één keer, bij
                    // het tekenen. Achteraf zetten is dus te laat — dan verschijnt het
                    // NIEUW-label nooit.
                    foreach (var listing in vers)
                    {
                        listing.IsNew = bewaard &&
                                        search.IsUnviewed(EerstGezien(listing)) &&
                                        search.Matches(listing) &&
                                        BinnenPrijs(search, listing);
                    }

                    delivered(vers);
                }

                // Buiten de try: ook een mislukte site heeft een tijd, en die hoort op het scherm.
                var start = DateTime.Now;

                try
                {
                    var bron = SourceFactory.Create(def);

                    // Enkel wanneer iemand meekijkt: zonder melder vraagt de app de brug ook
                    // geen tussentijdse versies (zie de parameter tussentijds).
                    var melder = tussentijds && delivered is not null
                        ? new Progress<List<Listing>>(Lever)
                        : null;

                    var resultaten = await bron.SearchAsync(
                        search.Query, def.ResultLimit(), setting.ToFilters(), melder, ct);

                    lock (outcome) outcome.SiteCounts[setting.Site] = resultaten.Count;

                    var verdacht = search.VerdachtLeeg(setting.Site, resultaten.Count);

                    if (verdacht is not null)
                    {
                        Fout(setting.Site, verdacht);
                        Log.Write($"{logNaam}: {setting.Site} {verdacht}");
                    }

                    Lever(resultaten);

                    Log.Write($"{logNaam}: {setting.Site} gaf {resultaten.Count} resultaten " +
                              $"in {(DateTime.Now - start).TotalSeconds:F1}s");

                    siteKlaar?.Invoke(new SiteKlaar(setting.Site, resultaten.Count, DateTime.Now - start, verdacht));
                }
                // Enkel doorgooien als de zoekopdracht zelf gestopt wordt. Een time-out van
                // HttpClient is óók een OperationCanceledException, en die gooide vroeger de hele
                // beurt om: de resultaten van de andere sites gingen verloren, het tijdstip werd
                // niet bewaard, en de planner nam dezelfde zoekopdracht bij elke tik opnieuw.
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                // Deze site kan met dit zoekwoord niets aanvangen. Dat is een antwoord en geen
                // mislukking, dus het komt wel op haar tab maar telt nergens als fout mee.
                catch (UnsupportedQueryException nvt)
                {
                    lock (outcome)
                    {
                        outcome.SiteErrors[setting.Site] = nvt.Message;
                        outcome.QueryNotSupported.Add(setting.Site);
                    }

                    Log.Write($"{logNaam}: {setting.Site} {nvt.Message}");
                    siteKlaar?.Invoke(new SiteKlaar(setting.Site, 0, DateTime.Now - start, nvt.Message));
                }
                catch (Exception ex)
                {
                    var melding = FriendlyError.Describe(ex);

                    Fout(setting.Site, melding);
                    Log.Write($"{logNaam}: {setting.Site} mislukte - {ex.Message}");

                    siteKlaar?.Invoke(new SiteKlaar(setting.Site, 0, DateTime.Now - start, melding));
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

            // Twee soorten nieuw. Voor de melding: wat deze beurt voor het eerst zag, zodat je
            // niet elk uur een melding krijgt over dezelfde zoekertjes. Voor de teller en het
            // NIEUW-label: wat je nog niet bekeek, ook als een vorige beurt het al vond.
            // Enkel bij een bewaarde zoekopdracht: zonder geschiedenis zou alles "voor het eerst
            // gezien" zijn, en daar hoort niemand een melding over te krijgen.
            if (bewaard)
            {
                foreach (var listing in outcome.All)
                {
                    if (!eerderGezien.ContainsKey(listing.Key)) outcome.New.Add(listing);
                }
            }

            // EEN HALVE BEURT IS GEEN BEURT. Drukte je op de stopknop, dan is dit niet wat de
            // zoekopdracht opleverde maar wat er toevallig al binnen was. Hieronder wordt
            // weggeschreven dat je dat allemaal "gezien" hebt, wordt het tijdstip verzet (en
            // dus de volgende geplande beurt) en wordt de teller opnieuw berekend. Dat hoort
            // niet te gebeuren: je zou zoekertjes als gezien wegzetten die je nooit zag.
            //
            // Wat al binnen was, blijft gewoon op het scherm staan - het hoofdscherm vangt deze
            // annulering op en toont "Gestopt. N resultaten van de sites die wel klaar waren."
            ct.ThrowIfCancellationRequested();

            if (onthouden)
            {
                if (outcome.All.Count > 0)
                    _history.MarkSeen(search.Id, outcome.All.Select(l => l.Key));

                // Nu pas vastleggen wat je nog niet bekeek, met het tijdstip van nú. Wie de
                // zoekopdracht opende terwijl deze beurt liep, zag de nieuwe van daarvoor al;
                // met het tijdstip van bij de start telden die anders opnieuw mee.
                search.LastViewed = _history.GetLastViewed(search.Id);
            }

            foreach (var listing in outcome.All)
                listing.IsNew = bewaard && search.IsUnviewed(EerstGezien(listing));

            if (onthouden)
            {
                search.NewCount = outcome.All.Count(l => l.IsNew);
                _history.SetLastRun(search.Id, DateTime.Now, search.NewCount);

                search.LastRun = DateTime.Now;

                // Wat er misliep bij de zoekopdracht zelf bijhouden: de lijst toont het,
                // en na twee mislukte beurten op rij komt er een melding (zie SearchScheduler).
                //
                // Zonder de sites die dit zoekwoord niet kennen. Die horen hier niet thuis:
                // ze zouden bij elke beurt opnieuw "mislukken", na twee beurten een melding
                // uitlokken en de zoekopdracht daarna voor altijd rood laten staan - terwijl
                // er niets stuk is.
                outcome.JustBroken.AddRange(
                    search.RecordRun(werk.Select(p => p.Setting.Site).Concat(verdwenen),
                                     EchteFouten(outcome), outcome.SiteCounts));

                _history.Update(search);
            }

            Log.Write($"{logNaam}: '{search.Name}' klaar — {outcome.All.Count} resultaten, " +
                      $"{outcome.New.Count} nieuw, {outcome.All.Count(l => l.IsNew)} nog niet bekeken, " +
                      $"{EchteFouten(outcome).Count} site(s) mislukt" +
                      (outcome.QueryNotSupported.Count > 0
                          ? $", {outcome.QueryNotSupported.Count} site(s) kennen dit zoekwoord niet"
                          : ""));

            return outcome;
        }
        finally
        {
            // Wie het slot zelf nam, geeft het zelf weer vrij - het hoofdscherm houdt het vast
            // tot de resultaten bewaard zijn.
            if (!slotGenomen) Gate.Release();
        }
    }

    /// <summary>
    /// De fouten waar de gebruiker iets aan moet doen, dus zonder de sites die dit zoekwoord
    /// gewoon niet kennen. Die staan wél in <see cref="SearchOutcome.SiteErrors"/> - daar leest
    /// de tab van die site uit waarom er niets kwam - maar ze tellen nergens als mislukking.
    /// </summary>
    internal static Dictionary<string, string> EchteFouten(SearchOutcome outcome) =>
        outcome.SiteErrors
            .Where(p => !outcome.QueryNotSupported.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

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

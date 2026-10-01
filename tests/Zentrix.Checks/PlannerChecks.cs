using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// De planner en wat een beurt bewaart: een zoekopdracht die niet kan draaien, een site die
/// niet antwoordt, wanneer iets aan de beurt is, en het tellen van mislukte beurten.
/// </summary>
public static class PlannerChecks
{
    public static async Task RunAsync()
    {
        using var snel = new Proefsite { PerPagina = { [1] = 5 } };
        using var traag = new Proefsite { Zwijgt = true };

        var store = new SiteStore();
        store.Load();
        store.Add(snel.Site("Snel"));
        store.Add(traag.Site("Traag"));

        var history = new HistoryStore();
        var runner = new SearchRunner(store, history);

        SavedSearch Zoekopdracht(string term, params string[] sites)
        {
            var s = new SavedSearch
            {
                Query = term,
                SiteSettings = sites.Select(n => new SiteSetting { Site = n, Enabled = true }).ToList(),
                Schedule = new SearchSchedule { Mode = ScheduleMode.Interval, IntervalMinutes = 60, NotifyOnNew = false }
            };
            s.Id = history.Add(s);
            return s;
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Planner: een zoekopdracht die niet kan draaien, krijgt toch haar tijdstip");
        {
            var leeg = Zoekopdracht("fiets");
            var outcome = await runner.RunAsync(leeg);

            Check.Dat(outcome.NotRunReason == "geen site aangevinkt", $"reden: '{outcome.NotRunReason}'");
            Check.Dat(leeg.LastRun is not null && history.GetAll().Single(x => x.Id == leeg.Id).LastRun is not null,
                "LastRun gezet, in het object en in de databank");
            Check.Dat(leeg.Schedule.NextRun(leeg.LastRun) > DateTime.Now, "niet langer aan de beurt");

            var proef = Zoekopdracht("proef");
            var p = await runner.RunAsync(proef, markSeen: false);
            Check.Dat(p.NotRunReason is not null && proef.LastRun is null, "proefbeurt (Nu uitvoeren) verzet het schema niet");

            var zonderTerm = Zoekopdracht("", "Snel");
            var z = await runner.RunAsync(zonderTerm);
            Check.Dat(z.NotRunReason is not null && zonderTerm.LastRun is not null, "zonder zoekterm: ook genoteerd");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Planner: een site die niet antwoordt, gooit de beurt niet om");
        if (Check.Snel)
        {
            Check.Overgeslagen("overgeslagen met --snel (wacht 30 s op de time-out van HttpClient)");
        }
        else
        {
            var beide = Zoekopdracht("cd", "Snel", "Traag");
            var klok = Stopwatch.StartNew();

            SearchOutcome? outcome = null;
            Exception? fout = null;
            try { outcome = await runner.RunAsync(beide); } catch (Exception ex) { fout = ex; }

            Check.Dat(fout is null, $"geen uitzondering naar buiten ({fout?.GetType().Name})");
            Check.Dat(outcome?.All.Count == 5, $"de resultaten van de snelle site blijven ({outcome?.All.Count}, {klok.Elapsed.TotalSeconds:F0} s)");
            Check.Dat(outcome?.SiteErrors.GetValueOrDefault("Traag")?.Contains("binnen de tijd") == true,
                $"fout bij Traag: '{outcome?.SiteErrors.GetValueOrDefault("Traag")}'");
            Check.Dat(beide.LastRun is not null && beide.LastErrors.ContainsKey("Traag"), "LastRun en LastErrors bewaard");
        }

        {
            using var annuleer = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var anders = Zoekopdracht("annuleer", "Traag");
            Exception? a = null;
            try { await runner.RunAsync(anders, ct: annuleer.Token); } catch (Exception ex) { a = ex; }
            Check.Dat(a is OperationCanceledException, $"een echte annulering gaat wel door ({a?.GetType().Name})");

            // EEN HALVE BEURT IS GEEN BEURT. Wat er bij het afbreken toevallig al binnen was, is
            // niet wat de zoekopdracht opleverde: het mag dus niet als "gezien" weggeschreven
            // worden, en het tijdstip mag niet verzet worden (dat verschuift de volgende
            // geplande beurt). Vroeger liep de runner na het afbreken gewoon door naar het
            // wegschrijven.
            Check.Dat(anders.LastRun is null,
                $"een afgebroken beurt krijgt geen tijdstip ({anders.LastRun})");

            Check.Dat(anders.NewCount == 0,
                $"en de teller blijft staan ({anders.NewCount})");

            // En de site die onderbroken werd, is niet mislukt - ze was bezig. Vroeger kreeg ze
            // "gaf geen antwoord binnen de tijd" aan haar tab en ging haar foutteller omhoog.
            Check.Dat(anders.LastErrors.Count == 0,
                $"stoppen telt niet als fout van de site ({string.Join(", ", anders.LastErrors.Keys)})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Planner: tussentijdse leveringen wanneer iemand meekijkt");
        {
            // Drie pagina's, zodat er tijdens het ophalen al iets te leveren valt. De eerste
            // stap naar één zoeklus: het scherm kan hier pas op over als de planner net als
            // het scherm per pagina levert, en niet pas als een site helemaal klaar is.
            using var blader = new Proefsite { PerPagina = { [1] = 30, [2] = 30, [3] = 10 } };
            store.Add(blader.Site("Blader", paginering: true));

            async Task<(List<IReadOnlyList<Listing>> Leveringen, SearchOutcome Uitkomst)> Beurt(bool meekijken)
            {
                var zoek = Zoekopdracht("cd", "Blader");
                var leveringen = new List<IReadOnlyList<Listing>>();

                var uit = await runner.RunAsync(zoek, markSeen: false,
                    delivered: lading => leveringen.Add(lading.ToList()), tussentijds: meekijken);

                return (leveringen, uit);
            }

            var mee = await Beurt(true);
            var zonder = await Beurt(false);

            var alles = mee.Leveringen.SelectMany(l => l).ToList();

            Check.Dat(mee.Leveringen.Count > 1,
                $"met iemand die meekijkt: {mee.Leveringen.Count} leveringen ({string.Join("+", mee.Leveringen.Select(l => l.Count))})");
            Check.Dat(alles.Count == alles.Select(l => l.Key).Distinct().Count(),
                "geen enkel zoekertje komt twee keer door");
            Check.Dat(alles.Count == mee.Uitkomst.All.Count && mee.Uitkomst.All.Count == 70,
                $"samen precies de uitkomst ({alles.Count} geleverd, {mee.Uitkomst.All.Count} in de uitkomst)");
            Check.Dat(mee.Leveringen.SelectMany(l => l).All(l => l.IsNew), "en ze zijn al als nieuw gemarkeerd");

            Check.Dat(zonder.Leveringen.Count == 1 && zonder.Uitkomst.All.Count == 70,
                $"zonder toeschouwer: één levering op het einde ({zonder.Leveringen.Count})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Elke site meldt zelf dat ze klaar is");
        {
            // De derde stap naar één zoeklus: het scherm zet per site de teller op haar tab, de
            // tijd in de statusregel en de fout op het waarschuwingsteken, zodra die site klaar
            // is. De runner gaf dat pas op het einde mee, in outcome.SiteErrors.
            //
            // "Dood" luistert nergens: dat mislukt meteen, in tegenstelling tot "Traag" hierboven,
            // dat 30 seconden op een time-out wacht.
            store.Add(new SiteDefinition
            {
                Name = "Dood",
                BaseUrl = "http://127.0.0.1:1",
                SearchUrlTemplate = "http://127.0.0.1:1/s?q={query}",
                ItemSelector = "div.item",
                TitleSelector = "a.t",
                UrlSelector = "a.t@href"
            });

            var meldingen = new List<SiteKlaar>();

            // "Weg" staat wel in de zoekopdracht, maar niet meer in Sites beheren.
            var zoek = Zoekopdracht("cd", "Snel", "Dood", "Weg");
            var uit = await runner.RunAsync(zoek, markSeen: false,
                siteKlaar: m => { lock (meldingen) meldingen.Add(m); });

            var gelukt = meldingen.SingleOrDefault(m => m.Site == "Snel");
            var mislukt = meldingen.SingleOrDefault(m => m.Site == "Dood");
            var weg = meldingen.SingleOrDefault(m => m.Site == "Weg");

            Check.Dat(meldingen.Count == 3, $"drie sites, drie meldingen ({meldingen.Count})");
            Check.Dat(gelukt is { Aantal: 5, Fout: null } && gelukt.Duur > TimeSpan.Zero,
                $"de site die lukte: 5 zoekertjes, geen fout, {gelukt?.Duur.TotalMilliseconds:F0} ms");
            Check.Dat(mislukt is { Aantal: 0 } && mislukt.Fout is { Length: > 0 } &&
                      mislukt.Fout == uit.SiteErrors.GetValueOrDefault("Dood"),
                $"de site die mislukte: dezelfde melding als in de uitkomst ('{mislukt?.Fout}')");
            Check.Dat(weg?.Fout?.Contains("bestaat niet meer") == true,
                $"een site die niet meer bestaat, komt ook langs ('{weg?.Fout}')");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Zoeken zonder bewaarde zoekopdracht");
        {
            // De tweede stap naar één zoeklus: het zoekscherm zoekt ook los, met enkel wat er op
            // dat moment op het scherm staat. Zo'n zoekopdracht is niet bewaard (Id 0), en dan
            // mag er niets in de databank geschreven worden - en bestaat "nieuw" niet.
            SavedSearch Los(params string[] sites) => new()
            {
                Query = "cd",
                SiteSettings = sites.Select(n => new SiteSetting { Site = n, Enabled = true }).ToList()
            };

            var voor = history.GetAll().Count;
            var leveringen = new List<IReadOnlyList<Listing>>();

            var los = Los("Snel");
            var uit = await runner.RunAsync(los, markSeen: false,
                delivered: lading => leveringen.Add(lading.ToList()), tussentijds: true);

            Check.Dat(uit.All.Count == 5 && leveringen.SelectMany(l => l).Count() == 5,
                $"een losse zoekopdracht levert gewoon resultaten ({uit.All.Count})");
            Check.Dat(uit.All.All(l => !l.IsNew) && leveringen.SelectMany(l => l).All(l => !l.IsNew),
                "niets staat als nieuw gemarkeerd: zonder zoekopdracht is er niets om niet bekeken te hebben");
            Check.Dat(uit.New.Count == 0, $"en er valt niets te melden ({uit.New.Count})");
            Check.Dat(los.LastRun is null && history.GetAll().Count == voor && history.GetSeen(0).Count == 0,
                "er is niets in de databank geschreven");

            // Ook met markSeen aan, mocht een aanroeper dat meegeven: zonder rij valt er niets
            // te onthouden. Vroeger schreef dat rijen met searchId 0.
            var tweede = Los("Snel");
            await runner.RunAsync(tweede, markSeen: true);

            Check.Dat(tweede.LastRun is null && history.GetSeen(0).Count == 0 && history.GetAll().Count == voor,
                "ook met markSeen aan blijft de databank ongemoeid");

            // En een losse zoekopdracht die niet kan draaien, noteert niets.
            var zonderSite = Los();
            var niet = await runner.RunAsync(zonderSite, markSeen: true);

            Check.Dat(niet.NotRunReason == "geen site aangevinkt" && zonderSite.LastRun is null,
                $"niet uitvoerbaar: wel een reden ('{niet.NotRunReason}'), geen tijdstip");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Het hoofdscherm zoekt met dezelfde lus");
        {
            // De verhuizing zelf: het scherm zoekt niet meer zelf maar roept de runner aan. Het
            // heeft daarvoor twee dingen nodig die de planner niet nodig heeft.
            //
            // Ten eerste het slot: het scherm neemt dat zelf, zodat het "wachten tot de
            // zoekopdracht op de achtergrond klaar is" kan zeggen, en het houdt het vast tot de
            // resultaten bewaard zijn. Nam de runner het dan ook, dan stond de app stil.
            await SearchRunner.Gate.WaitAsync();

            var taak = runner.RunAsync(Zoekopdracht("cd", "Snel"), markSeen: false,
                                       slotGenomen: true, logNaam: "zoeken");

            // Met een wachttijd erbij: zonder slotGenomen loopt dit vast, en een controle die
            // blijft hangen zegt niets.
            var opTijd = await Task.WhenAny(taak, Task.Delay(TimeSpan.FromSeconds(10))) == taak;

            Check.Dat(opTijd && taak.Result.All.Count == 5,
                $"met het slot al genomen draait de beurt gewoon door ({(opTijd ? taak.Result.All.Count : -1)})");
            Check.Dat(SearchRunner.Gate.CurrentCount == 0, "en het slot blijft van wie het nam");

            SearchRunner.Gate.Release();
            Check.Dat(SearchRunner.Gate.CurrentCount == 1, "pas die geeft het weer vrij");

            // Ook wanneer de beurt afgebroken wordt. Dit is de gevaarlijke combinatie: gaf de
            // runner het slot bij het afbreken tóch vrij, dan geeft het scherm het daarna een
            // tweede keer vrij, en een semafoor van één gooit dan SemaphoreFullException - de
            // hele zoekopdracht valt om.
            using (var afbreken = new CancellationTokenSource())
            {
                await SearchRunner.Gate.WaitAsync();

                var gestopt = runner.RunAsync(Zoekopdracht("stop", "Traag"), markSeen: false,
                                              slotGenomen: true, logNaam: "zoeken", ct: afbreken.Token);
                afbreken.Cancel();

                Exception? afgebroken = null;
                try { await gestopt; } catch (Exception ex) { afgebroken = ex; }

                Check.Dat(afgebroken is OperationCanceledException,
                    $"een afgebroken beurt gooit door naar het scherm ({afgebroken?.GetType().Name})");
                Check.Dat(SearchRunner.Gate.CurrentCount == 0, "en ook dan blijft het slot van wie het nam");

                SearchRunner.Gate.Release();
            }

            // Ten tweede het logboek: dezelfde code, maar je wil kunnen zien wie er aan het werk
            // was. Een beurt van het scherm heet "zoeken", een geplande beurt "planner".
            var logboek = File.ReadAllText(Log.FilePath);

            Check.Dat(logboek.Contains("zoeken: 'Cd' gestart op 1 site(s)") &&
                      logboek.Contains("zoeken: Snel gaf 5 resultaten"),
                "het logboek zegt 'zoeken' en niet 'planner' voor een beurt van het scherm");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Planner: aan de beurt is aan de beurt");
        {
            SearchScheduler Planner(List<string> statussen, params SavedSearch[] lijst)
            {
                var planner = new SearchScheduler(new ObservableCollection<SavedSearch>(lijst), runner, history);
                planner.Status += s => statussen.Add(s);
                return planner;
            }

            var eerste = Zoekopdracht("aaa");
            var tweede = Zoekopdracht("bbb", "Snel");
            eerste.LastRun = tweede.LastRun = DateTime.Now.AddHours(-2);

            var st = new List<string>();
            var p1 = Planner(st, eerste, tweede);

            await p1.TickAsync();
            Check.Dat(eerste.LastRun > DateTime.Now.AddMinutes(-1) && tweede.LastRun < DateTime.Now.AddHours(-1),
                "tik 1: de onuitvoerbare zoekopdracht vooraan wordt genoteerd");
            Check.Dat(st.Any(s => s.Contains("is niet uitgevoerd: geen site aangevinkt")), "de statusregel zegt waarom");

            await p1.TickAsync();
            Check.Dat(tweede.LastRun > DateTime.Now.AddMinutes(-1), "tik 2: de volgende zoekopdracht draait");

            await p1.TickAsync();
            Check.Dat(st.Count(s => s.Contains("draait")) == 2, "tik 3: niemand meer aan de beurt");

            var nieuw = Zoekopdracht("ccc", "Snel");
            await Planner(new(), nieuw).TickAsync();
            Check.Dat(nieuw.LastRun is not null, "interval, nog nooit gedraaid: draait bij de eerste tik");

            var dagelijks = Zoekopdracht("ddd", "Snel");
            dagelijks.Schedule = new SearchSchedule { Mode = ScheduleMode.Daily, DailyHour = 0, NotifyOnNew = false };
            dagelijks.LastRun = DateTime.Today.AddHours(-12);
            await Planner(new(), dagelijks).TickAsync();
            Check.Dat(dagelijks.LastRun >= DateTime.Today, "dagelijks, het uur is voorbij en vandaag nog niet gedraaid: draait");

            var vandaagAl = Zoekopdracht("eee", "Snel");
            vandaagAl.Schedule = new SearchSchedule { Mode = ScheduleMode.Daily, DailyHour = 0, NotifyOnNew = false };
            vandaagAl.LastRun = DateTime.Today.AddMinutes(1);
            var voor = vandaagAl.LastRun;
            await Planner(new(), vandaagAl).TickAsync();
            Check.Dat(vandaagAl.LastRun == voor, "dagelijks, vandaag al gedraaid: niet nog eens");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Mislukte beurten tellen (RecordRun)");
        {
            var s = new SavedSearch { Query = "test" };
            var sites = new[] { "A", "B" };
            Dictionary<string, string> Kapot(string site) => new() { [site] = "kapot" };

            Check.Dat(s.RecordRun(sites, Kapot("A")).Count == 0, "eerste mislukking: nog geen melding");
            Check.Dat(s.RecordRun(sites, Kapot("A")).SequenceEqual(new[] { "A" }), "tweede op rij: melding");
            Check.Dat(s.RecordRun(sites, Kapot("A")).Count == 0, "derde op rij: geen nieuwe melding");
            Check.Dat(s.StatusLabel.Contains("1 site mislukt") && s.HasErrors, $"de lijst toont het ('{s.StatusLabel}')");
            s.RecordRun(sites, new Dictionary<string, string>());
            Check.Dat(s.FailureStreaks.Count == 0 && !s.HasErrors, "site lukt weer: alles gewist");

            var h = history;
            var z = Zoekopdracht("bewaren");
            z.RecordRun(sites, Kapot("A"));
            h.Update(z);
            var terug = h.GetAll().Single(x => x.Id == z.Id);
            Check.Dat(terug.LastErrors.GetValueOrDefault("A") == "kapot" && terug.FailureStreaks.GetValueOrDefault("A") == 1,
                "fouten en reeksen overleven de databank");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rijstroken: rechtstreeks samen, browser en brug elk na elkaar, de drie naast elkaar");
        {
            var werk = new List<(string Naam, SiteDefinition Def)>
            {
                ("direct1", new SiteDefinition()),
                ("direct2", new SiteDefinition()),
                ("browser1", new SiteDefinition { NeedsBrowser = true }),
                ("browser2", new SiteDefinition { Engine = SiteEngine.LinkText }),
                ("brug1", new SiteDefinition { UseBridge = true }),
                ("brug2", new SiteDefinition { UseBridge = true, NeedsBrowser = true })
            };

            var bezig = new Dictionary<string, int>();
            var maxBezig = new Dictionary<string, int>();
            var totaalMax = 0;
            var totaalBezig = 0;

            string Strook(string naam) => naam[..^1];

            await SearchRunner.RunInLanesAsync(werk, w => w.Def, async w =>
            {
                lock (bezig)
                {
                    var strook = Strook(w.Naam);
                    bezig[strook] = bezig.GetValueOrDefault(strook) + 1;
                    maxBezig[strook] = Math.Max(maxBezig.GetValueOrDefault(strook), bezig[strook]);
                    totaalMax = Math.Max(totaalMax, ++totaalBezig);
                }

                await Task.Delay(200);

                lock (bezig)
                {
                    bezig[Strook(w.Naam)]--;
                    totaalBezig--;
                }
            });

            Check.Dat(maxBezig["direct"] == 2, "rechtstreekse sites tegelijk");
            Check.Dat(maxBezig["browser"] == 1, "browsersites na elkaar, ook de linkmotor zonder browservinkje");
            Check.Dat(maxBezig["brug"] == 1, "brugsites na elkaar");
            Check.Dat(totaalMax >= 3, $"de drie stroken tegelijk (tot {totaalMax} tegelijk)");
        }
    }
}

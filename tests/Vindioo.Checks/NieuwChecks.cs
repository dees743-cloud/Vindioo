using Microsoft.Data.Sqlite;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo.Checks;

/// <summary>
/// "Nieuw" is wat je nog niet bekeek, niet wat de laatste beurt nieuw vond. Tot september
/// 2026 was het dat laatste: de planner vond om 12:07 48 nieuwe bij "Cd speler", een druk
/// op het driehoekje 26 seconden later zette de teller op 0, en de 48 waren nergens meer
/// als nieuw terug te vinden.
/// </summary>
public static class NieuwChecks
{
    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Nieuw: de regel zelf (IsUnviewed)");
        {
            var t = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(2));
            var nooit = new SavedSearch();
            var geopend = new SavedSearch { LastViewed = t };

            Check.Dat(nooit.IsUnviewed(t), "nog nooit geopend: alles is nieuw");
            Check.Dat(geopend.IsUnviewed(null), "nog nooit gezien: nieuw");
            Check.Dat(geopend.IsUnviewed(t.AddSeconds(1)) && !geopend.IsUnviewed(t.AddSeconds(-1)),
                "opgedoken na het openen: nieuw; ervoor: bekeken");
            Check.Dat(!geopend.IsUnviewed(t), "precies op het moment van openen: bekeken");
            Check.Dat(!geopend.IsUnviewed(t.ToUniversalTime().AddSeconds(-1)),
                "een tijdstip in een andere tijdzone telt als hetzelfde moment");
        }

        using var site = new Proefsite();
        var store = new SiteStore();
        store.Load();
        store.Add(site.Site("NieuwSite"));
        var history = new HistoryStore();
        var runner = new SearchRunner(store, history);

        SavedSearch Zoekopdracht()
        {
            var s = new SavedSearch
            {
                Query = "cd",
                SiteSettings = { new SiteSetting { Site = "NieuwSite", Enabled = true } },
                Schedule = new SearchSchedule { Mode = ScheduleMode.Interval, NotifyOnNew = false }
            };
            s.Id = history.Add(s);
            return s;
        }

        int TellerInDatabank(int id) => history.GetAll().Single(s => s.Id == id).NewCount;

        // ---------------------------------------------------------------------------
        Check.Groep("Nieuw blijft nieuw tot je de zoekopdracht opent");
        {
            site.PerPagina.Clear();
            site.PerPagina[1] = 5;
            var z = Zoekopdracht();

            var o1 = await runner.RunAsync(z);
            Check.Dat(o1.New.Count == 5 && z.NewCount == 5 && o1.All.All(l => l.IsNew),
                $"eerste beurt: alle 5 nieuw (melding over {o1.New.Count}, teller {z.NewCount})");

            var o2 = await runner.RunAsync(z);
            Check.Dat(o2.New.Count == 0, "tweede beurt zonder te kijken: geen tweede melding over dezelfde 5");
            Check.Dat(z.NewCount == 5 && o2.All.Count(l => l.IsNew) == 5 && TellerInDatabank(z.Id) == 5,
                $"... maar de 5 blijven nieuw, met het label en in de teller ({z.NewCount}; vroeger 0)");

            history.SetViewed(z.Id, DateTimeOffset.Now);
            Check.Dat(TellerInDatabank(z.Id) == 0 && history.GetLastViewed(z.Id) is not null,
                "geopend: teller op 0, tijdstip bewaard");

            site.PerPagina[1] = 7;
            var o3 = await runner.RunAsync(z);
            var nieuw = o3.All.Where(l => l.IsNew).Select(l => l.Title).OrderBy(u => u).ToList();
            Check.Dat(o3.New.Count == 2 && z.NewCount == 2 && nieuw.SequenceEqual(new[] { "Zoekertje 1-5", "Zoekertje 1-6" }),
                $"daarna enkel wat er bijkwam: {string.Join(", ", nieuw)}");

            // Opent iemand de zoekopdracht terwijl een beurt loopt, dan zag hij de nieuwe van
            // daarvoor al. De beurt telt ze op het einde niet opnieuw mee.
            site.PerPagina[1] = 8;
            var o4 = await runner.RunAsync(z, delivered: _ => history.SetViewed(z.Id, DateTimeOffset.Now));
            var nieuw4 = o4.All.Where(l => l.IsNew).Select(l => l.Title).ToList();
            Check.Dat(z.NewCount == 1 && TellerInDatabank(z.Id) == 1 && nieuw4.SequenceEqual(new[] { "Zoekertje 1-7" }),
                $"geopend tijdens een beurt: enkel wat die beurt voor het eerst zag ({string.Join(", ", nieuw4)})");

            // Een beurt die niet kan draaien, maakt niets minder nieuw.
            z.SiteSettings[0].Enabled = false;
            var o5 = await runner.RunAsync(z);
            Check.Dat(o5.NotRunReason is not null && z.NewCount == 1 && TellerInDatabank(z.Id) == 1,
                "een beurt die niet draaide, laat de teller staan (vroeger 0)");
            z.SiteSettings[0].Enabled = true;

            // Een site die één beurt niets gaf: wat terugkomt en je nog niet bekeek, is nog nieuw.
            site.PerPagina[1] = 0;
            await runner.RunAsync(z);
            site.PerPagina[1] = 8;
            var o6 = await runner.RunAsync(z);
            Check.Dat(o6.All.Where(l => l.IsNew).Select(l => l.Title).SequenceEqual(new[] { "Zoekertje 1-7" }) && o6.New.Count == 0,
                "een zoekertje dat één beurt ontbrak, komt terug als nieuw, zonder tweede melding");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Nieuw: de overstap van een bestaande databank (StartpuntBekeken)");
        {
            // Zoals de oude code het achterliet: twee gekend, één nieuw bij de laatste beurt.
            var oud = Zoekopdracht();
            history.MarkSeen(oud.Id, new[] { "A:1", "A:2" });
            await Task.Delay(30);
            history.MarkSeen(oud.Id, new[] { "A:3" });
            history.SaveOutcome(oud.Id, new[]
            {
                new Listing { Source = "A", ExternalId = "1", Title = "een" },
                new Listing { Source = "A", ExternalId = "2", Title = "twee" },
                new Listing { Source = "A", ExternalId = "3", Title = "drie", IsNew = true }
            });
            history.SetLastRun(oud.Id, DateTime.Now, 1);

            // De eerste beurt ooit: alles nieuw.
            var eerste = Zoekopdracht();
            history.MarkSeen(eerste.Id, new[] { "A:1", "A:2" });
            history.SaveOutcome(eerste.Id, new[]
            {
                new Listing { Source = "A", ExternalId = "1", Title = "een", IsNew = true },
                new Listing { Source = "A", ExternalId = "2", Title = "twee", IsNew = true }
            });
            history.SetLastRun(eerste.Id, DateTime.Now, 2);

            var nooit = Zoekopdracht();

            // De kolom weghalen, zodat de volgende start doet alsof ze er net bijkomt.
            using (var verbinding = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
            {
                verbinding.Open();
                using var weg = verbinding.CreateCommand();
                weg.CommandText = "ALTER TABLE searches DROP COLUMN lastViewed";
                weg.ExecuteNonQuery();
            }

            var opnieuw = new HistoryStore();
            var alle = opnieuw.GetAll();
            var o = alle.Single(s => s.Id == oud.Id);
            var e = alle.Single(s => s.Id == eerste.Id);
            var n = alle.Single(s => s.Id == nooit.Id);
            var gezien = opnieuw.GetSeen(oud.Id);

            Check.Dat(o.NewCount == 1 && o.LastViewed == gezien["A:2"],
                $"de teller blijft 1, bekeken tot het laatste wat al gekend was ({o.NewCount})");
            Check.Dat(!o.IsUnviewed(gezien["A:1"]) && o.IsUnviewed(gezien["A:3"]), "... en enkel A:3 is nieuw");
            Check.Dat(e.NewCount == 2 && e.IsUnviewed(opnieuw.GetSeen(eerste.Id)["A:1"]),
                "na enkel een eerste beurt: alles blijft nieuw");
            Check.Dat(n.LastViewed is null, "nog nooit gedraaid: geen startpunt, alles wordt nieuw");

            var nogEens = new HistoryStore().GetAll().Single(s => s.Id == oud.Id);
            Check.Dat(nogEens.LastViewed == o.LastViewed, "een volgende start verzet het startpunt niet meer");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("E-mail: hoogstens 50 zoekertjes, en zeggen dat er meer is");
        {
            var z = new SavedSearch { Query = "cd speler" };
            List<Listing> Lijst(int n) => Enumerable.Range(1, n)
                .Select(i => new Listing { Source = "2dehands", Title = $"Speler {i}", Url = $"https://x/{i}" }).ToList();

            var veel = Notifier.MailTekst(z, Lijst(1700));
            Check.Dat(System.Text.RegularExpressions.Regex.Matches(veel, "<li>").Count == Notifier.MailMaximum,
                $"1700 nieuwe: er staan er {Notifier.MailMaximum} in de mail");
            Check.Dat(veel.Contains("En nog 1650 meer") && veel.Contains("Cd speler"),
                "met eronder hoeveel er nog zijn en waar je ze vindt");

            var weinig = Notifier.MailTekst(z, Lijst(12));
            Check.Dat(System.Text.RegularExpressions.Regex.Matches(weinig, "<li>").Count == 12 && !weinig.Contains("En nog"),
                "12 nieuwe: alle 12, zonder 'en nog'");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Een zoekopdracht verwijderen ruimt alles op");
        {
            var weg = Zoekopdracht();
            history.MarkSeen(weg.Id, new[] { "A:1" });
            history.SaveOutcome(weg.Id, new[] { new Listing { Source = "A", ExternalId = "1", Title = "een" } });

            history.Delete(weg.Id);
            Check.Dat(history.GetAll().All(s => s.Id != weg.Id) && history.GetSeen(weg.Id).Count == 0 &&
                      history.GetOutcome(weg.Id).Count == 0,
                "de zoekopdracht, wat al gezien was, en de bewaarde resultaten (die bleven vroeger achter)");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Bewaarde resultaten: alles komt terug, ook vanaf een achtergronddraad");
        {
            // Sinds 22 september 2026 één commando voor alle zoekertjes, met enkel nieuwe
            // waarden per rij. Een waarde die van de vorige rij bleef hangen, zou hier opvallen:
            // de rijen verschillen in elk veld, en de middelste heeft geen prijs en geen foto.
            var z = Zoekopdracht();
            var lijst = new List<Listing>
            {
                new() { Source = "A", ExternalId = "1", Title = "Eerste", Price = 12.5m, PriceLabel = "",
                        Location = "Gent", Url = "https://x/1", LargeImageUrl = "https://x/1-groot.jpg",
                        ImageUrls = { "https://x/1.jpg" }, IsNew = true },
                new() { Source = "B", ExternalId = "2", Title = "Tweede", Price = null, PriceLabel = "Bieden",
                        Location = "", Url = "https://x/2", LargeImageUrl = "", IsNew = false },
                new() { Source = "A", ExternalId = "3", Title = "Derde", Price = 300m, PriceLabel = "",
                        Location = "Brugge", Url = "https://x/3", LargeImageUrl = "https://x/3-groot.jpg",
                        ImageUrls = { "https://x/3.jpg" }, IsNew = false }
            };

            string Vorm(Listing l) =>
                $"{l.Source}|{l.ExternalId}|{l.Title}|{l.Price}|{l.PriceLabel}|{l.Location}|{l.Url}|" +
                $"{string.Join(",", l.ImageUrls)}|{l.LargeImageUrl}|{l.IsNew}";

            history.SaveOutcome(z.Id, lijst);
            var terug = history.GetOutcome(z.Id);
            Check.Dat(terug.Select(Vorm).SequenceEqual(lijst.Select(Vorm)),
                "drie zoekertjes, in dezelfde volgorde, met elk veld (ook een lege prijs en geen foto)");

            history.SaveOutcome(z.Id, lijst.Take(1).ToList());
            Check.Dat(history.GetOutcome(z.Id).Count == 1, "een volgende beurt vervangt de vorige");

            // Het scherm bewaart op een achtergronddraad, en intussen kan er iets anders schrijven.
            // Dan hoort SQLite te wachten, niet "database is locked" te geven.
            using var ander = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
            ander.Open();
            using var slot = ander.BeginTransaction();
            using (var schrijf = ander.CreateCommand())
            {
                schrijf.Transaction = slot;
                schrijf.CommandText = "UPDATE searches SET newCount = newCount WHERE id = $id";
                schrijf.Parameters.AddWithValue("$id", z.Id);
                schrijf.ExecuteNonQuery();
            }

            var klok = System.Diagnostics.Stopwatch.StartNew();
            var bewaren = Task.Run(() => history.SaveOutcome(z.Id, lijst));
            await Task.Delay(400);
            var wachtteNog = !bewaren.IsCompleted;
            slot.Commit();

            Exception? fout = null;
            try { await bewaren; } catch (Exception ex) { fout = ex; }

            Check.Dat(wachtteNog && fout is null && history.GetOutcome(z.Id).Count == 3,
                $"terwijl een andere verbinding schrijft: het bewaren wacht en lukt daarna ({klok.ElapsedMilliseconds} ms" +
                $"{(fout is null ? "" : ", " + fout.Message)})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Enkel in de titel: de zeef op de zoekwoorden");
        {
            // Waarvoor dit bestaat: zoek je "matras 140x200", dan geven de sites ook alles
            // terug waar die woorden ergens staan - in de beschrijving, bij de verzendkosten,
            // of in een opsomming van andere maten die de verkoper ook heeft.
            //
            // Nagemeten op 3 oktober 2026 met precies die zoekterm: van 300 resultaten houden
            // er 238 (2dehands) en 245 (Marktplaats) alle woorden in hun titel, en er viel er
            // GEEN ENKELE onterecht af. De maat staat daar als 140x200 (244x) of 140X200 (8x).
            const string term = "matras 140x200";

            Check.Dat(SavedSearch.InTitel(term, "Gloednieuwe matras 140x200"),
                "alle woorden in de titel: die blijft");

            Check.Dat(SavedSearch.InTitel(term, "Veiling - Emma Essential matras 140X200 cm Nieuw!"),
                "hoofdletters tellen niet mee (140X200 is dezelfde maat)");

            Check.Dat(SavedSearch.InTitel(term, "Matras 140x200 Matt Sleeps | Refurbished"),
                "en de volgorde doet er niet toe");

            Check.Dat(!SavedSearch.InTitel(term, "Mooi matras, zie beschrijving voor de maten"),
                "een van de twee woorden ontbreekt: die valt eruit");

            Check.Dat(!SavedSearch.InTitel(term, "Bedbodem 140x200"),
                "en dit is geen matras");

            // GEEN hele woorden, en dat is met opzet. "140x200" zit in "140x200cm", en wie op
            // hele woorden zou controleren, gooit juist de goede zoekertjes weg.
            Check.Dat(SavedSearch.InTitel(term, "matras 140x200cm pocketvering"),
                "een maat die aan de eenheid vastgeplakt zit, telt gewoon mee");

            // Een lege zoekterm laat alles door: er valt dan niets te zeven, en niets tonen
            // is een rare uitkomst van een schakelaar die "enkel in de titel" heet.
            Check.Dat(SavedSearch.InTitel("", "om het even wat"), "zonder zoekterm blijft alles staan");
            Check.Dat(!SavedSearch.InTitel("fiets", ""), "een zoekertje zonder titel valt eruit");

            // En de weg die de app echt loopt: via Matches, zodat ook een geplande beurt
            // 's nachts dezelfde zeef gebruikt.
            var zoek = new SavedSearch { Query = term, TitleOnly = true };

            Check.Dat(zoek.Matches(new Listing { Title = "matras 140x200 nieuw" }),
                "Matches laat door wat de titel draagt");

            Check.Dat(!zoek.Matches(new Listing { Title = "matras, diverse maten" }),
                "en houdt tegen wat het enkel in zijn beschrijving had");

            // Staat de schakelaar uit, dan verandert er niets.
            var uit = new SavedSearch { Query = term, TitleOnly = false };

            Check.Dat(uit.Matches(new Listing { Title = "matras, diverse maten" }),
                "met de schakelaar uit blijft alles staan");

            // De twee zeven bijten elkaar niet: enkel-met-foto en enkel-in-de-titel gelden
            // allebei, en een zoekertje moet door allebei.
            var samen = new SavedSearch { Query = term, TitleOnly = true, PhotosOnly = true };
            var metFoto = new Listing { Title = "matras 140x200" };
            metFoto.ImageUrls.Add("https://voorbeeld.be/1.jpg");

            Check.Dat(samen.Matches(metFoto), "titel en foto allebei in orde: blijft");
            Check.Dat(!samen.Matches(new Listing { Title = "matras 140x200" }),
                "goede titel maar geen foto: valt alsnog af");
        }
    }
}

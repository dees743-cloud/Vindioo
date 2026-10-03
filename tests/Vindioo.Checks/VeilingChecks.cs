using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo.Checks;

/// <summary>
/// De waarschuwing dat een bewaarde veiling bijna afloopt.
///
/// Het gevaar hier is niet dat ze niet vertrekt, maar dat ze <b>te vaak</b> vertrekt: de planner
/// tikt elke halve minuut, dus een regel die één keer te breed staat, stuurt je tweehonderd
/// berichten op een avond. Daarom gaat het grootste deel hieronder over wat er <i>niet</i>
/// gestuurd wordt.
///
/// Geen netwerk nodig: de waarschuwing werkt met het einde dat al in de databank staat.
/// </summary>
public static class VeilingChecks
{
    public static void Run()
    {
        var momenten = new[] { 1440, 60, 15 };
        var nu = new DateTime(2026, 10, 2, 12, 0, 0);

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: wanneer er wel en niet gewaarschuwd wordt");
        {
            int? Vraag(DateTime? einde, int? alGemeld = null) =>
                AuctionWatch.Aanstaand(einde, nu, momenten, alGemeld);

            Check.Dat(Vraag(null) is null, "zonder sluitingstijd geen waarschuwing");

            Check.Dat(Vraag(nu.AddDays(3)) is null,
                "drie dagen op voorhand nog niet: dat is buiten elke drempel");

            Check.Dat(Vraag(nu.AddHours(20)) == 1440,
                $"twintig uur vooraf gaat de drempel van een dag af ({Vraag(nu.AddHours(20))})");

            Check.Dat(Vraag(nu.AddMinutes(-5)) is null,
                "een veiling die al voorbij is, krijgt geen waarschuwing meer");

            Check.Dat(Vraag(nu) is null, "en precies op het einde ook niet");

            // Dit is de kern: dezelfde drempel mag maar één keer.
            Check.Dat(Vraag(nu.AddHours(20), alGemeld: 1440) is null,
                "dezelfde drempel gaat geen tweede keer af");

            Check.Dat(Vraag(nu.AddMinutes(45), alGemeld: 1440) == 60,
                "maar een fijnere drempel wel, later");

            Check.Dat(Vraag(nu.AddMinutes(10), alGemeld: 60) == 15,
                "en daarna de laatste");

            Check.Dat(Vraag(nu.AddMinutes(5), alGemeld: 15) is null,
                "na de laatste is het stil");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: de app stond uit, en dan niet drie berichten");
        {
            // Stond de app een nacht uit en kom je terug met nog twintig minuten te gaan, dan zit
            // je tegelijk binnen 1440, 60 én 15. De grootste nemen zou drie meldingen na elkaar
            // geven - één per tik - want elke fijnere blijft dan openstaan.
            var over = nu.AddMinutes(20);

            var eerste = AuctionWatch.Aanstaand(over, nu, momenten, null);
            Check.Dat(eerste == 60, $"binnen drie drempels tegelijk: de kleinste die past ({eerste})");

            var tweede = AuctionWatch.Aanstaand(over, nu, momenten, eerste);
            Check.Dat(tweede is null, "en de grovere gaan niet alsnog achteraf af");

            // De drempel van een kwartier moet daarna wél nog kunnen.
            var later = AuctionWatch.Aanstaand(over, nu.AddMinutes(10), momenten, eerste);
            Check.Dat(later == 15, $"tien minuten later gaat het kwartier wel af ({later})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: over de overgang naar de wintertijd");
        {
            // Zondag 25 oktober 2026 gaat de klok een uur terug. Wie met kale DateTime rekent,
            // telt dat uur niet mee en waarschuwt een uur te laat - dezelfde fout die de
            // afteltimer had (zie docs/resultaten.md).
            var voor = new DateTime(2026, 10, 25, 1, 30, 0);   // vlak voor de overgang
            var na = new DateTime(2026, 10, 25, 3, 0, 0);      // na de overgang

            var zomertijd = TimeZoneInfo.Local.IsDaylightSavingTime(voor);
            var wintertijd = !TimeZoneInfo.Local.IsDaylightSavingTime(na);

            if (!zomertijd || !wintertijd)
            {
                // Op een machine zonder zomertijd (de CI draait op UTC) valt er niets te meten.
                Check.Overgeslagen("deze tijdzone kent geen zomertijd: de overgang is niet nagemeten");
            }
            else
            {
                // Op de klok is dat anderhalf uur, op de echte tijdlijn tweeënhalf.
                var echt = Listing.Resterend(na, voor);
                Check.Dat(echt == TimeSpan.FromHours(2.5),
                    $"de echte tijd tot het einde telt het extra uur mee ({echt})");

                // En dus valt het nog buiten de drempel van een uur, terwijl een kale
                // aftrekking ("1u30") er al binnen zou zitten.
                Check.Dat(AuctionWatch.Aanstaand(na, voor, new[] { 60 }, null) is null,
                    "dus de drempel van een uur gaat nog niet af");

                Check.Dat((na - voor) == TimeSpan.FromHours(1.5),
                    "terwijl een kale aftrekking 1u30 zegt, en dus te vroeg zou waarschuwen");
            }
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: hoe lang er nog staat in de melding");
        {
            // De tekst zegt de ECHT resterende tijd, niet de drempel die afging: stond de app een
            // nacht uit, dan is "nog 1 uur" bij twintig minuten gewoon onwaar.
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromMinutes(20)) == "20 minuten", "20 minuten");
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromMinutes(1)) == "1 minuut", "enkelvoud bij één");
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromSeconds(30)) == "minder dan een minuut", "onder de minuut");
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromMinutes(90)) == "1 uur", "anderhalf uur leest als 1 uur");
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromHours(20)) == "20 uur", "uren");
            Check.Dat(AuctionWatch.Hoelang(TimeSpan.FromDays(2)) == "2 dagen", "dagen");

            Check.Dat(AuctionWatch.Noem(1440) == "1 dag vooraf" && AuctionWatch.Noem(240) == "4 uur vooraf" &&
                      AuctionWatch.Noem(15) == "15 minuten vooraf",
                "en de keuzes in het instellingenscherm lezen als taal");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: het einde overleeft een herstart");
        {
            // Tot 2 oktober 2026 bewaarde een favoriet zijn einddatum niet. Dan weet de app na
            // een herstart van geen enkele favoriet nog wanneer hij afloopt - en een
            // waarschuwing die een herstart niet overleeft, is geen waarschuwing.
            var geschiedenis = new HistoryStore();
            var einde = new DateTime(2026, 10, 5, 20, 30, 0);

            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "kavel-1",
                Title = "Lot 229 - wii spelcomputer",
                Url = "https://www.voorbeeld.be/kavel/229",
                EndsAt = einde
            });

            var terug = geschiedenis.GetFavorites().First(f => f.ExternalId == "kavel-1");

            Check.Dat(terug.EndsAt == einde, $"de sluitingstijd komt uit de databank terug ({terug.EndsAt})");
            Check.Dat(terug.AlertedLead is null, "en er is nog niets gemeld");

            geschiedenis.SetFavoriteAlerted(terug.Key, 60);
            terug = geschiedenis.GetFavorites().First(f => f.ExternalId == "kavel-1");

            Check.Dat(terug.AlertedLead == 60, $"wat gemeld is, blijft staan ({terug.AlertedLead})");

            // Anti-sniping: een bod vlak voor sluitingstijd verlengt de veiling. Schuift het
            // einde op, dan hoort er voor dat nieuwe einde opnieuw gewaarschuwd te worden.
            geschiedenis.SetFavoriteEnd(terug.Key, einde.AddMinutes(10));
            terug = geschiedenis.GetFavorites().First(f => f.ExternalId == "kavel-1");

            Check.Dat(terug.EndsAt == einde.AddMinutes(10), "een verlengde veiling krijgt haar nieuwe einde");
            Check.Dat(terug.AlertedLead is null,
                $"en mag opnieuw gemeld worden ({terug.AlertedLead?.ToString() ?? "leeg"})");

            // Hetzelfde einde nog eens wegschrijven mag dat NIET doen, anders stuurt elke ronde
            // Nakijken je de waarschuwingen opnieuw.
            geschiedenis.SetFavoriteAlerted(terug.Key, 15);
            geschiedenis.SetFavoriteEnd(terug.Key, einde.AddMinutes(10));
            terug = geschiedenis.GetFavorites().First(f => f.ExternalId == "kavel-1");

            Check.Dat(terug.AlertedLead == 15, "hetzelfde einde opnieuw bewaren wist dat niet");

            geschiedenis.RemoveFavorite(terug.Key);
        }
    }

    /// <summary>
    /// De hele ronde, zoals de planner hem elke halve minuut draait. Zonder kanaal aan zegt
    /// <see cref="Notifier"/> dat zelf en gaat er niets de deur uit - maar de boekhouding moet
    /// wél kloppen, want daar hangt aan of je het bericht één of tweehonderd keer krijgt.
    /// </summary>
    public static async Task RunAsync()
    {
        Check.Groep("Veiling loopt af: één ronde van de planner");

        var bewaard = AppSettings.Current.Notify;

        try
        {
            AppSettings.Current.Notify = new NotifySettings
            {
                Tray = false,
                Telegram = false,
                Email = false,
                AuctionAlert = true,
                AuctionAlertMinutes = new List<int> { 1440, 60, 15 }
            };

            var geschiedenis = new HistoryStore();

            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "sluit-bijna",
                Title = "Kavel dat zo meteen sluit",
                Url = "https://www.voorbeeld.be/kavel/1",
                EndsAt = DateTime.Now.AddMinutes(40)
            });

            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "nog-lang",
                Title = "Kavel dat nog dagen loopt",
                Url = "https://www.voorbeeld.be/kavel/2",
                EndsAt = DateTime.Now.AddDays(5)
            });

            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "geen-veiling",
                Title = "Gewoon zoekertje zonder einde",
                Url = "https://www.voorbeeld.be/zoekertje/3"
            });

            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            var na = geschiedenis.GetFavorites().ToDictionary(f => f.ExternalId);

            Check.Dat(na["sluit-bijna"].AlertedLead == 60,
                $"het kavel binnen het uur is gemeld ({na["sluit-bijna"].AlertedLead?.ToString() ?? "niets"})");

            Check.Dat(na["nog-lang"].AlertedLead is null, "dat van over vijf dagen niet");
            Check.Dat(na["geen-veiling"].AlertedLead is null, "en een zoekertje zonder einde al helemaal niet");

            // En nu het echte gevaar: de planner tikt elke halve minuut.
            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);
            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            na = geschiedenis.GetFavorites().ToDictionary(f => f.ExternalId);

            Check.Dat(na["sluit-bijna"].AlertedLead == 60,
                "nog twee tikken later staat het er nog steeds één keer");

            // Staat de waarschuwing uit, dan gebeurt er niets - ook niet voor een nieuw kavel.
            AppSettings.Current.Notify.AuctionAlert = false;

            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "uit",
                Title = "Kavel terwijl de waarschuwing uit staat",
                Url = "https://www.voorbeeld.be/kavel/4",
                EndsAt = DateTime.Now.AddMinutes(30)
            });

            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "uit").AlertedLead is null,
                "met de waarschuwing uit gebeurt er niets");

            foreach (var sleutel in new[] { "sluit-bijna", "nog-lang", "geen-veiling", "uit" })
                geschiedenis.RemoveFavorite("proef:" + sleutel);
        }
        finally
        {
            AppSettings.Current.Notify = bewaard;
        }
    }
}

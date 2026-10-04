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

            Check.Dat(AlertMoments.Noem(1440) == "1 dag vooraf" && AlertMoments.Noem(240) == "4 uur vooraf" &&
                      AlertMoments.Noem(15) == "15 minuten vooraf",
                "en een moment leest als taal");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling loopt af: de momenten vul je zelf in");
        {
            // Sinds 4 oktober 2026 zijn dit geen vier vaste vinkjes meer maar invulbare rijen,
            // per favoriet. Minuten blijft de eenheid waarin gerekend en bewaard wordt; het
            // invulveld toont de grootste eenheid die er zonder rest in past, zodat je terugziet
            // wat je intypte in plaats van 1440.
            Check.Dat(AlertMoments.Toon(1440) == (1, "dagen"), "1440 minuten toont als 1 dag");
            Check.Dat(AlertMoments.Toon(120) == (2, "uur"), "120 toont als 2 uur");
            Check.Dat(AlertMoments.Toon(15) == (15, "minuten"), "15 blijft 15 minuten");
            Check.Dat(AlertMoments.Toon(90) == (90, "minuten"),
                "90 past niet rond in uren, dus blijft het minuten");

            Check.Dat(AlertMoments.NaarMinuten(24, "uur") == 1440 &&
                      AlertMoments.NaarMinuten(2, "dagen") == 2880 &&
                      AlertMoments.NaarMinuten(15, "minuten") == 15,
                "en terug vanuit het invulveld");

            // De kolom is tekst, dus er kan rommel in staan. Eén onleesbaar stuk mag de
            // favoriet niet meenemen in zijn val.
            Check.Dat(AlertMoments.Lees("1440,60").SequenceEqual(new[] { 1440, 60 }), "inlezen");
            Check.Dat(AlertMoments.Lees("60,1440").SequenceEqual(new[] { 1440, 60 }),
                "altijd groot naar klein, hoe het er ook in staat");
            Check.Dat(AlertMoments.Lees("60,rommel,,0,-5,60").SequenceEqual(new[] { 60 }),
                "rommel, nul, negatief en dubbels vallen weg");
            Check.Dat(AlertMoments.Lees(null).Count == 0 && AlertMoments.Lees("").Count == 0,
                "leeg geeft geen momenten, en dus geen waarschuwing");

            // Tien is het maximum, aan allebei de kanten van de lijn.
            var teveel = Enumerable.Range(1, 20).Select(i => i * 60).ToList();

            Check.Dat(AlertMoments.Lees(string.Join(",", teveel)).Count == AlertMoments.Hoogstens,
                $"bij het inlezen blijven er hoogstens {AlertMoments.Hoogstens} over");

            Check.Dat(AlertMoments.Schrijf(teveel).Split(',').Length == AlertMoments.Hoogstens,
                "en bij het wegschrijven ook");

            Check.Dat(AlertMoments.Schrijf(new[] { 60, 1440, 60 }) == "1440,60",
                "wegschrijven ontdubbelt en sorteert");
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
            // Alle kanalen uit: er mag in een controle niets de deur uit. Wat hier getest
            // wordt is of de juiste drempel wordt opgeschreven, en dat gebeurt vóór het
            // versturen.
            AppSettings.Current.Notify = new NotifySettings
            {
                Tray = false,
                Telegram = false,
                Email = false
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

            // Sinds 4 oktober 2026 draagt elke favoriet zijn eigen momenten en kanalen. Een
            // favoriet waar niets voor gekozen is, blijft stil - ook al loopt zijn veiling af.
            foreach (var sleutel in new[] { "sluit-bijna", "nog-lang", "geen-veiling" })
                geschiedenis.SetFavoriteAlert("proef:" + sleutel,
                    new[] { 1440, 60, 15 }, AlertChannels.Tray);

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

            // Een favoriet waarvoor niets gekozen is, zwijgt. Dat is de stand van elke
            // bestaande favoriet na deze wijziging: niemand heeft voor hém iets ingesteld.
            geschiedenis.AddFavorite(new Listing
            {
                Source = "proef",
                ExternalId = "uit",
                Title = "Kavel zonder gekozen momenten",
                Url = "https://www.voorbeeld.be/kavel/4",
                EndsAt = DateTime.Now.AddMinutes(30)
            });

            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "uit").AlertedLead is null,
                "zonder gekozen momenten gebeurt er niets");

            // Wel momenten, maar nergens heen: dan ook niet. Anders stelt iemand iets in, ziet
            // het er goed uit, en komt er nooit iets aan.
            geschiedenis.SetFavoriteAlert("proef:uit", new[] { 60 }, AlertChannels.Geen);
            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "uit").AlertedLead is null,
                "momenten zonder kanaal tellen niet als waarschuwing");

            // En mét een kanaal gaat hij alsnog af, zodat bovenstaande geen loos resultaat is.
            geschiedenis.SetFavoriteAlert("proef:uit", new[] { 60 }, AlertChannels.Tray);
            await AuctionWatch.TickAsync(geschiedenis, DateTime.Now);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "uit").AlertedLead == 60,
                "met een kanaal erbij wél");

            // DE RONDGANG DOOR SQLITE. Twee kolommen erbij, en een waarde die stil wegvalt is
            // een waarschuwing die nooit afgaat.
            geschiedenis.SetFavoriteAlert("proef:nog-lang",
                new[] { 2880, 120, 15 }, AlertChannels.Mail | AlertChannels.Telegram);

            var terug = geschiedenis.GetFavorites().First(f => f.ExternalId == "nog-lang");

            Check.Dat(terug.AlertLeads.SequenceEqual(new[] { 2880, 120, 15 }),
                $"de momenten komen heel terug uit de databank ({string.Join(",", terug.AlertLeads)})");

            Check.Dat(terug.AlertChannels == (AlertChannels.Mail | AlertChannels.Telegram),
                $"en de kanalen ook ({terug.AlertChannels})");

            // Andere momenten betekent opnieuw kunnen waarschuwen: zet je er een fijner moment
            // bij dan wat al gemeld was, dan zou dat anders nooit meer afgaan.
            geschiedenis.SetFavoriteAlert("proef:sluit-bijna", new[] { 1440, 30 }, AlertChannels.Tray);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "sluit-bijna").AlertedLead is null,
                "andere momenten wissen wat er al gemeld was");

            foreach (var sleutel in new[] { "sluit-bijna", "nog-lang", "geen-veiling", "uit" })
                geschiedenis.RemoveFavorite("proef:" + sleutel);

            // ---------------------------------------------------------------------------
            Check.Groep("De favoriet kiest zijn kanaal, de app houdt de gegevens");
            {
                // Een zeef en geen schakelaar: een kanaal dat centraal uit staat, gaat niet
                // alsnog aan omdat een favoriet erom vraagt.
                var alles = new NotifySettings { Tray = true, Telegram = true, Email = true };

                var enkelMail = alles.Alleen(AlertChannels.Mail);

                Check.Dat(!enkelMail.Tray && !enkelMail.Telegram && enkelMail.Email,
                    "enkel e-mail gevraagd: enkel e-mail blijft over");

                var tweeKanalen = alles.Alleen(AlertChannels.Mail | AlertChannels.Telegram);

                Check.Dat(!tweeKanalen.Tray && tweeKanalen.Telegram && tweeKanalen.Email,
                    "twee kanalen gevraagd: die twee");

                Check.Dat(alles.Alleen(AlertChannels.Geen) is { Tray: false, Telegram: false, Email: false },
                    "geen kanaal gevraagd: niets");

                var zonderMail = new NotifySettings { Tray = false, Telegram = false, Email = false };

                Check.Dat(!zonderMail.Alleen(AlertChannels.Mail).Email,
                    "een kanaal dat centraal uit staat, gaat niet aan omdat een favoriet het vraagt");

                // De gegevens blijven staan: de zeef maakt een kopie en raakt de instellingen
                // zelf niet aan.
                var met = new NotifySettings { Email = true, SmtpHost = "smtp.voorbeeld.be", MailTo = "ik@voorbeeld.be" };
                var gezeefd = met.Alleen(AlertChannels.Mail);

                Check.Dat(gezeefd.SmtpHost == "smtp.voorbeeld.be" && gezeefd.MailTo == "ik@voorbeeld.be",
                    "de mailserver reist mee in de kopie");

                Check.Dat(met.Telegram == false && met.Email,
                    "en het origineel is niet gewijzigd");
            }
        }
        finally
        {
            AppSettings.Current.Notify = bewaard;
        }
    }
}

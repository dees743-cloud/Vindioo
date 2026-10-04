using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo.Checks;

/// <summary>
/// De prijswacht is het enige stuk van Vindioo dat uit zichzelf het net op gaat. Overal elders
/// gebeurt er pas iets wanneer de gebruiker op een knop duwt of een zoekopdracht gepland staat,
/// en de waarschuwing voor een aflopende veiling kost met opzet geen enkel verzoek.
///
/// <para>Daarom staat hier vooral één vraag: <b>wanneer gaat hij NIET kijken.</b> Een rem die
/// stilletjes wegvalt, verandert deze app in iets wat elke halve minuut langs vijf sites gaat.</para>
///
/// <para>Er gaat in deze controles geen enkel verzoek de deur uit: de ronde krijgt een lege
/// siteslijst mee, en dan keert <c>FavoriteWatch.CheckAsync</c> meteen terug met "die site staat
/// niet meer in Sites beheren" - vóór er iets opgehaald wordt.</para>
/// </summary>
public static class PrijswachtChecks
{
    public static async Task RunAsync()
    {
        var nu = new DateTime(2026, 10, 4, 12, 0, 0);

        // ---------------------------------------------------------------------------
        Check.Groep("Prijswacht: wanneer hij NIET gaat kijken");
        {
            Listing Favoriet(bool aan, AlertChannels kanalen, DateTime? gekeken, DateTime? einde = null) => new()
            {
                Source = "proef",
                ExternalId = "x",
                Title = "Kavel",
                AlertPrice = aan,
                AlertChannels = kanalen,
                PriceCheckedAt = gekeken,
                EndsAt = einde
            };

            Check.Dat(!PriceAlert.AanDeBeurt(Favoriet(false, AlertChannels.Mail, null), nu),
                "niet aangezet: er wordt niets opgehaald");

            Check.Dat(!PriceAlert.AanDeBeurt(Favoriet(true, AlertChannels.Geen, null), nu),
                "aangezet maar nergens heen te sturen: ook niet");

            Check.Dat(PriceAlert.AanDeBeurt(Favoriet(true, AlertChannels.Mail, null), nu),
                "nog nooit gekeken: nu wel, en dat ijkt meteen de prijs");

            Check.Dat(!PriceAlert.AanDeBeurt(Favoriet(true, AlertChannels.Mail, nu.AddHours(-5)), nu),
                "vijf uur geleden gekeken: nog niet aan de beurt");

            Check.Dat(PriceAlert.AanDeBeurt(Favoriet(true, AlertChannels.Mail, nu.AddHours(-6)), nu),
                "zes uur geleden: wel");

            // Een pc die bijgesteld wordt of de overgang naar de wintertijd mag geen reden zijn
            // om een halve dag te zwijgen.
            Check.Dat(PriceAlert.AanDeBeurt(Favoriet(true, AlertChannels.Mail, nu.AddHours(2)), nu),
                "een tijdstip in de toekomst (klok teruggezet): gewoon kijken");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijswacht: sneller zodra een veiling vandaag sluit");
        {
            // Zes uur is te grof voor een kavel dat vanavond sluit - daar gaat het bod in de
            // laatste uren omhoog, en dat is net wat je wil weten.
            Listing Kavel(DateTime? gekeken, TimeSpan overTotEinde) => new()
            {
                Source = "proef",
                ExternalId = "k",
                Title = "Kavel",
                AlertPrice = true,
                AlertChannels = AlertChannels.Mail,
                PriceCheckedAt = gekeken,
                EndsAt = nu + overTotEinde
            };

            Check.Dat(PriceAlert.AanDeBeurt(Kavel(nu.AddHours(-1), TimeSpan.FromHours(5)), nu),
                "sluit over 5 uur en een uur geleden gekeken: aan de beurt");

            Check.Dat(!PriceAlert.AanDeBeurt(Kavel(nu.AddMinutes(-30), TimeSpan.FromHours(5)), nu),
                "en een half uur geleden gekeken: nog niet");

            Check.Dat(!PriceAlert.AanDeBeurt(Kavel(nu.AddHours(-1), TimeSpan.FromDays(5)), nu),
                "sluit pas over vijf dagen: dan geldt het gewone ritme van zes uur");

            // Voorbij is voorbij: daar valt geen bod meer op te volgen.
            Check.Dat(!PriceAlert.AanDeBeurt(Kavel(nu.AddHours(-2), TimeSpan.FromHours(-1)), nu),
                "een veiling die al afgelopen is, krijgt geen spoedritme");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijswacht: wat als nieuws telt");
        {
            // Vergeleken wordt er met de prijs waarover je het LAATST bericht kreeg, niet met
            // de prijs op de kaart. Die blijft met opzet staan op wat het kostte toen je de
            // favoriet bewaarde - anders kreeg je elke ronde opnieuw hetzelfde bericht.
            Check.Dat(!PriceAlert.IsNieuws(gemeld: null, bewaard: 10m, nu: null),
                "geen prijs gelezen: geen nieuws");

            Check.Dat(!PriceAlert.IsNieuws(null, 10m, 0m), "nul is geen prijs");
            Check.Dat(!PriceAlert.IsNieuws(null, 10m, 10m), "dezelfde prijs: geen nieuws");
            Check.Dat(PriceAlert.IsNieuws(null, 10m, 24m), "nog nooit gemeld: dan telt de bewaarde prijs");
            Check.Dat(PriceAlert.IsNieuws(null, 10m, 8m), "ook omlaag is nieuws");

            Check.Dat(!PriceAlert.IsNieuws(gemeld: 24m, bewaard: 10m, nu: 24m),
                "al gemeld op 24: dan is 24 geen nieuws meer, ook al staat er 10 op de kaart");

            Check.Dat(PriceAlert.IsNieuws(24m, 10m, 30m), "en 30 wel");

            Check.Dat(!PriceAlert.IsNieuws(null, null, 24m),
                "zonder enig ijkpunt geen bericht - anders meldt de eerste ronde altijd iets");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijswacht: een ronde kiest enkel wie aan de beurt is");
        {
            var geschiedenis = new HistoryStore();

            void Zet(string id, string titel) => geschiedenis.AddFavorite(new Listing
            {
                Source = "bestaat-niet",
                ExternalId = id,
                Title = titel,
                Url = "https://www.voorbeeld.be/" + id,
                Price = 10m
            });

            Zet("wil-wel", "Favoriet met de prijswacht aan");
            Zet("wil-niet", "Favoriet zonder prijswacht");
            Zet("geen-kanaal", "Favoriet met de prijswacht aan maar nergens heen");

            geschiedenis.SetFavoriteAlert("bestaat-niet:wil-wel", Array.Empty<int>(), AlertChannels.Tray, prijs: true);
            geschiedenis.SetFavoriteAlert("bestaat-niet:geen-kanaal", Array.Empty<int>(), AlertChannels.Geen, prijs: true);

            // Het aanzetten ijkt meteen: zonder dit zou de eerste ronde "de prijs is veranderd"
            // melden terwijl er sinds jouw keuze niets gebeurd is.
            var naHetAanzetten = geschiedenis.GetFavorites().First(f => f.ExternalId == "wil-wel");

            Check.Dat(naHetAanzetten.AlertPrice, "de prijswacht staat aan na het bewaren");
            Check.Dat(naHetAanzetten.NotifiedPrice == 10m,
                $"en het ijkpunt staat meteen op de prijs van vandaag ({naHetAanzetten.NotifiedPrice})");
            Check.Dat(naHetAanzetten.PriceCheckedAt is null,
                "nog niet gekeken, dus de eerste meting laat geen zes uur op zich wachten");

            // Een lege siteslijst: CheckAsync keert meteen terug met "die site staat niet meer
            // in Sites beheren", vóór er iets opgehaald wordt. Er gaat hier dus niets de deur uit.
            await PriceAlert.TickAsync(geschiedenis, Array.Empty<SiteDefinition>(), nu);

            var na = geschiedenis.GetFavorites().ToDictionary(f => f.ExternalId);

            Check.Dat(na["wil-wel"].PriceCheckedAt is not null,
                "wie aan de beurt was, is nagekeken");

            Check.Dat(na["wil-niet"].PriceCheckedAt is null,
                "wie de prijswacht uit heeft, is niet aangeraakt");

            Check.Dat(na["geen-kanaal"].PriceCheckedAt is null,
                "en wie nergens heen kan sturen ook niet - geen bericht, geen verzoek");

            // Geen prijs gelezen (de site bestaat niet), dus het ijkpunt mag niet verschuiven.
            Check.Dat(na["wil-wel"].NotifiedPrice == 10m,
                "een mislukte meting verschuift het ijkpunt niet");

            // En meteen daarna nog een ronde: dan is er niemand meer aan de beurt.
            var voor = na["wil-wel"].PriceCheckedAt;
            await PriceAlert.TickAsync(geschiedenis, Array.Empty<SiteDefinition>(), nu);

            Check.Dat(geschiedenis.GetFavorites().First(f => f.ExternalId == "wil-wel").PriceCheckedAt == voor,
                "een tik later is hij niet opnieuw aan de beurt");

            foreach (var id in new[] { "wil-wel", "wil-niet", "geen-kanaal" })
                geschiedenis.RemoveFavorite("bestaat-niet:" + id);
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijswacht: hoogstens een paar tegelijk");
        {
            // Niet omdat het er veel zijn, maar omdat een handvol verzoeken in één seconde het
            // patroon is waar een site op let.
            Check.Dat(PriceAlert.HoogstensPerTik is > 0 and <= 5,
                $"er gaan er hoogstens {PriceAlert.HoogstensPerTik} per tik, niet alles ineens");

            Check.Dat(PriceAlert.Dringend < PriceAlert.Gewoon,
                "het spoedritme is sneller dan het gewone");

            Check.Dat(PriceAlert.Gewoon >= TimeSpan.FromHours(1),
                "en het gewone ritme is geen minutenwerk");
        }
    }
}

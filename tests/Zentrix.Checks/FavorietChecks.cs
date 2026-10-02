using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// Het opvolgen van een favoriet: staat het zoekertje er nog, en wat kost het nu?
///
/// Een favoriet is een kopie, dus de prijs erop is die van de dag dat je hem bewaarde. De
/// vraag is wat de advertentiepagina vandaag zegt, en het antwoord moet <b>bewijs</b> hebben:
/// een status van de site, een einddatum, of een prijs. Een pagina die binnenkomt zonder een
/// van die drie is "niet na te gaan" en niet "staat er nog".
///
/// De stukken HTML hieronder komen uit de echte metingen van 30 september 2026: het
/// <c>ld+json</c>-blok zoals 2dehands en Marktplaats het schrijven, de "gezocht"-advertentie
/// met <c>price 0</c>, en de einddatum zoals ze op een afgesloten kavel van AlleVeilingen
/// blijft staan. De proefsite antwoordt met 410 voor het zoekertje dat weg is, want dat is
/// wat 2dehands doet.
/// </summary>
public static class FavorietChecks
{
    /// <summary>
    /// Zoals 2dehands en Marktplaats het op hun advertentiepagina zetten. Het bedrag gaat er
    /// met Replace in en niet met interpolatie: JSON staat vol accolades, en die tellen in
    /// een geïnterpoleerde tekst mee.
    /// </summary>
    private static string MetLdJson(decimal prijs) => """
        <html><head>
        <script type="application/ld+json">
        {"@context":"https://schema.org","@type":"BreadcrumbList","itemListElement":[]}
        </script>
        <script type="application/ld+json">
        {"@context":"https://schema.org","@type":"Product","name":"Radio cd speler",
         "image":["https://voorbeeld.be/1.jpg"],
         "offers":{"@type":"Offer","priceCurrency":"EUR","price":BEDRAG}}
        </script>
        </head><body><img src="https://voorbeeld.be/1.jpg"><p>beschrijving</p></body></html>
        """.Replace("BEDRAG", prijs.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Favoriet opvolgen: de prijs uit het ld+json-blok");
        {
            Check.Dat(FavoriteWatch.PrijsUitPagina(MetLdJson(69)) == 69m,
                "een Product met offers.price 69 geeft 69");

            Check.Dat(FavoriteWatch.PrijsUitPagina(MetLdJson(1590)) == 1590m,
                "en 1590 geeft 1590");

            // Een nul telt niet als prijs, net als elders in de app: bij een
            // "gezocht"-advertentie van 2dehands staat er letterlijk price 0.
            Check.Dat(FavoriteWatch.PrijsUitPagina(MetLdJson(0)) is null,
                "price 0 telt niet als prijs (een 'gezocht'-advertentie)");

            Check.Dat(FavoriteWatch.PrijsUitPagina("<html><body>geen blok</body></html>") is null,
                "een pagina zonder ld+json geeft niets");

            // Een blok mag een lijst zijn, of zijn prijs als tekst schrijven, of hem pas
            // diep onder @graph zetten. Alle drie komen voor in het wild.
            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type="application/ld+json">[{"@type":"Thing"},{"@type":"Product","offers":{"price":"12.50"}}]</script>""")
                == 12.50m, "een lijst met de prijs als tekst geeft 12,50");

            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type="application/ld+json">{"@graph":[{"offers":{"lowPrice":7}}]}</script>""")
                == 7m, "lowPrice onder @graph telt ook");

            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type="application/ld+json">{kapot</script>""" + MetLdJson(42)) == 42m,
                "een onleesbaar blok houdt het volgende niet tegen");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Favoriet opvolgen: wat de pagina vandaag zegt");
        {
            using var site = new Proefsite();

            var def = site.Site("Proefsite");
            def.DetailImagesSelector = "img@src";
            def.DetailEndDateSelector = "div[title='Einddatum']::match(Einde op\\s+([\\d/]+\\s+[\\d:]+))";

            // Per pad een ander antwoord, zodat één proefsite alle gevallen dekt.
            site.Antwoord = adres => adres switch
            {
                "/nogtekoop" => MetLdJson(69),
                "/gesloten" => "<html><body><div title='Einddatum'>Kavel nummer: 55852</div>" +
                               "<div title='Einddatum'>Einde op 14/09/2026 19:30</div></body></html>",
                "/loopt" => "<html><body><div title='Einddatum'>Einde op 31/12/2099 19:30</div>" +
                            "<img src='https://voorbeeld.be/1.jpg'></body></html>",
                _ => "<html><body><p>niets bruikbaars</p></body></html>"
            };

            site.Status["/weg"] = 410;
            site.Status["/bestaatniet"] = 404;

            var sites = new[] { def };

            async Task<FavoriteStatus> Kijk(string pad) =>
                await FavoriteWatch.CheckAsync(Favoriet(site.Poort, pad), sites);

            var teKoop = await Kijk("/nogtekoop");
            Check.Dat(teKoop.Staat == FavoriteState.TeKoop && teKoop.PrijsNu == 69m,
                $"een advertentie die er nog staat: {teKoop.Staat}, prijs {teKoop.PrijsNu}");

            var weg = await Kijk("/weg");
            Check.Dat(weg.Staat == FavoriteState.Weg, $"410 is weg ({weg.Staat})");

            var bestaatniet = await Kijk("/bestaatniet");
            Check.Dat(bestaatniet.Staat == FavoriteState.Weg, $"404 ook ({bestaatniet.Staat})");

            var gesloten = await Kijk("/gesloten");
            Check.Dat(gesloten.Staat == FavoriteState.Afgelopen &&
                      gesloten.Einde == new DateTime(2026, 9, 14, 19, 30, 0),
                $"een kavel met een einddatum in het verleden is afgelopen ({gesloten.Einde})");

            // Een veiling die nog loopt is gewoon te koop - niet afgelopen. Zonder deze
            // controle zou "einddatum gevonden" al genoeg zijn en was elke veiling voorbij.
            var loopt = await Kijk("/loopt");
            Check.Dat(loopt.Staat == FavoriteState.TeKoop,
                $"een veiling die nog loopt staat er nog ({loopt.Staat})");

            // De tweede div wint: de eerste heeft wel het attribuut maar niet het patroon.
            // Dat is de regel "het eerste element waar het patroon ook echt op past".

            var niets = await Kijk("/anders");
            Check.Dat(niets.Staat == FavoriteState.Onbekend,
                $"een pagina waar niets uit te lezen valt, is NIET 'staat er nog' ({niets.Staat})");

            var geenSite = await FavoriteWatch.CheckAsync(
                new Listing { Source = "Verdwenen", ExternalId = "1", Url = "https://voorbeeld.be/x" }, sites);

            Check.Dat(geenSite.Staat == FavoriteState.Onbekend && geenSite.Uitleg.Contains("Sites beheren"),
                "een site die niet meer bestaat, zegt dat");

            var geenAdres = await FavoriteWatch.CheckAsync(
                new Listing { Source = "Proefsite", ExternalId = "1", Url = "" }, sites);

            Check.Dat(geenAdres.Staat == FavoriteState.Onbekend,
                "een zoekertje zonder webadres wordt niet opgehaald");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Favoriet opvolgen: de regel op de kaart");
        {
            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.Weg), 5m) == "Weg van de site",
                "weg");

            var eind = new DateTime(2026, 9, 14, 19, 30, 0);
            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.Afgelopen, Einde: eind), 5m)
                          .StartsWith("Veiling afgelopen op 14"),
                "afgelopen, met de datum erbij");

            // Het verschil is juist wat je wil zien: bij een kavel van Catawiki op 2dehands
            // ging het bod tussen het bewaren en het terugkijken van EUR 5 naar EUR 24.
            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.TeKoop, 24m), 5m) == "Nu € 24 - was € 5",
                "een prijs die veranderde, met de oude erbij");

            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.TeKoop, 5m), 5m) == "Staat er nog, € 5",
                "een prijs die gelijk bleef");

            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.TeKoop, 39.95m), 5m)
                      == "Nu € 39,95 - was € 5",
                "centen enkel wanneer ze er zijn, zoals op de kaart");

            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.TeKoop), 5m) == "Staat er nog",
                "zonder prijs blijft het bij 'staat er nog'");

            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.Onbekend, Uitleg: "Chrome staat dicht."), 5m)
                      == "Chrome staat dicht.",
                "niet na te gaan: de reden komt op de kaart");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Favorieten: aan de kaart te zien welke weg is en welke afgelopen");
        {
            // Tot 2 oktober 2026 stond er wel "1 weg, 2 afgelopen" boven de lijst, maar was aan
            // geen enkele kaart te zien wélke dat waren. Nu zijn het twee aparte vlaggen: een
            // rood kruis over wat weg is, een stempel "AFGELOPEN" over een veiling die voorbij
            // is. Opruimen gaat op diezelfde vlaggen af.
            var weg = new Listing();
            weg.WatchIsGone = true;

            var afgelopen = new Listing();
            afgelopen.WatchIsEnded = true;

            Check.Dat(weg.WatchIsGone && !weg.WatchIsEnded, "weg is niet afgelopen");
            Check.Dat(afgelopen.WatchIsEnded && !afgelopen.WatchIsGone, "en afgelopen is niet weg");
            Check.Dat(weg.WatchIsWarning && afgelopen.WatchIsWarning, "allebei zijn ze een waarschuwing");
            Check.Dat(!new Listing().WatchIsWarning, "een gewoon zoekertje niet");

            // WatchIsWarning is afgeleid, en een afgeleide eigenschap die haar wijziging niet
            // meldt, breekt stil: de kaart blijft dan in de oude kleur staan. Het scherm is hier
            // niet bij, dus dit is de plaats waar dat vastligt.
            var gemeld = new List<string>();
            var proef = new Listing();
            proef.PropertyChanged += (_, e) => gemeld.Add(e.PropertyName ?? "");

            proef.WatchIsGone = true;

            Check.Dat(gemeld.Contains(nameof(Listing.WatchIsGone)) &&
                      gemeld.Contains(nameof(Listing.WatchIsWarning)),
                $"een kruis zetten meldt ook dat de waarschuwing veranderde ({string.Join(", ", gemeld)})");

            gemeld.Clear();
            proef.WatchIsGone = true;

            Check.Dat(gemeld.Count == 0, "dezelfde waarde nog eens zetten meldt niets");

            // En de keuze die Opruimen maakt: enkel wat nagekeken en fout bevonden is.
            var lijst = new List<Listing> { weg, afgelopen, new(), new Listing { WatchText = "Staat er nog, € 5" } };

            Check.Dat(lijst.Count(f => f.WatchIsWarning) == 2,
                "opruimen neemt enkel wat weg of afgelopen is, niet wat er nog staat");
        }
    }

    private static Listing Favoriet(int poort, string pad) => new()
    {
        Source = "Proefsite",
        ExternalId = pad,
        Title = "Bewaard zoekertje",
        Price = 5m,
        Url = $"http://127.0.0.1:{poort}{pad}"
    };
}

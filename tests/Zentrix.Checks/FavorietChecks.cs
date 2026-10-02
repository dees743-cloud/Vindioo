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

    /// <summary>
    /// Een kavelpagina zoals AlleVeilingen ze schrijft, overgenomen van vijf echte pagina's
    /// (2 oktober 2026). Twee dingen staan hier met opzet precies zo in, want juist die twee
    /// maken dat de gewone lezer het bod niet vindt:
    ///
    /// <list type="number">
    ///   <item>het type van het script staat er als <c>application/ld&amp;#x2B;json</c>, dus
    ///         een regex over de ruwe tekst zoekt zich suf naar <c>ld+json</c>;</item>
    ///   <item>het bod zit niet in <c>offers.price</c> maar in <c>additionalProperty</c>, onder
    ///         een naam die de site zelf verzint ("Huidig bod").</item>
    /// </list>
    /// </summary>
    internal const string Kavelpagina = """
        <html><head>
        <script type="application/ld&#x2B;json">
        {"@context":"https://schema.org","@graph":[
          {"@type":"WebPage","name":"Lot 1 - elektrische fiets villette"},
          {"@type":"Product","name":"Lot 1 - elektrische fiets villette","sku":"57380",
           "additionalProperty":[{"@type":"PropertyValue","name":"Huidig bod","value":270.00,"unitText":"EUR"},
                                 {"@type":"PropertyValue","name":"Startbod","value":80.00,"unitText":"EUR"}]}]}
        </script>
        </head><body>
        <div class="row"><div class="col-7">Huidig bod</div><div class="col-5">&#8364; 270,00</div></div>
        <div title='Einddatum'>Einde op 31/12/2099 19:30</div>
        </body></html>
        """;

    /// <summary>Wat er in het sitebestand van AlleVeilingen staat.</summary>
    internal const string KavelprijsSelector =
        """script[type='application/ld+json']::match("name":"Huidig bod","value":([\d.]+))""";

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
        Check.Groep("Favoriet opvolgen: de pagina wordt ontleed, niet met een regex gelezen");
        {
            // Tot 2 oktober 2026 zocht FavoriteWatch zijn ld+json-blok met
            //     <script[^>]+type=["']application/ld\+json["']
            // en dat vindt enkel wat er LETTERLIJK staat. Een pagina mag haar scripttype op
            // meer manieren opschrijven, en dan is het nog altijd hetzelfde type. Deze vier
            // vormen gaven met de oude lezer alle vier NIETS.
            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type="application/ld&#x2B;json">{"@type":"Product","offers":{"price":42}}</script>""")
                == 42m, "een scripttype met een karakterverwijzing (ld&#x2B;json) telt gewoon mee");

            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type=application/ld+json>{"@type":"Product","offers":{"price":42}}</script>""")
                == 42m, "zonder aanhalingstekens ook");

            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script type=" application/ld+json ">{"@type":"Product","offers":{"price":42}}</script>""")
                == 42m, "en met spaties eromheen");

            Check.Dat(FavoriteWatch.PrijsUitPagina(
                    """<script>{"@type":"Product","offers":{"price":42}}</script>""")
                is null, "maar een script ZONDER type telt niet mee - dat is geen datablok");

            // Hetzelfde geldt voor og:title: een regex kent de volgorde van attributen niet.
            Check.Dat(FavoriteWatch.TitelUitPagina(
                    """<html><head><title>Van alles</title><meta content="Denon DCD-520" property="og:title"></head></html>""")
                == "Denon DCD-520",
                "og:title telt ook wanneer content vóór property staat");

            // En de tekst komt er ontleed uit, zonder dat er met de hand gedecodeerd wordt.
            Check.Dat(FavoriteWatch.TitelUitPagina(
                    """<html><head><meta property="og:title" content="Sony &amp; Philips"></head></html>""")
                == "Sony & Philips", "en &amp; is gewoon een ampersand");

            Check.Dat(FavoriteWatch.TitelUitPagina("<html><head><title>Radio &#039;s</title></head></html>")
                == "Radio 's", "net als in de titel van de pagina");

            // DE ANDERE KANT, en die is even belangrijk: de INHOUD van een script mag niet
            // ontleed worden. JSON zit vol tekens die in HTML iets betekenen, en als een parser
            // &amp; daar tot & zou maken, verandert de titel van een zoekertje stilletjes.
            // De HTML-norm noemt de inhoud van <script> "raw text" - geen karakterverwijzingen.
            Check.Dat(FavoriteWatch.TitelUitPagina(
                    """<script type="application/ld+json">{"@type":"Product","name":"Bang &amp; Olufsen","offers":{"price":5}}</script>""")
                == "Bang &amp; Olufsen",
                "wat IN het blok staat blijft onaangeroerd: de JSON bepaalt zelf wat het betekent");

            // Een pagina die niet te ontleden valt, mag niets laten omvallen.
            Check.Dat(FavoriteWatch.PrijsUitPagina("<<<>>> geen html") is null, "onzin geeft niets");
            Check.Dat(FavoriteWatch.TitelUitPagina("") == "", "en een lege pagina ook niet");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Favoriet opvolgen: het bod op een kavelpagina (DetailPriceSelector)");
        {
            // Waarom dit veld er moest komen. Het blok wordt sinds 2 oktober 2026 wél gevonden
            // (de lezer gaat nu langs de ontlede pagina), maar er staat geen prijs in waar de
            // standaard hem verwacht: het bod zit in additionalProperty onder een naam die de
            // site zelf verzint. Zonder deze controle lijkt het veld overbodig en haalt de
            // volgende lezer het weg.
            Check.Dat(FavoriteWatch.PrijsUitPagina(Kavelpagina) is null,
                "de ld+json-lezer vindt hier geen prijs: het bod staat in additionalProperty " +
                "en niet in offers.price");

            var veiling = new SiteDefinition { Name = "Proefveiling", DetailPriceSelector = KavelprijsSelector };

            var bod = await FavoriteWatch.PrijsAsync(veiling, Kavelpagina);
            Check.Dat(bod == 270m, $"met de selector komt het huidige bod er wel uit ({bod})");

            // TEGENPROEF, en wel twee: een verkeerd label en een verkeerd type. Allebei horen
            // ze niets te geven, anders bewijst de controle hierboven niets.
            var anderLabel = new SiteDefinition
            {
                DetailPriceSelector = """script[type='application/ld+json']::match("name":"Hoogste bod","value":([\d.]+))"""
            };

            Check.Dat(await FavoriteWatch.PrijsAsync(anderLabel, Kavelpagina) is null,
                "een label dat niet op de pagina staat, geeft niets");

            var anderType = new SiteDefinition
            {
                DetailPriceSelector = """script[type='application/json']::match("name":"Huidig bod","value":([\d.]+))"""
            };

            Check.Dat(await FavoriteWatch.PrijsAsync(anderType, Kavelpagina) is null,
                "en een script van een ander type ook niet");

            // De selector gaat voor, maar hij duwt de gewone weg niet weg: levert hij niets op,
            // dan telt het ld+json-blok gewoon weer. Anders zou een opmaakwijziging bij één site
            // de prijs bij alle andere mee om zeep helpen.
            var terugval = new SiteDefinition { DetailPriceSelector = "div.bestaatniet" };

            Check.Dat(await FavoriteWatch.PrijsAsync(terugval, MetLdJson(69)) == 69m,
                "een selector die niets vindt, valt terug op het ld+json-blok");

            Check.Dat(await FavoriteWatch.PrijsAsync(new SiteDefinition(), MetLdJson(69)) == 69m,
                "en een site zonder selector gedraagt zich zoals voordien");

            // PriceInCents hoort bij de zoek-API van een site (2dehands geeft daar centen), niet
            // bij haar advertentiepagina - die toont wat een bezoeker ziet. Zou dat hier wel
            // meetellen, dan werd 270 euro ineens 2,70.
            var centen = new SiteDefinition { DetailPriceSelector = KavelprijsSelector, PriceInCents = true };

            Check.Dat(await FavoriteWatch.PrijsAsync(centen, Kavelpagina) == 270m,
                "PriceInCents geldt niet voor de pagina van het zoekertje zelf");

            // Een nul telt niet als prijs, net als bij het ld+json-blok: een kavel waarop nog
            // niet geboden is, hoort "geen prijs" te geven en niet "gratis".
            var zonderBod = new SiteDefinition { DetailPriceSelector = KavelprijsSelector };

            Check.Dat(await FavoriteWatch.PrijsAsync(zonderBod,
                    Kavelpagina.Replace("\"value\":270.00", "\"value\":0")) is null,
                "een bod van nul telt niet als prijs");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Favoriet opvolgen: wat de pagina vandaag zegt");
        {
            using var site = new Proefsite();

            var def = site.Site("Proefsite");
            def.DetailImagesSelector = "img@src";
            def.DetailEndDateSelector = "div[title='Einddatum']::match(Einde op\\s+([\\d/]+\\s+[\\d:]+))";
            def.DetailPriceSelector = KavelprijsSelector;

            // Per pad een ander antwoord, zodat één proefsite alle gevallen dekt.
            site.Antwoord = adres => adres switch
            {
                "/nogtekoop" => MetLdJson(69),
                "/kavelmetbod" => Kavelpagina,
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

            // Dezelfde site heeft nu ook een DetailPriceSelector. Dat de regel hierboven blijft
            // kloppen, is de helft van het bewijs: de selector mag de gewone weg niet verdringen.
            var metBod = await Kijk("/kavelmetbod");
            Check.Dat(metBod.Staat == FavoriteState.TeKoop && metBod.PrijsNu == 270m,
                $"een kavel waarop geboden wordt: {metBod.Staat}, bod {metBod.PrijsNu}");

            Check.Dat(metBod.Einde == new DateTime(2099, 12, 31, 19, 30, 0),
                $"en de sluitingstijd komt er in dezelfde beurt uit ({metBod.Einde})");

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

            // Een kavel dat je vóór 2 oktober 2026 via een veilinghuis bewaarde, heeft geen
            // prijs op de kaart staan. Nakijken geeft nu wél een bod, en dan hoort daar niet
            // "was € 0" bij maar gewoon het bedrag.
            Check.Dat(FavoriteWatch.Tekst(new FavoriteStatus(FavoriteState.TeKoop, 270m), null)
                      == "Staat er nog, € 270",
                "een favoriet zonder bewaarde prijs krijgt het bod er gewoon bij");

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

using Vindioo.Models;
using Vindioo.Services;
using Vindioo.Sources;

namespace Vindioo.Checks;

/// <summary>
/// Rechtsklikken in Chrome op een zoekertje: <i>Zet in favorieten van Vindioo</i>.
///
/// Dit is de enige weg waarbij de app iets aanneemt dat ze niet gevraagd heeft, en dan nog een
/// webadres dat ze zelf gaat ophalen. Daarom gaat het grootste deel hieronder over wat er
/// <b>geweigerd</b> wordt, en niet over wat er lukt.
/// </summary>
public static class RechtsklikChecks
{
    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: de titel van een pagina waarvan we enkel het adres hebben");
        {
            // Een sitebestand beschrijft de ZOEKpagina. Er is wel een selector voor de einddatum
            // en de foto's van een advertentiepagina, maar niet voor haar titel - die stond
            // nooit ergens anders dan in het zoekresultaat. Vandaar drie wegen.
            const string metProduct = """
                <html><head>
                <title>Radio cd speler te koop | 2dehands</title>
                <script type="application/ld+json">
                {"@type":"BreadcrumbList","name":"Audio en Hifi","itemListElement":[]}
                </script>
                <script type="application/ld+json">
                {"@type":"Product","name":"Denon DCD-520 cd-speler",
                 "offers":{"price":69,"priceCurrency":"EUR"}}
                </script>
                </head><body></body></html>
                """;

            Check.Dat(FavoriteWatch.TitelUitPagina(metProduct) == "Denon DCD-520 cd-speler",
                $"de naam van het Product, niet die van het kruimelpad ({FavoriteWatch.TitelUitPagina(metProduct)})");

            // Dat kruimelpad is geen verzonnen geval: het staat op elke advertentiepagina van
            // 2dehands, en het staat er VOOR het product. Zonder de eis "dit object draagt ook
            // een prijs" zou je dus altijd de categorie als titel krijgen.
            const string enkelKruimels = """
                <html><head><title>Van alles | 2dehands</title>
                <script type="application/ld+json">
                {"@type":"BreadcrumbList","name":"Audio en Hifi","itemListElement":[]}
                </script></head><body></body></html>
                """;

            Check.Dat(FavoriteWatch.TitelUitPagina(enkelKruimels) == "Van alles | 2dehands",
                $"zonder prijs in het blok valt hij terug op de titel van de pagina ({FavoriteWatch.TitelUitPagina(enkelKruimels)})");

            const string metOg = """
                <html><head><title>Kavel 229 | AlleVeilingen</title>
                <meta property="og:title" content="Lot 229 - wii spelcomputer met toebehoren">
                </head><body></body></html>
                """;

            Check.Dat(FavoriteWatch.TitelUitPagina(metOg) == "Lot 229 - wii spelcomputer met toebehoren",
                "og:title gaat voor op de titel van de pagina");

            Check.Dat(FavoriteWatch.TitelUitPagina("<html><head><title>Caf&eacute; &amp; meer</title></head></html>")
                      == "Café & meer",
                "en HTML-tekens worden ontcijferd");

            Check.Dat(FavoriteWatch.TitelUitPagina("<html><body>niets</body></html>") == "",
                "een pagina zonder enige titel geeft niets");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: volgparameters tellen niet mee voor de identiteit");
        {
            // Het adres dat uit Chrome komt, is het adres zoals jij het voor je hebt - en daar
            // hangt vaak een ?fbclid= of een #foto2 aan die in het zoekresultaat niet staat.
            // Zonder dit zou dezelfde kavel twee kaarten krijgen.
            Check.Dat(GenericSource.SchoonAdres("https://www.2dehands.be/v/audio/123?fbclid=abc")
                      == "https://www.2dehands.be/v/audio/123",
                "fbclid gaat eraf");

            Check.Dat(GenericSource.SchoonAdres("https://www.2dehands.be/v/audio/123#foto2")
                      == "https://www.2dehands.be/v/audio/123",
                "het stuk achter # ook");

            Check.Dat(GenericSource.SchoonAdres(
                          "https://www.2dehands.be/v/123?utm_source=mail&correlationId=x&gclid=1")
                      == "https://www.2dehands.be/v/123?correlationId=x",
                $"wat geen volgparameter is, blijft staan ({GenericSource.SchoonAdres("https://www.2dehands.be/v/123?utm_source=mail&correlationId=x&gclid=1")})");

            // De lijst is bij naam en met opzet kort: op sommige sites dragen deze wél betekenis.
            // Liever een dubbel zoekertje dan twee kavels die als één tellen.
            Check.Dat(GenericSource.SchoonAdres("https://www.voorbeeld.be/v?ref=zoek&id=9&source=x")
                      == "https://www.voorbeeld.be/v?ref=zoek&id=9&source=x",
                "ref, id en source blijven: die betekenen op sommige sites wél iets");

            Check.Dat(GenericSource.SchoonAdres("https://www.voorbeeld.be/v/1") == "https://www.voorbeeld.be/v/1",
                "een gewoon adres verandert niet");

            // Dit is de belangrijkste: een adres dat al bewaard is, mag niet ineens iets anders
            // worden. Anders telt alles wat je al zag opnieuw als nieuw.
            Check.Dat(GenericSource.SchoonAdres("https://www.marktplaats.nl/v/a/m2050684965-iets")
                      == "https://www.marktplaats.nl/v/a/m2050684965-iets",
                "en wordt niet herschreven (geen poort erbij, geen andere escapes)");

            // En hetzelfde langs de weg die de app echt gebruikt.
            Check.Dat(GenericSource.IdUitLink(null, "https://www.2dehands.be/v/1?gclid=x")
                      == "https://www.2dehands.be/v/1",
                "zonder IdPattern is de opgeschoonde link de identiteit");

            Check.Dat(GenericSource.IdUitLink(new System.Text.RegularExpressions.Regex(@"/kavel/(\d+)"),
                                              "https://www.voorbeeld.be/kavel/229?fbclid=x") == "229",
                "met een IdPattern verandert er niets: dat wint gewoon");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: welke site hoort bij dit adres");
        {
            var tweedehands = new SiteDefinition
            {
                Name = "2dehands",
                SearchUrlTemplate = "https://www.2dehands.be/q/{query}/",
                BaseUrl = "https://www.2dehands.be"
            };

            var veilingen = new SiteDefinition
            {
                Name = "AlleVeilingen",
                SearchUrlTemplate = "https://alleveilingen.be/zoeken?q={query}",
                BaseUrl = "https://alleveilingen.be"
            };

            var sites = new[] { tweedehands, veilingen };

            Check.Dat(FavoriteFromUrl.SiteVoor("https://www.2dehands.be/v/audio/123", sites)?.Name == "2dehands",
                "een gewone link komt bij de juiste site");

            // www ervoor of eraf is dezelfde site. Zonder dit zou een link van 2dehands.be
            // "Vindioo kent deze site niet" opleveren terwijl de site er gewoon staat.
            Check.Dat(FavoriteFromUrl.SiteVoor("https://2dehands.be/v/audio/123", sites)?.Name == "2dehands",
                "en zonder www ook");

            Check.Dat(FavoriteFromUrl.SiteVoor("https://www.alleveilingen.be/kavel/9", sites)?.Name == "AlleVeilingen",
                "omgekeerd ook: met www terwijl het bestand er geen heeft");

            Check.Dat(FavoriteFromUrl.SiteVoor("https://www.marktplaats.nl/v/1", sites) is null,
                "een site die je niet hebt, geeft niets");

            Check.Dat(FavoriteFromUrl.SiteVoor("geen adres", sites) is null, "en onzin ook niet");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: wat er geweigerd wordt voor er iets opgehaald wordt");
        {
            var history = new HistoryStore();

            var sites = new List<SiteDefinition>
            {
                new() { Name = "2dehands", SearchUrlTemplate = "https://www.2dehands.be/q/{query}/" }
            };

            // Wat hier binnenkomt gaat de app ZELF ophalen. Een adres op je eigen netwerk zou
            // haar dus naar de beheerpagina van je router kunnen sturen.
            var prive = await FavoriteFromUrl.VoegToeAsync("https://192.168.1.1/zoekertje", sites, history);
            Check.Dat(!prive.Ok && prive.Melding.Contains("eigen netwerk"),
                $"een adres op je eigen netwerk wordt geweigerd ({prive.Melding})");

            var plat = await FavoriteFromUrl.VoegToeAsync("http://www.2dehands.be/v/1", sites, history);
            Check.Dat(!plat.Ok && plat.Melding.Contains("https"),
                $"http in plaats van https ook ({plat.Melding})");

            var onbekend = await FavoriteFromUrl.VoegToeAsync("https://www.ergensanders.be/v/1", sites, history);
            Check.Dat(!onbekend.Ok && onbekend.Melding.Contains("kent deze site niet"),
                $"een onbekende site krijgt een zin die zegt wat je eraan doet ({onbekend.Melding})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: een kavel van een veilinghuis dat Vindioo niet kent");
        {
            // Je staat op bopa.be en wil dat kavel bewaren, maar bopa.be staat niet bij je sites.
            // Vindioo zoekt het dan terug op je veilingsites, en bewaart ENKEL bij een zekere
            // treffer: de pagina van de kandidaat moet het adres bevatten waarop jij klikte.

            // 1. De zoekterm. <h1> gaat voor, want de <title> van zo'n pagina is vaak die van de
            //    site. Bij BOPA staat er letterlijk "BOPA Veilingen | Uw veiling makelaar voor
            //    online veilingen" in de titel - ook ná het renderen, dat is nagemeten.
            const string bopa = """
                <html><head><title>BOPA Veilingen | Uw veiling makelaar voor online veilingen</title></head>
                <body><h1>Lot 1: Elektrische fiets Villette</h1></body></html>
                """;

            Check.Dat(FavoriteFromUrl.Zoekterm(bopa) == "Lot 1: Elektrische fiets Villette",
                $"de h1 wordt de zoekterm, niet de titel van de site ({FavoriteFromUrl.Zoekterm(bopa)})");

            Check.Dat(FavoriteFromUrl.Zoekterm("<html><head><title>Kavel 9</title></head><body></body></html>")
                      == "Kavel 9",
                "zonder h1 valt hij terug op de gewone weg");

            Check.Dat(FavoriteFromUrl.Zoekterm("<h1>Caf&eacute; <span>stoel</span></h1>") == "Café stoel",
                "met de tekens ontcijferd en de tags eruit");

            // 2. Het stuk waarmee we de terugkoppeling nakijken.
            Check.Dat(FavoriteFromUrl.Kern("https://www.bopa.be/auction/520/lot/57380?fbclid=x")
                      == "bopa.be/auction/520/lot/57380",
                $"de kern: zonder schema, zonder www, zonder volgparameter ({FavoriteFromUrl.Kern("https://www.bopa.be/auction/520/lot/57380?fbclid=x")})");

            Check.Dat(FavoriteFromUrl.Kern("https://bopa.be/auction/520/lot/57380/") ==
                      FavoriteFromUrl.Kern("https://www.bopa.be/auction/520/lot/57380"),
                "met of zonder www en afsluitende streep is dezelfde kern");

            // 3. De kandidaten uit een zoekpagina, met IdPattern als zeef. De HTML hieronder is
            //    gebouwd naar wat alleveilingen.be echt teruggeeft: het pad draagt het
            //    veilinghuis, en elke kavel staat er twee keer in (de foto en de titel).
            var site = new SiteDefinition
            {
                Name = "AlleVeilingen",
                BaseUrl = "https://alleveilingen.be",
                IdPattern = @"/kavel/(\d+)",
                IsAuction = true
            };

            const string zoekpagina = """
                <a href="/nl/Bopa/kavel/12528539/lot-1---elektrische-fiets-villette"><img src="/foto/1.webp"></a>
                <a href="/nl/Bopa/kavel/12528539/lot-1---elektrische-fiets-villette">Lot 1</a>
                <a href="/nl/VH-Auctions/kavel/12605214/elektrische-fiets-norta">Norta</a>
                <a href="/nl/veilinghuizen">Alle veilinghuizen</a>
                <a href="https://alleveilingen.be/nl/Vavato/kavel/999/iets">Vavato</a>
                """;

            var kandidaten = FavoriteFromUrl.Kandidaten(zoekpagina, site, 8);

            Check.Dat(kandidaten.Count == 3, $"drie kavels, de gewone links tellen niet mee ({kandidaten.Count})");

            Check.Dat(kandidaten[0] == "https://alleveilingen.be/nl/Bopa/kavel/12528539/lot-1---elektrische-fiets-villette",
                $"relatieve adressen krijgen het basisadres ({kandidaten[0]})");

            Check.Dat(kandidaten.Distinct().Count() == kandidaten.Count,
                "en dezelfde kavel telt één keer, al staat hij er twee keer in");

            Check.Dat(FavoriteFromUrl.Kandidaten(zoekpagina, site, 2).Count == 2,
                "de rem op het aantal werkt: elke kandidaat kost een verzoek");

            Check.Dat(FavoriteFromUrl.Kandidaten(zoekpagina, new SiteDefinition { IdPattern = "" }, 8).Count == 0,
                "een site zonder IdPattern doet niet mee: dan is een kavellink niet te herkennen");

            // 4. En de weigeringen, zonder dat er één verzoek de deur uitgaat.
            var history = new HistoryStore();

            var kort = await FavoriteFromUrl.VoegToeAsync("https://www.bopa.be/", new List<SiteDefinition>(), history);
            Check.Dat(!kort.Ok && kort.Melding.Contains("te kort"),
                $"een adres zonder pad bewijst niets en wordt niet gezocht ({kort.Melding})");

            var geenVeiling = await FavoriteFromUrl.VoegToeAsync(
                "https://www.bopa.be/auction/520/lot/57380",
                new List<SiteDefinition> { new() { Name = "2dehands", IsAuction = false, Enabled = true } },
                history);

            Check.Dat(!geenVeiling.Ok && geenVeiling.Melding.Contains("geen veilingsite"),
                $"zonder veilingsite valt er niets terug te zoeken ({geenVeiling.Melding})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: wat er van een pagina gemaakt wordt");
        {
            var history = new HistoryStore();

            var def = new SiteDefinition
            {
                Name = "Proefveiling",
                SearchUrlTemplate = "https://www.proefveiling.be/zoek?q={query}",
                IdPattern = @"/kavel/(\d+)",
                DetailImagesSelector = "img@src",
                DetailEndDateSelector = "div[title='Einddatum']::match(Einde op\\s+([\\d/]+\\s+[\\d:]+))"
            };

            const string pagina = """
                <html><head>
                <title>Kavel 229 | Proefveiling</title>
                <script type="application/ld+json">
                {"@type":"Product","name":"Lot 229 - wii spelcomputer",
                 "offers":{"price":20,"priceCurrency":"EUR"}}
                </script>
                </head><body>
                <div title='Einddatum'>Einde op 31/12/2099 19:30</div>
                <img src='https://www.proefveiling.be/foto/229.jpg'>
                </body></html>
                """;

            const string adres = "https://www.proefveiling.be/kavel/229";

            var uit = await FavoriteFromUrl.VanPaginaAsync(adres, def, pagina, history);

            Check.Dat(uit.Ok, $"de favoriet komt erin ({uit.Melding})");
            Check.Dat(uit.Melding.Contains("Lot 229") && uit.Melding.Contains("20"),
                $"en de melding zegt wat er bijkwam, met de prijs ({uit.Melding})");

            var bewaard = history.GetFavorites().First(f => f.Source == "Proefveiling");

            Check.Dat(bewaard.Title == "Lot 229 - wii spelcomputer", $"de titel ({bewaard.Title})");
            Check.Dat(bewaard.Price == 20m, $"de prijs ({bewaard.Price})");
            Check.Dat(bewaard.Url == adres, "het adres");

            // Hetzelfde id als het zoekresultaat eruit zou halen. Zonder dat zou dezelfde kavel
            // twee keer in je favorieten staan: één keer via het zoekresultaat en één keer via
            // Chrome, met twee verschillende sleutels.
            Check.Dat(bewaard.ExternalId == "229", $"het id uit IdPattern, niet de hele link ({bewaard.ExternalId})");

            Check.Dat(bewaard.EndsAt == new DateTime(2099, 12, 31, 19, 30, 0),
                $"de sluitingstijd uit de pagina, zodat de waarschuwing meteen werkt ({bewaard.EndsAt})");

            Check.Dat(bewaard.Thumbnail == "https://www.proefveiling.be/foto/229.jpg", "en een foto");

            // Twee keer hetzelfde mag niet twee favorieten geven.
            var nogEens = await FavoriteFromUrl.VanPaginaAsync(adres, def, pagina, history);

            Check.Dat(!nogEens.Ok && nogEens.Melding.Contains("staat al"),
                $"dezelfde kavel een tweede keer: dat wordt gezegd, niet gedaan ({nogEens.Melding})");

            Check.Dat(history.GetFavorites().Count(f => f.Source == "Proefveiling") == 1,
                "en er staat er maar één");

            // Zonder titel is het een lege kaart in je favorieten; dan is "niet gelukt"
            // eerlijker dan iets bewaren waar je niets aan hebt.
            var leeg = await FavoriteFromUrl.VanPaginaAsync(
                "https://www.proefveiling.be/kavel/777", def, "<html><body>niets</body></html>", history);

            Check.Dat(!leeg.Ok && leeg.Melding.Contains("geen titel"),
                $"een pagina waar niets uit te lezen valt, wordt niet bewaard ({leeg.Melding})");

            Check.Dat(history.GetFavorites().Count(f => f.Source == "Proefveiling") == 1,
                "en die staat er dus ook niet bij");

            foreach (var favoriet in history.GetFavorites().Where(f => f.Source == "Proefveiling").ToList())
                history.RemoveFavorite(favoriet.Key);
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Rechtsklik: het bod komt mee van een kavel van een veilinghuis");
        {
            // Tot 2 oktober 2026 kwam een kavel dat je via een veilinghuis toevoegde zonder
            // prijs binnen, en stond er in de uitleg dat het bod pas met JavaScript een getal
            // werd. Dat klopte niet: het staat gewoon in de pagina, alleen niet op de plaats
            // waar de algemene lezer kijkt. Zie Kavelpagina in FavorietChecks.
            var history = new HistoryStore();

            var def = new SiteDefinition
            {
                Name = "Proefveiling",
                IsAuction = true,
                IdPattern = @"/kavel/(\d+)",
                DetailPriceSelector = FavorietChecks.KavelprijsSelector,
                DetailEndDateSelector = "div[title='Einddatum']::match(Einde op\\s+([\\d/]+\\s+[\\d:]+))"
            };

            const string adres = "https://www.proefveiling.be/kavel/57380";

            // De titel komt hier van buiten mee, net als bij de echte weg langs een
            // veilingsite: daar leest ViaVeilingsiteAsync hem uit de h1.
            var uit = await FavoriteFromUrl.VanPaginaAsync(adres, def, FavorietChecks.Kavelpagina, history,
                                                           titel: "Lot 1 - elektrische fiets villette");

            Check.Dat(uit.Ok, $"de favoriet komt erin ({uit.Melding})");

            Check.Dat(uit.Melding.Contains("270"),
                $"en de melding die in Chrome verschijnt, noemt het bod ({uit.Melding})");

            var bewaard = history.GetFavorites().First(f => f.Source == "Proefveiling");

            Check.Dat(bewaard.Price == 270m, $"het bod staat op de favoriet ({bewaard.Price})");
            Check.Dat(bewaard.EndsAt == new DateTime(2099, 12, 31, 19, 30, 0),
                $"en de sluitingstijd ook ({bewaard.EndsAt})");

            // TEGENPROEF: zonder het veld in het sitebestand blijft de favoriet prijsloos -
            // precies zoals het vóór deze wijziging ging. Vijf echte kavels van vier
            // veilinghuizen gaven alle vijf NULL.
            history.RemoveFavorite(bewaard.Key);

            var zonder = new SiteDefinition
            {
                Name = "Proefveiling",
                IsAuction = true,
                IdPattern = @"/kavel/(\d+)"
            };

            await FavoriteFromUrl.VanPaginaAsync(adres, zonder, FavorietChecks.Kavelpagina, history,
                                                 titel: "Lot 1 - elektrische fiets villette");

            Check.Dat(history.GetFavorites().First(f => f.Source == "Proefveiling").Price is null,
                "zonder DetailPriceSelector blijft dezelfde pagina prijsloos");

            foreach (var favoriet in history.GetFavorites().Where(f => f.Source == "Proefveiling").ToList())
                history.RemoveFavorite(favoriet.Key);
        }
    }
}

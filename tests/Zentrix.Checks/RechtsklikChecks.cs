using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// Rechtsklikken in Chrome op een zoekertje: <i>Zet in favorieten van Zentrix</i>.
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
            // "Zentrix kent deze site niet" opleveren terwijl de site er gewoon staat.
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
    }
}

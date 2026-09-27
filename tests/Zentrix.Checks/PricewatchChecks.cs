using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// De Pricewatch van Tweakers: wat kost dit nieuw, en wat was de laatst bekende prijs.
///
/// Het opzoeken en het uitlezen staan hier los van het netwerk. De productnamen hieronder komen
/// uit de echte sitemap van 27 september 2026, en de stukken HTML uit de echte productpagina's -
/// zo meten de controles wat er werkelijk staat, en niet wat ik denk dat er staat.
/// </summary>
public static class PricewatchChecks
{
    /// <summary>Een stukje van de echte productlijst, met de valstrikken erin.</summary>
    private static IEnumerable<(string, string)> Lijst() => new[]
    {
        ("1629298", "marantz-cd6007-zwart"),
        ("1629380", "marantz-cd6007-zilver"),
        ("2369470", "marantz-cd-70"),
        ("1867374", "marantz-cd-60"),
        ("2036194", "marantz-cd-50n-zwart"),
        ("2260984", "sony-playstation-5-slim-825gb-digital-edition-wit-zwart"),
        ("2271184", "nyko-charge-base-for-playstation-5"),
        ("1234567", "hp-intel-core-i5-2500-tray"),
        ("7654321", "hp-elitebook-840-g1-f1n25ea"),
        ("1111111", "wii-carnival-games-mini-golf-wii"),
        ("2222222", "nintendo-wii-sports-resort-+-controller"),
        ("3333333", "sony-playstation-5-slim-825gb-digital-edition-marvels-wolverine-limited-edition-geel-wit")
    };

    public static void Run()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Pricewatch: het juiste product voorstellen, en anders niets");
        {
            var cd6007 = Pricewatch.Kies("marantz cd6007", Lijst(), 6);

            Check.Dat(cd6007.Count == 2 && cd6007[0].Slug.StartsWith("marantz-cd6007"),
                $"marantz cd6007 geeft de twee CD6007's ({string.Join(", ", cd6007.Select(v => v.Slug))})");

            // De regel die de meeste onzin tegenhoudt. Zonder hem kwam "Marantz CD5003" uit op de
            // CD-70: een ander toestel, met een andere prijs, en niets dat het verraadt.
            var cd5003 = Pricewatch.Kies("Marantz - CD5003 Cd-speler", Lijst(), 6);

            Check.Dat(cd5003.Count == 0,
                $"een modelnummer dat er niet is, geeft niets ({string.Join(", ", cd5003.Select(v => v.Slug))})");

            var elite = Pricewatch.Kies("HP EliteBook 840 G7", Lijst(), 6);

            Check.Dat(!elite.Any(v => v.Slug.Contains("core-i5")),
                "een EliteBook komt niet uit op een losse processor");

            // Minstens twee woorden raak: één merk is geen treffer.
            Check.Dat(Pricewatch.Kies("marantz", Lijst(), 6).Count == 0, "één los merk is geen voorstel");
            Check.Dat(Pricewatch.Kies("commodore 64", Lijst(), 6).Count == 0,
                "en wat Tweakers niet kent, geeft eerlijk niets");

            // Hoe minder overbodige woorden in de productnaam, hoe preciezer de treffer. En de
            // woorden tellen één keer: de Wolverine-editie heeft "edition" twee keer in haar
            // naam staan, en stond daardoor bóven het toestel waar je naar kijkt.
            var ps5 = Pricewatch.Kies("Sony PlayStation 5 Slim 825GB Digital Edition", Lijst(), 6);

            Check.Dat(ps5.Count > 0 && ps5[0].Id == "2260984",
                $"de kale PlayStation 5 Slim staat boven de bundels ({ps5.FirstOrDefault()?.Slug})");

            Check.Dat(Pricewatch.Kies("", Lijst(), 6).Count == 0, "een lege term geeft niets");

            var naam = new Pricewatch.Voorstel("1629298", "marantz-cd6007-zwart");

            Check.Dat(naam.Naam == "Marantz cd6007 zwart", $"de naam is leesbaar ({naam.Naam})");
            Check.Dat(naam.Url == "https://tweakers.net/pricewatch/1629298/marantz-cd6007-zwart.html",
                "en de link klopt");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Pricewatch: de prijs van een productpagina lezen");
        {
            var product = new Pricewatch.Voorstel("1629298", "marantz-cd6007-zwart");

            // Zoals het er op 27 september 2026 stond: de prijs in het ld+json-blok (schema.org).
            var teKoop = Pricewatch.Lees("""
                <html><head>
                  <script type="application/ld+json">
                  {"@context":"https://schema.org","@graph":[
                    {"@type":"Organization","name":"Tweakers"},
                    {"@type":"Product","name":"Marantz CD6007 Zwart",
                     "offers":{"@type":"AggregateOffer","lowPrice":395,"highPrice":449,
                               "offerCount":8,"priceCurrency":"EUR"}}]}
                  </script>
                </head><body><div id="section-prices">Amazon.com.be € 395,-</div></body></html>
                """, product);

            Check.Dat(teKoop.Vanaf == 395 && teKoop.Tot == 449 && teKoop.Winkels == 8,
                $"nieuwprijs, bovengrens en winkels ({teKoop.Vanaf}, {teKoop.Tot}, {teKoop.Winkels})");
            Check.Dat(teKoop.Naam == "Marantz CD6007 Zwart",
                $"de naam komt uit het blok, niet uit de link ({teKoop.Naam})");
            Check.Dat(teKoop.Regel == "Nieuw vanaf € 395 bij 8 winkels", $"de regel: {teKoop.Regel}");
            Check.Dat(teKoop.TweedehandsRegel.Length == 0, "geen V&A-regel als er geen advertenties zijn");

            // Niet meer te koop: dan staat er géén offers-blok, en zegt de pagina het in een zin.
            // De   is de vaste spatie die Tweakers tussen het euroteken en het bedrag zet.
            var weg = Pricewatch.Lees("""
                <html><body>
                  <div id="section-prices"><div class="noPriceMessage"><p>Er zijn geen actuele prijzen
                  bekend van dit product. De laatst bekende laagste prijs was € 41,51 op 20 juni 2026.</p>
                  </div></div>
                </body></html>
                """.Replace("\\u00a0", " "), product);

            Check.Dat(weg.Vanaf is null, "zonder offers-blok is er geen nieuwprijs");
            Check.Dat(weg.LaatstBekend == 41.51m, $"maar wel de laatst bekende ({weg.LaatstBekend})");
            Check.Dat(weg.LaatstBekendOp == "20 juni 2026", $"met de datum erbij ({weg.LaatstBekendOp})");
            Check.Dat(weg.Regel.StartsWith("Niet meer te koop"), $"de regel: {weg.Regel}");

            // Hun eigen tweedehandsmarkt staat op dezelfde pagina - dus zonder hun zoekpagina,
            // die robots.txt verbiedt, ooit aan te raken.
            var vraagAanbod = Pricewatch.Lees("""
                <html><body><div id="section-vraagaanbod">
                  <div class="pdp-va-offer"><span>11</span> advertenties voor dit product</div>
                  <div class="pdp-va-offer"><span>10</span> aangeboden, vanaf <span>€ 420,-</span></div>
                  <div class="pdp-va-offer"><span>1</span> gevraagd</div>
                </div></body></html>
                """, product);

            Check.Dat(vraagAanbod.Advertenties == 11, $"het aantal advertenties ({vraagAanbod.Advertenties})");
            Check.Dat(vraagAanbod.TweedehandsVanaf == 420, $"en vanaf hoeveel ({vraagAanbod.TweedehandsVanaf})");
            Check.Dat(vraagAanbod.TweedehandsRegel == "Vraag & Aanbod: 11 advertenties, vanaf € 420",
                $"de regel: {vraagAanbod.TweedehandsRegel}");

            // Een pagina die niets van dit alles heeft, mag niet omvallen.
            var leeg = Pricewatch.Lees("<html><body>niets</body></html>", product);

            Check.Dat(leeg.Vanaf is null && leeg.LaatstBekend is null && leeg.Advertenties == 0,
                "een pagina zonder prijs valt niet om");
            Check.Dat(leeg.Regel == "Geen prijs bekend bij Tweakers", $"en zegt dat: {leeg.Regel}");

            // Kapotte JSON in één blok mag de rest niet meeslepen: er staan er meerdere op een pagina.
            var stuk = Pricewatch.Lees("""
                <html><head>
                  <script type="application/ld+json">{ dit is geen json </script>
                  <script type="application/ld+json">
                  {"@type":"Product","name":"Toch gevonden",
                   "offers":{"lowPrice":"€ 1.299,00","offerCount":3}}
                  </script>
                </head><body></body></html>
                """, product);

            Check.Dat(stuk.Naam == "Toch gevonden" && stuk.Vanaf == 1299m,
                $"een kapot blok laat het volgende staan ({stuk.Naam}, {stuk.Vanaf})");
        }
    }
}

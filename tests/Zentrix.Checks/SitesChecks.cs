using System.Text.Json;
using System.Text.RegularExpressions;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// Wat de motoren met een sitebestand doen: de linkmotor op kaarten zoals Facebook ze toont,
/// een vast id uit een link (IdPattern), en een zoekterm in een JSON-blok (Discogs).
/// Alles op zelfgeschreven HTML; een echte, bewaarde pagina hoort niet in deze repository.
/// </summary>
public static class SitesChecks
{
    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Linkmotor: kaarten met losse tekststukken, zoals Facebook ze toont");
        {
            var def = new SiteDefinition
            {
                Name = "Facebook Marketplace",
                Engine = SiteEngine.LinkText,
                BaseUrl = "https://www.facebook.com",
                ItemSelector = "a[href*='/marketplace/item/']",
                LinkText = new LinkTextOptions
                {
                    IdPattern = @"/marketplace/item/(\d+)",
                    UrlTemplate = "https://www.facebook.com/marketplace/item/{id}/",
                    PriceMarkers = { "€", "Gratis" },
                    LocationPattern = @",\s*(VLG|WAL|BRU)$"
                }
            };

            // Zo staat het in de echte pagina: elk stuk in een eigen element, zonder regeleinden.
            const string html = """
                <html><body>
                <a href="/marketplace/item/1/?ref=search"><img src="a.jpg"><span>Zojuist geplaatst</span><span>€&nbsp;50</span><span>€&nbsp;70</span><span>Meisjes fiets 26</span><span>Kortrijk, VLG</span></a>
                <a href="/marketplace/item/2/"><span>€&nbsp;1.234</span><span>Giant fiets </span><span>Ronse, VLG</span></a>
                <a href="/marketplace/item/3/"><span>€&nbsp;80</span><span>Hemiksem, VLG</span></a>
                <a href="/marketplace/item/4/"><span>Gratis</span><span>Kast</span><span>Gent, VLG</span></a>
                <a href="/marketplace/item/5/"><span>€&nbsp;25</span><span>Krachtige PC | 32</span><span>GB SSD</span><span>Roeselare, VLG</span></a>
                <a href="/marketplace/item/6/">€ 175Commodore 64Aalter</a>
                <a href="/marketplace/item/1/?ref=anders"><span>€ 50</span><span>Meisjes fiets 26</span><span>Kortrijk, VLG</span></a>
                <a href="/marketplace/item/create/"><span>Iets te koop aanbieden</span></a>
                </body></html>
                """;

            var lijst = new LinkTextSource(def).ReadPage(html);
            var per = lijst.ToDictionary(l => l.ExternalId);

            Check.Dat(per.TryGetValue("1", out var een) && een.Title == "Meisjes fiets 26" && een.PriceLabel.EndsWith("50") &&
                      een.Price == 50 && een.Location == "Kortrijk, VLG",
                $"label en oude prijs horen niet bij prijs of titel ('{een?.Title}', '{een?.PriceLabel}', '{een?.Location}')");
            Check.Dat(per.TryGetValue("2", out var twee) && twee.Title == "Giant fiets" && twee.Price == 1234 && twee.Location == "Ronse, VLG",
                "een kaart zonder hoofdletter tussen titel en plaats valt niet meer weg");
            Check.Dat(!per.ContainsKey("3"), "een kaart zonder titel blijft weg");
            Check.Dat(per.TryGetValue("4", out var vier) && vier.PriceLabel == "Gratis" && vier.Title == "Kast", "Gratis is een prijs");
            Check.Dat(per.TryGetValue("5", out var vijf) && vijf.Title == "Krachtige PC | 32" && vijf.Location == "Roeselare, VLG",
                $"de plaats krijgt geen stuk van de titel meer ('{vijf?.Location}')");
            Check.Dat(per.TryGetValue("6", out var zes) && zes.Title == "Commodore 64" && zes.Location == "Aalter" && zes.Price == 175,
                "alles in één tekststuk: de oude manier werkt nog");
            Check.Dat(lijst.Count(l => l.ExternalId == "1") == 1 && een!.Url == "https://www.facebook.com/marketplace/item/1/",
                "hetzelfde zoekertje twee keer: één keer, met een nette link");

            // Het logboek zegt waarom kaarten wegvallen. Zonder die regel weet je bij Facebook
            // enkel dat het scrollen 126 kaarten telde en de motor er 98 overhield.
            var regel = System.IO.File.ReadLines(Log.FilePath).LastOrDefault(r => r.Contains("kaarten ->")) ?? "(geen regel)";
            Check.Dat(regel.EndsWith("Facebook Marketplace: 8 kaarten -> 5 zoekertjes (1 dubbel, 1 zonder titel, 1 geen zoekertje)"),
                $"het logboek splitst uit wat er wegviel ({regel})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("IdPattern: een vast id uit de link, ook als de link de zoekopdracht bevat");
        {
            const string html = """
                <html><body>
                <article class="card"><h2 class="card-title"><a href="/nl/Huis/kavel/12392006/computer?fi=cGc6Mg">Computer</a></h2></article>
                <article class="card"><h2 class="card-title"><a href="/nl/Huis/kavel/12392006/computer?fi=cGc6Mw">Computer</a></h2></article>
                </body></html>
                """;

            SiteDefinition Def(string patroon) => new()
            {
                Name = "AlleVeilingen",
                BaseUrl = "https://alleveilingen.be",
                ItemSelector = "article.card",
                TitleSelector = "h2.card-title a",
                UrlSelector = "h2.card-title a@href",
                IdPattern = patroon
            };

            var zonder = await new GenericSource(Def("")).ReadPageAsync(html);
            var met = await new GenericSource(Def(@"/kavel/(\d+)")).ReadPageAsync(html);
            var kapot = await new GenericSource(Def(@"/kavel/(\d+")).ReadPageAsync(html);

            Check.Dat(zonder.Select(l => l.Key).Distinct().Count() == 2, "zonder patroon: pagina 2 en 3 zijn twee zoekertjes");
            Check.Dat(met.Select(l => l.Key).Distinct().Single() == "AlleVeilingen:12392006" &&
                      met[0].Url.EndsWith("?fi=cGc6Mg"), "met patroon: één zoekertje, en de link blijft volledig");
            Check.Dat(kapot.Count == 2 && kapot[0].ExternalId.StartsWith("https://"), "een ongeldig patroon: gewoon de volledige link");
        }

        Check.Groep("IdPattern: bestaande sleutels in de databank gaan mee (ApplyIdPatterns)");
        {
            var history = new HistoryStore();
            var zoek = new SavedSearch { Query = "idpatroon" };
            zoek.Id = history.Add(zoek);

            const string a = "AV:https://x/nl/kavel/1/a?fi=MQ";
            const string b = "AV:https://x/nl/kavel/1/a?fi=Mg";
            const string c = "AV:https://x/nl/kavel/2/b?fi=MQ";
            const string ander = "Anders:https://x/nl/kavel/3/c";

            history.MarkSeen(zoek.Id, new[] { a, b, c, ander });
            history.AddFavorite(new Listing { Source = "AV", ExternalId = "https://x/nl/kavel/2/b?fi=MQ", Title = "B", Url = "https://x/nl/kavel/2/b?fi=MQ" });
            history.SaveOutcome(zoek.Id, new[] { new Listing { Source = "AV", ExternalId = "https://x/nl/kavel/1/a?fi=MQ", Title = "A" } });

            var sites = new[]
            {
                new SiteDefinition { Name = "AV", IdPattern = @"/kavel/(\d+)" },
                new SiteDefinition { Name = "Anders" }
            };

            var eerste = history.ApplyIdPatterns(sites);
            Check.Dat(history.GetSeenKeys(zoek.Id).SetEquals(new[] { "AV:1", "AV:2", ander }),
                $"al gezien: omgezet, dubbels samengevoegd, een site zonder patroon ongemoeid ({string.Join(", ", history.GetSeenKeys(zoek.Id))})");
            Check.Dat(history.GetFavoriteKeys().Contains("AV:2") && history.GetFavorites().Any(f => f.ExternalId == "2"),
                "favorieten: sleutel en id omgezet");
            Check.Dat(history.GetOutcome(zoek.Id).Single().ExternalId == "1", "bewaarde resultaten: omgezet");
            Check.Dat(eerste > 0 && history.ApplyIdPatterns(sites) == 0, "een tweede keer verandert niets");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Zoek-URL: een zoekterm in een JSON-blok (Discogs)");
        {
            var def = new SiteDefinition
            {
                Name = "Discogs",
                Kind = SiteKind.Json,
                SearchUrlTemplate = "https://www.discogs.com/graphql?operationName=MarketplaceSearch" +
                                    "&variables={\"query\":\"{query}\",\"filter\":{{filters}},\"first\":100}" +
                                    "&extensions={\"persistedQuery\":{\"version\":1}}"
            };

            string Query(string term)
            {
                var url = SearchUrlBuilder.Build(def, term, null);
                var variables = Uri.UnescapeDataString(Regex.Match(url, "variables=([^&]*)").Groups[1].Value);
                using var doc = JsonDocument.Parse(variables);
                return doc.RootElement.GetProperty("query").GetString()!;
            }

            string? Fout(Func<string> f) { try { f(); return null; } catch (Exception ex) { return ex.Message; } }

            Check.Dat(Query("cd") == "cd", "een gewone zoekterm");
            Check.Dat(Fout(() => Query("12\" maxi")) is null && Query("12\" maxi") == "12\" maxi",
                "een aanhalingsteken in de zoekterm geeft nog geldige JSON");
            Check.Dat(Query(@"AC\DC é") == @"AC\DC é", "een backslash en een accent ook");

            var gewoon = new SiteDefinition { SearchUrlTemplate = "https://site.be/zoek?q={query}" };
            Check.Dat(SearchUrlBuilder.Build(gewoon, "12\" maxi", null) == "https://site.be/zoek?q=12%22%20maxi",
                "een zoekterm buiten een JSON-blok: enkel URL-versleuteling, zoals vroeger");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("::match: enkel de stad, of enkel het land, uit een langere tekst");
        {
            // De vijf vormen zoals AlleVeilingen ze op 17 september 2026 gaf: met straat,
            // met een gehucht ervoor, met en zonder postcode, en enkel de stad.
            const string plaatsen = """
                <html><body>
                <article class="card"><div class="card-body"><div>
                  <h2 class="card-title"><a href="/nl/H/kavel/1/a"><strong>Een</strong></a></h2>
                  <p> <span class="SVGMapMarker"></span> Rijksweg 2, 9681 Maarkedal, België</p>
                </div></div></article>
                <article class="card"><div class="card-body"><div>
                  <h2 class="card-title"><a href="/nl/H/kavel/2/b"><strong>Twee</strong></a></h2>
                  <p> <span class="SVGMapMarker"></span> Huildonck, Waaslandlaan 14, 9160 Lokeren, België</p>
                </div></div></article>
                <article class="card"><div class="card-body"><div>
                  <h2 class="card-title"><a href="/nl/H/kavel/3/c"><strong>Drie</strong></a></h2>
                  <p> <span class="SVGMapMarker"></span> 5580 Rochefort, België</p>
                </div></div></article>
                <article class="card"><div class="card-body"><div>
                  <h2 class="card-title"><a href="/nl/H/kavel/4/d"><strong>Vier</strong></a></h2>
                  <p> <span class="SVGMapMarker"></span> Brugge, België</p>
                </div></div></article>
                <article class="card"><div class="card-body"><div>
                  <h2 class="card-title"><a href="/nl/H/kavel/5/e"><strong>Vijf</strong></a></h2>
                  <p> <span class="SVGMapMarker"></span> Lichtaart, 2460 Kasterlee, België</p>
                </div></div></article>
                </body></html>
                """;

            const string stad = @"(?:^|,)\s*(?:\d{4,6}\s+)?([^,]+?)\s*,\s*[^,]+$";

            SiteDefinition Veiling(string plaats) => new()
            {
                Name = "AlleVeilingen",
                BaseUrl = "https://alleveilingen.be",
                ItemSelector = "article.card",
                TitleSelector = "h2.card-title a strong",
                UrlSelector = "h2.card-title a@href",
                LocationSelector = plaats
            };

            var met = await new GenericSource(Veiling("div.card-body > div > p::match(" + stad + ")")).ReadPageAsync(plaatsen);
            var zonder = await new GenericSource(Veiling("div.card-body > div > p")).ReadPageAsync(plaatsen);

            Check.Dat(met.Select(l => l.Location).SequenceEqual(new[]
                { "Maarkedal", "Lokeren", "Rochefort", "Brugge", "Kasterlee" }),
                $"straat, gehucht, postcode en land vallen weg ({string.Join(" | ", met.Select(l => l.Location))})");
            Check.Dat(zonder[0].Location == "Rijksweg 2, 9681 Maarkedal, België",
                "zonder patroon blijft alles staan, zoals bij de sites die enkel de stad geven");

            // eBay zet het land in dezelfde soort regel als de verzendkosten; enkel de
            // laatste, en op een advertentiekaart staat ze er niet.
            const string ebay = """
                <html><body>
                <li class="s-card"><a class="s-card__link" href="https://ebay.be/itm/1"><span class="s-card__title">Een</span></a>
                  <div class="su-card-container__attributes__primary">
                    <div class="s-card__attribute-row"><span>EUR 60,00</span></div>
                    <div class="s-card__attribute-row"><span>+EUR 15,00 verzendkosten</span></div>
                    <div class="s-card__attribute-row"><span>van Nederland</span></div>
                  </div></li>
                <li class="s-card"><a class="s-card__link" href="https://ebay.be/itm/2"><span class="s-card__title">Twee</span></a>
                  <div class="su-card-container__attributes__primary">
                    <div class="s-card__attribute-row"><span>EUR 9,99</span></div>
                    <div class="s-card__attribute-row"><span>+EUR 4,00 verzendkosten</span></div>
                  </div></li>
                </body></html>
                """;

            var kaarten = await new GenericSource(new SiteDefinition
            {
                Name = "eBay",
                BaseUrl = "https://benl.ebay.be",
                ItemSelector = "li.s-card",
                TitleSelector = ".s-card__title",
                UrlSelector = "a.s-card__link@href",
                LocationSelector = ".su-card-container__attributes__primary .s-card__attribute-row:last-child" +
                                   @"::match(^(?:van|uit)\s+(.+)$)"
            }).ReadPageAsync(ebay);

            Check.Dat(kaarten.Count == 2 && kaarten[0].Location == "Nederland",
                $"het land zonder 'van' ervoor ('{kaarten.FirstOrDefault()?.Location}')");
            Check.Dat(kaarten.Count == 2 && kaarten[1].Location == "",
                $"een kaart zonder land geeft leeg, niet de verzendkosten ('{kaarten.ElementAtOrDefault(1)?.Location}')");

            // Dezelfde regel bij de drie andere sites die meer dan een stad gaven. De teksten
            // zijn gemeten op 17 september 2026; bij leboncoin stond de plaatsselector op het
            // eerste p.text-caption, en dat is de CATEGORIE - vandaar de buren in de selector.
            const string andere = """
                <html><body>
                <article class="k"><h3><a href="https://k.de/1">Een</a></h3>
                  <div class="text-onSurfaceNonessential"><span>72461 Albstadt</span></div></article>
                <article class="a"><h3><a href="https://a.be/1">Twee</a></h3>
                  <span data-testid="dealer-address">BE-1160 Auderghem</span></article>
                <article class="l"><h3><a href="https://l.fr/1">Drie</a></h3>
                  <div><p class="text-caption text-neutral">Photo, audio &amp; vidéo</p><p class="sr-only">Catégorie : Photo, audio &amp; vidéo.</p><p class="text-caption text-neutral">Argenteuil 95100 Centre-ville</p><p class="sr-only">Située à Argenteuil 95100 Centre-ville.</p></div></article>
                </body></html>
                """;

            async Task<string> Plaats(string kaart, string plaats)
            {
                var lijst = await new GenericSource(new SiteDefinition
                {
                    Name = "Proef",
                    ItemSelector = "article." + kaart,
                    TitleSelector = "h3",
                    UrlSelector = "h3 a@href",
                    LocationSelector = plaats
                }).ReadPageAsync(andere);

                return lijst.Single().Location;
            }

            Check.Dat(await Plaats("k", @"div.text-onSurfaceNonessential span::match(^(?:\d{4,6}\s+)?(.+)$)") == "Albstadt",
                "Kleinanzeigen: de postcode voor de stad valt weg");
            Check.Dat(await Plaats("a", @"[data-testid='dealer-address']::match(^(?:[A-Z]{2}-)?(?:\d{4,6}\s+)?(.+)$)") == "Auderghem",
                "AutoScout24: het landcodeje en de postcode vallen weg");
            Check.Dat(await Plaats("l", @"p.text-caption.text-neutral + p.sr-only + p.text-caption.text-neutral::match(^(.+?)(?:\s+\d{4,5}\b.*)?$)") == "Argenteuil",
                "leboncoin: de stad in plaats van de categorie, zonder postcode en wijk");

            // De randgevallen van de regel zelf.
            const string los = """
                <html><body>
                <div class="r"><a href="https://site.be/1">Titel</a><p class="p">Verzonden vanuit Duitsland</p></div>
                </body></html>
                """;

            SiteDefinition Los(string plaats) => new()
            {
                Name = "Proef",
                ItemSelector = "div.r",
                TitleSelector = "a",
                UrlSelector = "a@href",
                LocationSelector = plaats
            };

            // Twee elementen die op dezelfde selector passen: met een patroon wordt het
            // eerste genomen waar het patroon ook echt op past. Zo staat het op een
            // kavelpagina van AlleVeilingen, waar div[title='Einddatum'] twee keer voorkomt
            // en de eerste het kavelnummer bevat.
            const string tweemaal = """
                <html><body>
                <div class="r"><a href="https://site.be/1">Titel</a>
                  <div title="Einddatum"><span>Einddatum</span>Kavel nummer: 35266</div>
                  <div title="Einddatum"><span>Einddatum</span>Einde op 29/09/2026 19:00</div>
                </div></body></html>
                """;

            var tweede = await new GenericSource(new SiteDefinition
            {
                Name = "Proef",
                ItemSelector = "div.r",
                TitleSelector = "a",
                UrlSelector = "a@href",
                LocationSelector = @"div[title='Einddatum']::match(Einde op\s+([\d/]+\s+[\d:]+))"
            }).ReadPageAsync(tweemaal);

            var zonderPatroon = await new GenericSource(new SiteDefinition
            {
                Name = "Proef",
                ItemSelector = "div.r",
                TitleSelector = "a",
                UrlSelector = "a@href",
                LocationSelector = "div[title='Einddatum']"
            }).ReadPageAsync(tweemaal);

            Check.Dat(tweede.Single().Location == "29/09/2026 19:00",
                $"met een patroon: het eerste element waar het patroon op past ('{tweede.Single().Location}')");
            Check.Dat(zonderPatroon.Single().Location.Contains("Kavel nummer"),
                "zonder patroon: gewoon het eerste element, zoals in CSS");

            var heel = await new GenericSource(Los(@"p.p::match(Duits\w+)")).ReadPageAsync(los);
            var kapot = await new GenericSource(Los(@"p.p::match(Duits(\w+)")).ReadPageAsync(los);
            var samen = await new GenericSource(Los(@"p.p::replace(Verzonden vanuit ,)::match(^(\w+)$)")).ReadPageAsync(los);

            Check.Dat(heel.Single().Location == "Duitsland", "een patroon zonder groep: de hele treffer");
            Check.Dat(kapot.Single().Location == "Verzonden vanuit Duitsland",
                "een ongeldig patroon laat de waarde staan in plaats van de zoekopdracht te laten vallen");
            Check.Dat(samen.Single().Location == "Duitsland", "::replace en ::match samen, in die volgorde");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Veiling: hoelang er nog geboden kan worden, achter de plaats");
        {
            // Zo staat het op de kaart van Catawiki (een aftelklok zonder datetime) en van
            // eBay (enkel bij een veilingkavel, naast het aantal biedingen en het tijdstip).
            const string html = """
                <html><body>
                <article class="cw"><p class="t"><a href="https://cw/l/1">Kavel</a></p>
                  <div class="c-lot-card__timer"><time>Nog 3 dagen</time></div></article>
                <li class="s-card"><a class="s-card__link" href="https://ebay.be/itm/1"><span class="s-card__title">Veiling</span></a>
                  <div class="su-card-container__attributes__primary">
                    <div class="s-card__attribute-row"><span>EUR 60,00</span></div>
                    <div class="s-card__attribute-row"><span>0 biedingen</span><span> · </span><span class="s-card__time"><span class="clipped">Resterende tijd</span><span class="s-card__time-left">Nog 9d 12u</span> <span class="s-card__time-end">(27/09, 11:24)</span></span></div>
                    <div class="s-card__attribute-row"><span>van Nederland</span></div>
                  </div></li>
                <li class="s-card"><a class="s-card__link" href="https://ebay.be/itm/2"><span class="s-card__title">Nu kopen</span></a>
                  <div class="su-card-container__attributes__primary">
                    <div class="s-card__attribute-row"><span>EUR 25,00</span></div>
                    <div class="s-card__attribute-row"><span>van België</span></div>
                  </div></li>
                </body></html>
                """;

            var catawiki = (await new GenericSource(new SiteDefinition
            {
                Name = "Catawiki",
                ItemSelector = "article.cw",
                TitleSelector = "p.t a",
                UrlSelector = "p.t a@href",
                TimeLeftSelector = "time"
            }).ReadPageAsync(html)).Single();

            var ebay = await new GenericSource(new SiteDefinition
            {
                Name = "eBay",
                ItemSelector = "li.s-card",
                TitleSelector = ".s-card__title",
                UrlSelector = "a.s-card__link@href",
                TimeLeftSelector = ".s-card__time-left",
                LocationSelector = ".su-card-container__attributes__primary .s-card__attribute-row:last-child" +
                                   @"::match(^(?:van|uit)\s+(.+)$)"
            }).ReadPageAsync(html);

            Check.Dat(catawiki.TimeLeft == "Nog 3 dagen" && catawiki.Location == "",
                $"Catawiki: enkel een aftelklok, geen plaats ('{catawiki.TimeLeft}')");
            Check.Dat(catawiki.PlaceLine == "Nog 3 dagen",
                $"zonder plaats blijft de tijd alleen over, zonder los scheidingsteken ('{catawiki.PlaceLine}')");
            Check.Dat(ebay[0].PlaceLine == "Nederland · Nog 9d 12u",
                $"eBay: eerst de plaats, dan de tijd ('{ebay[0].PlaceLine}')");
            Check.Dat(ebay[1].TimeLeft == "" && ebay[1].PlaceLine == "België",
                $"Nu kopen is geen veiling: enkel de plaats ('{ebay[1].PlaceLine}')");

            // Bij Catawiki komt de klok, net als de prijs, pas met JavaScript in de pagina.
            var vroeg = new Listing { Source = "Catawiki", ExternalId = "1", Title = "Kavel" };
            var laat = new Listing { Source = "Catawiki", ExternalId = "1", Title = "Kavel", TimeLeft = "Nog 2 dagen" };

            Check.Dat(vroeg.MergeFrom(laat) && vroeg.PlaceLine == "Nog 2 dagen",
                "een latere levering van de brug vult de aftelklok aan");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Einddatum: lezen en tonen");
        {
            var nu = new DateTime(2026, 9, 18, 12, 0, 0);

            Check.Dat(DetailFetcher.LeesDatum("29/09/2026 19:00") == new DateTime(2026, 9, 29, 19, 0, 0),
                "dag eerst, zoals AlleVeilingen het schrijft - ook op een Engelstalige Windows");
            Check.Dat(DetailFetcher.LeesDatum("2026-09-29T19:00:00") == new DateTime(2026, 9, 29, 19, 0, 0),
                "een ISO-tijdstip werkt ook");
            Check.Dat(DetailFetcher.LeesDatum("binnenkort") is null && DetailFetcher.LeesDatum("") is null,
                "wat geen datum is, geeft niets");

            Check.Dat(Listing.TijdTot(nu.AddDays(11).AddHours(7), nu) == "Nog 11 dagen" &&
                      Listing.TijdTot(nu.AddDays(1).AddHours(3), nu) == "Nog 1 dag",
                "dagen");
            Check.Dat(Listing.TijdTot(nu.AddHours(5), nu) == "Nog 5 uur" &&
                      Listing.TijdTot(nu.AddHours(1), nu) == "Nog 1 uur",
                "uren");
            Check.Dat(Listing.TijdTot(nu.AddMinutes(20), nu) == "Nog 20 min" &&
                      Listing.TijdTot(nu.AddSeconds(30), nu) == "Nog 1 min",
                "minuten, en de laatste seconden blijven 'Nog 1 min'");
            Check.Dat(Listing.TijdTot(nu.AddMinutes(-1), nu) == "Afgelopen" && Listing.TijdTot(null, nu) == "",
                "voorbij, of onbekend");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Volgorde: de veiling die het eerst afloopt");
        {
            var nu = new DateTime(2026, 9, 18, 12, 0, 0);

            // De vormen zoals Catawiki en eBay ze op 17 en 18 september 2026 schreven.
            Check.Dat(Listing.SchatEinde("Nog 3 dagen", nu) == nu.AddDays(3) &&
                      Listing.SchatEinde("Nog 1 dag", nu) == nu.AddDays(1) &&
                      Listing.SchatEinde("Nog 21 uur", nu) == nu.AddHours(21),
                "Catawiki: dagen, een dag, uren");
            Check.Dat(Listing.SchatEinde("Nog 9d 12u", nu) == nu.AddDays(9).AddHours(12) &&
                      Listing.SchatEinde("Nog 45 min", nu) == nu.AddMinutes(45) &&
                      Listing.SchatEinde("Nog 2u 10m", nu) == nu.AddHours(2).AddMinutes(10),
                "eBay: dagen en uren samen, minuten");
            Check.Dat(Listing.SchatEinde("01:23:45", nu) == nu + new TimeSpan(1, 23, 45),
                "een aftelklok met seconden");
            Check.Dat(Listing.SchatEinde("Afgelopen", nu) is null && Listing.SchatEinde("Om 19:30", nu) is null &&
                      Listing.SchatEinde("", nu) is null,
                "geen resterende tijd, of een klokuur: niets");

            var eerder = new Listing { Source = "S", ExternalId = "1", EndsAt = DateTime.Now.AddHours(5) };
            eerder.TimeLeft = "Nog 3 dagen";
            Check.Dat(eerder.EndsAtOrEstimate == eerder.EndsAt,
                "een echte datum wint van een schatting uit de tekst");

            Listing Zoekertje(string id, string tijd = "", DateTime? einde = null) =>
                new() { Source = "S", ExternalId = id, Title = id, TimeLeft = tijd, EndsAt = einde };

            var lijst = new List<Listing>
            {
                Zoekertje("gewoon"),
                Zoekertje("drie-dagen", "Nog 3 dagen"),
                Zoekertje("voorbij", einde: DateTime.Now.AddHours(-1)),
                Zoekertje("kavelpagina", einde: DateTime.Now.AddHours(5)),
                Zoekertje("twee-uur", "Nog 2 uur"),
                Zoekertje("ebay", "Nog 1d 3u")
            };

            var comparer = new ListingComparer(ListingSort.EndingSoonest);
            lijst.Sort((a, b) => comparer.Compare(a, b));
            var volgorde = string.Join(" ", lijst.Select(l => l.ExternalId));

            Check.Dat(volgorde.StartsWith("twee-uur kavelpagina ebay drie-dagen"),
                $"eerst wat het eerst afloopt, over de sites heen ({volgorde})");
            Check.Dat(volgorde.EndsWith("gewoon voorbij"),
                "een gewoon zoekertje en een veiling die al voorbij is: achteraan");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Timer rechtsonder: wat hij toont en wanneer hij aftelt");
        {
            var nu = new DateTime(2026, 9, 18, 12, 0, 0);

            Check.Dat(Listing.TimerTekst(nu.AddDays(3).AddHours(4).AddMinutes(10), "", nu) == "3d 04u" &&
                      Listing.TimerTekst(nu.AddHours(4).AddMinutes(12), "", nu) == "4u 12m" &&
                      Listing.TimerTekst(nu.AddMinutes(12).AddSeconds(34), "", nu) == "12m 34s",
                "dagen en uren, uren en minuten, en in het laatste uur per seconde");
            Check.Dat(Listing.TimerTekst(nu.AddSeconds(-1), "", nu) == "Afgelopen", "voorbij");
            Check.Dat(Listing.TimerTekst(null, "Nog 3 dagen", nu) == "3 dagen" && Listing.TimerTekst(null, "", nu) == "",
                "zonder exact tijdstip de tekst van de site, zonder 'Nog'; een gewoon zoekertje niets");
            Check.Dat(Listing.IsDringend(nu.AddMinutes(59), nu) && !Listing.IsDringend(nu.AddMinutes(61), nu) &&
                      !Listing.IsDringend(nu.AddMinutes(-1), nu),
                "dringend: enkel het laatste uur, en niet meer als het voorbij is");

            // Aftellen vanaf "Nog 3 dagen" zou een precisie tonen die er niet is. eBay schrijft
            // zijn klok fijner naarmate het einde nadert ("Nog 6s", gemeten 18 september 2026).
            Check.Dat(new Listing { TimeLeft = "Nog 3 dagen" }.TimerEinde is null &&
                      new Listing { TimeLeft = "Nog 9d 12u" }.TimerEinde is null,
                "een schatting op de dag of het uur: de timer telt niet af");
            Check.Dat(new Listing { TimeLeft = "Nog 6s" }.TimerEinde is not null &&
                      new Listing { TimeLeft = "Nog 3u 20m" }.TimerEinde is not null,
                "een schatting op de minuut of de seconde: wel");

            var exact = new Listing { TimeLeft = "Nog 3 dagen", EndsAt = DateTime.Now.AddDays(3) };
            Check.Dat(exact.TimerEinde == exact.EndsAt, "een exact tijdstip wint altijd");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Exacte sluiting via de API van een site (EndTimeApi)");
        {
            DetailFetcher.Vergeet();

            var gevraagd = new List<string>();
            using var site = new Proefsite
            {
                Antwoord = adres =>
                {
                    lock (gevraagd) gevraagd.Add(adres);

                    // Zoals Catawiki antwoordt: het id als getal, het tijdstip in UTC.
                    var ids = Regex.Match(adres, @"ids=([^&]*)").Groups[1].Value.Split(',');
                    var lots = ids.Select(id => $"{{\"id\":{id},\"bidding_end_time\":\"2026-09-21T18:45:00Z\",\"closed\":false}}");
                    return "{\"lots\":[" + string.Join(",", lots) + "],\"meta\":{}}";
                }
            };

            var veiling = site.Site("Proefveiling");   // rechtstreeks; Catawiki zelf loopt via de brug
            veiling.EndTimeApi = new EndTimeApiOptions
            {
                UrlTemplate = $"http://127.0.0.1:{site.Poort}/api?ids={{ids}}",
                BatchSize = 24, ListPath = "lots", IdPath = "id", EndPath = "bidding_end_time"
            };

            var kavels = Enumerable.Range(1, 30)
                .Select(i => new Listing { Source = "Proefveiling", ExternalId = (1000 + i).ToString(),
                                           Title = "Kavel " + i, TimeLeft = "Nog 3 dagen" })
                .ToList();
            var gewoon = new Listing { Source = "Gewone site", ExternalId = "9", Title = "Gewoon" };

            var aangevuld = await DetailFetcher.FillFromApiAsync(kavels.Append(gewoon), new[] { veiling });
            var verwacht = new DateTime(2026, 9, 21, 18, 45, 0, DateTimeKind.Utc).ToLocalTime();

            Check.Dat(aangevuld == 30 && kavels.All(k => k.EndsAt == verwacht),
                $"alle 30 kavels het exacte tijdstip, in onze tijd ({aangevuld}, {kavels[0].EndsAt:dd/MM HH:mm})");
            Check.Dat(gevraagd.Count == 2, $"30 kavels in reeksen van 24: twee verzoeken ({gevraagd.Count})");
            Check.Dat(gewoon.EndsAt is null, "een site zonder API: niets");
            Check.Dat(kavels[0].TimerEinde == verwacht,
                "de timer telt nu echt af, ook al zegt de site enkel 'Nog 3 dagen'");

            var opnieuw = kavels.Select(k => new Listing { Source = k.Source, ExternalId = k.ExternalId }).ToList();
            var tweede = await DetailFetcher.FillFromApiAsync(opnieuw, new[] { veiling });
            Check.Dat(gevraagd.Count == 2 && tweede == 30, "een tweede keer: uit het geheugen, zonder verzoek");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Einddatum: de kavelpagina ophalen (DetailFetcher)");
        {
            DetailFetcher.Vergeet();

            var gevraagd = new List<string>();
            using var site = new Proefsite
            {
                Antwoord = adres =>
                {
                    lock (gevraagd) gevraagd.Add(adres);

                    // Zo staat het op een kavelpagina van AlleVeilingen.
                    return "<html><body><div class='ml-1 mb-2' title='Einddatum'>" +
                           "<div class='tooltip'><span>Einddatum</span></div>" +
                           "<div><span class='svgcardicon SVGCalendar'></span>Einde op 29/09/2026 19:00</div>" +
                           "</div></body></html>";
                }
            };

            var veiling = site.Site("Proefveiling");
            veiling.DetailEndDateSelector = @"div[title='Einddatum']::match(Einde op\s+([\d/]+\s+[\d:]+))";
            var gewoon = site.Site("Gewone site");

            Listing Kavel(string bron, string id, string tijd = "") => new()
            {
                Source = bron, ExternalId = id, Title = "Kavel " + id, Location = "Hooglede",
                TimeLeft = tijd, Url = $"http://127.0.0.1:{site.Poort}/kavel/{id}"
            };

            var kavel = Kavel("Proefveiling", "1");
            var andereSite = Kavel("Gewone site", "2");
            var heeftAlTijd = Kavel("Proefveiling", "3", "Nog 3 dagen");
            var raarAdres = new Listing { Source = "Proefveiling", ExternalId = "4", Url = "file:///C:/Windows/notepad.exe" };

            await DetailFetcher.FillAsync(new[] { kavel, andereSite, heeftAlTijd, raarAdres },
                                          new[] { veiling, gewoon });

            Check.Dat(kavel.EndsAt == new DateTime(2026, 9, 29, 19, 0, 0),
                $"de datum van de kavelpagina staat bij het zoekertje ({kavel.EndsAt})");
            Check.Dat(kavel.PlaceLine == "Hooglede \u00b7 " + Listing.TijdTot(kavel.EndsAt, DateTime.Now),
                $"en komt achter de plaats te staan ('{kavel.PlaceLine}')");
            Check.Dat(gevraagd.Count == 1,
                $"enkel het kavel van de site mét selector werd opgehaald ({gevraagd.Count} verzoek(en))");
            Check.Dat(andereSite.EndsAt is null && heeftAlTijd.EndsAt is null && raarAdres.EndsAt is null,
                "een site zonder selector, een zoekertje dat zijn tijd al toont en een link die geen webadres is: overgeslagen");

            // Hetzelfde kavel opnieuw tonen (bladeren) mag geen tweede verzoek geven.
            var opnieuw = Kavel("Proefveiling", "1");
            await DetailFetcher.FillAsync(new[] { opnieuw }, new[] { veiling });

            Check.Dat(gevraagd.Count == 1 && opnieuw.EndsAt == kavel.EndsAt,
                $"wat al opgehaald is, blijft onthouden ({gevraagd.Count} verzoek(en) in totaal)");
        }

    }
}

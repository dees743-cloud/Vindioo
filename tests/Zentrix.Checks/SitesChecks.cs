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
    }
}

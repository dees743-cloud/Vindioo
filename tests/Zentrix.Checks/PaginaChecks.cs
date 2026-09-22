using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// Hoeveel pagina's er gevraagd worden (de gewone weg, zonder brug), en in welke volgorde
/// de resultaten gemeld worden.
/// </summary>
public static class PaginaChecks
{
    public static async Task RunAsync()
    {
        Check.Groep("Pagina's: stoppen bij de laatste (lokale proefsite)");

        // Met offset: pagineren zoals de API van 2dehands, "vanaf het hoeveelste zoekertje".
        async Task<(int Resultaten, List<int> Paginas, List<List<Listing>> Meldingen)> Scenario(
            Dictionary<int, int> perPagina, bool metOffset = false)
        {
            using var site = new Proefsite();
            site.PerPagina.Clear();
            foreach (var (pagina, aantal) in perPagina) site.PerPagina[pagina] = aantal;

            var meldingen = new List<List<Listing>>();
            var progress = new SynchroneMelder<List<Listing>>(l => { lock (meldingen) meldingen.Add(l); });

            var def = site.Site("Proefsite", paginering: !metOffset);
            if (metOffset)
            {
                def.SearchUrlTemplate += "&limit=30&offset={offset}";
                def.PageSize = 30;
            }

            // Zoveel als de app zelf vraagt: de rem van dit soort site (zie ResultLimit).
            var gevonden = await new GenericSource(def).SearchAsync("cd", def.ResultLimit(), null, progress);

            lock (site.Gevraagd) return (gevonden.Count, site.Gevraagd.OrderBy(p => p).ToList(), meldingen);
        }

        string Lijst(List<int> p) => string.Join(",", p);

        var s1 = await Scenario(new() { [1] = 30, [2] = 29, [3] = 10 });
        Check.Dat(Lijst(s1.Paginas) == "1,2,3" && s1.Resultaten == 69, $"30, 29, 10: stopt na pagina 3 ({Lijst(s1.Paginas)})");

        var s2 = await Scenario(new() { [1] = 30, [2] = 30 });
        Check.Dat(Lijst(s2.Paginas) == "1,2,3" && s2.Resultaten == 60, $"30, 30, leeg: stopt op de lege pagina ({Lijst(s2.Paginas)})");

        var s3 = await Scenario(new() { [1] = 30, [2] = 12 });
        Check.Dat(Lijst(s3.Paginas) == "1,2" && s3.Resultaten == 42, $"30, 12: een pagina onder de helft is de laatste ({Lijst(s3.Paginas)})");

        // Een site die rechtstreeks antwoordt, mag 20 pagina's (tot 22 september 2026 overal 10).
        var s4 = await Scenario(Enumerable.Range(1, 25).ToDictionary(p => p, _ => 30));
        Check.Dat(s4.Paginas.Count == 20 && s4.Resultaten == 600,
            $"altijd vol: tot de rem van twintig pagina's ({s4.Paginas.Count}, {s4.Resultaten} resultaten)");

        var s7 = await Scenario(new() { [1] = 30, [2] = 30, [3] = 10 }, metOffset: true);
        Check.Dat(Lijst(s7.Paginas) == "1,2,3" && s7.Resultaten == 70,
            $"met {{offset}}: vanaf 0, 30 en 60, en dan gestopt ({Lijst(s7.Paginas)}, {s7.Resultaten} resultaten)");

        var s5 = await Scenario(new() { [1] = 2, [2] = 0 });
        Check.Dat(Lijst(s5.Paginas) == "1" && s5.Resultaten == 2, $"2 op pagina 1: geen pagina 2 meer ({Lijst(s5.Paginas)})");

        var s6 = await Scenario(new() { [1] = 10, [2] = 3 });
        Check.Dat(Lijst(s6.Paginas) == "1,2", $"precies {GenericSource.MinimumVoorVervolgpagina} op pagina 1: pagina 2 wordt nog gevraagd ({Lijst(s6.Paginas)})");

        Check.Groep("Pagina's: elke pagina wordt meteen gemeld, pagina 1 eerst");
        Check.Dat(s1.Meldingen.Count == 3 && s1.Meldingen[0].Count == 30 && s1.Meldingen[0][0].Title == "Zoekertje 1-0",
            $"30, 29, 10: drie meldingen, de eerste is pagina 1 ({string.Join(",", s1.Meldingen.Select(m => m.Count))})");
        Check.Dat(s5.Meldingen.Count == 1 && s5.Meldingen[0].Count == 2, "ook een enkele pagina wordt gemeld");

        Check.Groep("Hoeveel een site mag leveren (ResultLimit, PageLimit)");
        {
            var direct = new SiteDefinition { Name = "Direct" };
            var browser = new SiteDefinition { Name = "Browser", NeedsBrowser = true };
            var brug = new SiteDefinition { Name = "Brug", UseBridge = true };
            var link = new SiteDefinition { Name = "Link", Engine = SiteEngine.LinkText, NeedsBrowser = true };

            Check.Dat(direct.ResultLimit() == 2000 && direct.PageLimit() == 20, "rechtstreeks: 2000, in hoogstens 20 pagina's");
            Check.Dat(browser.ResultLimit() == 500 && browser.PageLimit() == 10, "via de browser: 500, 10 pagina's");
            Check.Dat(brug.ResultLimit() == 500 && brug.PageLimit() == 10, "via de brug: 500, 10 pagina's");
            Check.Dat(link.ResultLimit() == 300, "de linkmotor (Facebook, scrollen): 300");
            Check.Dat(!new SiteDefinition { Engine = SiteEngine.LinkText }.AnswersDirectly(),
                "de linkmotor telt nooit als rechtstreeks, ook zonder vinkje: ze loopt altijd via de browser");
        }

        Check.Groep("{offset}: pagineren vanaf het hoeveelste zoekertje");
        {
            var def = new SiteDefinition
            {
                SearchUrlTemplate = "https://x/api?query={query}&limit=100&offset={offset}",
                PageSize = 100
            };

            Check.Dat(SearchUrlBuilder.Build(def, "cd", null, 1).EndsWith("&offset=0") &&
                      SearchUrlBuilder.Build(def, "cd", null, 3).EndsWith("&offset=200"),
                $"pagina 1 vanaf 0, pagina 3 vanaf 200 ({SearchUrlBuilder.Build(def, "cd", null, 3)})");
            Check.Dat(SearchUrlBuilder.SupportsPaging(def), "telt als paginering");

            def.PageSize = 0;
            Check.Dat(!SearchUrlBuilder.SupportsPaging(def) && SearchUrlBuilder.Build(def, "cd", null, 2).EndsWith("&offset=0"),
                "zonder PageSize geen vervolgpagina: anders vraagt de app tien keer dezelfde pagina");

            var metSjabloon = new SiteDefinition { SearchUrlTemplate = "https://x/api?q={query}", PageTemplate = "&offset={offset}", PageSize = 24 };
            Check.Dat(SearchUrlBuilder.Build(metSjabloon, "cd", null, 1) == "https://x/api?q=cd" &&
                      SearchUrlBuilder.Build(metSjabloon, "cd", null, 2).EndsWith("&offset=24"),
                "ook in PageTemplate: pagina 1 zonder, pagina 2 vanaf 24");
        }
    }
}

/// <summary>
/// Een IProgress die meteen meldt, op dezelfde draad. De gewone Progress&lt;T&gt; meldt via de
/// threadpool, en dan is de volgorde van de meldingen niet te controleren.
/// </summary>
public sealed class SynchroneMelder<T>(Action<T> actie) : IProgress<T>
{
    public void Report(T value) => actie(value);
}

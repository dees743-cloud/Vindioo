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

        async Task<(int Resultaten, List<int> Paginas, List<List<Listing>> Meldingen)> Scenario(Dictionary<int, int> perPagina)
        {
            using var site = new Proefsite();
            site.PerPagina.Clear();
            foreach (var (pagina, aantal) in perPagina) site.PerPagina[pagina] = aantal;

            var meldingen = new List<List<Listing>>();
            var progress = new SynchroneMelder<List<Listing>>(l => { lock (meldingen) meldingen.Add(l); });

            var gevonden = await new GenericSource(site.Site("Proefsite", paginering: true)).SearchAsync("cd", 500, null, progress);

            lock (site.Gevraagd) return (gevonden.Count, site.Gevraagd.OrderBy(p => p).ToList(), meldingen);
        }

        string Lijst(List<int> p) => string.Join(",", p);

        var s1 = await Scenario(new() { [1] = 30, [2] = 29, [3] = 10 });
        Check.Dat(Lijst(s1.Paginas) == "1,2,3" && s1.Resultaten == 69, $"30, 29, 10: stopt na pagina 3 ({Lijst(s1.Paginas)})");

        var s2 = await Scenario(new() { [1] = 30, [2] = 30 });
        Check.Dat(Lijst(s2.Paginas) == "1,2,3" && s2.Resultaten == 60, $"30, 30, leeg: stopt op de lege pagina ({Lijst(s2.Paginas)})");

        var s3 = await Scenario(new() { [1] = 30, [2] = 12 });
        Check.Dat(Lijst(s3.Paginas) == "1,2" && s3.Resultaten == 42, $"30, 12: een pagina onder de helft is de laatste ({Lijst(s3.Paginas)})");

        var s4 = await Scenario(Enumerable.Range(1, 12).ToDictionary(p => p, _ => 30));
        Check.Dat(s4.Paginas.Count == 10 && s4.Resultaten == 300, $"altijd vol: tot de rem van tien ({s4.Paginas.Count})");

        var s5 = await Scenario(new() { [1] = 2, [2] = 0 });
        Check.Dat(Lijst(s5.Paginas) == "1" && s5.Resultaten == 2, $"2 op pagina 1: geen pagina 2 meer ({Lijst(s5.Paginas)})");

        var s6 = await Scenario(new() { [1] = 10, [2] = 3 });
        Check.Dat(Lijst(s6.Paginas) == "1,2", $"precies {GenericSource.MinimumVoorVervolgpagina} op pagina 1: pagina 2 wordt nog gevraagd ({Lijst(s6.Paginas)})");

        Check.Groep("Pagina's: elke pagina wordt meteen gemeld, pagina 1 eerst");
        Check.Dat(s1.Meldingen.Count == 3 && s1.Meldingen[0].Count == 30 && s1.Meldingen[0][0].Title == "Zoekertje 1-0",
            $"30, 29, 10: drie meldingen, de eerste is pagina 1 ({string.Join(",", s1.Meldingen.Select(m => m.Count))})");
        Check.Dat(s5.Meldingen.Count == 1 && s5.Meldingen[0].Count == 2, "ook een enkele pagina wordt gemeld");
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

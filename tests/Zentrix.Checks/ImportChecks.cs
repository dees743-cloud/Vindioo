using System.IO;
using System.Text.Json;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// Wat er van <b>buiten</b> binnenkomt: een gedeeld sitebestand.
///
/// Zo'n bestand is bedoeld om door te geven, en dat is precies waar het wringt. Wie er een
/// krijgt, voert hem uit met zijn eigen browser: de brug opent de zoek-URL in jouw Chrome, en
/// bij een API-opdracht doet ze daar een <c>fetch</c> met <c>credentials: "include"</c>. Een
/// verzonnen adres laat jouw aangemelde browser dus verzoeken doen naar eender welke site - of
/// naar de beheerpagina van je router, die vaak niet eens een aanmelding vraagt.
///
/// En de <c>Id</c> uit zo'n bestand ging rechtstreeks in een bestandspad.
///
/// Allebei gevonden in een codeanalyse van 30 september 2026.
/// </summary>
public static class ImportChecks
{
    private static string Schrijf(string naam, object site)
    {
        var map = Path.Combine(Path.GetTempPath(), "zentrix-import-proef");
        Directory.CreateDirectory(map);

        var pad = Path.Combine(map, naam);
        File.WriteAllText(pad, JsonSerializer.Serialize(site, new JsonSerializerOptions { WriteIndented = true }));
        return pad;
    }

    public static void Run()
    {
        var store = new SiteStore();
        store.Load();

        // ---------------------------------------------------------------------------
        Check.Groep("Importeren: waar een gedeeld sitebestand de app naartoe stuurt");
        {
            var goed = Schrijf("goed.json", new
            {
                Name = "Proefsite goed",
                BaseUrl = "https://www.voorbeeld.be",
                SearchUrlTemplate = "https://www.voorbeeld.be/q/{query}/",
                ItemSelector = "div.item"
            });

            Check.Dat(store.Import(goed, out _) is not null, "een gewone https-site komt gewoon binnen");

            // En de kern: wat er NIET in mag.
            var gevallen = new (string Naam, string Url, string Woord)[]
            {
                ("http.json",   "http://www.voorbeeld.be/q/{query}/",  "https"),
                ("router.json", "https://192.168.1.1/q/{query}/",      "eigen netwerk"),
                ("lokaal.json", "https://127.0.0.1:8731/q/{query}/",   "eigen netwerk"),
                ("naam.json",   "https://router/q/{query}/",           "eigen netwerk"),
                ("bestand.json","file:///C:/Windows/win.ini",          "https"),
                ("onzin.json",  "zomaar wat tekst",                    "webadres")
            };

            foreach (var (naam, url, woord) in gevallen)
            {
                var pad = Schrijf(naam, new
                {
                    Name = "Proefsite " + naam,
                    BaseUrl = "https://www.voorbeeld.be",
                    SearchUrlTemplate = url,
                    ItemSelector = "div.item"
                });

                var site = store.Import(pad, out var reden);

                Check.Dat(site is null && reden.Contains(woord, StringComparison.OrdinalIgnoreCase),
                    $"{url[..Math.Min(34, url.Length)]} wordt geweigerd ({(site is null ? reden : "KWAM BINNEN")})");
            }

            // Het basisadres en de einddatum-API tellen mee: anders stuur je de app alsnog
            // ergens heen via een veld dat niemand nakijkt.
            var achterdeur = Schrijf("achterdeur.json", new
            {
                Name = "Proefsite achterdeur",
                BaseUrl = "http://192.168.0.1",
                SearchUrlTemplate = "https://www.voorbeeld.be/q/{query}/",
                ItemSelector = "div.item"
            });

            Check.Dat(store.Import(achterdeur, out var waarom) is null,
                $"ook het basisadres wordt nagekeken, niet enkel de zoek-URL ({waarom})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Importeren: de Id bepaalt niet waar er geschreven wordt");
        {
            var map = Path.Combine(Path.GetTempPath(), "zentrix-import-proef");
            var buiten = Path.Combine(map, "ontsnapt.json");
            if (File.Exists(buiten)) File.Delete(buiten);

            // Een Id die uit de sitesmap probeert te breken. Path.Combine laat zoiets gewoon
            // door, en met een volledig pad negeert het de sitesmap zelfs helemaal.
            var pad = Schrijf("boos.json", new
            {
                Id = @"..\..\..\..\" + Path.GetFileName(map) + @"\ontsnapt",
                Name = "Proefsite boos",
                BaseUrl = "https://www.voorbeeld.be",
                SearchUrlTemplate = "https://www.voorbeeld.be/q/{query}/",
                ItemSelector = "div.item"
            });

            var site = store.Import(pad, out _);

            Check.Dat(site is not null && !site.Id.Contains("..") && !site.Id.Contains('\\') && !site.Id.Contains('/'),
                $"de Id is een slug geworden ('{site?.Id}')");

            Check.Dat(!File.Exists(buiten),
                "er is niets buiten de sitesmap geschreven");

            Check.Dat(site is not null && File.Exists(Path.Combine(AppPaths.SitesFolder, site.Id + ".json")),
                "en het bestand staat gewoon in de sitesmap");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Importeren: tonen waar een sitebestand heen gaat");
        {
            var def = new SiteDefinition
            {
                Name = "Proef",
                BaseUrl = "https://www.voorbeeld.be",
                SearchUrlTemplate = "https://zoek.voorbeeld.be/q/{query}/?pg={page}",
                EndTimeApi = new EndTimeApiOptions { UrlTemplate = "https://api.voorbeeld.be/lots?ids={ids}" }
            };

            var hosts = SiteUrlCheck.Hosts(def);

            Check.Dat(hosts.Count == 3 && hosts.Contains("zoek.voorbeeld.be") && hosts.Contains("api.voorbeeld.be"),
                $"alle hosts van een sitebestand, ontdubbeld: {string.Join(", ", hosts)}");

            // De plaatshouders mogen het nakijken niet in de weg zitten: {query} in het pad en
            // een heel JSON-blok in de querystring (de stijl van AlleVeilingen).
            Check.Dat(SiteUrlCheck.IsVeiligAdres(
                    "https://alleveilingen.be/nl/zoeken?fi={\"s\":\"{query}\",\"pg\":{page}}", out _),
                "een zoek-URL met plaatshouders en een JSON-blok blijft gewoon geldig");
        }
    }
}

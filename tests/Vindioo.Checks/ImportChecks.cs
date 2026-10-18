using System.IO;
using System.Text.Json;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo.Checks;

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
        var map = Path.Combine(Path.GetTempPath(), "vindioo-import-proef");
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
            var map = Path.Combine(Path.GetTempPath(), "vindioo-import-proef");
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
        Check.Groep("De AI-analyse: een pagina mag de app niet ergens anders heen sturen");
        {
            // De analyse stelt een sitebestand op uit een pagina die we NIET vertrouwen.
            // Verborgen tekst op zo'n pagina kan het model vragen een ander adres neer te
            // zetten, en baseUrl is daar het aantrekkelijkste veld voor: hij vult élke
            // relatieve link en foto aan. Bij een brugsite haalt jouw eigen Chrome die dan op,
            // met jouw cookies.
            const string zoek = "https://www.voorbeeld.be/zoeken?q={query}";
            var adres = new Uri("https://www.voorbeeld.be/zoeken?q=cd");

            Check.Dat(SiteAnalyzer.VeiligeBasis("https://www.voorbeeld.be", zoek, adres)
                      == "https://www.voorbeeld.be",
                "dezelfde site: overgenomen");

            Check.Dat(SiteAnalyzer.VeiligeBasis("https://aanvaller.be", zoek, adres)
                      == "https://www.voorbeeld.be",
                "een andere site: genegeerd, en teruggevallen op de zoek-URL");

            // Ook een subdomein is een andere host. Dat is met opzet streng: een relatief pad
            // hoort per definitie bij de site waar het staat, dus er gaat niets verloren.
            Check.Dat(SiteAnalyzer.VeiligeBasis("https://beelden.aanvaller.be", zoek, adres)
                      == "https://www.voorbeeld.be",
                "een subdomein van een vreemde site ook");

            // En een stille terugval naar onversleuteld telt evengoed als wegsturen.
            Check.Dat(SiteAnalyzer.VeiligeBasis("http://www.voorbeeld.be", zoek, adres)
                      == "https://www.voorbeeld.be",
                "dezelfde site maar via http: genegeerd");

            Check.Dat(SiteAnalyzer.VeiligeBasis("", zoek, adres) == "https://www.voorbeeld.be",
                "niets voorgesteld: de host van de zoek-URL");

            Check.Dat(SiteAnalyzer.VeiligeBasis("zomaar wat tekst", zoek, adres) == "https://www.voorbeeld.be",
                "geen geldig adres: ook teruggevallen");

            // Wie zelf een site op zijn eigen pc toevoegt, mag dat gewoon - het gaat erom dat
            // het ANTWOORD niet van de zoekpagina mag afwijken, niet om waar die staat.
            const string lokaal = "http://127.0.0.1:8080/zoeken?q={query}";
            var lokaalAdres = new Uri("http://127.0.0.1:8080/zoeken?q=cd");

            Check.Dat(SiteAnalyzer.VeiligeBasis("http://127.0.0.1:8080", lokaal, lokaalAdres)
                      == "http://127.0.0.1:8080",
                "een eigen testsite op deze pc werkt gewoon");
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

        // ---------------------------------------------------------------------------
        Check.Groep("Niet het sjabloon telt, maar de URL die er echt gebouwd wordt");
        {
            // Een adres mag een gebruikersnaam dragen: in "https://naam@host/" is alles voor
            // de @ de naam en is host de echte bestemming. Een sitebestand waarvan het
            // SJABLOON niets meer is dan "https://www.2dehands.be" komt door elke controle,
            // maar met een vast fragment dat met @ begint wijst de gebouwde URL ergens anders
            // heen. Nagemeten op 18 oktober 2026: de echte host werd 127.0.0.1, en dat is de
            // poort van de brug zelf.
            SiteDefinition Site(string? fragment = null, string? pageTemplate = null) => new()
            {
                Name = "Proef",
                SearchUrlTemplate = "https://www.2dehands.be",
                BaseUrl = "https://www.2dehands.be",
                PageTemplate = pageTemplate ?? "",
                ItemSelector = "div",
                TitleSelector = "a",
                UrlSelector = "a@href",
                CustomFilters = fragment is null
                    ? new List<CustomFilter>()
                    : new List<CustomFilter>
                      {
                          new() { Key = "x", Label = "Proef", Kind = CustomFilterKind.Toggle, Fragment = fragment }
                      }
            };

            Check.Dat(SiteUrlCheck.IsVeilig(Site(), out _),
                "een gewoon sitebestand komt er gewoon door");

            Check.Dat(!SiteUrlCheck.IsVeilig(Site(fragment: "@127.0.0.1:8731/geheim"), out var reden),
                $"een fragment met @ wordt geweigerd ('{reden}')");

            Check.Dat(reden.Contains("127.0.0.1"),
                "en de reden noemt de ECHTE bestemming, niet die uit het sjabloon");

            // Dezelfde truc past ook in PageTemplate, en dat stond in geen enkel rapport.
            Check.Dat(!SiteUrlCheck.IsVeilig(Site(pageTemplate: "@10.0.0.5/x"), out var reden2),
                $"en via PageTemplate net zo goed ('{reden2}')");

            // Wat NIET mag gebeuren: een werkend bestand weigeren. Bij AlleVeilingen zitten de
            // fragmenten in een JSON-blok en beginnen ze met een aanhalingsteken, dus de regel
            // "een fragment moet met ? of & beginnen" zou dat bestand kapotmaken.
            var jsonStijl = new SiteDefinition
            {
                Name = "JsonStijl",
                SearchUrlTemplate = "https://alleveilingen.be/nl/kavels?fi={\"s\":\"{query}\",\"pg\":{page}{filters}}",
                BaseUrl = "https://alleveilingen.be",
                ItemSelector = "article",
                TitleSelector = "h2",
                UrlSelector = "a@href",
                CustomFilters =
                {
                    new() { Key = "r", Label = "Provincie", Kind = CustomFilterKind.Choice,
                            Fragment = ",\"r\":[\"{value}\"]",
                            Options = { new FilterChoice { Value = "BEWV", Label = "West-Vlaanderen" } } }
                }
            };

            Check.Dat(SiteUrlCheck.IsVeilig(jsonStijl, out var reden3),
                $"een fragment in een JSON-blok blijft gewoon geldig ('{reden3}')");

            // En het basisadres moet bij de zoekhost horen. Dat adres maakt van een relatieve
            // link een volledig adres, en zo'n adres wordt daarna opgehaald - bij een brugsite
            // in jouw eigen Chrome, met jouw cookies. Allebei de adressen zijn apart https en
            // geen prive-adres, dus de gewone controle ziet daar niets van.
            var elders = Site();
            elders.BaseUrl = "https://www.marktplaats.nl";

            Check.Dat(!SiteUrlCheck.IsVeilig(elders, out var reden4),
                $"een basisadres op een andere host wordt geweigerd ('{reden4}')");

            Check.Dat(reden4.Contains("marktplaats") && reden4.Contains("2dehands"),
                "en de reden noemt allebei de hosts, zodat je ziet wat er wringt");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Wat de app najaagt, hoort op de site zelf te staan");
        {
            // De link van een zoekertje en de kandidaten die uit een zoekpagina gevist worden,
            // komen uit de INHOUD van een pagina. Die staat niet onder onze controle, en bij
            // een brugsite wordt zo'n adres in jouw eigen Chrome geopend, met jouw cookies.
            // Tot 18 oktober 2026 werd er enkel gekeken of het http of https was.
            var site = new SiteDefinition
            {
                Name = "Proef",
                SearchUrlTemplate = "https://www.2dehands.be/q/{query}/",
                BaseUrl = "https://www.2dehands.be",
                ItemSelector = "div", TitleSelector = "a", UrlSelector = "a@href"
            };

            Check.Dat(SiteUrlCheck.HoortBijSite("https://www.2dehands.be/v/123", site),
                "een link op de site zelf mag");

            Check.Dat(!SiteUrlCheck.HoortBijSite("https://kwaadaardig.be/v/123", site),
                "een link naar een andere host niet");

            Check.Dat(!SiteUrlCheck.HoortBijSite("https://127.0.0.1:8731/geheim", site),
                "en al helemaal niet naar iets op je eigen pc");

            Check.Dat(!SiteUrlCheck.HoortBijSite("file:///C:/Windows/win.ini", site),
                "en enkel http of https");

            Check.Dat(!SiteUrlCheck.HoortBijSite("", site) && !SiteUrlCheck.HoortBijSite("geen adres", site),
                "leeg of onzin telt niet als adres");

            // Een proefsite draait op 127.0.0.1, en die moet gewoon blijven werken: de regel is
            // DEZELFDE HOST, niet "geen prive-adres".
            var lokaal = new SiteDefinition
            {
                Name = "Proefsite",
                SearchUrlTemplate = "http://127.0.0.1:8080/s?q={query}",
                BaseUrl = "http://127.0.0.1:8080",
                ItemSelector = "div", TitleSelector = "a", UrlSelector = "a@href"
            };

            Check.Dat(SiteUrlCheck.HoortBijSite("http://127.0.0.1:8080/v/1", lokaal),
                "een lokale proefsite blijft zichzelf najagen");

            Check.Dat(!SiteUrlCheck.HoortBijSite("http://10.0.0.5/v/1", lokaal),
                "maar ook die mag niet naar de buren");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Voor het importeren is te zien waar een bestand de app naartoe stuurt");
        {
            // De hosts stonden tot 18 oktober 2026 alleen in het LOGBOEK, en daar kijkt niemand
            // voor hij op Importeren duwt. Nu haalt het scherm ze op zonder iets te importeren,
            // en zet ze in de vraag zelf.
            var map = Path.Combine(Path.GetTempPath(), "vindioo-hostproef", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(map, "sites"));

            File.WriteAllText(Path.Combine(map, "sites", "een.json"), """
                {
                  "Name": "Een",
                  "SearchUrlTemplate": "https://www.voorbeeld.be/zoek?q={query}",
                  "BaseUrl": "https://www.voorbeeld.be",
                  "ItemSelector": "div", "TitleSelector": "a", "UrlSelector": "a@href"
                }
                """);

            File.WriteAllText(Path.Combine(map, "sites", "twee.json"), """
                {
                  "Name": "Twee",
                  "SearchUrlTemplate": "https://api.anders.com/s?q={query}",
                  "BaseUrl": "https://api.anders.com",
                  "ItemSelector": "div", "TitleSelector": "a", "UrlSelector": "a@href"
                }
                """);

            File.WriteAllText(Path.Combine(map, "sites", "stuk.json"), "{ dit is geen json");

            var bestanden = SiteStore.Bestanden(map);

            Check.Dat(bestanden.Count == 3,
                $"de sites-map wordt gevonden vanuit de map erboven ({bestanden.Count} bestanden)");

            var hosts = SiteStore.HostsVan(bestanden);

            Check.Dat(hosts.Contains("www.voorbeeld.be") && hosts.Contains("api.anders.com"),
                $"en allebei de hosts komen eruit ({string.Join(", ", hosts)})");

            Check.Dat(hosts.Count == 2,
                "een onleesbaar bestand wordt stil overgeslagen - de import klaagt er zelf over");

            Check.Dat(SiteStore.HostsVan(Array.Empty<string>()).Count == 0,
                "en zonder bestanden valt er niets te tonen");

            Directory.Delete(map, recursive: true);
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Twee omwegen langs de controle op een adres bij jou thuis");
        {
            // Gemeten op 18 oktober 2026, allebei toen werkend.
            //
            // DE PUNT OP HET EIND. "localhost." en "127.0.0.1." zijn in DNS dezelfde adressen -
            // de punt zegt alleen dat de naam absoluut is - maar met die punt vindt Contains('.')
            // een punt, past geen enkel achtervoegsel meer, en lukt IPAddress.TryParse niet.
            Check.Dat(SiteUrlCheck.IsPriveAdres("127.0.0.1."), "127.0.0.1. is nog altijd je eigen pc");
            Check.Dat(SiteUrlCheck.IsPriveAdres("localhost."), "localhost. ook");
            Check.Dat(SiteUrlCheck.IsPriveAdres("10.0.0.5."), "en 10.0.0.5. je eigen netwerk");
            Check.Dat(SiteUrlCheck.IsPriveAdres("nas.local."), "net als nas.local.");

            // EEN IPv4-ADRES IN EEN IPv6-JASJE. ::ffff:10.0.0.5 is gewoon 10.0.0.5, maar de
            // IPv6-tak kende enkel link-local, site-local en unique-local. Loopback werd wél al
            // gevangen; het gat zat enkel bij de prive-reeksen.
            Check.Dat(SiteUrlCheck.IsPriveAdres("[::ffff:10.0.0.5]"), "::ffff:10.0.0.5 is 10.0.0.5");
            Check.Dat(SiteUrlCheck.IsPriveAdres("[::ffff:192.168.1.1]"), "en ::ffff:192.168.1.1 ook");
            Check.Dat(SiteUrlCheck.IsPriveAdres("[::ffff:127.0.0.1]"), "loopback werd al gevangen");

            // En een echte site blijft een echte site, punt of geen punt.
            Check.Dat(!SiteUrlCheck.IsPriveAdres("www.2dehands.be"), "een gewone host blijft toegelaten");
            Check.Dat(!SiteUrlCheck.IsPriveAdres("www.2dehands.be."), "ook met een punt op het eind");
        }
    }
}

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// De AI-controle op een foto (<see cref="PhotoAnalyzer"/>): het knippen, het samenvoegen en wat
/// er naar het model gaat. De grafische kaart zit hier niet in - een controle moet overal draaien
/// en in een seconde klaar zijn - dus Ollama is nagebootst (<see cref="NepOllama"/>). Wat het
/// model werkelijk van een foto máákt, is apart gemeten met echte foto's; zie CLAUDE.md.
/// </summary>
public static class FotoChecks
{
    public static async Task RunAsync()
    {
        using var ollama = new NepOllama();
        PhotoAnalyzer.UrlVoorControles = $"http://127.0.0.1:{ollama.Poort}";

        var groot = Foto(1600, 900);
        var klein = Foto(600, 400);
        var staand = Foto(900, 1600);

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: een hoop wordt in stukken gelezen");
        {
            // Zes stukken lezen, en daarna één keer vertellen. Op de hele foto gaf het model bij
            // het meten 14 namen en op zes stukken 57: de kleine labels zijn anders te klein.
            ollama.Namen = ["SuperDisk", "Galactix"];
            ollama.Beschrijving = "Een hoop diskettes.";

            var uit = await new PhotoAnalyzer().AnalyseerAsync(groot);

            Check.Dat(uit.Fout is null, $"het lukte ({uit.Fout})");
            Check.Dat(uit.Stukken == 6, $"de foto ging in 6 stukken ({uit.Stukken})");
            Check.Dat(ollama.Vragen.Count == 7, $"zes keer lezen en één keer vertellen ({ollama.Vragen.Count})");
            Check.Dat(uit.Beschrijving == "Een hoop diskettes.", $"de alinea komt door ('{uit.Beschrijving}')");

            // Elk stuk is een ander stuk van de foto: anders knipt hij niet en lees je zes keer
            // hetzelfde.
            var beelden = ollama.Vragen.Take(6).Select(v => v.Beeld).ToList();
            Check.Dat(beelden.Distinct().Count() == 6, "en elk stuk is een ánder stuk van de foto");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: knippen enkel wanneer het zin heeft");
        {
            ollama.Wis();
            var uit = await new PhotoAnalyzer().AnalyseerAsync(klein);

            Check.Dat(uit.Stukken == 1 && ollama.Vragen.Count == 2,
                $"een kleine foto gaat in één keer ({uit.Stukken} stuk, {ollama.Vragen.Count} vragen)");

            ollama.Wis();
            var snel = await new PhotoAnalyzer().AnalyseerAsync(groot, grondig: false);

            Check.Dat(snel.Stukken == 1 && ollama.Vragen.Count == 2,
                $"en zonder 'grondig' ook een grote ({snel.Stukken} stuk, {ollama.Vragen.Count} vragen)");

            ollama.Wis();
            var rechtop = await new PhotoAnalyzer().AnalyseerAsync(staand);

            Check.Dat(rechtop.Stukken == 6, $"een staande foto ook in 6 stukken ({rechtop.Stukken})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: wat er gelezen is, gaat mee naar de laatste vraag");
        {
            ollama.Wis();

            // De stukken overlappen, dus dezelfde naam komt meermaals terug - en de ene hoek van
            // een foto leest "SuperDisk" waar de andere "SUPERDISK" leest.
            ollama.Namen = ["SuperDisk", "SUPERDISK", " Galactix ", ""];
            ollama.Beschrijving = "Gelezen.";

            var uit = await new PhotoAnalyzer().AnalyseerAsync(groot);

            Check.Dat(uit.Gelezen.Count == 2, $"dezelfde naam telt één keer ({uit.Gelezen.Count})");
            Check.Dat(uit.Gelezen[0] == "SuperDisk" && uit.Gelezen[1] == "Galactix",
                $"met de eerste schrijfwijze, zonder spaties ({string.Join(", ", uit.Gelezen)})");

            var laatste = ollama.Vragen[^1];

            Check.Dat(laatste.Prompt.Contains("SuperDisk") && laatste.Prompt.Contains("Galactix"),
                "alles wat gelezen is, staat in de laatste vraag");
            Check.Dat(laatste.Prompt.Contains("NIETS over staat, kleur, ouderdom"),
                "met het verbod om er iets bij te verzinnen");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: wat het model moet meekrijgen");
        {
            ollama.Wis();
            await new PhotoAnalyzer().AnalyseerAsync(klein);

            var eerste = ollama.Vragen[0];

            Check.Dat(eerste.Body["think"]?.GetValue<bool>() == false,
                "nadenken staat uit: lezen is geen denkwerk");
            Check.Dat(eerste.Body["format"]?["properties"]?["namen"] is not null,
                "de antwoordvorm gaat mee - zonder die vorm liep het model vast in herhaling");
            Check.Dat(eerste.Body["options"]?["temperature"]?.GetValue<double>() == 0 &&
                      eerste.Body["options"]?["repeat_penalty"]?.GetValue<double>() > 1,
                "geen toeval, en herhalen wordt bestraft");
            Check.Dat(eerste.Body["keep_alive"]?.GetValue<string>() == "10m",
                "het model blijft even geladen: anders kost elke foto opnieuw 40 s");
            Check.Dat(eerste.Body["model"]?.GetValue<string>() == AppSettings.Current.AiModel,
                $"het model uit de instellingen ({AppSettings.Current.AiModel})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: wat er misloopt");
        {
            // Eén stuk dat zich niet aan de vorm houdt, mag de rest niet meenemen.
            ollama.Wis();
            ollama.Namen = ["Galactix"];
            ollama.Beschrijving = "Toch iets.";
            ollama.RommelBijVraag = 2;

            var uit = await new PhotoAnalyzer().AnalyseerAsync(groot);

            Check.Dat(uit.Fout is null && uit.Gelezen.Count == 1 && uit.Beschrijving == "Toch iets.",
                $"een stuk met onzin laat de rest staan ({uit.Gelezen.Count} namen, '{uit.Beschrijving}')");

            ollama.RommelBijVraag = -1;

            // En Ollama dat niet draait, is het gewone geval bij iemand die het nooit startte.
            PhotoAnalyzer.UrlVoorControles = "http://127.0.0.1:1";
            var dood = await new PhotoAnalyzer().AnalyseerAsync(klein);

            Check.Dat(dood.Fout is not null && dood.Fout.Contains("Ollama antwoordt niet"),
                $"Ollama dicht: een zin die verder helpt ('{dood.Fout}')");
            Check.Dat(dood.Beschrijving.Length == 0, "en geen verzonnen beschrijving");

            PhotoAnalyzer.UrlVoorControles = $"http://127.0.0.1:{ollama.Poort}";
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Foto: alle foto's van één zoekertje");
        {
            // De zoekpagina geeft er één; op de pagina van het zoekertje staan er meer. Die
            // pagina zet dezelfde foto twee keer neer - klein in het rijtje, groot bovenaan -
            // en geeft er één met een pad in plaats van een volledig adres.
            using var site = new Proefsite
            {
                VasteInhoud = """
                    <html><body>
                      <div class="gallery">
                        <img src="https://voorbeeld.be/foto1_klein.jpg">
                        <img src="https://voorbeeld.be/foto2_klein.jpg">
                        <img src="https://voorbeeld.be/foto1_klein.jpg">
                        <img src="/fotos/foto3_klein.jpg">
                        <img src="">
                      </div>
                      <img src="https://voorbeeld.be/logo.png">
                    </body></html>
                    """
            };

            var def = site.Site("Fotosite");
            def.DetailImagesSelector = ".gallery img@src::replace(_klein,_groot)";

            var zoekertje = new Listing
            {
                Source = "Fotosite",
                Title = "Doos vol dvd's",
                Url = $"http://127.0.0.1:{site.Poort}/z/1",
                ImageUrls = { "https://voorbeeld.be/uit-de-lijst.jpg" }
            };

            var lijst = new[] { def };
            var fotos = await DetailFetcher.FotosAsync(zoekertje, lijst);

            Check.Dat(fotos.Count == 4, $"vier foto's: die van de lijst plus drie van de pagina ({fotos.Count})");
            Check.Dat(fotos[0] == "https://voorbeeld.be/uit-de-lijst.jpg",
                "de foto die we al hadden staat vooraan - die is er zeker");
            Check.Dat(fotos.Contains("https://voorbeeld.be/foto1_groot.jpg"),
                "::replace werkt, dus de grote variant");
            Check.Dat(fotos.Count(f => f.Contains("foto1")) == 1, "dezelfde foto telt één keer");
            Check.Dat(fotos.Any(f => f == $"http://127.0.0.1:{site.Poort}/fotos/foto3_groot.jpg"),
                $"een pad wordt een volledig adres ({fotos.LastOrDefault()})");
            Check.Dat(!fotos.Any(f => f.Contains("logo")), "en enkel wat de selector aanwijst");

            // Foto's staan lang niet altijd in de <body>. 2dehands zet ze in het ld+json-blok van
            // de <head> - de webstandaard die ook Google leest - en met alleen de body vond de
            // selector daar nul elementen terwijl ze er gewoon stonden. Meteen ook de twee andere
            // dingen die dat blok vraagt: alle treffers binnen één element, en ::replace vooraf
            // om de / van JSON weer een schuine streep te maken.
            var uni = new string(new[] { (char)92, 'u', '0', '0', '2', 'F' });

            var kop = "<html><head><script type=\"application/ld+json\">" +
                      "{\"image\":[\"https:" + uni + uni + "foto.be" + uni + "een_klein.jpg\"," +
                      "\"https:" + uni + uni + "foto.be" + uni + "twee_klein.jpg\"]}" +
                      "</script></head><body><p>niets</p></body></html>";

            var uitKop = await GenericSource.ReadFieldsAsync(kop,
                "script[type='application/ld+json']::replace(" + uni + ",/)::replace(_klein,_groot)" +
                "::match(https://foto\\.be/[A-Za-z0-9/._-]+)");

            Check.Dat(uitKop.Count == 2, $"een blok in de <head> wordt gevonden ({uitKop.Count})");
            Check.Dat(uitKop.All(f => f.EndsWith("_groot.jpg")),
                $"met ::replace vooraf en élke treffer erin ({string.Join(", ", uitKop)})");

            // Een site zonder dat veld: dan blijft het bij de foto uit de lijst, zonder verzoek.
            var zonder = new Listing
            {
                Source = "Geen", Title = "Iets", Url = $"http://127.0.0.1:{site.Poort}/z/2",
                ImageUrls = { "https://voorbeeld.be/enkel-deze.jpg" }
            };

            var alleen = await DetailFetcher.FotosAsync(zonder, new[] { site.Site("Geen") });

            Check.Dat(alleen.Count == 1 && alleen[0] == "https://voorbeeld.be/enkel-deze.jpg",
                $"zonder selector blijft het bij die ene ({alleen.Count})");

            // En een pagina die niet antwoordt, mag het venster niet leegmaken.
            var stuk = new Listing
            {
                Source = "Fotosite", Title = "Iets", Url = "http://127.0.0.1:1/z/3",
                ImageUrls = { "https://voorbeeld.be/toch-deze.jpg" }
            };

            var na = await DetailFetcher.FotosAsync(stuk, lijst);

            Check.Dat(na.Count == 1 && na[0] == "https://voorbeeld.be/toch-deze.jpg",
                $"een pagina die niet lukt: de foto uit de lijst blijft ({na.Count})");

            // En twee zoekertjes van dezelfde site zonder eigen id krijgen niet elkaars foto's:
            // daarom hangt het onthouden aan het adres van de pagina en niet aan Listing.Key.
            var buur = new Listing
            {
                Source = "Fotosite", Title = "Een ander", Url = $"http://127.0.0.1:{site.Poort}/z/9",
                ImageUrls = { "https://voorbeeld.be/van-de-buur.jpg" }
            };

            var vanBuur = await DetailFetcher.FotosAsync(buur, lijst);

            Check.Dat(vanBuur[0] == "https://voorbeeld.be/van-de-buur.jpg",
                "en het onthouden hangt aan het adres, niet aan een leeg id");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("De pagina van een zoekertje: foto's, verkoper en sinds in één verzoek");
        {
            // Het detailvenster wil drie dingen van dezelfde pagina. Die drie keer ophalen zou
            // bij een brugsite twaalf seconden kosten, dus het is één verzoek - en dat wordt
            // hier geteld, want dat is het hele punt.
            var verzoeken = 0;

            using var site = new Proefsite
            {
                Antwoord = _ =>
                {
                    Interlocked.Increment(ref verzoeken);
                    return """
                        <html><body>
                          <div class="gallery">
                            <img src="https://voorbeeld.be/een.jpg">
                            <img src="https://voorbeeld.be/twee.jpg">
                          </div>
                          <div class="stat"><b>7x</b> bekeken</div>
                          <div class="stat">Sinds <b>24 sep. '26</b></div>
                          <div class="verkoper">Verkocht door Japoto</div>
                          <div class="tekst">Eerste regel<br>Tweede regel<br><br><br>Na drie breaks\nEn een mislukt regeleinde<br>   </div>
                        </body></html>
                        """;
                }
            };

            var def = site.Site("Detailsite");
            def.DetailImagesSelector = ".gallery img@src";
            def.DetailSellerSelector = @".verkoper::match(Verkocht door\s+(.+))";
            def.DetailPostedSelector = @"div.stat::match(Sinds\s+(.+))";
            def.DetailDescriptionSelector = "div.tekst";

            var lijst = new[] { def };

            var zoekertje = new Listing
            {
                Source = "Detailsite", Title = "Een cd-speler",
                Url = $"http://127.0.0.1:{site.Poort}/z/100",
                ImageUrls = { "https://voorbeeld.be/uit-de-lijst.jpg" }
            };

            var det = await DetailFetcher.DetailsAsync(zoekertje, lijst);

            Check.Dat(verzoeken == 1, $"één verzoek voor alle vier ({verzoeken})");
            Check.Dat(det.Fotos.Count == 3, $"de foto's: die van de lijst plus twee ({det.Fotos.Count})");

            // Het detailvenster laat de foto van de zoekpagina vallen zodra de pagina er geeft,
            // en heeft daarvoor de lijst zónder die foto nodig. Uit de samengevoegde lijst is
            // dat niet af te leiden: bij 2dehands ís het hetzelfde adres.
            Check.Dat(det.PaginaFotos.Count == 2 &&
                      !det.PaginaFotos.Contains("https://voorbeeld.be/uit-de-lijst.jpg"),
                $"PaginaFotos is enkel wat op de pagina stond ({det.PaginaFotos.Count})");
            Check.Dat(det.Verkoper == "Japoto", $"de verkoper ('{det.Verkoper}')");
            Check.Dat(det.Sinds == "24 sep. '26", $"en sinds wanneer ('{det.Sinds}')");
            Check.Dat(det.Fout is null, "zonder klacht");

            // "Sinds" staat niet vooraan in het rijtje: met een patroon neemt de motor het
            // eerste element waar dat patroon ook echt op past, niet botweg het eerste.
            Check.Dat(!det.Sinds.Contains("bekeken"), "en niet de teller die ervoor staat");

            // De beschrijving houdt haar regelindeling. Een selector plakt alle witruimte plat
            // tot één spatie - terecht voor een titel, maar het maakt van een beschrijving één
            // brij. Drie <br> na elkaar worden hoogstens één lege regel, en de lege regel aan
            // het einde valt weg.
            var regels = det.Beschrijving.Split('\n');

            Check.Dat(regels.Length == 5,
                $"de beschrijving houdt haar regels ({regels.Length}): {string.Join(" | ", regels)}");
            Check.Dat(regels[0] == "Eerste regel" && regels[1] == "Tweede regel",
                "de eerste twee regels staan apart");
            Check.Dat(regels[2].Length == 0 && regels[3] == "Na drie breaks",
                "drie breaks na elkaar geven hoogstens één lege regel");

            // 2dehands zet in het stuk uit zijn eigen databank een regeleinde als de twee
            // tékens \ en n. Onbewerkt staat dat zo op het scherm.
            Check.Dat(regels[4] == "En een mislukt regeleinde",
                $"een letterlijke \\n wordt alsnog een regeleinde ('{regels[4]}')");
            Check.Dat(!det.Beschrijving.EndsWith("\n") && !det.Beschrijving.Contains('\u001F'),
                "geen losse witruimte aan het einde, en geen stuurteken in beeld");

            // Nog eens: onthouden, dus geen tweede verzoek.
            var weer = await DetailFetcher.DetailsAsync(zoekertje, lijst);

            Check.Dat(verzoeken == 1, $"een tweede keer kijken vraagt de pagina niet opnieuw ({verzoeken})");
            Check.Dat(weer.Fotos.Count == 3 && weer.Verkoper == "Japoto" && weer.Sinds == "24 sep. '26" &&
                      weer.Beschrijving == det.Beschrijving,
                "en geeft hetzelfde terug");

            // Een site die nog geen van de drie velden heeft: geen verzoek, en een uitleg in
            // plaats van een leeg venster waarvan niemand weet of het aan het laden is.
            var kaal = site.Site("Kaal");
            var elders = new Listing
            {
                Source = "Kaal", Title = "Iets", Url = $"http://127.0.0.1:{site.Poort}/z/101",
                ImageUrls = { "https://voorbeeld.be/enkel-deze.jpg" }
            };

            var zonder = await DetailFetcher.DetailsAsync(elders, new[] { kaal });

            Check.Dat(verzoeken == 1, $"een site zonder die velden haalt niets op ({verzoeken})");
            Check.Dat(zonder.Fout is not null && zonder.Fout.Contains("zegt nog niet"),
                $"en zegt waarom: {zonder.Fout}");
            Check.Dat(zonder.Fotos.Count == 1, "de foto uit de lijst blijft staan");

            // Een pagina die niet antwoordt, mag het venster niet leegmaken.
            var stuk = new Listing
            {
                Source = "Detailsite", Title = "Iets", Url = "http://127.0.0.1:1/z/102",
                ImageUrls = { "https://voorbeeld.be/toch-deze.jpg" }
            };

            var na = await DetailFetcher.DetailsAsync(stuk, lijst);

            Check.Dat(na.Fotos.Count == 1 && na.Verkoper.Length == 0,
                "een pagina die niet lukt: de foto uit de lijst blijft");
            Check.Dat(na.Fout is not null, $"met de reden erbij: {na.Fout}");

            // FotosAsync is sinds het detailvenster dezelfde weg, met enkel de foto's eruit.
            var alleenFotos = await DetailFetcher.FotosAsync(zoekertje, lijst);

            Check.Dat(alleenFotos.Count == 3, $"de AI-controle loopt over dezelfde weg ({alleenFotos.Count})");

            // Het kale CSS-stuk van een selector: daarmee wacht de browser tot de foto er echt
            // staat. Zonder dat lees je een pagina die nog niet af is - bij Facebook stond er
            // dan geen énkele img op, terwijl er 9 MB omhulsel al binnen was.
            Check.Dat(DetailFetcher.CssDeel("img[alt^='Productfoto van']@src") == "img[alt^='Productfoto van']",
                "het @attribuut valt weg voor het wachten");
            Check.Dat(DetailFetcher.CssDeel(".gallery img@src::replace(_klein,_groot)") == ".gallery img",
                "de opschoonregels ook");
            Check.Dat(DetailFetcher.CssDeel("script[type='application/ld+json']::match(https://a\\.be/x)")
                      == "script[type='application/ld+json']",
                "een ::match met een adres erin knipt niet te vroeg");
            Check.Dat(DetailFetcher.CssDeel("div.tekst") == "div.tekst", "een kale selector blijft heel");
            Check.Dat(DetailFetcher.CssDeel("") is null && DetailFetcher.CssDeel("   ") is null,
                "niets ingevuld: dan valt er ook niets te wachten");
        }

        PhotoAnalyzer.UrlVoorControles = null;
    }

    /// <summary>
    /// Een foto met ruis erin. Ruis en geen effen vlak, want twee effen stukken zijn byte voor
    /// byte gelijk, en dan is niet te zien of er werkelijk geknipt is.
    /// </summary>
    private static byte[] Foto(int breedte, int hoogte)
    {
        var stride = breedte * 3;
        var punten = new byte[stride * hoogte];
        new Random(1234).NextBytes(punten);

        var beeld = BitmapSource.Create(breedte, hoogte, 96, 96, PixelFormats.Bgr24, null, punten, stride);

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(beeld));

        using var uit = new MemoryStream();
        encoder.Save(uit);

        return uit.ToArray();
    }
}

/// <summary>
/// Doet wat Ollama doet: een POST op /api/generate aannemen en een antwoord teruggeven in de
/// vorm die erom gevraagd werd. Houdt elke vraag bij, zodat de controles kunnen nakijken wat er
/// werkelijk naar het model ging.
/// </summary>
public sealed class NepOllama : IDisposable
{
    public sealed record Vraag(JsonObject Body, string Prompt, string Beeld);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public int Poort { get; }

    public List<Vraag> Vragen { get; } = new();

    /// <summary>Welke namen elk stuk "leest".</summary>
    public List<string> Namen { get; set; } = new() { "Iets" };

    /// <summary>Wat er bij de laatste vraag als alinea terugkomt.</summary>
    public string Beschrijving { get; set; } = "Een beschrijving.";

    /// <summary>Bij deze vraag (vanaf 0) geen geldige JSON teruggeven, maar rommel.</summary>
    public int RommelBijVraag { get; set; } = -1;

    public NepOllama()
    {
        _listener.Start();
        Poort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(LusAsync);
    }

    public void Wis()
    {
        lock (Vragen) Vragen.Clear();
    }

    private async Task LusAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }

            _ = Task.Run(() => BehandelAsync(client));
        }
    }

    private async Task BehandelAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stroom = client.GetStream();
                var kop = new StringBuilder();
                var byteje = new byte[1];

                while (!kop.ToString().EndsWith("\r\n\r\n"))
                {
                    if (await stroom.ReadAsync(byteje, _stop.Token) == 0) return;
                    kop.Append((char)byteje[0]);
                }

                var lengte = 0;
                foreach (var regel in kop.ToString().Split("\r\n"))
                {
                    if (regel.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        lengte = int.Parse(regel[15..].Trim());
                }

                var body = new byte[lengte];
                var gelezen = 0;
                while (gelezen < lengte)
                {
                    var n = await stroom.ReadAsync(body.AsMemory(gelezen), _stop.Token);
                    if (n == 0) return;
                    gelezen += n;
                }

                var json = JsonNode.Parse(Encoding.UTF8.GetString(body))!.AsObject();
                int nummer;

                lock (Vragen)
                {
                    nummer = Vragen.Count;
                    Vragen.Add(new Vraag(json,
                        json["prompt"]?.GetValue<string>() ?? "",
                        json["images"]?[0]?.GetValue<string>() ?? ""));
                }

                // Wat Ollama teruggeeft: het antwoord van het model als tekst, in een veld.
                var laatste = json["format"]?["properties"]?["beschrijving"] is not null;
                var binnenin = nummer == RommelBijVraag
                    ? "{dit is geen json"
                    : laatste
                        ? new JsonObject { ["beschrijving"] = Beschrijving }.ToJsonString()
                        : new JsonObject { ["namen"] = new JsonArray(Namen.Select(n => (JsonNode)n!).ToArray()) }.ToJsonString();

                var antwoord = new JsonObject { ["response"] = binnenin, ["done"] = true }.ToJsonString();
                var uit = Encoding.UTF8.GetBytes(antwoord);

                var kopregels = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                    $"Content-Length: {uit.Length}\r\nConnection: close\r\n\r\n");

                await stroom.WriteAsync(kopregels, _stop.Token);
                await stroom.WriteAsync(uit, _stop.Token);
                await stroom.FlushAsync(_stop.Token);
            }
            catch
            {
                // Een verbinding die wegvalt is hier niets om over te vallen.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

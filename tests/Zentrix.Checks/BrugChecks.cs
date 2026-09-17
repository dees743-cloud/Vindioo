using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// De lokale server van de brug, met verzoeken zoals een webpagina en zoals de extensie ze
/// sturen, en een nagebootste extensie die opdrachten aanneemt en pagina's terugstuurt.
/// Heeft poort 8731 nodig: draait Zentrix, dan wordt dit overgeslagen.
/// </summary>
public static class BrugChecks
{
    public static async Task RunAsync()
    {
        Check.Groep("Brug: enkel de extensie kan een verkeerde koppelcode melden");

        if (!PoortVrij())
        {
            Check.Overgeslagen($"poort {BridgeServer.Port} is bezet (draait Zentrix?): de brugcontroles zijn overgeslagen");
            return;
        }

        var brug = BridgeServer.Instance;

        // Eerst: wat als een ander programma de poort bezet houdt.
        using (var bezetter = new TcpListener(IPAddress.Loopback, BridgeServer.Port))
        {
            bezetter.Start();

            Exception? startFout = null;
            try { brug.Start(); } catch (Exception ex) { startFout = ex; }
            Check.Dat(startFout is null && brug.PortBusy, "poort bezet: geen uitzondering, wel PortBusy");

            var status = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(5));
            Check.Dat(status == BridgeStatus.PortInUse, $"EnsureBridgeAsync zegt het meteen ({status})");

            bezetter.Stop();
        }

        brug.Start();
        Check.Dat(!brug.PortBusy, "poort weer vrij: de brug start alsnog");

        var web = await StuurAsync("GET", "/job?token=verzonnen", "Origin: https://kwaad.example");
        Check.Dat(!brug.WrongCodeRecently, "webpagina (fetch) met een verzonnen code: telt niet");
        Check.Dat(!web.Contains("Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase),
            "webpagina krijgt geen toestemming om het antwoord te lezen");

        await StuurAsync("GET", "/job?token=verzonnen");
        Check.Dat(!brug.WrongCodeRecently, "webpagina (img, zonder Origin) met een verzonnen code: telt niet");

        var voorvraagWeb = await StuurAsync("OPTIONS", "/job?token=verzonnen", "Origin: https://kwaad.example",
            "Access-Control-Request-Headers: x-zentrix-brug");
        Check.Dat(!voorvraagWeb.Contains("Access-Control-Allow", StringComparison.OrdinalIgnoreCase),
            "voorvraag van een webpagina voor de kopregel: geweigerd");

        var voorvraagExt = await StuurAsync("OPTIONS", "/job?token=x", "Origin: chrome-extension://abcdefgh",
            "Access-Control-Request-Headers: x-zentrix-brug");
        Check.Dat(voorvraagExt.Contains("Access-Control-Allow-Origin: chrome-extension://abcdefgh") &&
                  voorvraagExt.Contains(BridgeServer.ExtensionHeader),
            "voorvraag van de extensie: toegestaan");

        var fout = await StuurAsync("GET", "/job?token=verzonnen", BridgeServer.ExtensionHeader + ": 1");
        Check.Dat(brug.WrongCodeRecently && fout.Contains("verkeerde koppelcode"), "extensie met een verkeerde code: gemeld");

        var ping = await StuurAsync("GET", "/ping?token=" + brug.Token, BridgeServer.ExtensionHeader + ": 1");
        Check.Dat(ping.Contains("\"ok\":true") && brug.ExtensionAlive, "extensie met de juiste code: /ping klopt");

        // ---------------------------------------------------------------------------
        Check.Groep("Brug: streamen enkel als iemand meeleest, vervolgpagina's in golven van drie");

        using var site = new Proefsite();
        using var extensie = new NepExtensie(brug.Token);

        var def = site.Site("Brugsite", paginering: true);
        def.UseBridge = true;

        // Planner: geen melder, dus geen stream.
        site.PerPagina.Clear();
        site.PerPagina[1] = 30;
        extensie.Opdrachten.Clear();
        await new GenericSource(def).SearchAsync("cd", 20);
        Check.Dat(extensie.Opdrachten.Count >= 1 && extensie.Opdrachten.All(o => !o.Stream),
            "zonder melder (zoals de planner): stream staat uit");

        // Scherm: met melder, dus pagina 1 streamt, de vervolgpagina's niet.
        site.PerPagina[2] = 30;
        extensie.Opdrachten.Clear();
        await new GenericSource(def).SearchAsync("cd", 40, null, new SynchroneMelder<List<Listing>>(_ => { }));
        Check.Dat(extensie.Opdrachten.FirstOrDefault()?.Stream == true && extensie.Opdrachten.Skip(1).All(o => !o.Stream),
            $"met melder: enkel pagina 1 streamt ({string.Join(",", extensie.Opdrachten.Select(o => o.Stream))})");

        async Task<(List<int> Paginas, List<int> Golven)> Golven(Dictionary<int, int> perPagina)
        {
            site.PerPagina.Clear();
            foreach (var (p, n) in perPagina) site.PerPagina[p] = n;
            lock (site.Gevraagd) site.Gevraagd.Clear();
            extensie.Opdrachten.Clear();
            extensie.Golven.Clear();

            await new GenericSource(def).SearchAsync("cd", 500);

            lock (site.Gevraagd) return (site.Gevraagd.OrderBy(x => x).ToList(), extensie.Golven.ToList());
        }

        var vol = await Golven(Enumerable.Range(1, 12).ToDictionary(p => p, _ => 24));
        Check.Dat(vol.Paginas.Count == 10 && vol.Golven.SequenceEqual(new[] { 1, 3, 3, 3 }),
            $"volle pagina's: 1, dan golven van drie tot de rem ({vol.Paginas.Count} pagina's, golven {string.Join("+", vol.Golven)})");

        var kort = await Golven(new() { [1] = 24, [2] = 24, [3] = 24, [4] = 5 });
        Check.Dat(string.Join(",", kort.Paginas) == "1,2,3,4",
            $"24, 24, 24, 5: na de golf met de korte pagina stopt het ({string.Join(",", kort.Paginas)})");

        var weinig = await Golven(new() { [1] = 2 });
        Check.Dat(string.Join(",", weinig.Paginas) == "1",
            $"2 op pagina 1: geen vervolgpagina's (vroeger 9 samen) ({string.Join(",", weinig.Paginas)})");
    }

    private static bool PoortVrij()
    {
        try
        {
            var proef = new TcpListener(IPAddress.Loopback, BridgeServer.Port);
            proef.Start();
            proef.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<string> StuurAsync(string methode, string pad, params string[] kopregels)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", BridgeServer.Port);
        var stream = client.GetStream();

        var verzoek = new StringBuilder($"{methode} {pad} HTTP/1.1\r\nHost: 127.0.0.1:{BridgeServer.Port}\r\n");
        foreach (var kop in kopregels) verzoek.Append(kop).Append("\r\n");
        verzoek.Append("Content-Length: 0\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(verzoek.ToString()));
        using var lezer = new StreamReader(stream);
        return await lezer.ReadToEndAsync();
    }
}

/// <summary>
/// Doet wat de extensie doet: om werk vragen, de pagina ophalen (hier gewoon met HttpClient)
/// en terugsturen, met eerst een tussentijdse versie als de opdracht dat vraagt. Houdt bij
/// hoeveel opdrachten er telkens samen klaarstonden: dat zijn de golven.
/// </summary>
public sealed class NepExtensie : IDisposable
{
    public sealed record Opdracht(string Url, bool Stream);

    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private static readonly HttpClient Http = new();

    public List<Opdracht> Opdrachten { get; } = new();
    public List<int> Golven { get; } = new();

    public NepExtensie(string token)
    {
        _token = token;
        _ = Task.Run(LusAsync);
    }

    private async Task LusAsync()
    {
        var golf = new List<(string Id, string Url, bool Stream)>();

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var job = await VraagAsync();

                if (job is not null)
                {
                    golf.Add(job.Value);

                    // Even wachten: de app zet een golf in één keer klaar.
                    await Task.Delay(30);
                    continue;
                }

                if (golf.Count > 0)
                {
                    lock (Golven) Golven.Add(golf.Count);
                    await Task.WhenAll(golf.Select(VoerUitAsync));
                    golf.Clear();
                }

                await Task.Delay(50);
            }
            catch when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }
    }

    private async Task<(string Id, string Url, bool Stream)?> VraagAsync()
    {
        using var verzoek = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{BridgeServer.Port}/job?token={_token}");
        verzoek.Headers.Add(BridgeServer.ExtensionHeader, "1");

        using var antwoord = await Http.SendAsync(verzoek, _stop.Token);
        using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(_stop.Token));

        if (!doc.RootElement.TryGetProperty("url", out var url)) return null;

        var opdracht = (doc.RootElement.GetProperty("id").GetString()!, url.GetString()!,
                        doc.RootElement.TryGetProperty("stream", out var s) && s.GetBoolean());

        lock (Opdrachten) Opdrachten.Add(new Opdracht(opdracht.Item2, opdracht.Item3));
        return opdracht;
    }

    private async Task VoerUitAsync((string Id, string Url, bool Stream) opdracht)
    {
        var html = await Http.GetStringAsync(opdracht.Url, _stop.Token);

        if (opdracht.Stream)
            await StuurAsync(new { id = opdracht.Id, html, partial = true });

        await StuurAsync(new { id = opdracht.Id, html });
    }

    private async Task StuurAsync(object inhoud)
    {
        using var verzoek = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{BridgeServer.Port}/result?token={_token}")
        {
            Content = new StringContent(JsonSerializer.Serialize(inhoud), Encoding.UTF8, "application/json")
        };
        verzoek.Headers.Add(BridgeServer.ExtensionHeader, "1");

        using var _ = await Http.SendAsync(verzoek, _stop.Token);
    }

    public void Dispose() => _stop.Cancel();
}

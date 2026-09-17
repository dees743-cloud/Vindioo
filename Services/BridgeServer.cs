using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zentrix.Services;

/// <summary>
/// Kleine lokale server waarmee de browserextensie praat. Luistert enkel op
/// 127.0.0.1, dus alleen bereikbaar vanaf deze computer. De extensie vraagt
/// om werk, opent de pagina in een tabblad en stuurt de HTML terug.
/// </summary>
public class BridgeServer
{
    public const int Port = 8731;

    /// <summary>
    /// Aan deze kopregel herkennen we de extensie. Elke webpagina in elke browser op deze
    /// pc kan de brug aanspreken, maar een webpagina kan geen eigen kopregel meesturen
    /// zonder eerst toestemming te vragen, en die toestemming krijgt enkel de extensie
    /// (zie <see cref="WriteAsync"/>).
    /// </summary>
    public const string ExtensionHeader = "X-Zentrix-Brug";

    private static readonly Lazy<BridgeServer> Shared = new(() => new BridgeServer());
    public static BridgeServer Instance => Shared.Value;

    /// <summary>Koppelcode; moet overeenkomen met wat in de extensie staat.</summary>
    public string Token { get; }

    /// <summary>True zodra de extensie zich gemeld heeft.</summary>
    public bool ExtensionConnected { get; private set; }

    /// <summary>Wanneer de extensie zich het laatst meldde.</summary>
    public DateTime LastContact { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Meldt de extensie zich nu nog? Ze vraagt elke 250 ms om werk, dus als er
    /// enkele seconden niets kwam staat Chrome dicht of slaapt de extensie.
    /// Anders dan <see cref="ExtensionConnected"/>, dat blijft staan zodra ze
    /// zich ooit gemeld heeft.
    /// </summary>
    public bool ExtensionAlive => DateTime.Now - LastContact < TimeSpan.FromSeconds(3);

    /// <summary>Wanneer de extensie zich het laatst meldde met een andere koppelcode.</summary>
    public DateTime LastWrongCode { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Meldt de extensie zich, maar met een code die niet klopt? Dan draait Chrome en
    /// staat de extensie aan, en is het enige wat ontbreekt de juiste code. Zonder dit
    /// onderscheid leek dat op "de extensie meldt zich niet" - en daar zochten we in
    /// september 2026 een uur naar.
    /// </summary>
    public bool WrongCodeRecently => DateTime.Now - LastWrongCode < TimeSpan.FromSeconds(3);

    /// <summary>Een verkeerde code komt elke 250 ms binnen; eens per minuut loggen volstaat.</summary>
    private DateTime _wrongCodeLogged = DateTime.MinValue;

    /// <summary>
    /// Eén opdracht voor de extensie. <see cref="OnPartial"/> wordt aangeroepen bij
    /// elke tussentijdse levering, zodat de app al resultaten kan tonen terwijl de
    /// pagina nog aan het bijladen is. <see cref="Completion"/> loopt af bij de
    /// laatste, volledige levering.
    ///
    /// <paramref name="Headers"/> en <paramref name="RawText"/> horen bij elkaar: een
    /// JSON-API wil je niet als pagina openen (dan krijg je de JSON in een &lt;pre&gt;
    /// en kan je geen kopregels meegeven), maar laten ophalen met een fetch vanuit een
    /// pagina van die site. Dat is de enige weg naar sites achter Cloudflare: het is de
    /// echte Chrome van de gebruiker die de verbinding legt.
    ///
    /// <paramref name="ItemTimeoutMs"/> is hoe lang de extensie op het eerste zoekertje
    /// wacht. Leeg betekent haar eigen standaard (acht seconden); een vervolgpagina
    /// krijgt minder, want die is vaak gewoon leeg.
    /// </summary>
    private record Job(string Id, string Url, TaskCompletionSource<string> Completion,
        Func<string, Task>? OnPartial,
        IDictionary<string, string>? Headers = null, bool RawText = false,
        string? WaitSelector = null, int? ItemTimeoutMs = null);

    private readonly ConcurrentQueue<Job> _waiting = new();
    private readonly ConcurrentDictionary<string, Job> _running = new();

    private TcpListener? _listener;

    private BridgeServer()
    {
        Token = LoadOrCreateToken();
    }

    /// <summary>De koppelcode blijft bewaard, zodat je hem maar één keer moet invullen.</summary>
    private static string LoadOrCreateToken()
    {
        Directory.CreateDirectory(AppPaths.Folder);
        var file = AppPaths.BridgeCodeFile;

        if (File.Exists(file))
        {
            var existing = File.ReadAllText(file).Trim();
            if (existing.Length > 8) return existing;
        }

        var token = Guid.NewGuid().ToString("N");
        File.WriteAllText(file, token);
        return token;
    }

    /// <summary>
    /// Kon de server niet starten omdat de poort bezet is? Dan werkt de brug niet, maar de
    /// rest van de app wel. Een volgende <see cref="Start"/> probeert het opnieuw.
    /// </summary>
    public bool PortBusy { get; private set; }

    public void Start()
    {
        if (_listener is not null) return;

        var listener = new TcpListener(IPAddress.Loopback, Port);

        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // Een ander programma, of een Zentrix van een andere Windows-gebruiker, houdt de
            // poort vast. Vroeger viel de app daardoor al om bij het opbouwen van het
            // hoofdscherm: de snelkoppeling leek dan niets te doen.
            if (!PortBusy) Log.Write($"brug: poort {Port} is bezet; de brug werkt niet tot die vrijkomt");
            PortBusy = true;
            return;
        }

        PortBusy = false;
        _listener = listener;

        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch
            {
                break;   // server gestopt
            }
        }
    }

    /// <summary>
    /// Vraagt de extensie om een pagina op te halen en wacht op het antwoord.
    /// Geef <paramref name="onPartial"/> mee om tussentijdse versies van de pagina
    /// te ontvangen terwijl ze nog aan het laden is.
    /// </summary>
    public async Task<string> FetchAsync(string url, CancellationToken ct = default,
        Func<string, Task>? onPartial = null,
        IDictionary<string, string>? headers = null, bool rawText = false,
        string? waitSelector = null, int? itemTimeoutMs = null)
    {
        Start();

        if (PortBusy)
            throw new InvalidOperationException(ChromeLauncher.Describe(BridgeStatus.PortInUse));

        var job = new Job(Guid.NewGuid().ToString("N"), url,
            new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
            onPartial, headers, rawText, waitSelector, itemTimeoutMs);

        _waiting.Enqueue(job);
        Log.Write($"brug: opdracht {job.Id[..8]} in wachtrij -> {url}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));

        await using (timeout.Token.Register(() => job.Completion.TrySetException(
            new TimeoutException("Geen antwoord van de browserextensie. Staat Chrome open en is de extensie geïnstalleerd?"))))
        {
            try
            {
                var html = await job.Completion.Task;
                Log.Write($"brug: opdracht {job.Id[..8]} volledig binnen ({html.Length} tekens)");
                return html;
            }
            catch (Exception ex)
            {
                Log.Write($"brug: opdracht {job.Id[..8]} mislukt — {ex.Message}");
                throw;
            }
        }
    }

    // ---------- afhandeling van verzoeken ----------

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();

                // Kop en body zelf uitlezen op byte-niveau.
                //
                // Waarom niet met een StreamReader: Content-Length telt BYTES,
                // terwijl een reader TEKENS teruggeeft. Bij accenten (é, è) zijn
                // er méér bytes dan tekens, en dan bleef de server wachten op
                // tekens die nooit kwamen — de Franse pagina's van leboncoin
                // liepen daardoor vast.
                var buffer = new byte[8192];
                using var incoming = new MemoryStream();
                var headerEnd = -1;

                while (headerEnd < 0)
                {
                    var n = await stream.ReadAsync(buffer);
                    if (n <= 0) return;

                    incoming.Write(buffer, 0, n);
                    headerEnd = FindHeaderEnd(incoming.GetBuffer(), (int)incoming.Length);
                }

                var received = incoming.GetBuffer();
                var headerText = Encoding.UTF8.GetString(received, 0, headerEnd);
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var parts = lines[0].Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0];
                var path = parts[1];

                var contentLength = 0;
                string? origin = null;
                var vanExtensie = false;

                foreach (var line in lines.Skip(1))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line[15..].Trim(), out contentLength);
                    else if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                        origin = line[7..].Trim();
                    else if (line.StartsWith(ExtensionHeader + ":", StringComparison.OrdinalIgnoreCase))
                        vanExtensie = true;
                }

                // Wat er na de lege regel al binnen is, hoort bij de body. Meteen op
                // de juiste maat: een pagina van leboncoin is ruim een miljoen tekens,
                // en een geheugenstroom die telkens verdubbelt kopieert die meermaals.
                var bodyStart = headerEnd + 4;
                using var bodyBytes = new MemoryStream(Math.Max(contentLength, 0));
                bodyBytes.Write(received, bodyStart, (int)incoming.Length - bodyStart);

                while (bodyBytes.Length < contentLength)
                {
                    var n = await stream.ReadAsync(buffer);
                    if (n <= 0) break;
                    bodyBytes.Write(buffer, 0, n);
                }

                // De JSON rechtstreeks uit de bytes lezen, zonder er eerst een tekst
                // van te maken: dat scheelt een volledige kopie van de pagina.
                var body = bodyBytes.GetBuffer().AsMemory(0, (int)bodyBytes.Length);

                var response = Handle(method, path, body, vanExtensie);
                await WriteAsync(stream, response, origin);
            }
            catch (Exception ex)
            {
                Log.Write($"brug: verbinding mislukt — {ex.Message}");
            }
        }
    }

    /// <summary>Zoekt de lege regel die kop en body scheidt (\r\n\r\n).</summary>
    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' &&
                data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return -1;
    }

    /// <param name="vanExtensie">Droeg het verzoek de kopregel <see cref="ExtensionHeader"/>?</param>
    private string Handle(string method, string path, ReadOnlyMemory<byte> body, bool vanExtensie)
    {
        // De extensie stuurt eerst een controlevraag; die moet zonder inhoud slagen.
        if (method == "OPTIONS") return "";

        var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "";
        var route = path.Split('?')[0];

        var token = GetParam(query, "token");
        if (token != Token)
        {
            // Onthouden, zodat de app "verkeerde code" kan zeggen in plaats van "geen
            // contact". De extensie zelf leest dit antwoord ook: zie background.js.
            //
            // Enkel als het verzoek van de extensie komt. Anders kon elke webpagina met een
            // verzonnen code de brug laten zeggen dat de code niet klopt, en dan sloeg de app
            // alle brugsites over.
            if (vanExtensie)
            {
                LastWrongCode = DateTime.Now;

                if (DateTime.Now - _wrongCodeLogged > TimeSpan.FromMinutes(1))
                {
                    _wrongCodeLogged = DateTime.Now;
                    Log.Write("brug: de extensie meldt zich met een andere koppelcode");
                }
            }

            return JsonSerializer.Serialize(new { error = "verkeerde koppelcode" });
        }

        ExtensionConnected = true;
        LastContact = DateTime.Now;

        switch (route)
        {
            // De popup van de extensie controleert of een geplakte code klopt. Die
            // controle is het tokennazicht hierboven; hier valt niets meer te doen.
            case "/ping":
                return JsonSerializer.Serialize(new { ok = true });

            // De extensie vraagt of er werk is.
            case "/job":
                while (_waiting.TryDequeue(out var job))
                {
                    // Een opdracht waarop niemand meer wacht (afgelopen tijd, of de
                    // zoekopdracht is gestopt) niet meer uitvoeren. Anders opent Chrome
                    // zodra de brug weer werkt nog tabbladen voor zoekopdrachten die
                    // allang voorbij zijn.
                    if (job.Completion.Task.IsCompleted)
                    {
                        Log.Write($"brug: opdracht {job.Id[..8]} overgeslagen, niemand wacht er nog op");
                        continue;
                    }

                    _running[job.Id] = job;
                    Log.Write($"brug: extensie neemt opdracht {job.Id[..8]} aan");
                    return JsonSerializer.Serialize(new
                    {
                        id = job.Id,
                        url = job.Url,
                        headers = job.Headers,
                        rawText = job.RawText,
                        waitSelector = job.WaitSelector,
                        itemTimeoutMs = job.ItemTimeoutMs,

                        // Tussentijdse versies enkel sturen als iemand ze leest. Een
                        // vervolgpagina of een geplande zoekopdracht doet dat niet, en
                        // dan kopieerde de extensie elke 0,75 s de hele pagina voor niets:
                        // 111 van de 159 miljoen tekens, gemeten op 15 september 2026.
                        stream = job.OnPartial is not null
                    });
                }
                return JsonSerializer.Serialize(new { });

            // De extensie levert de opgehaalde pagina af.
            case "/result":
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var id = doc.RootElement.GetProperty("id").GetString() ?? "";

                    // Tussentijdse levering: doorgeven en de opdracht open laten staan,
                    // want de volledige versie komt nog.
                    if (doc.RootElement.TryGetProperty("partial", out var partial) &&
                        partial.ValueKind == JsonValueKind.True)
                    {
                        if (_running.TryGetValue(id, out var pending) &&
                            doc.RootElement.TryGetProperty("html", out var partialHtml))
                        {
                            var html = partialHtml.GetString() ?? "";
                            Log.Write($"brug: tussentijdse levering voor {id[..8]} ({html.Length} tekens)");
                            _ = InvokePartialAsync(pending, html);
                        }
                        else
                        {
                            Log.Write($"brug: tussentijdse levering voor onbekende opdracht {id}");
                        }

                        return JsonSerializer.Serialize(new { ok = true });
                    }

                    if (_running.TryRemove(id, out var done))
                    {
                        if (doc.RootElement.TryGetProperty("error", out var error) &&
                            error.ValueKind == JsonValueKind.String)
                        {
                            done.Completion.TrySetException(
                                new InvalidOperationException(error.GetString()));
                        }
                        else
                        {
                            done.Completion.TrySetResult(
                                doc.RootElement.GetProperty("html").GetString() ?? "");
                        }
                    }
                }
                catch
                {
                    // Onleesbaar antwoord: de wachtende zoekopdracht loopt vanzelf af.
                }
                return JsonSerializer.Serialize(new { ok = true });

            default:
                return JsonSerializer.Serialize(new { ok = true });
        }
    }

    /// <summary>
    /// Geeft een tussentijdse pagina door. Een mislukte tussenlevering mag de
    /// lopende zoekopdracht nooit stukmaken; de volledige versie komt sowieso nog.
    /// </summary>
    private static async Task InvokePartialAsync(Job job, string html)
    {
        try
        {
            if (job.OnPartial is not null) await job.OnPartial(html);
        }
        catch
        {
            // Bewust stil: dit is enkel een voorproefje.
        }
    }

    private static string GetParam(string query, string name)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name) return Uri.UnescapeDataString(kv[1]);
        }
        return "";
    }

    /// <summary>
    /// Schrijft het antwoord. De kopregels voor CORS krijgt enkel de extensie
    /// (<c>chrome-extension://</c>): daarmee mag zij de antwoorden lezen en de kopregel
    /// <see cref="ExtensionHeader"/> meesturen. Tot september 2026 stond hier "*", en dan
    /// mocht elke webpagina dat ook.
    ///
    /// Een verzoek zonder Origin (zoals de extensie het met haar hostrechten doorgaans
    /// stuurt) valt buiten CORS en heeft die kopregels niet nodig.
    /// </summary>
    private static async Task WriteAsync(NetworkStream stream, string json, string? origin)
    {
        var payload = Encoding.UTF8.GetBytes(json);

        var cors = origin is not null && origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
            ? $"Access-Control-Allow-Origin: {origin}\r\n" +
              $"Access-Control-Allow-Headers: Content-Type, {ExtensionHeader}\r\n" +
              "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
              "Access-Control-Max-Age: 600\r\n" +
              "Vary: Origin\r\n"
            : "";

        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            cors +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
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

    /// <summary>
    /// De handtekening over een verzoek of een antwoord: HMAC-SHA256 met de koppelcode.
    ///
    /// Hiermee gaat de code zélf nooit meer over de lijn. Dat was het lek: de extensie stuurde
    /// hem als <c>?token=</c> naar wie poort 8731 ook maar vasthield, dus een programma dat die
    /// poort eerst bezet kende hem - en kon jouw aangemelde browser pagina's laten ophalen, met
    /// jouw cookies. Nu bewijst elke kant enkel dát hij de code kent.
    /// </summary>
    public const string SignatureHeader = "X-Zentrix-Sig";

    /// <summary>
    /// De handtekening over <b>enkel de nonce</b>, die dus al na te kijken is met de kopregels
    /// in de hand - voor er één byte van de body gelezen is.
    ///
    /// Dat klinkt dubbelop naast <see cref="SignatureHeader"/>, en dat is het niet. De brug kon
    /// de koppelcode vroeger nakijken voor ze een body las, omdat die in het adres stond: wie de
    /// code niet kende, kreeg nooit een body gelezen, hoe groot die ook zei te zijn. Een
    /// handtekening óver de body kan dat per definitie niet, dus zonder deze tweede kopregel zou
    /// die rem er stilletjes uit zijn.
    /// </summary>
    public const string PreHeader = "X-Zentrix-Voor";

    /// <summary>Het wegwerpgetal dat per verzoek meegaat. Geen geheim: het mag in het adres.</summary>
    public const string NonceParam = "n";

    /// <summary>
    /// HMAC-SHA256 van <paramref name="data"/> met de koppelcode, in kleine letters hexadecimaal.
    /// De extensie doet precies hetzelfde met WebCrypto; zie <c>teken()</c> in background.js.
    /// </summary>
    internal static string Teken(string code, string data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>
    /// Klopt de handtekening? Vergelijken gebeurt in vaste tijd, zodat er niets uit de duur van
    /// de vergelijking af te leiden valt.
    /// </summary>
    private bool Klopt(string nonce, string handtekening, string inhoud)
    {
        if (nonce.Length == 0 || handtekening.Length == 0) return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(handtekening),
            Encoding.UTF8.GetBytes(Teken(Token, nonce + "\n" + inhoud)));
    }

    /// <summary>
    /// Meldde er zich recent een extensie die nog de oude manier gebruikt (de code in het
    /// adres)? Dan hoort de app te zeggen dat ze herladen moet worden, en niet "verkeerde code".
    /// </summary>
    public bool OudeExtensieRecent => DateTime.Now - _laatsteOude < TimeSpan.FromSeconds(10);

    private DateTime _laatsteOude = DateTime.MinValue;
    private DateTime _oudeGelogd = DateTime.MinValue;

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

    // ---------- grenzen aan wat de brug aanneemt ----------
    //
    // Tot 22 september 2026 las de brug elke kop en elke body tot het einde, hoe groot ook,
    // en reserveerde ze meteen de grootte die een verzoek aankondigde: "Content-Length:
    // 1500000000" legde 1,5 GB vast nog voor er één byte binnen was, en een verbinding die
    // zweeg, bleef open tot Zentrix stopte. Elk programma op deze pc kan de brug aanspreken.

    /// <summary>
    /// Zo groot mag een levering van de extensie zijn. De grootste in het logboek tot dan was
    /// een zoekpagina van Vinted van 8 miljoen tekens; dit laat daar ruim zestien keer die
    /// maat boven. Een grotere wordt geweigerd, en dat staat in het logboek.
    /// </summary>
    public const int MaxBodyBytes = 128 * 1024 * 1024;

    /// <summary>De kop van de extensie is een paar honderd bytes: het adres met de code, en wat Chrome meestuurt.</summary>
    public const int MaxHeaderBytes = 64 * 1024;

    /// <summary>
    /// Zoveel verbindingen tegelijk. De extensie heeft er hoogstens een handvol open: om werk
    /// vragen, en drie opdrachten die hun pagina terugsturen.
    /// </summary>
    public const int MaxConnections = 32;

    /// <summary>
    /// Zolang mag het duren voor een verzoek volledig binnen is. De extensie stuurt het in één
    /// keer over de eigen pc, dus dat is een fractie van een seconde. Instelbaar voor de controles.
    /// </summary>
    internal static TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private int _open;

    /// <summary>Wat bij drukte of stilte telkens kan terugkomen, staat hoogstens eens per minuut in het logboek.</summary>
    private DateTime _weigeringGelogd = DateTime.MinValue;

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

                // Meteen tellen, bij het aannemen: pas binnen de taak tellen laat een stortvloed
                // eerst allemaal binnen.
                if (Interlocked.Increment(ref _open) > MaxConnections)
                {
                    Interlocked.Decrement(ref _open);
                    client.Dispose();
                    LogBeperkt($"brug: meer dan {MaxConnections} verbindingen tegelijk; een volgende wordt meteen gesloten");
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try { await HandleClientAsync(client); }
                    finally { Interlocked.Decrement(ref _open); }
                });
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

        // De token is gekoppeld aan die van de zoekopdracht, dus hij vuurt om TWEE redenen:
        // de negentig seconden zijn om, of jij drukte op de stopknop. Dat verschil moet eruit
        // komen. Vroeger werd het allebei een TimeoutException, en dan kreeg die site "gaf geen
        // antwoord binnen de tijd" aan haar tab, ging haar foutteller omhoog, en telde de
        // zoekopdracht een mislukking die er geen was.
        await using (timeout.Token.Register(() =>
        {
            if (ct.IsCancellationRequested) job.Completion.TrySetCanceled(ct);
            else job.Completion.TrySetException(new TimeoutException(
                "Geen antwoord van de browserextensie. Staat Chrome open en is de extensie geïnstalleerd?"));
        }))
        {
            try
            {
                var html = await job.Completion.Task;
                Log.Write($"brug: opdracht {job.Id[..8]} volledig binnen ({html.Length} tekens)");
                return html;
            }
            catch (OperationCanceledException)
            {
                Log.Write($"brug: opdracht {job.Id[..8]} afgebroken (gestopt)");
                throw;
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
            // Is het verzoek na ReadTimeout nog niet volledig binnen, dan wordt het afgebroken.
            using var tijd = new CancellationTokenSource(ReadTimeout);

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
                    var n = await stream.ReadAsync(buffer, tijd.Token);
                    if (n <= 0) return;

                    incoming.Write(buffer, 0, n);
                    headerEnd = FindHeaderEnd(incoming.GetBuffer(), (int)incoming.Length);

                    if (headerEnd < 0 && incoming.Length > MaxHeaderBytes)
                    {
                        Log.Write($"brug: verzoek geweigerd, de kop is langer dan {MaxHeaderBytes / 1024} kB");
                        await WriteAsync(stream, JsonSerializer.Serialize(new { error = "kop te lang" }), null,
                            "431 Request Header Fields Too Large");
                        return;
                    }
                }

                var received = incoming.GetBuffer();
                var headerText = Encoding.UTF8.GetString(received, 0, headerEnd);
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var parts = lines[0].Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0];
                var path = parts[1];

                // Een long: een aangekondigde maat boven de 2 GB moet geweigerd worden, niet als 0 gelezen.
                var contentLength = 0L;
                string? origin = null;
                var vanExtensie = false;
                var handtekening = "";
                var voorcontrole = "";

                foreach (var line in lines.Skip(1))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        long.TryParse(line[15..].Trim(), out contentLength);
                    else if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                        origin = line[7..].Trim();
                    else if (line.StartsWith(ExtensionHeader + ":", StringComparison.OrdinalIgnoreCase))
                        vanExtensie = true;
                    else if (line.StartsWith(SignatureHeader + ":", StringComparison.OrdinalIgnoreCase))
                        handtekening = line[(SignatureHeader.Length + 1)..].Trim();
                    else if (line.StartsWith(PreHeader + ":", StringComparison.OrdinalIgnoreCase))
                        voorcontrole = line[(PreHeader.Length + 1)..].Trim();
                }

                var nonce = GetParam(QueryVan(path), NonceParam);

                // Wie de koppelcode niet kent, krijgt nooit een body gelezen - hoe groot die ook
                // zegt te zijn. Dat kon vroeger omdat de code in het adres stond; nu kan het met
                // de voorcontrole, die enkel over de nonce gaat en dus al klaar is voor er één
                // byte van de body binnen is. De handtekening over de body zelf komt daarna.
                var codeKlopt = vanExtensie && nonce.Length > 0 &&
                                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                                    Encoding.UTF8.GetBytes(voorcontrole),
                                    Encoding.UTF8.GetBytes(Teken(Token, nonce)));

                if (codeKlopt && contentLength > MaxBodyBytes)
                {
                    Log.Write($"brug: verzoek van {contentLength / (1024 * 1024)} MB geweigerd, hoogstens {MaxBodyBytes / (1024 * 1024)} MB");
                    await WriteAsync(stream, JsonSerializer.Serialize(new { error = "te groot" }), origin, "413 Content Too Large");
                    return;
                }

                var body = ReadOnlyMemory<byte>.Empty;
                using var bodyBytes = new MemoryStream(codeKlopt ? (int)Math.Max(contentLength, 0) : 0);

                if (codeKlopt)
                {
                    // Wat er na de lege regel al binnen is, hoort bij de body. Meteen op
                    // de juiste maat: een pagina van leboncoin is ruim een miljoen tekens,
                    // en een geheugenstroom die telkens verdubbelt kopieert die meermaals.
                    var bodyStart = headerEnd + 4;
                    bodyBytes.Write(received, bodyStart, (int)incoming.Length - bodyStart);

                    while (bodyBytes.Length < contentLength)
                    {
                        var n = await stream.ReadAsync(buffer, tijd.Token);
                        if (n <= 0) break;
                        bodyBytes.Write(buffer, 0, n);
                    }

                    // De JSON rechtstreeks uit de bytes lezen, zonder er eerst een tekst
                    // van te maken: dat scheelt een volledige kopie van de pagina.
                    body = bodyBytes.GetBuffer().AsMemory(0, (int)bodyBytes.Length);
                }

                var response = Handle(method, path, body, vanExtensie, nonce, handtekening);

                // Het antwoord wordt óók getekend. Zo weet de extensie dat zij met de échte app
                // praat en niet met een programma dat de poort eerst bezette - dat is de helft
                // die haar beschermt, want zij voert uit wat hieruit komt.
                await WriteAsync(stream, response, origin,
                    handtekening: nonce.Length > 0 ? Teken(Token, nonce + "\n" + response) : null);
            }
            catch (OperationCanceledException) when (tijd.IsCancellationRequested)
            {
                LogBeperkt($"brug: verzoek afgebroken, na {ReadTimeout.TotalSeconds:F0} s nog niet volledig binnen");
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
    private string Handle(string method, string path, ReadOnlyMemory<byte> body, bool vanExtensie,
                          string nonce, string handtekening)
    {
        // De extensie stuurt eerst een controlevraag; die moet zonder inhoud slagen.
        if (method == "OPTIONS") return "";

        var route = path.Split('?')[0];

        // Een extensie van voor 1 oktober 2026 stuurt de code nog in het adres. Die werkt niet
        // meer, en dat hoort de app te zeggen - "verkeerde koppelcode" zou je naar het verkeerde
        // scherm sturen, want aan de code zelf is niets mis.
        if (vanExtensie && handtekening.Length == 0 && GetParam(QueryVan(path), "token").Length > 0)
        {
            _laatsteOude = DateTime.Now;

            if (DateTime.Now - _oudeGelogd > TimeSpan.FromMinutes(1))
            {
                _oudeGelogd = DateTime.Now;
                Log.Write("brug: een oude versie van de extensie meldt zich - herlaad ze in chrome://extensions");
            }

            return JsonSerializer.Serialize(new { error = "verouderde extensie" });
        }

        if (!Klopt(nonce, handtekening, Encoding.UTF8.GetString(body.Span)))
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

    /// <summary>Het stuk van het adres na het vraagteken, of niets.</summary>
    private static string QueryVan(string path) =>
        path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "";

    private void LogBeperkt(string tekst)
    {
        if (DateTime.Now - _weigeringGelogd < TimeSpan.FromMinutes(1)) return;

        _weigeringGelogd = DateTime.Now;
        Log.Write(tekst);
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
    ///
    /// Een verkeerde koppelcode krijgt gewoon 200, met de fout in de JSON: zo leest de
    /// extensie die. Enkel een verzoek dat te groot is, krijgt een foutcode van HTTP.
    /// </summary>
    private static async Task WriteAsync(NetworkStream stream, string json, string? origin,
                                        string status = "200 OK", string? handtekening = null)
    {
        var payload = Encoding.UTF8.GetBytes(json);

        var cors = origin is not null && origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
            ? $"Access-Control-Allow-Origin: {origin}\r\n" +
              $"Access-Control-Allow-Headers: Content-Type, {ExtensionHeader}, {SignatureHeader}, {PreHeader}\r\n" +
              $"Access-Control-Expose-Headers: {SignatureHeader}\r\n" +
              "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
              "Access-Control-Max-Age: 600\r\n" +
              "Vary: Origin\r\n"
            : "";

        var header =
            $"HTTP/1.1 {status}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            (handtekening is null ? "" : $"{SignatureHeader}: {handtekening}\r\n") +
            cors +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
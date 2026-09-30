using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Zentrix.Models;

namespace Zentrix.Checks;

/// <summary>
/// Een lokale website voor de controles. Per paginanummer (<c>page=N</c> in de URL) geeft
/// ze een vast aantal zoekertjes; <see cref="Zwijgt"/> neemt de verbinding aan en
/// antwoordt nooit, zoals een site die blijft hangen.
/// </summary>
public sealed class Proefsite : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public int Poort { get; }

    /// <summary>Hoeveel zoekertjes elke pagina heeft; ontbreekt een pagina, dan is ze leeg.</summary>
    public Dictionary<int, int> PerPagina { get; } = new() { [1] = 5 };

    /// <summary>Neemt de verbinding aan en antwoordt nooit.</summary>
    public bool Zwijgt { get; init; }

    /// <summary>Hoeveel zoekertjes één stap van <c>offset=N</c> overslaat: offset 30 is pagina 2.</summary>
    public int OffsetPerPagina { get; set; } = 30;

    /// <summary>Wat er in plaats van de gewone pagina komt, bv. een controlepagina.</summary>
    public string? VasteInhoud { get; set; }

    /// <summary>
    /// Een antwoord dat afhangt van het gevraagde adres (pad en querystring, al ontsleuteld),
    /// bv. andere zoekertjes per zoekterm. Wint van de gewone pagina, niet van VasteInhoud.
    /// </summary>
    public Func<string, string>? Antwoord { get; set; }

    /// <summary>
    /// Een andere status dan 200 voor een bepaald pad. Nodig om een zoekertje na te bootsen
    /// dat van de site verdwenen is: 2dehands antwoordt daarop met 410, en dat is het enige
    /// harde bewijs dat een site kan geven (zie <see cref="Zentrix.Services.FavoriteWatch"/>).
    /// </summary>
    public Dictionary<string, int> Status { get; } = new();

    /// <summary>Welke pagina's er gevraagd zijn, in volgorde.</summary>
    public List<int> Gevraagd { get; } = new();

    /// <summary>
    /// De kopregels van elk verzoek, ruw. Nodig om na te gaan dat de eigen kopregels van een
    /// site echt vertrekken: Vinted antwoordt in het Frans zonder zijn taalcookie, en dat is
    /// aan het antwoord wél te zien maar aan de app niet.
    /// </summary>
    public List<string> Koppen { get; } = new();

    /// <summary>
    /// Stuurt het <b>eerste</b> verzoek door naar een ander domein, zoals de toestemmingsmuur
    /// van DPG bij Tweakers doet. Het tweede verzoek krijgt gewoon de pagina.
    ///
    /// De omleiding gaat naar <c>localhost</c> in plaats van <c>127.0.0.1</c>: dezelfde
    /// proefsite, maar voor de app een ánder domein - en dat is precies waar de regel op kijkt.
    /// </summary>
    public bool MuurEenKeer { get; set; }

    /// <summary>
    /// Echte foto's, op hun pad ("/klein.jpg"). Nodig waar de app een foto <i>nameet</i> in
    /// plaats van ze enkel door te geven: het formaat is niet aan een adres af te lezen.
    /// Wint van alles, want een foto is geen HTML.
    /// </summary>
    public Dictionary<string, byte[]> Fotos { get; } = new();

    public Proefsite()
    {
        _listener.Start();
        Poort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(LusAsync);
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
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var n = await stream.ReadAsync(buffer, _stop.Token);
                var verzoek = Encoding.ASCII.GetString(buffer, 0, n);
                var regel = verzoek.Split("\r\n")[0];

                lock (Koppen) Koppen.Add(verzoek);

                if (MuurEenKeer)
                {
                    MuurEenKeer = false;

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 302 Found\r\n" +
                        $"Location: http://localhost:{Poort}/toestemming\r\n" +
                        "Content-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }

                // De muur zelf: géén resultaten. Anders zou de omleiding toevallig een
                // resultatenpagina geven en zou de controle ook slagen zonder de regel
                // waar ze over gaat.
                if (regel.Contains("/toestemming"))
                {
                    var muur = Encoding.UTF8.GetBytes(
                        "<html><body><h1>Wil je ons toestemming geven?</h1></body></html>");

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                        $"Content-Length: {muur.Length}\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(muur);
                    return;
                }

                if (Zwijgt)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                    return;
                }

                // Een paginanummer (page=3), of vanaf het hoeveelste zoekertje (offset=60), zoals de
                // API van 2dehands: met OffsetPerPagina 30 is offset 60 de derde pagina.
                var m = Regex.Match(regel, @"[?&]page=(\d+)");
                var o = Regex.Match(regel, @"[?&]offset=(\d+)");
                var pagina = m.Success ? int.Parse(m.Groups[1].Value)
                    : o.Success ? int.Parse(o.Groups[1].Value) / OffsetPerPagina + 1
                    : 1;
                lock (Gevraagd) Gevraagd.Add(pagina);

                var adres = Uri.UnescapeDataString(regel.Split(' ') is { Length: > 1 } delen ? delen[1] : "");

                var isFoto = Fotos.TryGetValue(adres.Split('?')[0], out var foto);
                var body = isFoto ? foto! : Encoding.UTF8.GetBytes(
                    VasteInhoud ?? Antwoord?.Invoke(adres) ?? Pagina(pagina));

                var code = Status.GetValueOrDefault(adres.Split('?')[0], 200);

                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {code} {Reden(code)}\r\n" +
                    (isFoto ? "Content-Type: image/jpeg\r\n" : "Content-Type: text/html; charset=utf-8\r\n") +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                await stream.WriteAsync(body);
            }
            catch
            {
                // Verbinding weg of proefsite gestopt: niets aan te doen.
            }
        }
    }

    /// <summary>De reden bij een statuscode; HttpClient kijkt enkel naar het getal.</summary>
    private static string Reden(int code) => code switch
    {
        200 => "OK",
        404 => "Not Found",
        410 => "Gone",
        _ => "Status"
    };

    private string Pagina(int pagina)
    {
        var html = new StringBuilder("<html><body><h1>Resultaten</h1>");
        for (var i = 0; i < PerPagina.GetValueOrDefault(pagina); i++)
            html.Append($"<div class='item'><a class='t' href='/x/{pagina}-{i}'>Zoekertje {pagina}-{i}</a>" +
                        $"<span class='p'>€ {i + 1}</span></div>");

        // Een echte resultatenpagina is groot; zonder opvulling lijkt een lege pagina op
        // een controlepagina van een robotbeveiliging.
        html.Append("<footer>").Append('x', 70_000).Append("</footer></body></html>");
        return html.ToString();
    }

    /// <summary>Een sitebeschrijving die deze proefsite uitleest.</summary>
    public SiteDefinition Site(string naam, bool paginering = false) => new()
    {
        Name = naam,
        BaseUrl = $"http://127.0.0.1:{Poort}",
        SearchUrlTemplate = $"http://127.0.0.1:{Poort}/s?q={{query}}",
        PageTemplate = paginering ? "&page={page}" : "",
        ItemSelector = "div.item",
        TitleSelector = "a.t",
        UrlSelector = "a.t@href",
        PriceSelector = "span.p"
    };

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

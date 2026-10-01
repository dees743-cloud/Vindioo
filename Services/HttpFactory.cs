using System.Net;
using System.Net.Http;

namespace Zentrix.Services;

/// <summary>
/// Hoe de app zich aan een site voorstelt, en hoe een <see cref="HttpClient"/> hier gemaakt
/// wordt.
///
/// Dit stond tot 1 oktober 2026 op <b>vijf</b> plaatsen, en dat is precies hoe zulke dingen uit
/// elkaar groeien: vier ervan zeiden Chrome 128 en de vijfde Chrome 140. Niemand had dat
/// gekozen - het was er gewoon ingeslopen. Gevonden in een codeanalyse van 30 september 2026.
/// </summary>
public static class HttpFactory
{
    /// <summary>
    /// Waarmee de app zich voorstelt. Een site die een onbekende afzender ziet, geeft vaker een
    /// foutcode dan een pagina - zeker voor een foto van een CDN.
    /// </summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    /// <summary>
    /// Een client zoals de app ze overal gebruikt: gecomprimeerd, met koekjespot, en met die
    /// ene User-Agent.
    ///
    /// <para><b>Gecomprimeerd</b> (<see cref="DecompressionMethods.All"/>): zonder dit stuurde de
    /// app geen <c>Accept-Encoding</c> mee, kwamen pagina's ongecomprimeerd binnen (HTML en JSON
    /// zijn gezipt doorgaans vijf tot tien keer kleiner), en dat is precies wat een robot
    /// verraadt - een Chrome die geen compressie aankan bestaat niet.</para>
    ///
    /// <para><b>Met koekjespot</b>: <see cref="SocketsHttpHandler"/> houdt zijn cookies vanzelf
    /// bij, en daar leunt de app op. Een toestemmingsmuur op een ander domein zet een
    /// sessiecookie en laat het volgende verzoek gewoon door; dat was het verschil tussen
    /// <c>curl</c> (altijd de muur) en een browser (meteen de pagina). Zie "Een toestemmingsmuur
    /// op een ander domein" in <c>docs/brug.md</c>.</para>
    /// </summary>
    /// <param name="timeout">
    /// Hoelang er op een antwoord gewacht wordt. Dat verschilt wél per gebruik: een zoekpagina
    /// krijgt dertig seconden, iets wat enkel een extraatje is bij resultaten die al op het
    /// scherm staan tien, en de sitemap van Tweakers (11 MB) negentig.
    /// </param>
    public static HttpClient MaakClient(TimeSpan timeout)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = timeout
        };

        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return client;
    }
}

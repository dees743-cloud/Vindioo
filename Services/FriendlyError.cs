using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace Zentrix.Services;

/// <summary>
/// Zet een fout om in een zin die de gebruiker iets zegt. Wat hier uitkomt, staat op de tab
/// van een site, in de lijst met zoekopdrachten en in een melding op je telefoon - achter
/// de naam van de site, dus als het vervolg van een zin: "Catawiki: weigert de app ...".
///
/// Waarom dit er is: .NET en Playwright praten Engels en technisch. "Response status code
/// does not indicate success: 403 (Forbidden)" of "'&lt;' is an invalid start of a value"
/// kwam tot september 2026 ongewijzigd in een Telegram-bericht terecht, terwijl dat laatste
/// gewoon betekent dat de site een controlepagina toonde in plaats van resultaten.
///
/// Wat al Nederlands is (de meldingen van de brug, van de zoekmotor zelf), blijft staan.
/// </summary>
public static class FriendlyError
{
    public static string Describe(Exception ex)
    {
        // Wat er in een AggregateException of TargetInvocationException zit, telt.
        while (ex is AggregateException { InnerException: { } binnen }) ex = binnen;

        switch (ex)
        {
            case HttpRequestException { StatusCode: { } code }:
                return Statuscode(code);

            case HttpRequestException http when http.InnerException is SocketException ||
                                               http.HttpRequestError is HttpRequestError.NameResolutionError
                                                   or HttpRequestError.ConnectionError:
                return "is niet bereikbaar. Staat je internet aan?";

            // Een time-out van HttpClient is een TaskCanceledException. Een echte annulering
            // komt hier niet: die gooit de zoekopdracht zelf door.
            // Playwright gebruikt de gewone TimeoutException ("Timeout 45000ms exceeded").
            case OperationCanceledException:
            case TimeoutException when LijktEngels(ex.Message):
                return "gaf geen antwoord binnen de tijd.";

            case JsonException:
                return "gaf iets anders terug dan resultaten. Vaak is dat een controlepagina tegen robots.";

            case Microsoft.Playwright.PlaywrightException pw when pw.Message.Contains("net::ERR_NAME_NOT_RESOLVED") ||
                                                              pw.Message.Contains("net::ERR_INTERNET_DISCONNECTED") ||
                                                              pw.Message.Contains("net::ERR_CONNECTION"):
                return "is niet bereikbaar. Staat je internet aan?";

            case Microsoft.Playwright.PlaywrightException pw when pw.Message.Contains("Executable doesn't exist") ||
                                                              pw.Message.Contains("Chromium distribution 'chrome' is not found"):
                return "heeft Google Chrome nodig, en die is niet gevonden.";

            case Microsoft.Playwright.PlaywrightException pw when pw.Message.Contains("user data directory is already in use") ||
                                                              pw.Message.Contains("ProcessSingleton"):
                return "kon de browser van Zentrix niet starten: het profiel is nog in gebruik. Probeer het zo meteen opnieuw.";

            case Microsoft.Playwright.PlaywrightException:
                return "de browser van Zentrix gaf een fout. Details staan in het logboek.";

            case SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }:
                return $"de brug kan niet starten: poort {BridgeServer.Port} is in gebruik door een ander programma.";

            default:
                return ex.Message;
        }
    }

    private static string Statuscode(HttpStatusCode code) => (int)code switch
    {
        401 => "vraagt om aan te melden (401).",
        403 => "weigert de app (403). Probeer in Sites beheren 'Browser gebruiken', of de brug.",
        404 => "kent deze pagina niet (404).",
        429 => "vraagt om trager te zoeken (429: te veel verzoeken). Probeer het later opnieuw.",
        >= 500 => $"heeft zelf een probleem ({(int)code}). Probeer het later opnieuw.",
        _ => $"antwoordde met foutcode {(int)code}."
    };

    /// <summary>De brug geeft haar eigen Nederlandse time-out; die laten we staan.</summary>
    private static bool LijktEngels(string tekst) =>
        tekst.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
        tekst.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        tekst.Contains("operation has", StringComparison.OrdinalIgnoreCase);
}

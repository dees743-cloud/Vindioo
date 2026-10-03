using System.Net;
using System.Net.Sockets;
using Vindioo.Models;

namespace Vindioo.Services;

/// <summary>
/// Kijkt na waar een sitebestand de app naartoe stuurt.
///
/// Een sitebestand is bedoeld om te <b>delen</b>, en dat is precies waar het wringt: wie er een
/// krijgt, voert hem uit met zijn eigen browser en zijn eigen cookies. De brug opent de zoek-URL
/// in jouw Chrome, en bij een API-opdracht doet ze daar een <c>fetch</c> met
/// <c>credentials: "include"</c>. Een bestand met een verzonnen zoek-URL laat jouw aangemelde
/// browser dus verzoeken doen naar eender welke site - of naar een adres op je eigen netwerk,
/// zoals de beheerpagina van je router. Gevonden in een codeanalyse van 30 september 2026.
///
/// Daarom wordt er op <b>twee</b> plaatsen gekeken, want één slot vergeet je:
/// <list type="bullet">
///   <item>hier, bij het importeren: een bestand met een onveilig adres komt er niet in;</item>
///   <item>in <c>extension/background.js</c>: de extensie weigert zelf ook een opdracht naar een
///         ander schema dan https of naar een privé-adres.</item>
/// </list>
///
/// <b>Niet</b> bij het zoeken zelf. De controles werken met een proefsite op 127.0.0.1, en wie
/// zijn eigen site toevoegt mag doen wat hij wil - het gaat erom wat er van <b>buiten</b>
/// binnenkomt.
/// </summary>
public static class SiteUrlCheck
{
    /// <summary>
    /// Elk adres dat een sitebestand gebruikt: de zoek-URL, het basisadres en een eventuele
    /// API voor einddatums. Daarmee kan een scherm tonen waar een gedeeld bestand heen gaat.
    /// </summary>
    public static List<string> Adressen(SiteDefinition def)
    {
        var uit = new List<string>();

        void Voeg(string? adres)
        {
            if (!string.IsNullOrWhiteSpace(adres)) uit.Add(adres);
        }

        Voeg(def.SearchUrlTemplate);
        Voeg(def.BaseUrl);
        Voeg(def.EndTimeApi?.UrlTemplate);

        return uit;
    }

    /// <summary>De hosts waar een sitebestand naartoe gaat, ontdubbeld en op volgorde.</summary>
    public static List<string> Hosts(SiteDefinition def) =>
        Adressen(def)
            .Select(a => Uri.TryCreate(VervangPlaatshouders(a), UriKind.Absolute, out var u) ? u.Host : "")
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Mag dit sitebestand erin? Geeft de reden terug wanneer het antwoord nee is, zodat de
    /// gebruiker niet enkel een bestandsnaam ziet.
    /// </summary>
    public static bool IsVeilig(SiteDefinition def, out string reden)
    {
        foreach (var adres in Adressen(def))
        {
            if (!IsVeiligAdres(adres, out reden)) return false;
        }

        reden = "";
        return true;
    }

    /// <summary>Hetzelfde voor één adres.</summary>
    public static bool IsVeiligAdres(string adres, out string reden)
    {
        // De plaatshouders zijn op dit moment nog niet ingevuld; zet er iets onschuldigs in,
        // anders is geen enkele zoek-URL een geldige Uri.
        var proef = VervangPlaatshouders(adres);

        if (!Uri.TryCreate(proef, UriKind.Absolute, out var uri))
        {
            reden = $"'{Kort(adres)}' is geen volledig webadres";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            // http stuurt je zoekterm leesbaar over de lijn, en file:// of ftp:// horen hier
            // al helemaal niet thuis.
            reden = $"'{uri.Scheme}' in plaats van https ({Kort(adres)})";
            return false;
        }

        if (IsPriveHost(uri.Host))
        {
            reden = $"'{uri.Host}' is een adres op je eigen netwerk of pc";
            return false;
        }

        reden = "";
        return true;
    }

    /// <summary>
    /// Een host die binnen je eigen netwerk of op deze pc ligt. Dat is het gevaarlijke geval:
    /// jouw browser kan daar wél bij, en wat erachter zit vraagt vaak geen aanmelding.
    /// </summary>
    private static bool IsPriveHost(string host)
    {
        if (host.Length == 0) return true;

        // Namen zonder punt (bv. "router") en de gebruikelijke lokale achtervoegsels.
        if (!host.Contains('.')) return true;

        foreach (var staart in new[] { ".local", ".localhost", ".internal", ".home", ".lan" })
            if (host.EndsWith(staart, StringComparison.OrdinalIgnoreCase)) return true;

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();

            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254)                 // link-local
                || b[0] == 127                                  // loopback
                || b[0] == 0;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;

        return false;
    }

    /// <summary>
    /// De plaatshouders uit een zoek-URL weghalen, zodat er een geldig adres overblijft om na
    /// te kijken. <c>{query}</c> mag in het pad of in de querystring staan, en bij de URL-stijl
    /// Base64Json staat er een heel JSON-blok in.
    /// </summary>
    private static string VervangPlaatshouders(string adres) =>
        adres.Replace("{query}", "x")
             .Replace("{page}", "1")
             .Replace("{offset}", "0")
             .Replace("{filters}", "")
             .Replace("{value}", "x")
             .Replace("{id}", "1");

    private static string Kort(string tekst) => tekst.Length <= 60 ? tekst : tekst[..57] + "...";
}

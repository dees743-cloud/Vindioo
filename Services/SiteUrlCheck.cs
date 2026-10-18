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

        // En de GEBOUWDE url, niet alleen het sjabloon. Een vast fragment of een PageTemplate
        // dat met een @ begint, maakt alles ervoor tot gebruikersnaam en de host erna de echte
        // bestemming. Nagemeten op 18 oktober 2026: sjabloon "https://www.2dehands.be" komt
        // gewoon door de controle, maar met het fragment "@127.0.0.1:8731/geheim" wordt de
        // gebouwde url "https://www.2dehands.be@127.0.0.1:8731/geheim" - echte host 127.0.0.1,
        // en dat is de poort van de brug zelf.
        Voeg(Gebouwd(def));

        return uit;
    }

    /// <summary>
    /// De zoek-URL zoals hij er bij een echte zoekopdracht uitziet: met elk eigen filter
    /// ingevuld en op pagina 2, zodat ook <c>PageTemplate</c> meegenomen wordt.
    ///
    /// <para>Null wanneer hij niet te bouwen valt; dan blijft het sjabloon over, en dat wordt
    /// hierboven al nagekeken.</para>
    ///
    /// <para>Waarom niet gewoon "een fragment moet met ? of &amp; beginnen", zoals een
    /// leesronde voorstelde: dat klopt niet voor elke site. Bij AlleVeilingen zitten de
    /// fragmenten in een JSON-blok (<c>"r":["BEWV"]</c>) en beginnen ze met een
    /// aanhalingsteken. Die regel zou een werkend sitebestand weigeren; deze niet.</para>
    /// </summary>
    private static string? Gebouwd(SiteDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.SearchUrlTemplate)) return null;

        try
        {
            var filters = new SearchFilters();

            foreach (var filter in def.CustomFilters)
                filters.Custom[filter.Key] = filter.Options.Count > 0 ? filter.Options[0].Value : "1";

            return Sources.SearchUrlBuilder.Build(def, "x", filters, 2);
        }
        catch
        {
            // Een sjabloon dat niet te bouwen valt, is geen reden om de import te laten vallen:
            // het sjabloon zelf is al nagekeken, en de motor klaagt er later zelf over.
            return null;
        }
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

        if (!BasisHoortBijZoekhost(def, out reden)) return false;

        reden = "";
        return true;
    }

    /// <summary>
    /// <c>BaseUrl</c> moet naar dezelfde host wijzen als de zoek-URL.
    ///
    /// <para>Waarom: dat basisadres maakt van een relatieve link een volledig adres, en zo'n
    /// adres wordt daarna opgehaald - bij een brugsite in jouw eigen Chrome, met jouw cookies.
    /// Een gedeeld bestand dat zoekt op een gewone site maar zijn basisadres elders legt, laat
    /// de app dus pagina's van die andere plek ophalen en terugsturen. Allebei de adressen
    /// apart zijn https en geen privé-adres, dus de controle hierboven ziet daar niets van.</para>
    ///
    /// <para>Nagemeten op 18 oktober 2026: bij alle vijftien sitebestanden was de host al
    /// dezelfde, dus deze regel weigert niets wat vandaag werkt.</para>
    /// </summary>
    private static bool BasisHoortBijZoekhost(SiteDefinition def, out string reden)
    {
        reden = "";

        if (string.IsNullOrWhiteSpace(def.BaseUrl) || string.IsNullOrWhiteSpace(def.SearchUrlTemplate))
            return true;

        if (!Uri.TryCreate(VervangPlaatshouders(def.BaseUrl), UriKind.Absolute, out var basis) ||
            !Uri.TryCreate(VervangPlaatshouders(def.SearchUrlTemplate), UriKind.Absolute, out var zoek))
            return true;   // onbruikbaar adres: daar klaagt IsVeiligAdres al over

        if (string.Equals(basis.Host, zoek.Host, StringComparison.OrdinalIgnoreCase)) return true;

        reden = $"het basisadres wijst naar '{basis.Host}' en de zoek-URL naar '{zoek.Host}'";
        return false;
    }

    /// <summary>
    /// Mag de app dit adres ophalen voor deze site?
    ///
    /// <para>De adressen die de app najaagt - de link van een zoekertje, een kandidaat die uit
    /// een zoekpagina gevist is - komen uit de <b>inhoud van een pagina</b>. Die inhoud staat
    /// niet onder onze controle, en bij een brugsite wordt zo'n adres in jouw eigen Chrome
    /// geopend, met jouw cookies. Tot 18 oktober 2026 werd er enkel gekeken of het http of
    /// https was.</para>
    ///
    /// <para>De regel is daarom niet "https en geen privé-adres" maar <b>dezelfde host als de
    /// site zelf</b>. Dat is scherper - een zoekertje van 2dehands hoort op 2dehands te staan -
    /// en het blijft werken met een proefsite op 127.0.0.1, die de controles gebruiken.</para>
    /// </summary>
    public static bool HoortBijSite(string? adres, SiteDefinition def)
    {
        if (string.IsNullOrWhiteSpace(adres)) return false;

        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var eigen = def.BaseUrl is { Length: > 0 } b ? b : def.SearchUrlTemplate;

        // Zonder eigen host valt er niets te vergelijken; dan blijft het bij het schema.
        if (!Uri.TryCreate(VervangPlaatshouders(eigen), UriKind.Absolute, out var thuis)) return true;

        return string.Equals(uri.Host, thuis.Host, StringComparison.OrdinalIgnoreCase);
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
    /// <summary>
    /// Wijst deze host naar je eigen pc of je eigen netwerk? Openbaar omdat ook de
    /// fotoophaler het moet weten: een foto mag op een CDN staan, maar niet bij jou thuis.
    /// </summary>
    public static bool IsPriveAdres(string host) => IsPriveHost(host);

    private static bool IsPriveHost(string host)
    {
        // EEN PUNT OP HET EIND IS DEZELFDE NAAM. "localhost." en "127.0.0.1." zijn in DNS
        // volwaardige adressen - de punt zegt alleen dat de naam absoluut is - maar hier
        // haalden ze de hele controle onderuit: met die punt vindt Contains('.') een punt,
        // past geen enkel achtervoegsel meer, en lukt IPAddress.TryParse niet. Gemeten op
        // 18 oktober 2026: "127.0.0.1.", "localhost." en "10.0.0.5." kwamen er alle drie
        // gewoon door. Daarom eerst afknippen, en pas daarna kijken.
        host = host.TrimEnd('.');

        if (host.Length == 0) return true;

        // Namen zonder punt (bv. "router") en de gebruikelijke lokale achtervoegsels.
        if (!host.Contains('.')) return true;

        foreach (var staart in new[] { ".local", ".localhost", ".internal", ".home", ".lan" })
            if (host.EndsWith(staart, StringComparison.OrdinalIgnoreCase)) return true;

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;

        // EEN IPv4-ADRES IN IPv6-JASJE. "::ffff:10.0.0.5" is gewoon 10.0.0.5, maar de IPv6-tak
        // hieronder kent enkel link-local, site-local en unique-local en ziet daar niets in.
        // Loopback werd wél al gevangen - IPAddress.IsLoopback kent ::ffff:127.0.0.1 - dus het
        // gat zat enkel bij de prive-reeksen. Uitpakken en als IPv4 behandelen.
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

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

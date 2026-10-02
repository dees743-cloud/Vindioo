using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>Wat een favoriet vandaag doet.</summary>
public enum FavoriteState
{
    /// <summary>Niet na te gaan. Dat is een eerlijker antwoord dan gokken.</summary>
    Onbekend,

    /// <summary>De advertentie staat er nog.</summary>
    TeKoop,

    /// <summary>Een veiling die voorbij is: er valt niet meer op te bieden.</summary>
    Afgelopen,

    /// <summary>De pagina bestaat niet meer.</summary>
    Weg
}

/// <summary>Wat het nakijken van één favoriet opleverde.</summary>
/// <param name="Staat">Weg, afgelopen, nog te koop, of niet na te gaan.</param>
/// <param name="PrijsNu">De prijs van vandaag, of null wanneer die er niet uit te lezen was.</param>
/// <param name="Einde">Wanneer de veiling sloot, bij <see cref="FavoriteState.Afgelopen"/>.</param>
/// <param name="Uitleg">Waarom het niet na te gaan was. Leeg bij een gewoon antwoord.</param>
public record FavoriteStatus(FavoriteState Staat, decimal? PrijsNu = null,
                             DateTime? Einde = null, string Uitleg = "");

/// <summary>
/// Kijkt na of een bewaarde favoriet nog te koop staat, en tegen welke prijs.
///
/// Een favoriet is een <b>kopie</b>: hij blijft in de app staan ook als de site hem
/// weghaalt, en de prijs erop is die van de dag dat je hem bewaarde. Precies daar zit de
/// vraag: staat dit er nog, en is het intussen duurder geworden? Bij een kavel van Catawiki
/// op 2dehands ging het bod tussen het bewaren en het terugkijken van € 5 naar € 24.
///
/// Het kost <b>één verzoek per favoriet</b>, langs dezelfde drie wegen als het
/// detailvenster (rechtstreeks, de brug, of de aangemelde browser). Daarom gebeurt het
/// enkel wanneer je erom vraagt, en niet bij het openen van het tabblad.
///
/// Er is geen nieuw veld in de sitebestanden voor nodig. Dat is gemeten op 30 september
/// 2026, op de drie echte favorieten en op verse advertenties:
///
/// <list type="table">
///   <item><term>weg</term><description>
///     2dehands antwoordt met <b>HTTP 410</b> en vier tekens. Ondubbelzinnig.</description></item>
///   <item><term>afgelopen</term><description>
///     AlleVeilingen geeft gewoon een pagina (HTTP 200), maar de einddatum staat er nog -
///     "Einde op 14/09/2026 19:30" - en die wijst het sitebestand al aan met
///     <see cref="SiteDefinition.DetailEndDateSelector"/>. Staat er geen bedrag meer bij:
///     op een gesloten kavel is het bod van de pagina verdwenen.</description></item>
///   <item><term>prijs nu</term><description>
///     2dehands en Marktplaats zetten hem in het <c>ld+json</c>-blok van de advertentie
///     (<c>Product.offers.price</c>), en dat klopte op vier verse advertenties met wat de
///     zoekpagina zei: 0, 69, 1590 en 600. Dat is een webstandaard (schema.org), dus het
///     werkt op elke site die hem gebruikt - AlleVeilingen heeft er geen.</description></item>
/// </list>
///
/// Elk antwoord "nog te koop" heeft bewijs nodig: een prijs, of foto's van de
/// advertentiepagina. Komt er niets van de pagina, dan is het "niet na te gaan" en niet
/// "staat er nog" - anders meldt de app dat iets te koop staat terwijl niemand dat weet.
/// </summary>
public static class FavoriteWatch
{
    /// <summary>
    /// Kijkt één favoriet na. Gooit enkel door bij een echte annulering; al de rest komt
    /// als <see cref="FavoriteState.Onbekend"/> met de reden erbij.
    /// </summary>
    public static async Task<FavoriteStatus> CheckAsync(Listing favoriet,
                                                        IReadOnlyList<SiteDefinition> sites,
                                                        CancellationToken ct = default)
    {
        var def = sites.FirstOrDefault(s =>
            string.Equals(s.Name, favoriet.Source, StringComparison.OrdinalIgnoreCase));

        if (def is null)
            return new FavoriteStatus(FavoriteState.Onbekend,
                Uitleg: $"{favoriet.Source} staat niet meer in Sites beheren.");

        if (!IsWebadres(favoriet.Url))
            return new FavoriteStatus(FavoriteState.Onbekend, Uitleg: "Dit zoekertje heeft geen webadres.");

        // Bij een brugsite heeft het geen zin Chrome te laten starten voor een controle die
        // je zelf vroeg; het detailvenster doet hetzelfde.
        if (def.UseBridge && !BridgeServer.Instance.ExtensionAlive)
            return new FavoriteStatus(FavoriteState.Onbekend,
                Uitleg: $"{def.Name} loopt via de brug, en die meldde zich niet. Staat Chrome open?");

        string html;

        try
        {
            html = await DetailFetcher.HaalPaginaAsync(def, favoriet.Url, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException fout) when (fout.StatusCode is HttpStatusCode.NotFound
                                                                or HttpStatusCode.Gone)
        {
            // 404 en 410 zijn het enige harde bewijs dat een site kan geven. 403 hoort hier
            // NIET bij: dat is "de site weigert de app", en dan weet je niets over het
            // zoekertje zelf.
            Log.Write($"favoriet '{Kort(favoriet.Title)}' is weg ({(int)fout.StatusCode!} van {def.Name})");
            return new FavoriteStatus(FavoriteState.Weg);
        }
        catch (Exception fout)
        {
            Log.Write($"favoriet '{Kort(favoriet.Title)}' niet na te gaan - {fout.Message}");
            return new FavoriteStatus(FavoriteState.Onbekend, Uitleg: FriendlyError.Describe(fout));
        }

        // Een veiling die voorbij is, is niet weg maar afgelopen - en dat is een ander
        // antwoord: de pagina staat er nog, je kan er alleen niet meer op bieden.
        var einde = await EindeAsync(def, html, ct);
        if (einde is not null && einde < DateTime.Now)
            return new FavoriteStatus(FavoriteState.Afgelopen, Einde: einde);

        // Het einde gaat ook mee wanneer de veiling nog LOOPT. Tot 2 oktober 2026 werd het hier
        // weggegooid zodra bleek dat het in de toekomst lag, terwijl dat juist het nuttige geval
        // is: daarmee kan AuctionWatch waarschuwen voor ze sluit. Het kost niets extra - de
        // pagina is toch al gelezen.
        var prijs = PrijsUitPagina(html);
        if (prijs is > 0)
            return new FavoriteStatus(FavoriteState.TeKoop, prijs, einde);

        // Geen prijs uit de pagina te halen. Dan telt of de advertentie er nog ís: geeft
        // het sitebestand foto's op en komen die eruit, dan staat ze er nog.
        if (!string.IsNullOrWhiteSpace(def.DetailImagesSelector))
        {
            var fotos = await GenericSource.ReadFieldsAsync(html, def.DetailImagesSelector, ct);
            if (fotos.Count > 0) return new FavoriteStatus(FavoriteState.TeKoop, Einde: einde);
        }

        return new FavoriteStatus(FavoriteState.Onbekend,
            Uitleg: $"De pagina kwam binnen, maar er viel niets uit te lezen bij {def.Name}.");
    }

    /// <summary>
    /// De regel die op de kaart komt. De bewaarde prijs gaat mee, want het verschil is
    /// juist wat je wil zien: "Nu € 24 - was € 5".
    /// </summary>
    public static string Tekst(FavoriteStatus status, decimal? bewaard) => status.Staat switch
    {
        FavoriteState.Weg => "Weg van de site",

        FavoriteState.Afgelopen => status.Einde is { } e
            ? $"Veiling afgelopen op {e:d MMMM}"
            : "Veiling afgelopen",

        FavoriteState.TeKoop when status.PrijsNu is null => "Staat er nog",

        FavoriteState.TeKoop when bewaard is > 0 && status.PrijsNu != bewaard =>
            $"Nu {Bedrag(status.PrijsNu)} - was {Bedrag(bewaard)}",

        FavoriteState.TeKoop => $"Staat er nog, {Bedrag(status.PrijsNu)}",

        _ => status.Uitleg.Length > 0 ? status.Uitleg : "Niet na te gaan"
    };

    /// <summary>Dezelfde opmaak als op de kaart: centen enkel wanneer ze er zijn.</summary>
    private static string Bedrag(decimal? prijs) =>
        prijs is null ? "" : "€ " + prijs.Value.ToString(prijs.Value % 1 == 0 ? "N0" : "N2",
                                                         CultureInfo.GetCultureInfo("nl-BE"));

    /// <summary>De einddatum van de pagina, wanneer het sitebestand zegt waar die staat.</summary>
    private static async Task<DateTime?> EindeAsync(SiteDefinition def, string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(def.DetailEndDateSelector)) return null;

        var tekst = await GenericSource.ReadFieldAsync(html, def.DetailEndDateSelector, ct);
        return DetailFetcher.LeesDatum(tekst);
    }

    // ---------- de prijs uit het ld+json-blok ----------

    private static readonly Regex LdJson = new(
        """<script[^>]+type=["']application/ld\+json["'][^>]*>(.*?)</script>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// De prijs uit het <c>ld+json</c>-blok van de pagina (schema.org). Null wanneer er geen
    /// blok is, of geen prijs in staat.
    ///
    /// Een <b>nul telt niet als prijs</b>, net als elders in de app: bij een "gezocht"-
    /// advertentie van 2dehands staat er letterlijk <c>offers.price = 0</c>.
    /// </summary>
    internal static decimal? PrijsUitPagina(string html)
    {
        foreach (Match blok in LdJson.Matches(html))
        {
            try
            {
                using var boom = JsonDocument.Parse(blok.Groups[1].Value.Trim());
                if (ZoekPrijs(boom.RootElement) is { } prijs && prijs > 0) return prijs;
            }
            catch (JsonException)
            {
                // Eén onleesbaar blok mag de rest niet tegenhouden.
            }
        }

        return null;
    }

    /// <summary>
    /// De titel van een zoekertje waarvan we enkel het webadres hebben (zie
    /// <see cref="FavoriteFromUrl"/>). Drie wegen, in deze volgorde:
    ///
    /// <list type="number">
    ///   <item>de <c>name</c> uit het <c>ld+json</c>-blok, maar <b>enkel</b> van een object dat
    ///         ook een prijs of een <c>offers</c> draagt - anders pak je de naam van de site of
    ///         van een kruimelpad, want die staan er ook in;</item>
    ///   <item><c>og:title</c>, waar de meeste sites de titel van de advertentie in zetten;</item>
    ///   <item>de <c>&lt;title&gt;</c> van de pagina. Daar hangt meestal de naam van de site
    ///         achter ("... | 2dehands"), maar iets herkenbaars is beter dan niets.</item>
    /// </list>
    ///
    /// Waarom niet uit het sitebestand: dat beschrijft de <b>zoekpagina</b>. Er is wel een
    /// selector voor de einddatum en de foto's van een advertentiepagina, maar niet voor haar
    /// titel - die stond nooit ergens anders dan in het zoekresultaat.
    /// </summary>
    internal static string TitelUitPagina(string html)
    {
        foreach (Match blok in LdJson.Matches(html))
        {
            try
            {
                using var boom = JsonDocument.Parse(blok.Groups[1].Value.Trim());
                if (ZoekNaam(boom.RootElement) is { } naam && naam.Length > 0) return naam;
            }
            catch (JsonException)
            {
                // Eén onleesbaar blok mag de rest niet tegenhouden.
            }
        }

        foreach (var patroon in new[] { OgTitel, PaginaTitel })
        {
            var raak = patroon.Match(html);
            if (!raak.Success) continue;

            var tekst = WebUtility.HtmlDecode(raak.Groups[1].Value).Trim();
            if (tekst.Length > 0) return tekst;
        }

        return "";
    }

    private static readonly Regex OgTitel = new(
        """<meta[^>]+property=["']og:title["'][^>]+content=["'](.*?)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PaginaTitel = new(
        "<title[^>]*>(.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// De <c>name</c> van een object dat ook een prijs draagt. Die voorwaarde is nodig: een
    /// pagina heeft doorgaans meerdere <c>ld+json</c>-objecten, en de eerste <c>name</c> is vaak
    /// die van de site zelf of van een kruimelpad.
    /// </summary>
    private static string? ZoekNaam(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var heeftPrijs = element.TryGetProperty("offers", out _) ||
                                 element.TryGetProperty("price", out _);

                if (heeftPrijs && element.TryGetProperty("name", out var naam) &&
                    naam.ValueKind == JsonValueKind.String)
                {
                    var tekst = naam.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(tekst)) return tekst;
                }

                foreach (var veld in element.EnumerateObject())
                    if (ZoekNaam(veld.Value) is { } dieper) return dieper;

                return null;

            case JsonValueKind.Array:
                foreach (var kind in element.EnumerateArray())
                    if (ZoekNaam(kind) is { } gevonden) return gevonden;

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Het eerste veld dat een prijs is, hoe diep het ook zit. Een blok mag een lijst zijn,
    /// een <c>@graph</c> hebben, of de prijs pas onder <c>offers</c> zetten.
    /// </summary>
    private static decimal? ZoekPrijs(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var veld in element.EnumerateObject())
                {
                    if (veld.NameEquals("price") || veld.NameEquals("lowPrice"))
                    {
                        if (Bedragwaarde(veld.Value) is { } gevonden) return gevonden;
                        continue;
                    }

                    if (ZoekPrijs(veld.Value) is { } dieper) return dieper;
                }

                return null;

            case JsonValueKind.Array:
                foreach (var kind in element.EnumerateArray())
                    if (ZoekPrijs(kind) is { } gevonden) return gevonden;

                return null;

            default:
                return null;
        }
    }

    /// <summary>Een prijs staat er als getal of als tekst; allebei komen voor.</summary>
    private static decimal? Bedragwaarde(JsonElement waarde) => waarde.ValueKind switch
    {
        JsonValueKind.Number when waarde.TryGetDecimal(out var getal) => getal,

        JsonValueKind.String when decimal.TryParse(waarde.GetString(), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var getal) => getal,

        _ => null
    };

    private static bool IsWebadres(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var adres) &&
        (adres.Scheme == Uri.UriSchemeHttp || adres.Scheme == Uri.UriSchemeHttps);

    private static string Kort(string titel) => titel.Length <= 40 ? titel : titel[..40] + "...";
}

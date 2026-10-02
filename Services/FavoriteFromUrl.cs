using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Maakt een favoriet van één webadres: wat er gebeurt wanneer je in Chrome op een zoekertje
/// rechtsklikt en <i>Zet in favorieten van Zentrix</i> kiest.
///
/// Dit is de <b>enige</b> plaats waar de brug de andere kant op werkt. Overal elders geeft de app
/// werk aan de extensie; hier komt er iets binnen dat de app niet gevraagd heeft. Daarom staan er
/// drie sloten op, en ze doen alle drie iets anders:
///
/// <list type="number">
///   <item>het verzoek moet <b>getekend</b> zijn met de koppelcode - dat regelt
///         <see cref="BridgeServer"/> al voor elk pad;</item>
///   <item>het adres moet <b>veilig</b> zijn (<see cref="SiteUrlCheck.IsVeiligAdres"/>): https,
///         en geen adres op je eigen netwerk. Wat hier binnenkomt gaat de app immers zelf
///         ophalen;</item>
///   <item>de site moet <b>bekend</b> zijn. Zonder sitebestand is er geen naam voor de bron en
///         geen manier om een id uit de link te halen, en dan zou dezelfde kavel twee keer in je
///         favorieten komen: één keer via het zoekresultaat en één keer via Chrome.</item>
/// </list>
/// </summary>
public static class FavoriteFromUrl
{
    /// <summary>Wat er van een poging terechtkwam. De tekst gaat naar Chrome, dus ze is voor jou.</summary>
    public record Uitkomst(bool Ok, string Melding, Listing? Favoriet = null);

    /// <summary>
    /// Het sitebestand waar dit adres bij hoort, of null.
    ///
    /// Vergelijkt op host, en laat <c>www.</c> ervoor of eraf los: een sitebestand dat naar
    /// <c>www.2dehands.be</c> zoekt, hoort ook bij een link naar <c>2dehands.be</c>.
    /// </summary>
    public static SiteDefinition? SiteVoor(string url, IEnumerable<SiteDefinition> sites)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var adres)) return null;

        return sites.FirstOrDefault(site =>
            SiteUrlCheck.Hosts(site).Any(host => ZelfdeHost(host, adres.Host)));
    }

    /// <summary>
    /// Het <c>IdPattern</c> van een sitebestand als regex, of null. Een kapot patroon in een
    /// gedeeld bestand mag niets stukmaken: dan wordt de volledige link de identiteit, net als
    /// in <see cref="GenericSource"/>.
    /// </summary>
    private static System.Text.RegularExpressions.Regex? Patroon(SiteDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.IdPattern)) return null;

        try
        {
            return new System.Text.RegularExpressions.Regex(def.IdPattern);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool ZelfdeHost(string a, string b)
    {
        a = Kaal(a);
        b = Kaal(b);

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string Kaal(string host) =>
            host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// Haalt de pagina op, leest eruit wat er te lezen valt en zet het bij de favorieten.
    ///
    /// Geeft altijd een zin terug die je iets zegt, ook wanneer het niet lukte: dit gebeurt in
    /// een ander venster dan Zentrix, dus een stille mislukking zou je nooit opmerken.
    /// </summary>
    public static async Task<Uitkomst> VoegToeAsync(string url, IReadOnlyList<SiteDefinition> sites,
                                                    HistoryStore history, CancellationToken ct = default)
    {
        if (!SiteUrlCheck.IsVeiligAdres(url, out var reden))
            return new Uitkomst(false, $"Dit adres gaat niet: {reden}.");

        var def = SiteVoor(url, sites);

        if (def is null)
            return new Uitkomst(false, "Zentrix kent deze site niet. Voeg ze eerst toe bij Sites beheren.");

        string html;

        try
        {
            html = await DetailFetcher.HaalPaginaAsync(def, url, ct);
        }
        catch (Exception fout)
        {
            Log.Write($"favoriet uit Chrome mislukte voor {url} - {fout.Message}");
            return new Uitkomst(false, $"De pagina kwam niet binnen: {FriendlyError.Describe(fout)}");
        }

        return await VanPaginaAsync(url, def, html, history, ct);
    }

    /// <summary>
    /// Maakt de favoriet uit een pagina die al binnen is. Los van
    /// <see cref="VoegToeAsync"/> gehouden omdat het twee verschillende dingen zijn - ophalen en
    /// ervan maken - en omdat het ophalen https eist: de proefsite van de controles draait op
    /// <c>http://127.0.0.1</c>, en die regel verzwakken om haar te kunnen testen zou precies het
    /// slot weghalen waar het om gaat.
    /// </summary>
    internal static async Task<Uitkomst> VanPaginaAsync(string url, SiteDefinition def, string html,
                                                        HistoryStore history, CancellationToken ct = default)
    {
        // Hetzelfde id als het zoekresultaat eruit zou halen, zodat dezelfde kavel niet twee
        // keer in je favorieten komt: één keer via het zoekresultaat en één keer via Chrome.
        var listing = new Listing
        {
            Source = def.Name,
            ExternalId = GenericSource.IdUitLink(Patroon(def), url),
            Url = url,
            IsFavorite = true
        };

        if (history.GetFavoriteKeys().Contains(listing.Key))
            return new Uitkomst(false, "Dit zoekertje staat al bij je favorieten.");

        listing.Title = FavoriteWatch.TitelUitPagina(html);

        if (string.IsNullOrWhiteSpace(listing.Title))
        {
            // Zonder titel is het een lege kaart in je favorieten, en dan is "niet gelukt"
            // eerlijker dan iets bewaren waar je niets aan hebt.
            return new Uitkomst(false, $"Er viel geen titel uit deze pagina te lezen ({def.Name}).");
        }

        listing.Price = FavoriteWatch.PrijsUitPagina(html);

        // De einddatum en een foto, voor zover het sitebestand zegt waar ze staan. Allebei
        // mogen mislukken: dan staat de favoriet er gewoon zonder.
        if (!string.IsNullOrWhiteSpace(def.DetailEndDateSelector))
        {
            var tekst = await GenericSource.ReadFieldAsync(html, def.DetailEndDateSelector, ct);
            listing.EndsAt = DetailFetcher.LeesDatum(tekst);
        }

        if (!string.IsNullOrWhiteSpace(def.DetailImagesSelector))
        {
            var fotos = await GenericSource.ReadFieldsAsync(html, def.DetailImagesSelector, ct);
            if (fotos.Count > 0) listing.ImageUrls.Add(fotos[0]);
        }

        history.AddFavorite(listing);
        Log.Write($"favoriet uit Chrome: '{listing.Title}' van {def.Name}");

        var prijs = listing.Price is { } p ? $" (€{p:0.##})" : "";
        return new Uitkomst(true, $"Bij je favorieten gezet: {listing.Title}{prijs}", listing);
    }
}

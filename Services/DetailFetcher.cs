using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Haalt aan wat niet op de zoekpagina van een site staat, maar wel op de pagina van het
/// zoekertje zelf. Vandaag is dat één ding: de sluitingsdatum van een veiling
/// (<see cref="SiteDefinition.DetailEndDateSelector"/>).
///
/// Waarom dit bestaat: AlleVeilingen zet op zijn kaarten wel het huidige bod en het aantal
/// biedingen, maar niet wanneer de veiling sluit — en juist dat wil je weten. Op de pagina
/// van het kavel staat het wel, en die komt met een gewoon verzoek binnen in ongeveer twee
/// tiende van een seconde.
///
/// Wat het NIET doet: alles ophalen wat binnenkwam. Een zoekopdracht levert tot vijfhonderd
/// zoekertjes per site, en dan zouden dat vijfhonderd extra verzoeken zijn. Het gaat enkel
/// over de zoekertjes die op dat moment op het scherm staan (zie <c>ToonPagina</c>), zes
/// tegelijk, en wat één keer opgehaald is blijft onthouden zolang de app draait. Zo koste
/// het bij een pagina van vijftig kavels ongeveer twee seconden, op de achtergrond, terwijl
/// de resultaten al te zien zijn.
/// </summary>
public static class DetailFetcher
{
    /// <summary>Hoeveel pagina's er tegelijk opgehaald worden.</summary>
    private const int Tegelijk = 6;

    private static readonly SemaphoreSlim Poort = new(Tegelijk);

    /// <summary>
    /// Wat we al opgehaald hebben, op de sleutel van het zoekertje. Ook een mislukking komt
    /// hierin (als niets), anders probeert elke verversing van de lijst het opnieuw.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime?> Onthouden = new();

    private static readonly HttpClient Http = MaakClient();

    private static HttpClient MaakClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

        // Korter dan de 30 seconden van een zoekopdracht: dit is een extraatje bij
        // resultaten die al op het scherm staan, en niemand wacht erop.
        client.Timeout = TimeSpan.FromSeconds(10);

        return client;
    }

    /// <summary>
    /// Vult de sluitingsdatum aan van de zoekertjes die meegegeven worden, voor zover hun
    /// site er een selector voor heeft en ze die datum nog niet hebben. Werkt door tot alles
    /// binnen is of tot er geannuleerd wordt; een fout bij één zoekertje raakt de rest niet.
    /// </summary>
    public static async Task FillAsync(IEnumerable<Listing> listings, IReadOnlyList<SiteDefinition> sites,
                                       CancellationToken ct = default)
    {
        var werk = new List<(Listing Listing, string Selector)>();

        foreach (var listing in listings)
        {
            if (listing.EndsAt is not null || !string.IsNullOrWhiteSpace(listing.TimeLeft)) continue;

            var def = sites.FirstOrDefault(s =>
                string.Equals(s.Name, listing.Source, StringComparison.OrdinalIgnoreCase));

            if (def is null || string.IsNullOrWhiteSpace(def.DetailEndDateSelector)) continue;
            if (!IsWebadres(listing.Url)) continue;

            // Al eens opgehaald: meteen invullen, zonder verzoek. Bladeren naar een vorige
            // pagina en terug kost zo niets.
            if (Onthouden.TryGetValue(listing.Key, out var bekend))
            {
                if (bekend is not null) Zet(listing, bekend);
                continue;
            }

            werk.Add((listing, def.DetailEndDateSelector));
        }

        if (werk.Count == 0) return;

        var mislukt = 0;

        await Task.WhenAll(werk.Select(async paar =>
        {
            await Poort.WaitAsync(ct);

            try
            {
                var datum = await HaalAsync(paar.Listing.Url, paar.Selector, ct);
                Onthouden[paar.Listing.Key] = datum;

                if (datum is not null) Zet(paar.Listing, datum);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // De gebruiker bladerde verder of zocht opnieuw: gewoon stoppen.
            }
            catch (Exception)
            {
                // Eén kavelpagina die niet lukt, mag de rest niet tegenhouden. Niet
                // onthouden als fout: de volgende keer mag het opnieuw geprobeerd worden.
                Interlocked.Increment(ref mislukt);
            }
            finally
            {
                Poort.Release();
            }
        }));

        if (mislukt > 0)
            Log.Write($"einddatum: {mislukt} van de {werk.Count} kavelpagina's gaven niets");
    }

    /// <summary>Haalt één pagina op en leest er de datum uit.</summary>
    private static async Task<DateTime?> HaalAsync(string url, string selector, CancellationToken ct)
    {
        using var antwoord = await Http.GetAsync(url, ct);
        if (!antwoord.IsSuccessStatusCode) return null;

        var html = await antwoord.Content.ReadAsStringAsync(ct);
        var tekst = await GenericSource.ReadFieldAsync(html, selector, ct);

        return LeesDatum(tekst);
    }

    /// <summary>
    /// Zet de datum bij het zoekertje en laat het scherm het weten. Dat laatste moet op de
    /// schermdraad gebeuren: dit loopt op een achtergronddraad, en een melding van daar
    /// bereikt de binding niet — het zoekertje stond dan wel goed in het geheugen, maar op
    /// de kaart bleef enkel de plaats staan. Nagemeten met het hoofdscherm buiten beeld.
    /// Zonder venster (de controles) gebeurt het gewoon meteen.
    /// </summary>
    private static void Zet(Listing listing, DateTime? datum)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            listing.EndsAt = datum;
            listing.MeldTijdGewijzigd();
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            listing.EndsAt = datum;
            listing.MeldTijdGewijzigd();
        });
    }

    /// <summary>
    /// Een datum zoals een Europese site ze schrijft. De vaste vormen staan voorop, want
    /// <see cref="DateTime.TryParse(string, out DateTime)"/> leest "29/09/2026" op een
    /// Engelstalige Windows als een dag die niet bestaat, en geeft dan niets terug.
    /// </summary>
    internal static DateTime? LeesDatum(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return null;

        string[] vormen =
        {
            "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy HH:mm", "d-M-yyyy H:mm", "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm"
        };

        if (DateTime.TryParseExact(tekst, vormen, CultureInfo.InvariantCulture,
                                   DateTimeStyles.AllowWhiteSpaces, out var exact))
            return exact;

        // Een ISO-tijdstip (time@datetime) of wat de instellingen van deze pc aankunnen.
        if (DateTime.TryParse(tekst, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var iso))
            return iso.Kind == DateTimeKind.Utc ? iso.ToLocalTime() : iso;

        return DateTime.TryParse(tekst, out var lokaal) ? lokaal : null;
    }

    /// <summary>Enkel http en https: een sitebestand kan van een vreemde komen.</summary>
    private static bool IsWebadres(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Wat er onthouden is vergeten — enkel voor de controles.</summary>
    internal static void Vergeet() => Onthouden.Clear();
}

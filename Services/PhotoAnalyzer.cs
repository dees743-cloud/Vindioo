using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Zentrix.Services;

/// <summary>Wat de AI van één foto maakte.</summary>
/// <param name="Beschrijving">Eén alinea in gewone taal, over wat er te zien is.</param>
/// <param name="Gelezen">De namen en typenummers die letterlijk van de foto gelezen zijn.</param>
/// <param name="Duur">Hoelang het duurde, het laden van het model inbegrepen.</param>
/// <param name="Stukken">In hoeveel stukken de foto geknipt is; 1 betekent in één keer gelezen.</param>
/// <param name="Fout">De reden waarom het niet lukte, of null.</param>
public record PhotoInsight(string Beschrijving, IReadOnlyList<string> Gelezen,
                           TimeSpan Duur, int Stukken, string? Fout = null);

/// <summary>
/// Laat een AI op deze pc naar een foto van een zoekertje kijken en er in gewone taal over
/// vertellen. Bedoeld voor wat je met het blote oog niet ziet: een doos vol dvd's waarvan je de
/// titels niet kan lezen, of het typenummer op het label achteraan een oude radio.
///
/// **Het draait lokaal.** Ollama op 127.0.0.1, op de grafische kaart; er gaat geen foto de deur
/// uit. Gemeten op een RTX 4060 met qwen3.5:9b: ongeveer een seconde per foto, en de eerste keer
/// na een pauze veertig seconden extra omdat het model dan in de kaart geladen wordt.
///
/// Drie dingen die uit het meten kwamen en die de hele opzet bepalen (24 september 2026):
///
/// - **De foto moet in stukken.** Een vision-model verkleint zijn invoer naar een vast formaat, en
///   op een hele foto zijn kleine labels dan een paar beeldpunten hoog. Op een foto met elf
///   tijdschriften en dertig diskettehoesjes gaf de hele foto 14 namen - enkel de grote koppen -
///   en gaven zes stukken er 57, de kleine labels inbegrepen.
/// - **Het antwoord moet een vaste vorm hebben.** Met vrije tekst liep het model vast in
///   herhaling: 34 seconden om 200 keer "SuperDisk" te zeggen. Met een afgedwongen JSON-vorm
///   (<c>format</c>) gebeurde dat geen enkele keer meer.
/// - **Lezen is betrouwbaar, weten niet.** Wat het van de foto leest klopt grotendeels; wat het
///   eromheen verzint niet. Daarom leest het eerst enkel namen, en vertelt het pas daarna - met
///   het uitdrukkelijke verbod om iets over staat, kleur of ouderdom te schrijven dat het niet
///   gelezen heeft. Zonder dat verbod kwamen er toetsen bij die niet bestaan.
///
/// Het vertellen op het einde ziet alle gelezen namen in hun verband, en zet er meteen de
/// leesfouten uit recht: PORTEX stond in de losse stukken als "FORTEX", MOUSE MANAGER als
/// "HOUSE MANAGER" en HAIKU als "AIKU", en in de alinea stonden ze alle drie goed.
///
/// Wat het níet goed kan, en dus ook niet belooft: **tellen**. "Ongeveer twintig" voor elf
/// tijdschriften. Het geschatte aantal staat er daarom als een schatting.
/// </summary>
public class PhotoAnalyzer
{
    /// <summary>
    /// Waar Ollama luistert. De controles zetten dit op hun eigen nagebootste server; verder is
    /// het altijd wat er in de instellingen staat.
    /// </summary>
    internal static string? UrlVoorControles { get; set; }

    private static string Url => UrlVoorControles ?? AppSettings.Current.AiUrl;

    /// <summary>
    /// Ruim, want een foto op een trage kaart mag even duren, en de eerste vraag wacht op het
    /// laden van het model.
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Onder deze breedte of hoogte heeft knippen geen zin: dan is de hele foto al klein genoeg
    /// om in één keer scherp genoeg te zien.
    /// </summary>
    private const int MinimumVoorStukken = 900;

    /// <summary>
    /// Waarboven een foto eerst verkleind wordt. Een foto van 4000 beeldpunten breed kost
    /// evenredig veel rekenwerk; na het verkleinen is een stuk nog altijd zo'n 900 breed, en
    /// dat is precies wat er nodig is om een klein label te lezen.
    /// </summary>
    private const int GrootsteZijde = 2400;

    /// <summary>Het model hoeft niet na te denken voor het leest, en herhalen mag het niet.</summary>
    private static JsonObject Instellingen(int maxTokens) => new()
    {
        ["num_predict"] = maxTokens,
        ["temperature"] = 0,
        ["repeat_penalty"] = 1.2
    };

    private const string LeesVraag =
        "Lees de namen, titels en typenummers die op de voorwerpen in dit stuk foto staan. " +
        "Elke naam één keer, alleen wat je werkelijk kan lezen. Verzin niets.";

    /// <summary>
    /// Bekijkt één foto en vertelt erover.
    /// </summary>
    /// <param name="foto">De foto zelf, zoals ze van de site kwam.</param>
    /// <param name="grondig">
    /// De foto in stukken lezen. Aan voor een hoop spullen waarvan je de namen wil; uit voor één
    /// voorwerp, en dan is het een paar seconden in plaats van een halve minuut.
    /// </param>
    /// <param name="status">Welk stuk er nu gelezen wordt, voor de statusregel van het venster.</param>
    public async Task<PhotoInsight> AnalyseerAsync(byte[] foto, bool grondig = true,
                                                   IProgress<string>? status = null,
                                                   CancellationToken ct = default)
    {
        var klok = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var bron = Lees(foto);
            var stukken = grondig ? Knip(bron) : new List<BitmapSource> { bron };

            // Eerst enkel lezen, stuk voor stuk. Wat hier uitkomt zijn losse namen, en dat is met
            // opzet: een model dat mag vertellen terwijl het leest, begint te verzinnen.
            var gelezen = new List<string>();

            for (var i = 0; i < stukken.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                status?.Report(stukken.Count == 1
                    ? "De foto lezen..."
                    : $"Stuk {i + 1} van {stukken.Count} lezen...");

                var antwoord = await VraagAsync(LeesVraag, stukken[i], NamenVorm, 400, ct);

                if (antwoord?["namen"] is JsonArray namen)
                    gelezen.AddRange(namen.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0));
            }

            var uniek = Ontdubbel(gelezen);

            status?.Report("Er een zin van maken...");
            ct.ThrowIfCancellationRequested();

            var tekst = await VraagAsync(VertelVraag(uniek), bron, TekstVorm, 700, ct);
            var beschrijving = tekst?["beschrijving"]?.GetValue<string>() ?? "";

            Log.Write($"foto: {stukken.Count} stuk(ken), {uniek.Count} namen gelezen " +
                      $"in {klok.Elapsed.TotalSeconds:F1}s met {AppSettings.Current.AiModel}");

            return new PhotoInsight(beschrijving, uniek, klok.Elapsed, stukken.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var melding = Beschrijf(ex);
            Log.Write($"foto: mislukt - {ex.Message}");

            return new PhotoInsight("", Array.Empty<string>(), klok.Elapsed, 0, melding);
        }
    }

    /// <summary>
    /// Staat het model al in de grafische kaart? Zo niet, dan kost de eerste vraag er zo'n veertig
    /// seconden bij, en dat hoort het venster te zéggen in plaats van je te laten wachten voor een
    /// scherm dat niets doet. Ollama vertelt het in <c>/api/ps</c>.
    ///
    /// Antwoordt Ollama helemaal niet, dan geeft dit false: er is dan een ander probleem, en dat
    /// komt met een betere melding uit de eerste echte vraag.
    /// </summary>
    public static async Task<bool> ModelStaatKlaarAsync(CancellationToken ct = default)
    {
        try
        {
            using var antwoord = await Http.GetAsync($"{Url.TrimEnd('/')}/api/ps", ct);
            if (!antwoord.IsSuccessStatusCode) return false;

            var geladen = JsonNode.Parse(await antwoord.Content.ReadAsStringAsync(ct))?["models"] as JsonArray;

            return geladen?.Any(m => m?["name"]?.GetValue<string>() == AppSettings.Current.AiModel) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// De vraag op het einde. Alles wat gelezen is gaat mee, zodat het model de losse stukken in
    /// hun verband ziet; en het mag niets toevoegen wat het niet gelezen heeft.
    /// </summary>
    private static string VertelVraag(IReadOnlyList<string> gelezen) =>
        "Je kijkt naar de foto van een tweedehands-zoekertje. Hieronder staat wat er letterlijk " +
        "van de foto gelezen is. Schrijf daarover één alinea in gewoon Nederlands, zoals je het " +
        "aan iemand zou vertellen:\n" +
        "- begin met wat voor soort spullen het zijn en ONGEVEER hoeveel het er zijn;\n" +
        "- som daarna de namen op die gelezen zijn, zoveel mogelijk;\n" +
        "- schrijf NIETS over staat, kleur, ouderdom of details die je niet gelezen hebt.\n\n" +
        "GELEZEN VAN DE FOTO:\n" + string.Join("\n", gelezen);

    private static readonly JsonObject NamenVorm = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["namen"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
        },
        ["required"] = new JsonArray("namen")
    };

    private static readonly JsonObject TekstVorm = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["beschrijving"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("beschrijving")
    };

    /// <summary>
    /// Eén vraag aan Ollama, met een foto erbij en een afgedwongen antwoordvorm. Wat terugkomt is
    /// JSON in een tekstveld, dus het wordt twee keer uitgepakt.
    /// </summary>
    private static async Task<JsonNode?> VraagAsync(string vraag, BitmapSource beeld, JsonObject vorm,
                                                    int maxTokens, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = AppSettings.Current.AiModel,
            ["prompt"] = vraag,
            ["images"] = new JsonArray(Convert.ToBase64String(AlsJpeg(beeld))),
            ["stream"] = false,
            ["think"] = false,

            // Het model een tijdje in de grafische kaart laten staan: anders kost elke volgende
            // foto opnieuw die veertig seconden laden.
            ["keep_alive"] = "10m",
            ["format"] = vorm.DeepClone(),
            ["options"] = Instellingen(maxTokens)
        };

        using var inhoud = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var antwoord = await Http.PostAsync($"{Url.TrimEnd('/')}/api/generate", inhoud, ct);

        antwoord.EnsureSuccessStatusCode();

        var tekst = await antwoord.Content.ReadAsStringAsync(ct);
        var binnenin = JsonNode.Parse(tekst)?["response"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(binnenin)) return null;

        // Het model houdt zich bijna altijd aan de vorm, maar "bijna" is geen reden om de hele
        // beurt te laten vallen: dan blijft het gewoon bij wat de andere stukken gaven.
        try { return JsonNode.Parse(binnenin); }
        catch (JsonException)
        {
            Log.Write("foto: het model gaf iets anders terug dan de gevraagde vorm");
            return null;
        }
    }

    /// <summary>
    /// Dezelfde naam twee keer hoeft niet, en de stukken overlappen, dus dat gebeurt. Hoofdletters
    /// tellen niet mee bij het vergelijken - de ene hoek van de foto leest "SuperDisk" en de
    /// andere "SUPERDISK" - maar de eerste schrijfwijze blijft staan.
    /// </summary>
    private static List<string> Ontdubbel(IEnumerable<string> namen)
    {
        var gezien = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uit = new List<string>();

        foreach (var naam in namen)
        {
            var schoon = naam.Trim();
            if (schoon.Length > 0 && gezien.Add(schoon)) uit.Add(schoon);
        }

        return uit;
    }

    /// <summary>
    /// De foto in stukken, met overlap. Zonder die overlap valt een tijdschrift dat net op de
    /// snijlijn ligt in twee halve titels uiteen, en dan leest geen van beide stukken hem.
    /// Drie stukken over de lange zijde en twee over de korte: zo blijft een stuk vierkant genoeg.
    /// </summary>
    private static List<BitmapSource> Knip(BitmapSource bron)
    {
        if (Math.Max(bron.PixelWidth, bron.PixelHeight) < MinimumVoorStukken)
            return new List<BitmapSource> { bron };

        var liggend = bron.PixelWidth >= bron.PixelHeight;
        var kolommen = liggend ? 3 : 2;
        var rijen = liggend ? 2 : 3;

        var stukken = new List<BitmapSource>();
        var overlapX = bron.PixelWidth / (kolommen * 4);
        var overlapY = bron.PixelHeight / (rijen * 4);

        for (var rij = 0; rij < rijen; rij++)
        {
            for (var kolom = 0; kolom < kolommen; kolom++)
            {
                var x0 = Math.Max(0, kolom * bron.PixelWidth / kolommen - overlapX);
                var x1 = Math.Min(bron.PixelWidth, (kolom + 1) * bron.PixelWidth / kolommen + overlapX);
                var y0 = Math.Max(0, rij * bron.PixelHeight / rijen - overlapY);
                var y1 = Math.Min(bron.PixelHeight, (rij + 1) * bron.PixelHeight / rijen + overlapY);

                var stuk = new CroppedBitmap(bron, new System.Windows.Int32Rect(x0, y0, x1 - x0, y1 - y0));
                stuk.Freeze();
                stukken.Add(stuk);
            }
        }

        return stukken;
    }

    /// <summary>
    /// De foto inlezen en meteen bevriezen: dit loopt op een achtergronddraad, en een
    /// BitmapSource die niet bevroren is, hoort bij de draad die hem maakte. Een te grote foto
    /// wordt verkleind, want elk beeldpunt kost rekenwerk op de grafische kaart.
    /// </summary>
    private static BitmapSource Lees(byte[] foto)
    {
        using var stroom = new MemoryStream(foto);

        var plaatje = new BitmapImage();
        plaatje.BeginInit();
        plaatje.StreamSource = stroom;

        // Zonder OnLoad leest WPF de foto pas bij het tekenen, en dan is de stroom al dicht.
        plaatje.CacheOption = BitmapCacheOption.OnLoad;
        plaatje.EndInit();
        plaatje.Freeze();

        var grootste = Math.Max(plaatje.PixelWidth, plaatje.PixelHeight);
        if (grootste <= GrootsteZijde) return plaatje;

        var schaal = (double)GrootsteZijde / grootste;
        var kleiner = new TransformedBitmap(plaatje, new System.Windows.Media.ScaleTransform(schaal, schaal));
        kleiner.Freeze();

        return kleiner;
    }

    private static byte[] AlsJpeg(BitmapSource beeld)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(beeld));

        using var uit = new MemoryStream();
        encoder.Save(uit);

        return uit.ToArray();
    }

    /// <summary>
    /// Waarom het niet lukte, in een zin die de gebruiker verder helpt. Ollama dat niet draait is
    /// hier het gewone geval, en dat ziet er anders uit dan een site die weigert - vandaar niet
    /// <see cref="FriendlyError"/>.
    /// </summary>
    private static string Beschrijf(Exception ex) => ex switch
    {
        HttpRequestException => "Ollama antwoordt niet op " + Url + ". Draait het op deze pc? " +
                                "Start het, of zet een ander adres bij de instellingen.",
        TaskCanceledException => "Het model deed er te lang over. Een kleinere foto of een lichter " +
                                 "model helpt; zie de instelling AiModel.",
        _ => FriendlyError.Describe(ex)
    };
}

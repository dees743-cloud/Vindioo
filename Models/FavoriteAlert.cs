namespace Vindioo.Models;

/// <summary>
/// Waar een waarschuwing over één favoriet heen mag. Een vlaggenreeks en geen lijstje namen,
/// zodat het als één getal in de databank past en er geen tekst te ontleden valt.
///
/// Dit zegt enkel <b>welke</b> kanalen deze favoriet mag gebruiken. Of zo'n kanaal ook écht
/// kan versturen, hangt af van de instellingen van de app (een Telegram-token, een
/// mailserver); die staan één keer centraal en horen niet bij een kavel.
/// </summary>
[Flags]
public enum AlertChannels
{
    Geen = 0,
    Tray = 1,
    Telegram = 2,
    Mail = 4
}

/// <summary>
/// De momenten waarop een bewaarde veiling een waarschuwing geeft, in minuten vóór het einde.
///
/// <para>Tot 4 oktober 2026 waren dit vier vaste keuzes (1 dag, 4 uur, 1 uur, 15 minuten), één
/// keer ingesteld voor álle favorieten samen. Dat werkte niet zoals iemand er echt naar kijkt:
/// bij een kavel waar je op wil bieden wil je andere momenten dan bij een kavel dat je enkel
/// volgt, en op een gewone advertentie slaat het hele idee niet. Nu zet je ze per favoriet, en
/// vul je ze zelf in.</para>
///
/// <para>Minuten is de eenheid waarin ze bewaard worden, want daarmee rekent
/// <see cref="Vindioo.Services.AuctionWatch"/>. Het invulveld toont er een eenheid naast, zodat
/// je "24 uur" typt en niet 1440.</para>
/// </summary>
public static class AlertMoments
{
    /// <summary>Meer dan dit worden er geen. Tien momenten op één veiling is al veel.</summary>
    public const int Hoogstens = 10;

    /// <summary>Waarmee een nieuw venster begint: een dag vooraf, en een uur vooraf.</summary>
    public static readonly int[] Standaard = { 1440, 60 };

    /// <summary>
    /// Uit de databank: "1440,60" wordt {1440, 60}. Bestand tegen rommel, want dit is tekst in
    /// een kolom - een onleesbaar stuk wordt overgeslagen in plaats van de favoriet mee te
    /// nemen in zijn val.
    /// </summary>
    public static List<int> Lees(string? opgeslagen)
    {
        var uit = new List<int>();
        if (string.IsNullOrWhiteSpace(opgeslagen)) return uit;

        foreach (var stuk in opgeslagen.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(stuk.Trim(), out var minuten) && minuten > 0 && !uit.Contains(minuten))
                uit.Add(minuten);

        uit.Sort((a, b) => b.CompareTo(a));
        return uit.Count > Hoogstens ? uit.Take(Hoogstens).ToList() : uit;
    }

    /// <summary>En terug. Groot naar klein, zodat de kolom leesbaar blijft als je ernaar kijkt.</summary>
    public static string Schrijf(IEnumerable<int> momenten)
    {
        var net = momenten.Where(m => m > 0).Distinct().OrderByDescending(m => m).Take(Hoogstens);
        return string.Join(",", net);
    }

    /// <summary>
    /// Een getal met de grootste eenheid die er zonder rest in past: 1440 wordt (1, dag),
    /// 120 wordt (2, uur), 15 wordt (15, minuut). Zo ziet de gebruiker terug wat hij intypte.
    /// </summary>
    public static (int Getal, string Eenheid) Toon(int minuten)
    {
        if (minuten % 1440 == 0) return (minuten / 1440, "dagen");
        if (minuten % 60 == 0) return (minuten / 60, "uur");

        return (minuten, "minuten");
    }

    /// <summary>De andere kant op, vanuit het invulveld.</summary>
    public static int NaarMinuten(int getal, string eenheid) => eenheid switch
    {
        "dagen" => getal * 1440,
        "minuten" => getal,
        _ => getal * 60
    };

    /// <summary>"1 dag vooraf", "2 uur vooraf", "15 minuten vooraf" - voor in een melding.</summary>
    public static string Noem(int minuten)
    {
        var (getal, eenheid) = Toon(minuten);

        var woord = eenheid switch
        {
            "dagen" => getal == 1 ? "dag" : "dagen",
            "uur" => "uur",
            _ => getal == 1 ? "minuut" : "minuten"
        };

        return $"{getal} {woord} vooraf";
    }
}

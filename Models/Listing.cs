using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Zentrix.Models;

/// <summary>
/// Eén zoekertje of kavel, los van de site waar het vandaan komt.
/// Elke bron vertaalt zijn eigen antwoord naar dit model.
/// </summary>
public class Listing : ObservableObject
{
    /// <summary>Naam van de bron, bv. "2dehands" of "BOPA".</summary>
    public string Source { get; set; } = "";

    /// <summary>Id zoals de site het zelf gebruikt. Samen met Source uniek.</summary>
    public string ExternalId { get; set; } = "";

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Prijs in euro. Null wanneer onbekend, "op aanvraag" of bieden.</summary>
    public decimal? Price { get; set; }

    /// <summary>Ruwe prijstekst, bv. "Bieden" of "Gratis af te halen".</summary>
    public string PriceLabel { get; set; } = "";

    public string Location { get; set; } = "";

    /// <summary>
    /// Hoelang er bij een veiling nog geboden kan worden, zoals de site het schrijft
    /// ("Nog 3 dagen"). Leeg bij een gewoon zoekertje. Wordt niet bewaard bij een
    /// favoriet of bij de resultaten van een vorige beurt: dat zou een oude tekst zijn
    /// die nergens meer op slaat.
    /// </summary>
    public string TimeLeft
    {
        get => _timeLeft;
        set
        {
            _timeLeft = value ?? "";

            // Meteen omrekenen naar een tijdstip, op het moment dat de site het schreef:
            // "Nog 3 dagen" is over drie dagen vanaf nu, niet vanaf het moment waarop
            // iemand later op de knop "volgorde" drukt.
            (_geschatEinde, _schattingOpMinuut) = Schat(_timeLeft, DateTime.Now);
        }
    }

    private string _timeLeft = "";
    private DateTime? _geschatEinde;
    private bool _schattingOpMinuut;

    /// <summary>
    /// Het tijdstip waarvan de timer rechtsonder echt aftelt: het exacte (AlleVeilingen,
    /// Catawiki via zijn API), of een schatting die tot op de minuut klopt. eBay schrijft zijn
    /// aftelklok steeds fijner naarmate het einde nadert - "Nog 9d 12u", en in de laatste
    /// minuut "Nog 6s" - dus daar telt de timer af zodra het ertoe doet. Een schatting op de
    /// dag of het uur ("Nog 3 dagen") telt niet af: dan toont de timer de tekst van de site.
    /// </summary>
    public DateTime? TimerEinde => EndsAt ?? (_schattingOpMinuut ? _geschatEinde : null);

    /// <summary>
    /// Wanneer de veiling sluit, zo goed als we het weten: de echte datum als die er is
    /// (AlleVeilingen, van de kavelpagina), en anders geschat uit de tekst van de site
    /// ("Nog 9d 12u" is over negen dagen en twaalf uur). Daarop sorteert "Loopt het eerst
    /// af". Leeg bij een gewoon zoekertje.
    /// </summary>
    public DateTime? EndsAtOrEstimate => EndsAt ?? _geschatEinde;

    private static readonly Regex Duur = new(
        @"(\d+)\s*(dagen|dag|d|uren|uur|u|h|minuten|minuut|min|m|seconden|sec|s)(?![\p{L}])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Aftelklok = new(@"\b(\d{1,2}):(\d{2}):(\d{2})\b", RegexOptions.Compiled);

    /// <summary>
    /// Rekent een aftelklok zoals de veilingsites ze schrijven om naar een tijdstip:
    /// "Nog 3 dagen", "Nog 1 dag", "Nog 21 uur" (Catawiki), "Nog 9d 12u" (eBay), "Nog 45 min",
    /// en een aftelklok "01:23:45". Wat daar niet op lijkt ("Afgelopen", "Morgen"), geeft niets.
    ///
    /// "Nog 3 dagen" is tussen drie en vier dagen; het is een schatting om op te sorteren,
    /// niet om de minuut te weten. Een klokuur als "19:30" telt bewust niet: dat is een
    /// tijdstip en geen resterende tijd, en zonder seconden is het niet van elkaar te
    /// onderscheiden.
    /// </summary>
    public static DateTime? SchatEinde(string tekst, DateTime nu) => Schat(tekst, nu).Einde;

    /// <summary>
    /// Zie <see cref="SchatEinde"/>, en daarbij of de tekst tot op de minuut gaat: minuten,
    /// seconden of een aftelklok. Enkel dan mag de timer ervan aftellen.
    /// </summary>
    private static (DateTime? Einde, bool OpMinuut) Schat(string tekst, DateTime nu)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return (null, false);

        var klok = Aftelklok.Match(tekst);
        if (klok.Success)
            return (nu + new TimeSpan(int.Parse(klok.Groups[1].Value),
                                      int.Parse(klok.Groups[2].Value),
                                      int.Parse(klok.Groups[3].Value)), true);

        var totaal = TimeSpan.Zero;
        var gevonden = false;
        var opMinuut = false;

        foreach (Match deel in Duur.Matches(tekst))
        {
            var getal = int.Parse(deel.Groups[1].Value);
            var eenheid = deel.Groups[2].Value.ToLowerInvariant();

            totaal += eenheid switch
            {
                "dagen" or "dag" or "d" => TimeSpan.FromDays(getal),
                "uren" or "uur" or "u" or "h" => TimeSpan.FromHours(getal),
                "minuten" or "minuut" or "min" or "m" => TimeSpan.FromMinutes(getal),
                _ => TimeSpan.FromSeconds(getal)
            };
            gevonden = true;

            if (eenheid is "minuten" or "minuut" or "min" or "m" or "seconden" or "sec" or "s")
                opMinuut = true;
        }

        return gevonden ? (nu + totaal, opMinuut) : (null, false);
    }

    /// <summary>
    /// Wanneer de veiling sluit, als dat ergens te vinden was. AlleVeilingen zet die datum
    /// niet op zijn zoekpagina maar wel op de pagina van het kavel; <c>DetailFetcher</c>
    /// haalt ze daar op voor de zoekertjes die op het scherm staan.
    ///
    /// Anders dan <see cref="TimeLeft"/> is dit een echt tijdstip, dus het blijft kloppen:
    /// de app rekent er telkens opnieuw uit hoelang het nog duurt.
    /// </summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>
    /// Hoelang er nog geboden kan worden: de tekst van de site zelf wanneer die er staat,
    /// en anders uitgerekend uit <see cref="EndsAt"/>.
    /// </summary>
    public string TimeLeftText => !string.IsNullOrWhiteSpace(TimeLeft)
        ? TimeLeft
        : TijdTot(EndsAt, DateTime.Now);

    /// <summary>
    /// Zegt aan het scherm dat de resterende tijd veranderd is. <c>DetailFetcher</c> vult de
    /// sluitingsdatum pas aan nadat het zoekertje al op het scherm staat.
    /// </summary>
    public void MeldTijdGewijzigd()
    {
        OnPropertyChanged(nameof(EndsAt));
        OnPropertyChanged(nameof(TimerEinde));
        OnPropertyChanged(nameof(TimeLeftText));
        OnPropertyChanged(nameof(PlaceLine));
    }

    /// <summary>
    /// Wat de timer rechtsonder op de kaart toont (<c>CountdownBadge</c>). Met een exact
    /// sluitingstijdstip telt hij echt af: "3d 04u", "4u 12m", "12m 34s", "Afgelopen". Zonder
    /// exact tijdstip - Catawiki voor zijn API geantwoord heeft - de tekst van de site zonder
    /// "Nog" ervoor ("3 dagen"): aftellen vanaf "Nog 3 dagen" zou een precisie tonen die er
    /// niet is, want de echte sluiting kan evengoed 23 uur later zijn. Leeg bij een gewoon
    /// zoekertje.
    /// </summary>
    public static string TimerTekst(DateTime? exactEinde, string siteTekst, DateTime nu)
    {
        if (exactEinde is { } einde)
        {
            var over = einde - nu;

            if (over <= TimeSpan.Zero) return "Afgelopen";
            if (over.TotalDays >= 1) return $"{(int)over.TotalDays}d {over.Hours:00}u";
            if (over.TotalHours >= 1) return $"{(int)over.TotalHours}u {over.Minutes:00}m";

            return $"{over.Minutes}m {over.Seconds:00}s";
        }

        if (string.IsNullOrWhiteSpace(siteTekst)) return "";

        var tekst = siteTekst.Trim();
        return tekst.StartsWith("Nog ", StringComparison.OrdinalIgnoreCase) ? tekst[4..] : tekst;
    }

    /// <summary>Het laatste uur: dan kleurt de timer, want dan moet je beslissen.</summary>
    public static bool IsDringend(DateTime? exactEinde, DateTime nu) =>
        exactEinde is { } einde && einde > nu && einde - nu < TimeSpan.FromHours(1);

    /// <summary>
    /// Hoelang het nog duurt, in dezelfde stijl als de veilingsites het zelf schrijven.
    /// Grof op het eind toe: wie nog drie dagen heeft, hoeft de minuten niet te weten.
    /// </summary>
    public static string TijdTot(DateTime? einde, DateTime nu)
    {
        if (einde is null) return "";

        var over = einde.Value - nu;

        if (over <= TimeSpan.Zero) return "Afgelopen";
        if (over.TotalDays >= 2) return $"Nog {(int)over.TotalDays} dagen";
        if (over.TotalDays >= 1) return "Nog 1 dag";
        if (over.TotalHours >= 2) return $"Nog {(int)over.TotalHours} uur";
        if (over.TotalHours >= 1) return "Nog 1 uur";

        return $"Nog {Math.Max(1, (int)over.TotalMinutes)} min";
    }

    /// <summary>
    /// Wat er naast de prijs staat: de plaats, en bij een veiling erachter hoelang er nog
    /// geboden kan worden. Is er geen plaats — Catawiki zet er geen op zijn zoekpagina —
    /// dan blijft de tijd alleen over.
    /// </summary>
    public string PlaceLine => string.Join(" · ",
        new[] { Location, TimeLeftText }.Where(deel => !string.IsNullOrWhiteSpace(deel)));

    /// <summary>Naam van de verkoper, als het sitebestand die kent (SellerSelector). Anders leeg.</summary>
    public string Seller { get; set; } = "";

    /// <summary>Wanneer geplaatst, of de sluitingsdatum bij een veiling.</summary>
    public DateTime? Date { get; set; }

    /// <summary>Directe link naar het zoekertje.</summary>
    public string Url { get; set; } = "";

    /// <summary>URL's van de foto's. Pas later gedownload, en enkel als het nodig is.</summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>Score 0-100 uit de tekstbeoordeling.</summary>
    public int TextScore { get; set; }

    /// <summary>Score uit de fotobeoordeling. Null wanneer die niet gedraaid heeft.</summary>
    public int? ImageScore { get; set; }

    /// <summary>Uitleg van de AI: waarom is dit interessant of net niet.</summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Nog niet bekeken bij deze bewaarde zoekopdracht: opgedoken nadat je haar laatst opende
    /// (zie <see cref="SavedSearch.IsUnviewed"/>). Daaraan hangen het NIEUW-label en de
    /// schakelaar "Enkel nieuwe".
    /// </summary>
    public bool IsNew { get; set; }
    /// <summary>Eerste foto, of leeg wanneer er geen is. Voor de miniatuur in de lijst.</summary>
    public string Thumbnail => ImageUrls.Count > 0 ? ImageUrls[0] : "";

    /// <summary>
    /// Grotere versie van de foto, indien de site die apart aanbiedt. Wordt pas
    /// echt opgehaald wanneer de gebruiker de muis op het vergrootglas zet.
    /// </summary>
    public string LargeImageUrl { get; set; } = "";

    /// <summary>De grote foto, of de miniatuur als er geen grote beschikbaar is.</summary>
    public string LargeImage => !string.IsNullOrEmpty(LargeImageUrl) ? LargeImageUrl : Thumbnail;

    /// <summary>
    /// Aangeduid als favoriet. Waarneembaar, want het sterretje in de lijst moet
    /// meteen meeveranderen wanneer je erop klikt.
    /// </summary>
    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    /// <summary>
    /// Wat het opvolgen van een favoriet opleverde: "Weg van de site", "Veiling afgelopen op
    /// 14 september", "Nu € 24 - was € 5". Zie <see cref="Services.FavoriteWatch"/>.
    ///
    /// Bij een gewoon zoekertje blijft dit leeg, en dan staat die regel er niet. Zo blijft
    /// het sjabloon van de kaart gedeeld tussen de resultaten en de favorieten, in plaats van
    /// een tweede kopie die bij elke wijziging mee moet.
    /// </summary>
    private string _watchText = "";
    public string WatchText
    {
        get => _watchText;
        set => SetProperty(ref _watchText, value);
    }

    /// <summary>
    /// Weg of afgelopen: dan krijgt die regel de waarschuwingskleur. Een prijs die veranderde
    /// is nieuws, geen waarschuwing.
    /// </summary>
    private bool _watchIsWarning;
    public bool WatchIsWarning
    {
        get => _watchIsWarning;
        set => SetProperty(ref _watchIsWarning, value);
    }

    /// <summary>Sleutel voor de "al gezien"-tabel in SQLite.</summary>
    public string Key => $"{Source}:{ExternalId}";

    /// <summary>
    /// Vult ontbrekende gegevens aan uit een latere, vollediger versie van
    /// hetzelfde zoekertje, en meldt of er iets bijgekomen is.
    ///
    /// Nodig omdat sommige sites hun pagina in stukken opbouwen. Catawiki zet de
    /// prijs er pas met JavaScript in: in de HTML die de brug als eerste oppikt
    /// staat op die plaats een leeg vakje. Zonder deze aanvulling zou dat lege
    /// vakje blijven staan, want latere leveringen worden als dubbel herkend.
    ///
    /// Enkel aanvullen, nooit overschrijven: wat er al staat is even goed.
    /// </summary>
    public bool MergeFrom(Listing later)
    {
        var aangevuld = false;

        if (Price is null && later.Price is not null)
        {
            Price = later.Price;
            OnPropertyChanged(nameof(Price));
            aangevuld = true;
        }

        if (string.IsNullOrWhiteSpace(PriceLabel) && !string.IsNullOrWhiteSpace(later.PriceLabel))
        {
            PriceLabel = later.PriceLabel;
            OnPropertyChanged(nameof(PriceLabel));
            aangevuld = true;
        }

        if (string.IsNullOrWhiteSpace(Location) && !string.IsNullOrWhiteSpace(later.Location))
        {
            Location = later.Location;
            OnPropertyChanged(nameof(Location));
            OnPropertyChanged(nameof(PlaceLine));
            aangevuld = true;
        }

        // Bij Catawiki komt de aftelklok, net als de prijs, pas met JavaScript in de
        // pagina: in de eerste levering van de brug staat ze er nog niet.
        if (string.IsNullOrWhiteSpace(TimeLeft) && !string.IsNullOrWhiteSpace(later.TimeLeft))
        {
            TimeLeft = later.TimeLeft;
            OnPropertyChanged(nameof(TimeLeft));
            OnPropertyChanged(nameof(TimerEinde));
            OnPropertyChanged(nameof(PlaceLine));
            aangevuld = true;
        }

        if (Date is null && later.Date is not null)
        {
            Date = later.Date;
            OnPropertyChanged(nameof(Date));
            aangevuld = true;
        }

        if (EndsAt is null && later.EndsAt is not null)
        {
            EndsAt = later.EndsAt;
            OnPropertyChanged(nameof(EndsAt));
            OnPropertyChanged(nameof(TimerEinde));
            OnPropertyChanged(nameof(PlaceLine));
            aangevuld = true;
        }

        if (ImageUrls.Count == 0 && later.ImageUrls.Count > 0)
        {
            ImageUrls.AddRange(later.ImageUrls);
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(LargeImage));
            aangevuld = true;
        }

        if (string.IsNullOrWhiteSpace(LargeImageUrl) && !string.IsNullOrWhiteSpace(later.LargeImageUrl))
        {
            LargeImageUrl = later.LargeImageUrl;
            OnPropertyChanged(nameof(LargeImage));
            aangevuld = true;
        }

        return aangevuld;
    }
}
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
    public string TimeLeft { get; set; } = "";

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
        OnPropertyChanged(nameof(TimeLeftText));
        OnPropertyChanged(nameof(PlaceLine));
    }

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

    /// <summary>Sleutel voor de "al gezien"-tabel in SQLite.</summary>
    /// <summary>Nog niet eerder gezien bij deze bewaarde zoekopdracht.</summary>
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
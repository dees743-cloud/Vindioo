using CommunityToolkit.Mvvm.ComponentModel;

namespace Vindioo.Models;

/// <summary>
/// Een vastgezette zoekopdracht: wat er gezocht wordt, waar, met welke filters,
/// hoe vaak hij vanzelf opnieuw draait en of je een melding krijgt.
///
/// Dit is het enige object dat de planner nodig heeft. Alles wat één zoekopdracht
/// bepaalt zit erin, zodat hij ook kan draaien terwijl er niemand naar het scherm
/// kijkt — de app in het systeemvak, of geminimaliseerd.
///
/// Waarneembaar, want de lijst met zoekopdrachten moet meebewegen wanneer de
/// planner op de achtergrond iets vindt.
/// </summary>
public class SavedSearch : ObservableObject
{
    public int Id { get; set; }

    /// <summary>
    /// Het woord waarop gezocht wordt. Dit is het enige dat een zoekopdracht
    /// echt bepaalt; alles eromheen (sites, filters, schema) hangt eraan vast.
    /// </summary>
    private string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value)) OnPropertyChanged(nameof(Name));
        }
    }

    /// <summary>
    /// Hoe de zoekopdracht in lijsten en meldingen heet: gewoon het zoekwoord
    /// met een hoofdletter. Er is bewust geen apart naamveld — twee namen voor
    /// hetzelfde ding bijhouden levert alleen maar verwarring op.
    /// </summary>
    public string Name => Query.Length > 0
        ? char.ToUpper(Query[0]) + Query[1..]
        : "Nieuwe zoekopdracht";

    /// <summary>Alleen resultaten met minstens één foto.</summary>
    public bool PhotosOnly { get; set; }

    // ---------- sites en hun filters ----------

    /// <summary>
    /// Per site: of hij meezoekt en met welke filters. Dit vervangt de oude
    /// <see cref="Sites"/> plus één gedeelde prijs; elke site heeft nu zijn
    /// eigen postcode, straal, provincies en prijsgrenzen.
    /// </summary>
    public List<SiteSetting> SiteSettings { get; set; } = new();

    /// <summary>De instellingen van één site, of null wanneer die er niet in zit.</summary>
    public SiteSetting? For(string site) =>
        SiteSettings.FirstOrDefault(s => string.Equals(s.Site, site, StringComparison.OrdinalIgnoreCase));

    /// <summary>De namen van de sites die meezoeken.</summary>
    public List<string> ActiveSites =>
        SiteSettings.Where(s => s.Enabled).Select(s => s.Site).ToList();

    // ---------- timing en melding ----------

    public SearchSchedule Schedule { get; set; } = new();

    // ---------- stand van zaken ----------

    private DateTime? _lastRun;
    public DateTime? LastRun
    {
        get => _lastRun;
        set
        {
            if (SetProperty(ref _lastRun, value)) OnPropertyChanged(nameof(StatusLabel));
        }
    }

    /// <summary>
    /// Hoeveel zoekertjes van de laatste beurt je nog niet bekeken hebt: de teller in de
    /// lijst met zoekopdrachten. Dat is niet hetzelfde als "nieuw bij de laatste beurt".
    /// Tot september 2026 was het dat wel, en dan wiste elke volgende beurt de vorige
    /// nieuwe: de planner om 8u vond er 48, jij drukte op het driehoekje, en de teller
    /// stond op 0 zonder dat je er één gezien had. Zie <see cref="IsUnviewed"/>.
    /// </summary>
    private int _newCount;
    public int NewCount
    {
        get => _newCount;
        set => SetProperty(ref _newCount, value);
    }

    /// <summary>
    /// Wanneer je deze zoekopdracht laatst opende (dubbelklik, Enter of de teller). Leeg:
    /// nog nooit, en dan is alles nieuw. Staat in een eigen kolom en niet in het JSON-blokje
    /// <c>config</c>: dat blokje schrijft ook een beurt die al liep voor je keek, en die zou
    /// het tijdstip anders terugzetten.
    /// </summary>
    public DateTimeOffset? LastViewed { get; set; }

    /// <summary>
    /// Heb je een zoekertje nog niet bekeken? Ja als het nooit eerder gezien werd
    /// (<paramref name="firstSeen"/> leeg), of voor het eerst opdook nadat je de
    /// zoekopdracht laatst opende. Zo stapelen de nieuwe op over de beurten heen, tot je
    /// kijkt - en een zoekertje dat één beurt ontbrak (een site die mislukte) is nog
    /// altijd nieuw wanneer het terugkomt.
    /// </summary>
    public bool IsUnviewed(DateTimeOffset? firstSeen) =>
        firstSeen is null || LastViewed is null || firstSeen > LastViewed;

    /// <summary>Draait deze zoekopdracht op dit moment?</summary>
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value)) OnPropertyChanged(nameof(StatusLabel));
        }
    }

    /// <summary>Regel onder de naam in de lijst: het schema en wanneer hij laatst liep.</summary>
    public string StatusLabel
    {
        get
        {
            if (IsRunning) return "bezig met zoeken...";

            var stuk = Schedule.Describe();

            if (LastRun is { } run)
            {
                var geleden = DateTime.Now - run;

                stuk += geleden switch
                {
                    { TotalMinutes: < 1 } => " · net gedraaid",
                    { TotalMinutes: < 60 } => $" · {(int)geleden.TotalMinutes} min geleden",
                    { TotalHours: < 24 } => $" · {(int)geleden.TotalHours} uur geleden",
                    _ => $" · {run:d MMM HH:mm}"
                };
            }

            if (LastErrors.Count > 0)
                stuk += LastErrors.Count == 1 ? " · 1 site mislukt" : $" · {LastErrors.Count} sites mislukt";

            return stuk;
        }
    }

    /// <summary>Meldt de lijst dat de tekst onder het zoekwoord herberekend moet worden.</summary>
    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ErrorSummary));
    }

    // ---------- wat er misliep ----------

    /// <summary>
    /// Welke sites bij de laatste beurt mislukten, met hun melding. Staat in het
    /// JSON-blokje van de zoekopdracht, zodat de lijst het ook na een herstart toont.
    ///
    /// Waarom dit er is: een geplande zoekopdracht draait zonder dat iemand kijkt. De
    /// fouten stonden enkel in het logboek, dus een verlopen aanmelding bij Facebook
    /// bleef wekenlang onopgemerkt - en zonder berichten leek het gewoon alsof er
    /// niets nieuws te koop was.
    /// </summary>
    public Dictionary<string, string> LastErrors { get; set; } = new();

    /// <summary>Hoeveel beurten na elkaar elke site mislukte. Een site die weer lukt, valt eruit.</summary>
    public Dictionary<string, int> FailureStreaks { get; set; } = new();

    /// <summary>Na zoveel mislukte beurten op rij komt er een melding.</summary>
    public const int MeldenNaMislukkingen = 2;

    /// <summary>
    /// Hoeveel zoekertjes elke site bij de laatste beurt zonder fout gaf. Daaraan is te zien
    /// dat een site die ineens niets meer vindt, waarschijnlijk stuk is (zie
    /// <see cref="VerdachtLeeg"/>). Staat in het JSON-blokje, net als de fouten.
    /// </summary>
    public Dictionary<string, int> LastCounts { get; set; } = new();

    /// <summary>Vanaf zoveel zoekertjes vorige keer is "nu niets" verdacht.</summary>
    public const int VerdachtVanaf = 10;

    /// <summary>Na zoveel verdachte beurten op rij geldt "niets" als het nieuwe normaal.</summary>
    public const int LeegAanvaardNa = 3;

    /// <summary>
    /// Is het verdacht dat deze site nu niets vond? Dan de melding, anders null.
    ///
    /// Waarom dit er is: een site die haar opmaak wijzigt, geeft geen fout maar een lege
    /// lijst. De selector vindt niets, de zoekopdracht "lukt", en een geplande zoekopdracht
    /// meldt wekenlang niets nieuws. Nul terwijl het vorige keer tien of meer waren, telt
    /// daarom als fout: op de tab, in de lijst, en na twee beurten als melding.
    ///
    /// Maar een zoekterm kan ook echt uitverkocht raken. Na drie verdachte beurten op rij
    /// (de melding is dan al verstuurd) wordt nul aanvaard, anders blijft die waarschuwing
    /// voor altijd staan.
    /// </summary>
    public string? VerdachtLeeg(string site, int aantal)
    {
        if (aantal > 0) return null;
        if (!LastCounts.TryGetValue(site, out var vorige) || vorige < VerdachtVanaf) return null;
        if (FailureStreaks.GetValueOrDefault(site) >= LeegAanvaardNa) return null;

        return $"vond niets, terwijl de vorige beurt er {vorige} gaf. " +
               "De site is misschien veranderd of toont een controlepagina; probeer ze in Sites beheren met Testen.";
    }

    public bool HasErrors => LastErrors.Count > 0;

    /// <summary>
    /// De meldingen onder elkaar, voor de tooltip op de regel in de lijst. Null
    /// zonder fouten: een lege tekst gaf een leeg tooltipvakje.
    /// </summary>
    public string? ErrorSummary => LastErrors.Count == 0
        ? null
        : string.Join(Environment.NewLine, LastErrors.Select(p => $"{p.Key}: {p.Value}"));

    /// <summary>
    /// Verwerkt wat er bij een beurt misliep, en geeft de sites terug die nu voor de
    /// tweede keer op rij mislukten. Daarover hoort één melding te komen: één
    /// mislukte beurt kan een haperende verbinding zijn, twee op rij wijst op iets dat
    /// de gebruiker moet oplossen. Daarna blijft het stil tot de site weer lukt, anders
    /// krijg je elk uur hetzelfde bericht.
    /// </summary>
    /// <param name="ranSites">De sites die in deze beurt meededen (ook de overgeslagen).</param>
    /// <param name="errors">Per mislukte site de melding.</param>
    /// <param name="counts">Per site hoeveel zoekertjes ze gaf; zie <see cref="LastCounts"/>.</param>
    public List<string> RecordRun(IEnumerable<string> ranSites, IReadOnlyDictionary<string, string> errors,
                                  IReadOnlyDictionary<string, int>? counts = null)
    {
        var netKapot = new List<string>();

        foreach (var site in ranSites)
        {
            if (errors.ContainsKey(site))
            {
                var opRij = FailureStreaks.GetValueOrDefault(site) + 1;
                FailureStreaks[site] = opRij;

                if (opRij == MeldenNaMislukkingen) netKapot.Add(site);
            }
            else
            {
                FailureStreaks.Remove(site);

                // Enkel een beurt zonder fout wordt het nieuwe ijkpunt.
                if (counts is not null && counts.TryGetValue(site, out var aantal))
                    LastCounts[site] = aantal;
            }
        }

        LastErrors = new Dictionary<string, string>(errors);
        RefreshStatus();

        return netKapot;
    }

    /// <summary>Mag dit zoekertje meetellen voor deze zoekopdracht?</summary>
    public bool Matches(Listing listing) =>
        !PhotosOnly || listing.ImageUrls.Count > 0;

    // ---------- oud formaat ----------

    /// <summary>
    /// Namen van de aangevinkte bronnen. Enkel nog voor zoekopdrachten die
    /// bewaard zijn voor <see cref="SiteSettings"/> bestond; bij het inlezen
    /// worden ze daarnaar omgezet.
    /// </summary>
    public List<string> Sites { get; set; } = new();

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Postcode { get; set; } = "";
    public int RadiusKm { get; set; }

    /// <summary>
    /// Zet een zoekopdracht uit het oude formaat om: één gedeelde prijs en een
    /// lijstje sitenamen worden instellingen per site.
    /// </summary>
    public void MigrateLegacySites()
    {
        if (SiteSettings.Count > 0 || Sites.Count == 0) return;

        SiteSettings = Sites.Select(naam => new SiteSetting
        {
            Site = naam,
            Enabled = true,
            PriceMin = MinPrice,
            PriceMax = MaxPrice,
            Postcode = Postcode,
            RadiusKm = RadiusKm
        }).ToList();
    }
}

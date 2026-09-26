using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Services;

/// <summary>
/// Bepaalt wat een toestel ongeveer waard is, uit wat er vandaag voor hetzelfde model gevraagd
/// wordt. Opgeroepen met een rechtsklik op een foto (<c>PriceIndicationWindow</c>).
///
/// Waarom het niet volstaat om "de mediaan van alle treffers" te nemen: gemeten op 17 september
/// 2026 voor een Denon DCD-520 op 2dehands en Marktplaats.
///
///   - De DCD-520AE is een ander, nieuwer toestel dat meer kost (€ 80 tot € 250). Wie enkel op
///     "dcd520" zoekt, telt die mee en komt te hoog uit. Daarom: het exacte model telt, een
///     ander achtervoegsel is een variant met zijn eigen prijsvork.
///   - Catawiki adverteert zijn kavels op 2dehands, met het huidige bod als "vaste prijs" (€ 24).
///     Een bod dat nog kan stijgen, trekt de marktwaarde naar beneden. Daarom tellen veilingen
///     niet mee (<see cref="SiteDefinition.IsAuction"/> en <see cref="SiteDefinition.AuctionSellers"/>).
///   - Sets ("PMA 525R / DCD 520 AE / TU 1500 AE"), defecte toestellen ("luie laser"), "bieden"
///     zonder bedrag en onzinprijzen (€ 8.888.888) horen er ook niet bij.
///
/// Wat overblijft, geeft een mediaan en een prijsvork. Zijn het er te weinig, dan verbreedt de
/// app naar toestellen uit dezelfde reeks ("Denon DCD"), duidelijk gelabeld als geen exact model.
/// Geen AI: de eigenaar koos op 17 september 2026 voor enkel verbreden.
/// </summary>
public static class PriceIndicator
{
    /// <summary>Onder dit aantal vraagprijzen zegt de app "te weinig gegevens" en verbreedt ze.</summary>
    public const int MinimumVoorMarktwaarde = 3;

    /// <summary>Hoeveel zoekertjes per site. Eén pagina van een API is genoeg voor een prijsvork.</summary>
    private const int PerSite = 100;

    /// <summary>
    /// Woorden die voor een getal kunnen staan zonder een model te zijn: "lot 63", "uit 1983",
    /// "maat 42". Zonder deze lijst zou "Lot 63 - muziek cd's" een modelnummer hebben.
    /// </summary>
    private static readonly HashSet<string> GeenModel = new(StringComparer.OrdinalIgnoreCase)
    {
        "lot", "kavel", "nr", "no", "nummer", "art", "ref", "maat", "size", "jaar", "jaren", "anno",
        "uit", "van", "voor", "met", "en", "de", "het", "een", "in", "op", "aan", "tot", "ca", "circa",
        "from", "the", "and", "for", "with", "von", "mit", "aus", "und", "der", "die", "das",
        "le", "la", "les", "des", "du", "pour", "avec", "sur", "cm", "mm", "kg", "gr", "watt",
        "inch", "stuks", "pcs"
    };

    /// <summary>
    /// Losse woordjes van twee letters na een modelnummer die geen achtervoegsel zijn:
    /// "DCD-520 - Cd-speler" is een DCD-520, "DCD 520 AE" een DCD-520AE.
    /// </summary>
    private static readonly HashSet<string> GeenAchtervoegsel = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "dj", "en", "de", "la", "le", "du", "et", "un", "op", "in", "te", "zu", "im", "am",
        "of", "to", "is", "nr", "no", "ok", "tv", "pc", "hi", "lp", "cm", "mm", "kg", "fm", "mp"
    };

    /// <summary>
    /// Woorden die in het voorstel niet als merk mogen dienen: ze zeggen niets over het toestel,
    /// of het zijn soorten toestellen. "Lecteur CD DCD-520" wordt zo "DCD-520" en niet
    /// "CD DCD-520" - elk woord in de zoekterm moet immers in de titel staan.
    /// </summary>
    private static readonly HashSet<string> Opvulling = new(StringComparer.OrdinalIgnoreCase)
    {
        "te", "koop", "gratis", "nieuw", "nieuwe", "mooi", "mooie", "zgan", "perfecte", "goede",
        "staat", "prachtige", "vintage", "a", "an", "et", "und", "of",
        "cd", "dvd", "lp", "speler", "cd-speler", "player", "lecteur", "versterker", "amplifier",
        "receiver", "tuner", "tv", "hifi", "stereo"
    };

    private static readonly Regex Gezocht = new(
        @"\b(gezocht|gevraagd|zoek|zoeke|gesucht|suche|recherche|cherche|wanted|wtb)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Defect = new(
        @"defect|defekt|kapot|onderdelen|for parts|pour pi[eè]ces|pi[eè]ces d[eé]tach[eé]es|ersatzteil|" +
        @"bastler|werkt niet|doet het niet|ne fonctionne pas|funktioniert nicht|niet werkend|luie laser|" +
        @"reparatie|\bhs\b|\brepair\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SetWoord = new(
        @"\b(set|sets|paket|lot)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Iets dat twee toestellen verbindt. Een tweede modelnummer alleen is nog geen set:
    /// "Denon DCD-625 | KSS-240A loopwerk" is één speler met een nieuwe laser.
    /// </summary>
    private static readonly Regex Verbinding = new(
        @"[+&/,]|\b(en|and|und|et)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Toebehoren en onderdelen. Telt enkel als het woord vóór het model staat: "Afstandsbediening
    /// Denon rc-203 dcd-800" is een afstandsbediening, "Denon DCD-820 met afstandsbediening" een speler.
    /// Gemeten op 17 september 2026: één verkoper had er vijftien op Marktplaats.
    /// </summary>
    private static readonly Regex Toebehoren = new(
        @"afstandsbediening|afstandbediening|\bremote\b|t[eé]l[eé]commande|fernbedienung|handleiding|" +
        @"\bmanual\b|bedienungsanleitung|loopwerk|laser ?unit|\bsnaar\b|\briem\b|\bbelt\b|\bkabel\b|\bcable\b|" +
        @"\b(voor|for|pour|für|fits)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Een modelnummer in een titel: letters (eventueel twee groepjes met een streepje, "SL-PJ"),
    /// eventueel een streepje of spatie, cijfers, en een kort achtervoegsel eraan vast. "DCD-520",
    /// "dcd520ae", "PMA 525R", "SL-PJ22". Een getal vooraan ("32-bit") telt niet.
    ///
    /// Die twee groepjes letters kwamen er op 17 september 2026 bij: van "Technics - SL-PJ22" las
    /// de app enkel "PJ22", met "SL" als merk. Het voorstel werd "SL PJ22", zonder Technics.
    /// </summary>
    private static readonly Regex Modelcode = new(
        @"(?<![\p{L}\d])(?<l>\p{L}{2,6}(?:-\p{L}{1,6})?)(?<sep>[\s\-_./]{0,3})(?<d>\d{2,6})(?<s>\p{L}{0,3})(?![\p{L}\d])",
        RegexOptions.CultureInvariant);

    /// <summary>Hetzelfde voor wat de gebruiker intypt, waar de letters ook mogen ontbreken ("iphone 13").</summary>
    private static readonly Regex TermCode = new(
        @"(?<![\p{L}\d])(?:(?<l>\p{L}{2,6}(?:-\p{L}{1,6})?)(?<sep>[\s\-_./]{0,3}))?(?<d>\d{2,6})(?<s>\p{L}{0,3})(?![\p{L}\d])",
        RegexOptions.CultureInvariant);

    /// <summary>Een los achtervoegsel na het modelnummer in de zoekterm: "dcd 520 ae".</summary>
    private static readonly Regex LosAchtervoegsel = new(
        @"^[\s\-_./]{1,3}(?<s>\p{L}{2})(?![\p{L}\d])", RegexOptions.CultureInvariant);

    /// <summary>
    /// De zoekterm, ontleed: het modelnummer en de woorden eromheen. <paramref name="LetterParts"/> zijn
    /// de groepjes letters ("sl", "pj"); <paramref name="Joined"/> zegt of de cijfers er in de zoekterm
    /// vast aan stonden ("SL-PJ22") of met een streepje of spatie ("DCD-520"), voor de weergave.
    /// </summary>
    internal sealed record TermModel(IReadOnlyList<string> LetterParts, string Digits, string Suffix, bool Joined,
                                     IReadOnlyList<string> Words, string WordsText)
    {
        public bool HasModel => Digits.Length > 0;

        /// <summary>De letters aan elkaar, om te vergelijken: "slpj", "dcd".</summary>
        public string Letters => string.Concat(LetterParts);

        /// <summary>Zoals een mens het schrijft: "DCD-520AE", "SL-PJ22", of "13" zonder letters.</summary>
        public string Display => Weergave(this, Digits, Suffix);
    }

    // ==================== zoekterm ====================

    /// <summary>
    /// Welke van de namen die de AI-controle van de foto's las, de moeite zijn om als zoekterm
    /// aan te bieden. Wat eruit komt zijn <b>voorstellen om aan te klikken</b>, geen zoekterm:
    /// de app kiest er zelf geen, en dat is met opzet.
    ///
    /// **Waarom niet automatisch**, gemeten op 26 september 2026 op zes echte zoekertjes waarvan
    /// de titel geen modelnummer gaf: er kwam precies één marktwaarde uit, en die ging over een
    /// <b>Xbox 360 die op de achtergrond van een stereoset stond</b> (± € 7,98 uit 127
    /// vraagprijzen). De twee keer dat de foto het juiste typenummer wél gaf - "C77ES" en
    /// "KX-W407D" - leverde de prijsindicatie 0 vergelijkingen op, want de sites schrijven dat
    /// anders of hebben er niets van te koop. Een foto toont nu eenmaal meer dan het voorwerp,
    /// en welke naam het voorwerp is, ziet een mens in één oogopslag en een model niet.
    ///
    /// Wat het wél doet: de namen op een zinnige volgorde zetten. Wat op een typenummer lijkt
    /// eerst (dezelfde herkenner als hierboven), want dat is wat je zoekt en juist wat je op een
    /// kleine foto niet kan lezen.
    /// </summary>
    public static List<string> AlsZoektermen(IEnumerable<string> gelezen, int hoogstens = 15)
    {
        var schoon = new List<string>();

        foreach (var naam in gelezen ?? Array.Empty<string>())
        {
            var kort = Regex.Replace(naam ?? "", @"\s+", " ").Trim();

            // Een losse letter of een teken zegt niets, en een halve zin is geen zoekterm.
            if (kort.Length < 2 || kort.Length > 40) continue;
            if (!kort.Any(char.IsLetterOrDigit)) continue;
            if (schoon.Contains(kort, StringComparer.OrdinalIgnoreCase)) continue;

            schoon.Add(kort);
        }

        return schoon
            .OrderByDescending(n => ParseTerm(n).HasModel)
            .Take(hoogstens)
            .ToList();
    }

    /// <summary>
    /// Een voorstel voor de zoekterm uit de titel van een zoekertje: het merk en het model,
    /// bv. "Denon DCD-520" uit "Denon - DCD-520 - Lecteur de CD". Zonder modelnummer de eerste
    /// woorden die iets zeggen. De gebruiker kan het voorstel altijd aanpassen.
    /// </summary>
    public static string SuggestTerm(string title)
    {
        var tekst = (title ?? "").Trim();

        foreach (Match m in Modelcode.Matches(tekst))
        {
            if (!IsEchteCode(m)) continue;

            // Het merk is meestal het woord vlak voor het model.
            var ervoor = tekst[..m.Index];
            var merk = Regex.Match(ervoor, @"(\p{L}[\p{L}'&.]+)[^\p{L}\d]*$");
            var code = m.Value.Trim();

            return merk.Success && !GeenModel.Contains(merk.Groups[1].Value) && !Opvulling.Contains(merk.Groups[1].Value)
                ? $"{merk.Groups[1].Value} {code}"
                : code;
        }

        var woorden = Regex.Matches(tekst, @"\p{L}[\p{L}\d']*")
            .Select(w => w.Value)
            .Where(w => w.Length >= 2 && !GeenModel.Contains(w) && !Opvulling.Contains(w))
            .Take(3);

        return string.Join(" ", woorden);
    }

    /// <summary>
    /// Een echt modelnummer, en niet "Lot 63" of "fiets 28 inch"? Met een spatie ertussen moet
    /// het in hoofdletters staan ("PMA 525R"); vast of met een streepje mag het in kleine letters.
    /// </summary>
    private static bool IsEchteCode(Match m)
    {
        var letters = m.Groups["l"].Value;
        if (GeenModel.Contains(letters.Split('-')[0])) return false;

        var metSpatie = m.Groups["sep"].Value.Any(char.IsWhiteSpace);
        return !metSpatie || letters == letters.ToUpperInvariant();
    }

    /// <summary>Ontleedt een zoekterm in een modelnummer en de andere woorden.</summary>
    internal static TermModel ParseTerm(string term)
    {
        var tekst = (term ?? "").Trim();

        foreach (Match m in TermCode.Matches(tekst))
        {
            var letters = m.Groups["l"].Value;
            if (GeenModel.Contains(letters.Split('-')[0])) letters = "";

            var achtervoegsel = m.Groups["s"].Value;
            var einde = m.Index + m.Length;

            if (achtervoegsel.Length == 0 &&
                LosAchtervoegsel.Match(tekst[einde..]) is { Success: true } los &&
                !GeenAchtervoegsel.Contains(los.Groups["s"].Value))
            {
                achtervoegsel = los.Groups["s"].Value;
                einde += los.Length;
            }

            // Wat voor het model stond maar geen deel ervan werd ("lot 63"), blijft een woord.
            var begin = letters.Length > 0 ? m.Groups["l"].Index : m.Groups["d"].Index;
            var rest = (tekst[..begin] + " " + tekst[einde..]).Trim();

            return new TermModel(
                letters.ToLowerInvariant().Split('-', StringSplitOptions.RemoveEmptyEntries),
                m.Groups["d"].Value, achtervoegsel.ToLowerInvariant(),
                Joined: letters.Length > 0 && m.Groups["sep"].Value.Length == 0,
                Woorden(rest), Regex.Replace(rest, @"\s+", " ").Trim());
        }

        return new TermModel(Array.Empty<string>(), "", "", false, Woorden(tekst), Regex.Replace(tekst, @"\s+", " ").Trim());
    }

    private static List<string> Woorden(string tekst) =>
        Normaal(tekst).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToList();

    /// <summary>Kleine letters, zonder accenten, en enkel letters en cijfers gescheiden door spaties.</summary>
    internal static string Normaal(string tekst)
    {
        var zonderAccenten = new StringBuilder();
        foreach (var c in (tekst ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            zonderAccenten.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return Regex.Replace(zonderAccenten.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Een model in dezelfde schrijfwijze als de zoekterm: "DCD-" + "520AE", of "SL-PJ" + "25".</summary>
    private static string Weergave(TermModel model, string cijfers, string achtervoegsel) =>
        model.LetterParts.Count > 0
            ? string.Join("-", model.LetterParts).ToUpperInvariant() + (model.Joined ? "" : "-") +
              cijfers + achtervoegsel.ToUpperInvariant()
            : cijfers + achtervoegsel.ToUpperInvariant();

    /// <summary>
    /// De letters van het model als stukje reguliere expressie, met ruimte voor een streepje of spatie
    /// tussen de groepjes en voor de cijfers: "SL-PJ22", "SL PJ22" en "slpj22" zijn hetzelfde.
    /// </summary>
    private static string LettersPatroon(TermModel model) =>
        string.Concat(model.LetterParts.Select(deel => Regex.Escape(deel) + @"[\s\-_./]{0,3}"));

    /// <summary>
    /// Wat er naar de sites gaat. Twee schrijfwijzen, want de sites lezen een modelnummer verschillend
    /// (gemeten op 17 september 2026 op 2dehands en Marktplaats):
    ///
    ///   - zoals getypt, "Technics SL-PJ22": 2dehands leest "SL-PJ22" als één woord "pj22", en vond met
    ///     "sl pj 22" 1 treffer die er niet over ging in plaats van de 2 juiste;
    ///   - los, "Denon dcd 520": met "DCD-520" ontbraken de zoekertjes die "DCD 520 AE" schrijven.
    ///
    /// De treffers van beide worden samengevoegd; de titel wordt daarna streng nagekeken.
    /// </summary>
    internal static List<string> ZoekVormen(TermModel model, string term)
    {
        var vormen = new List<string> { Regex.Replace(term.Trim(), @"\s+", " ") };

        if (model.HasModel)
        {
            // Stonden de cijfers vast aan de letters ("SL-PJ22"), dan blijven ze dat: "sl pj 22" gaf
            // op 2dehands niets bruikbaars, "sl pj22" wel.
            var los = Regex.Replace(
                $"{model.WordsText} {string.Join(" ", model.LetterParts)}{(model.Joined ? "" : " ")}{model.Digits}{model.Suffix}",
                @"\s+", " ").Trim();

            if (!vormen.Contains(los, StringComparer.OrdinalIgnoreCase)) vormen.Add(los);
        }

        return vormen;
    }

    // ==================== zoeken ====================

    /// <summary>
    /// Zoekt de term op alle sites met <see cref="SiteDefinition.PriceReference"/>, en verbreedt
    /// als het exacte model te weinig vraagprijzen heeft. Neemt hetzelfde slot als een gewone
    /// zoekopdracht: twee tegelijk botsen in de brug en in het browserprofiel.
    /// </summary>
    public static async Task<PriceIndication> DetermineAsync(string term, Listing? origin,
        IReadOnlyList<SiteDefinition> sites, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var model = ParseTerm(term);
        var ind = new PriceIndication { Term = term.Trim(), Model = model.HasModel ? model.Display : "" };

        if (origin is not null)
            ind.OriginIsAuction = IsVeiling(origin, sites.FirstOrDefault(s => s.Name == origin.Source));

        var bronnen = sites.Where(s => s.PriceReference).ToList();
        if (bronnen.Count == 0)
        {
            ind.NoSites = true;
            return ind;
        }

        if (!SearchRunner.Gate.Wait(0))
        {
            status?.Report("Wacht tot de lopende zoekopdracht klaar is...");
            await SearchRunner.Gate.WaitAsync(ct);
        }

        var klok = Stopwatch.StartNew();
        using var browserLease = BrowserPool.Lease();

        try
        {
            if (bronnen.Any(b => b.UseBridge))
            {
                status?.Report("Chrome klaarzetten voor de brug...");
                var brug = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(30));

                if (brug != BridgeStatus.Ready)
                {
                    foreach (var site in bronnen.Where(b => b.UseBridge))
                        ind.SiteErrors[site.Name] = "overgeslagen, " + ChromeLauncher.Describe(brug);

                    bronnen = bronnen.Where(b => !b.UseBridge).ToList();
                }
            }

            ind.SitesSearched.AddRange(bronnen.Select(b => b.Name));

            var gevonden = await ZoekAsync(ZoekVormen(model, term), bronnen, ind, status, ct);
            var anders = Analyse(ind, model, gevonden, origin);

            if (ind.Market is null || ind.Market.Count < MinimumVoorMarktwaarde)
            {
                if (!model.HasModel)
                {
                    ind.BroadNote = "Verbreden gaat niet zonder modelnummer. Probeer zelf een algemenere zoekterm.";
                }
                else if (model.Letters.Length == 0)
                {
                    ind.BroadNote = "Verbreden gaat niet: het modelnummer heeft geen letters die de reeks aangeven.";
                }
                else
                {
                    // De groepjes letters los: "Technics SL PJ" vond op Marktplaats 7 andere SL-PJ's,
                    // "Technics SL-PJ" geen enkele (gemeten op 17 september 2026).
                    ind.BroadTerm = $"{model.WordsText} {string.Join(" ", model.LetterParts).ToUpperInvariant()}".Trim();
                    status?.Report($"Te weinig vraagprijzen, verbreden naar '{ind.BroadTerm}'...");

                    // Wat de eerste zoektocht al over andere toestellen van dezelfde reeks vond,
                    // telt ook: zo levert een site die bij de tweede keer faalt, toch iets op.
                    var breed = await ZoekAsync(new List<string> { ind.BroadTerm }, bronnen, ind, status, ct);
                    AnalyseBroad(ind, model, anders.Concat(breed), origin);
                }
            }
        }
        finally
        {
            SearchRunner.Gate.Release();
        }

        Log.Write($"prijsindicatie '{ind.Term}': {ind.Market?.Count ?? 0} vraagprijzen" +
                  (ind.Market is { } markt ? $", mediaan {markt.Median:0.##}" : "") +
                  $", {ind.Variants.Count} variant(en)" +
                  (ind.Broad is { } breedte ? $", verbreed '{ind.BroadTerm}' {breedte.Count} prijzen" : "") +
                  $", {ind.SiteErrors.Count} site(s) mislukt, in {klok.Elapsed.TotalSeconds:F1}s");

        return ind;
    }

    /// <summary>
    /// Zoekt elke schrijfwijze op elke site, per site na elkaar. Een zoekertje dat bij twee
    /// schrijfwijzen terugkomt, staat er één keer in. Een site die bij één schrijfwijze faalt en bij
    /// de andere lukt, is niet mislukt.
    /// </summary>
    private static async Task<List<(Listing Listing, SiteDefinition Def)>> ZoekAsync(IReadOnlyList<string> vormen,
        List<SiteDefinition> bronnen, PriceIndication ind, IProgress<string>? status, CancellationToken ct)
    {
        var gevonden = new List<(Listing, SiteDefinition)>();
        var sleutels = new HashSet<string>();

        await SearchRunner.RunInLanesAsync(bronnen, d => d, async def =>
        {
            string? fout = null;
            var gelukt = false;

            foreach (var vorm in vormen)
            {
                try
                {
                    status?.Report($"{def.Name} doorzoeken op '{vorm}'...");
                    var resultaten = await SourceFactory.Create(def).SearchAsync(vorm, PerSite, new SearchFilters(), null, ct);
                    gelukt = true;

                    lock (gevonden)
                        foreach (var listing in resultaten)
                            if (sleutels.Add(listing.Key))
                                gevonden.Add((listing, def));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    fout = FriendlyError.Describe(ex);
                    Log.Write($"prijsindicatie: {def.Name} mislukte op '{vorm}' - {ex.Message}");
                }
            }

            if (!gelukt && fout is not null)
                lock (ind) ind.SiteErrors[def.Name] = fout;
        });

        return gevonden;
    }

    // ==================== opschonen en rekenen ====================

    /// <summary>
    /// Deelt de treffers in: het exacte model, varianten, en wat niet meetelt. Rekent de
    /// marktwaarde en de prijsvork per variant uit. Geeft terug wat over een ander toestel ging,
    /// zodat het verbreden dat kan hergebruiken.
    /// </summary>
    internal static List<(Listing Listing, SiteDefinition Def)> Analyse(PriceIndication ind, TermModel model,
        IEnumerable<(Listing Listing, SiteDefinition Def)> gevonden, Listing? origin)
    {
        var anders = new List<(Listing, SiteDefinition)>();
        var patroon = model.HasModel ? ModelPatroon(model) : null;
        var gezien = new HashSet<string>();

        foreach (var (listing, def) in gevonden)
        {
            if (IsZelfde(listing, origin)) continue;

            var titel = listing.Title ?? "";
            if (!AlleWoorden(titel, model.Words))
            {
                anders.Add((listing, def));
                continue;
            }

            var item = new PriceComparable { Listing = listing, Kind = ComparableKind.Counted };
            var modelBegin = -1;

            if (patroon is not null)
            {
                var m = patroon.Match(titel);
                if (!m.Success)
                {
                    anders.Add((listing, def));
                    continue;
                }

                modelBegin = m.Index;
                var achtervoegsel = Achtervoegsel(m);
                item.Model = Weergave(model, model.Digits, achtervoegsel);
                if (!string.Equals(achtervoegsel, model.Suffix, StringComparison.OrdinalIgnoreCase))
                    item.Kind = ComparableKind.Variant;
            }

            // Hetzelfde zoekertje op twee sites (een handelaar op 2dehands en Marktplaats) telt één keer.
            if (!gezien.Add(Normaal(titel) + "|" + listing.Price)) continue;

            Uitsluiten(item, def, model, modelBegin);
            ind.Items.Add(item);
        }

        ind.OtherCount = anders.Count;

        ind.Market = Reeks(ind.Items.Where(i => i.Kind == ComparableKind.Counted).ToList());

        foreach (var groep in ind.Items.Where(i => i.Kind == ComparableKind.Variant).GroupBy(i => i.Model))
            if (Reeks(groep.ToList()) is { } reeks)
                ind.Variants[groep.Key] = reeks;

        return anders;
    }

    /// <summary>De toestellen uit dezelfde reeks ("Denon DCD"), zonder het model zelf en zijn varianten.</summary>
    internal static void AnalyseBroad(PriceIndication ind, TermModel model,
        IEnumerable<(Listing Listing, SiteDefinition Def)> gevonden, Listing? origin)
    {
        var reeks = new Regex(
            @"(?<![\p{L}\d])" + LettersPatroon(model) + @"(?<d>\d{2,6})(?<s>\p{L}{0,3})(?![\p{L}\d])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var sleutels = new HashSet<string>(ind.Items.Select(i => i.Listing.Key));
        var gezien = new HashSet<string>();

        foreach (var (listing, def) in gevonden)
        {
            if (IsZelfde(listing, origin) || !sleutels.Add(listing.Key)) continue;

            var titel = listing.Title ?? "";
            if (!AlleWoorden(titel, model.Words)) continue;

            var m = reeks.Match(titel);
            if (!m.Success || m.Groups["d"].Value == model.Digits) continue;
            if (!gezien.Add(Normaal(titel) + "|" + listing.Price)) continue;

            var item = new PriceComparable
            {
                Listing = listing,
                Kind = ComparableKind.Related,
                Model = Weergave(model, m.Groups["d"].Value, m.Groups["s"].Value)
            };

            Uitsluiten(item, def, model with { Digits = m.Groups["d"].Value }, m.Index);
            ind.BroadItems.Add(item);
        }

        ind.Broad = Reeks(ind.BroadItems.Where(i => i.Kind == ComparableKind.Related).ToList());
    }

    /// <summary>Het model in een titel, met het achtervoegsel dat erbij staat als aparte groep.</summary>
    private static Regex ModelPatroon(TermModel model) => new(
        @"(?<![\p{L}\d])" + LettersPatroon(model) + Regex.Escape(model.Digits) +
        @"(?!\d)(?:(?<vast>\p{L}{1,3})(?![\p{L}\d])|[\s\-_./]{1,3}(?<los>\p{L}{2})(?![\p{L}\d]))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Achtervoegsel(Match m)
    {
        if (m.Groups["vast"].Success) return m.Groups["vast"].Value.ToLowerInvariant();

        var los = m.Groups["los"].Value;
        return los.Length > 0 && !GeenAchtervoegsel.Contains(los) ? los.ToLowerInvariant() : "";
    }

    private static bool AlleWoorden(string titel, IReadOnlyList<string> woorden)
    {
        if (woorden.Count == 0) return true;

        var genormaliseerd = " " + Normaal(titel);
        return woorden.All(w => genormaliseerd.Contains(" " + w, StringComparison.Ordinal));
    }

    private static bool IsZelfde(Listing listing, Listing? origin) =>
        origin is not null &&
        (listing.Key == origin.Key ||
         (!string.IsNullOrWhiteSpace(origin.Url) && string.Equals(listing.Url, origin.Url, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Is de prijs van dit zoekertje een bod? Een veilingsite, of een veilinghuis op een gewone site.</summary>
    internal static bool IsVeiling(Listing listing, SiteDefinition? def) =>
        def is not null &&
        (def.IsAuction ||
         (!string.IsNullOrWhiteSpace(listing.Seller) &&
          def.AuctionSellers.Any(s => string.Equals(s.Trim(), listing.Seller.Trim(), StringComparison.OrdinalIgnoreCase))));

    /// <summary>
    /// Zet wat niet meetelt apart, met de belangrijkste reden eerst: iemand die zoekt, toebehoren,
    /// een bod, defect, een set, geen prijs. <paramref name="modelBegin"/> is waar het model in de
    /// titel begint, of -1 zonder modelnummer.
    /// </summary>
    private static void Uitsluiten(PriceComparable item, SiteDefinition def, TermModel model, int modelBegin)
    {
        var titel = item.Listing.Title ?? "";

        if (Gezocht.IsMatch(titel)) item.Kind = ComparableKind.Wanted;
        else if (modelBegin > 0 && Toebehoren.IsMatch(titel[..modelBegin])) item.Kind = ComparableKind.Accessory;
        else if (IsVeiling(item.Listing, def)) item.Kind = ComparableKind.Auction;
        else if (Defect.IsMatch(titel)) item.Kind = ComparableKind.Defect;
        else if (IsSet(titel, model)) item.Kind = ComparableKind.Set;
        else if (item.Listing.Price is not > 0) item.Kind = ComparableKind.NoPrice;
    }

    /// <summary>
    /// Het woord "set", of een ander toestel dat er met een "+", "/", komma of "en" bij staat
    /// ("PMA 525R / DCD 520 AE", "AVR-1912 AV Receiver en DCD-520AE").
    /// </summary>
    private static bool IsSet(string titel, TermModel model)
    {
        if (SetWoord.IsMatch(titel)) return true;
        if (!model.HasModel || !Verbinding.IsMatch(titel)) return false;

        var eigen = (model.Letters + model.Digits).ToLowerInvariant();

        return Modelcode.Matches(titel).Any(m =>
        {
            if (!IsEchteCode(m)) return false;

            var code = (m.Groups["l"].Value.Replace("-", "") + m.Groups["d"].Value).ToLowerInvariant();

            // Het eigen model, ook als de titel het anders schrijft: "SL PJ22" leest als "PJ22", en
            // dat is het einde van "slpj22". Zonder letters in de zoekterm ("iphone 13") telt het getal.
            return code != eigen && !eigen.EndsWith(code, StringComparison.Ordinal) &&
                   !(model.Letters.Length == 0 && m.Groups["d"].Value == model.Digits);
        });
    }

    /// <summary>
    /// De prijsvork van een groep. Vanaf vier prijzen gaan de uitschieters eruit (de gewone
    /// kwartielregel, anderhalve kwartielafstand) en is de vork het eerste tot het derde kwartiel;
    /// daaronder de laagste tot de hoogste prijs. Null als niemand een prijs heeft.
    /// </summary>
    internal static PriceRange? Reeks(List<PriceComparable> groep)
    {
        var metPrijs = groep.Where(i => i.Listing.Price is > 0).ToList();
        if (metPrijs.Count == 0) return null;

        if (metPrijs.Count >= 4)
        {
            var alle = metPrijs.Select(i => i.Listing.Price!.Value).OrderBy(p => p).ToList();
            var q1 = Kwantiel(alle, 0.25m);
            var q3 = Kwantiel(alle, 0.75m);
            var marge = 1.5m * (q3 - q1);

            foreach (var item in metPrijs.Where(i => i.Listing.Price < q1 - marge || i.Listing.Price > q3 + marge))
                item.Kind = ComparableKind.Outlier;

            metPrijs = metPrijs.Where(i => i.Kind != ComparableKind.Outlier).ToList();
        }

        var prijzen = metPrijs.Select(i => i.Listing.Price!.Value).OrderBy(p => p).ToList();
        var veel = prijzen.Count >= 4;

        return new PriceRange
        {
            Count = prijzen.Count,
            Median = Kwantiel(prijzen, 0.5m),
            Low = veel ? Kwantiel(prijzen, 0.25m) : prijzen[0],
            High = veel ? Kwantiel(prijzen, 0.75m) : prijzen[^1]
        };
    }

    /// <summary>
    /// Een kwantiel uit een gesorteerde reeks, met lineaire tussenwaarde. Dat is dezelfde methode
    /// die de meeste rekenbladen gebruiken, zodat een controle met de hand hetzelfde getal geeft.
    /// Stond tot 17 september 2026 in `PriceInsight`, bij de koopjesmarkering die eruit is.
    /// </summary>
    internal static decimal Kwantiel(List<decimal> gesorteerd, decimal deel)
    {
        var positie = (gesorteerd.Count - 1) * deel;

        var onder = (int)Math.Floor(positie);
        var boven = Math.Min(onder + 1, gesorteerd.Count - 1);

        return gesorteerd[onder] + (gesorteerd[boven] - gesorteerd[onder]) * (positie - onder);
    }
}

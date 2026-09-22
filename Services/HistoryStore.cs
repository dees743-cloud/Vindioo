using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zentrix.Models;

namespace Zentrix.Services;

/// <summary>
/// Bewaart vastgezette zoekopdrachten en onthoudt welke zoekertjes
/// al eens getoond zijn, zodat nieuwe herkenbaar blijven.
///
/// Naast naam en zoekterm heeft elke zoekopdracht een blokje JSON met zijn
/// sites, filters en schema. Dat blokje is wat de planner uitvoert wanneer de
/// app op de achtergrond draait.
/// </summary>
public class HistoryStore
{
    private readonly string _connectionString;

    public HistoryStore()
    {
        Directory.CreateDirectory(AppPaths.Folder);
        _connectionString = $"Data Source={AppPaths.DatabaseFile}";

        if (Initialize()) StartpuntBekeken();
    }

    /// <summary>
    /// Maakt de tabellen aan als ze nog niet bestaan. Geeft true terug wanneer de kolom
    /// <c>lastViewed</c> er net bijkwam: dan moeten de bestaande zoekopdrachten nog een
    /// startpunt krijgen (zie <see cref="StartpuntBekeken"/>).
    /// </summary>
    private bool Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS searches (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                name      TEXT NOT NULL,
                query     TEXT NOT NULL,
                sites     TEXT NOT NULL DEFAULT '',
                minPrice  REAL,
                maxPrice  REAL,
                lastRun   TEXT,
                country   TEXT NOT NULL DEFAULT '',
                photosOnly INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS seen (
                searchId  INTEGER NOT NULL,
                key       TEXT NOT NULL,
                firstSeen TEXT NOT NULL,
                PRIMARY KEY (searchId, key)
            );

            CREATE TABLE IF NOT EXISTS outcome (
                searchId    INTEGER NOT NULL,
                position    INTEGER NOT NULL,
                source      TEXT NOT NULL,
                externalId  TEXT NOT NULL,
                title       TEXT NOT NULL,
                price       REAL,
                priceLabel  TEXT NOT NULL DEFAULT '',
                location    TEXT NOT NULL DEFAULT '',
                url         TEXT NOT NULL DEFAULT '',
                image       TEXT NOT NULL DEFAULT '',
                largeImage  TEXT NOT NULL DEFAULT '',
                isNew       INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (searchId, position)
            );

            CREATE TABLE IF NOT EXISTS favorites (
                key         TEXT PRIMARY KEY,
                source      TEXT NOT NULL,
                externalId  TEXT NOT NULL,
                title       TEXT NOT NULL,
                price       REAL,
                priceLabel  TEXT NOT NULL DEFAULT '',
                location    TEXT NOT NULL DEFAULT '',
                url         TEXT NOT NULL DEFAULT '',
                image       TEXT NOT NULL DEFAULT '',
                largeImage  TEXT NOT NULL DEFAULT '',
                addedAt     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS recent (
                query TEXT PRIMARY KEY,
                ranAt TEXT NOT NULL
            );
            """;

        // Een oudere databank heeft nog een tabel "hidden": de zoekertjes die met het oogje
        // weggeklikt waren. Die mogelijkheid is er sinds september 2026 uit - je zoekt om te
        // zien, niet om te verbergen - en de tabel wordt niet meer gelezen. Ze blijft staan
        // in plaats van weggegooid te worden; dan gaat er niets verloren.

        command.ExecuteNonQuery();

        // Alles wat een zoekopdracht rijker maakt dan naam en prijs — de sites
        // met hun eigen filters, het schema, de verfijning — staat als JSON in
        // een enkele kolom. Per veld een kolom zou betekenen dat elke nieuwe
        // instelling opnieuw een migratie vraagt; zo blijft de tabel stabiel.
        AddColumn(connection, "searches", "config", "TEXT NOT NULL DEFAULT ''");
        AddColumn(connection, "searches", "newCount", "INTEGER NOT NULL DEFAULT 0");

        // Wanneer je de zoekopdracht laatst opende; zie SavedSearch.LastViewed.
        return AddColumn(connection, "searches", "lastViewed", "TEXT");
    }

    /// <summary>
    /// Voegt een kolom toe wanneer die er nog niet staat. SQLite kent geen
    /// "ADD COLUMN IF NOT EXISTS", dus we kijken eerst in de tabelbeschrijving.
    /// Geeft true terug als de kolom er net bijkwam.
    /// </summary>
    private static bool AddColumn(SqliteConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name";
        check.Parameters.AddWithValue("$name", column);

        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return false;

        using var add = connection.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        add.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// Eén keer, wanneer de kolom <c>lastViewed</c> er net bijkwam: geeft elke zoekopdracht die
    /// al gedraaid heeft een tijdstip "laatst bekeken" dat haar teller laat staan. Zonder
    /// startpunt zou alles wat ooit gezien werd als nieuw tellen: bij "Computer" 427
    /// zoekertjes in plaats van 64.
    ///
    /// Het startpunt komt uit de bewaarde resultaten van de laatste beurt: het laatste
    /// "eerst gezien" van wat toen al bekend was. Wat daarna opdook, is precies wat die
    /// beurt nieuw vond. Een zoekopdracht die nog nooit draaide, blijft leeg: voor haar is
    /// alles nieuw, zoals altijd bij de eerste beurt.
    /// </summary>
    private void StartpuntBekeken()
    {
        var zoekopdrachten = new List<(int Id, string? LastRun)>();

        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, lastRun FROM searches WHERE lastRun IS NOT NULL";
            using var reader = command.ExecuteReader();
            while (reader.Read()) zoekopdrachten.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        foreach (var (id, lastRun) in zoekopdrachten)
        {
            var gezien = GetSeen(id);
            var resultaten = GetOutcome(id);

            DateTimeOffset? EerstGezien(Listing l) =>
                gezien.TryGetValue(l.Key, out var t) ? t : null;

            var gekend = resultaten.Where(l => !l.IsNew).Select(EerstGezien).Where(t => t is not null).ToList();
            var nieuw = resultaten.Where(l => l.IsNew).Select(EerstGezien).Where(t => t is not null).ToList();

            // Wat toen al bekend was, geldt als bekeken. Was alles nieuw (de eerste beurt),
            // dan net voor het eerste. Zonder bewaarde resultaten: het tijdstip van de beurt.
            var startpunt = gekend.Count > 0 ? gekend.Max()!.Value
                : nieuw.Count > 0 ? nieuw.Min()!.Value.AddTicks(-1)
                : LeesTijdstip(lastRun) ?? DateTimeOffset.Now;

            // De teller opnieuw tellen met dat startpunt, zodat ze klopt met wat de lijst toont.
            var nogNiet = resultaten.Count(l => EerstGezien(l) is not { } t || t > startpunt);

            using var connection = Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE searches SET lastViewed = $viewed, newCount = $new WHERE id = $id";
            update.Parameters.AddWithValue("$viewed", startpunt.ToString("o"));
            update.Parameters.AddWithValue("$new", nogNiet);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();

            Log.Write($"zoekopdracht {id}: laatst bekeken op {startpunt:dd/MM HH:mm:ss} gezet, {nogNiet} nog niet bekeken");
        }
    }

    /// <summary>Een tijdstip zoals het in de databank staat ("o"-formaat, met de tijdzone erbij).</summary>
    private static DateTimeOffset? LeesTijdstip(string? tekst) =>
        DateTimeOffset.TryParse(tekst, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    // ---------- zoekopdrachten ----------

    public List<SavedSearch> GetAll()
    {
        var list = new List<SavedSearch>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, query, sites, minPrice, maxPrice, lastRun, config, newCount, lastViewed " +
            "FROM searches ORDER BY name";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var search = new SavedSearch
            {
                Id = reader.GetInt32(0),

                // Kolom 1 is de oude naam. Die wordt niet meer gebruikt: de naam
                // is gewoon het zoekwoord met een hoofdletter.
                Query = reader.GetString(2),
                Sites = reader.GetString(3).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
                MinPrice = reader.IsDBNull(4) ? null : (decimal)reader.GetDouble(4),
                MaxPrice = reader.IsDBNull(5) ? null : (decimal)reader.GetDouble(5),
                LastRun = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                NewCount = reader.GetInt32(8),
                LastViewed = reader.IsDBNull(9) ? null : LeesTijdstip(reader.GetString(9))
            };

            ReadConfig(search, reader.GetString(7));

            // Zoekopdrachten van voor de instellingen per site: het lijstje
            // namen en de ene gedeelde prijs worden nu instellingen per site.
            search.MigrateLegacySites();

            list.Add(search);
        }

        return list;
    }

    /// <summary>
    /// De rijke instellingen staan als JSON in een enkele kolom. Gaat er iets mis
    /// met dat blokje, dan houden we de zoekopdracht met zijn standaardwaarden:
    /// een stukgelopen instelling mag de hele lijst niet meenemen.
    /// </summary>
    private static void ReadConfig(SavedSearch search, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var config = JsonSerializer.Deserialize<SearchConfig>(json);
            if (config is null) return;

            search.PhotosOnly = config.PhotosOnly;
            search.SiteSettings = config.SiteSettings;
            search.Schedule = config.Schedule;

            // Ontbreken bij zoekopdrachten van voor september 2026: dan gewoon leeg.
            search.LastErrors = config.LastErrors ?? new();
            search.FailureStreaks = config.FailureStreaks ?? new();
            search.LastCounts = config.LastCounts ?? new();
        }
        catch (Exception ex)
        {
            Log.Write($"zoekopdracht '{search.Name}': instellingen onleesbaar - {ex.Message}");
        }
    }

    private static string WriteConfig(SavedSearch search) => JsonSerializer.Serialize(new SearchConfig
    {
        PhotosOnly = search.PhotosOnly,
        SiteSettings = search.SiteSettings,
        Schedule = search.Schedule,
        LastErrors = search.LastErrors,
        FailureStreaks = search.FailureStreaks,
        LastCounts = search.LastCounts
    });

    /// <summary>Vorm van het JSON-blokje in de kolom `config`.</summary>
    private sealed class SearchConfig
    {
        public bool PhotosOnly { get; set; }
        public List<SiteSetting> SiteSettings { get; set; } = new();
        public SearchSchedule Schedule { get; set; } = new();

        /// <summary>Wat er bij de laatste beurt misliep; zie <see cref="SavedSearch.LastErrors"/>.</summary>
        public Dictionary<string, string>? LastErrors { get; set; }

        /// <summary>Hoeveel beurten op rij elke site mislukte.</summary>
        public Dictionary<string, int>? FailureStreaks { get; set; }

        /// <summary>Hoeveel elke site vorige keer gaf; zie <see cref="SavedSearch.LastCounts"/>.</summary>
        public Dictionary<string, int>? LastCounts { get; set; }
    }

    /// <summary>
    /// Zet de sleutels van sites met een <see cref="SiteDefinition.IdPattern"/> om naar hun
    /// vaste id: "AlleVeilingen:https://.../kavel/123/...?fi=..." wordt "AlleVeilingen:123", in
    /// "al gezien", de favorieten en de bewaarde resultaten. Zonder dat telt elke kavel één
    /// keer opnieuw als nieuw zodra zo'n patroon erbij komt, met een melding over honderd
    /// zoekertjes die je al kende.
    ///
    /// Mag bij elke start draaien: een sleutel die al een id is, past niet meer op het patroon
    /// en blijft staan. Twee sleutels die hetzelfde id worden (de kavel van pagina 2 en die van
    /// pagina 3), worden er één. Geeft terug hoeveel rijen er veranderden.
    /// </summary>
    public int ApplyIdPatterns(IEnumerable<SiteDefinition> sites)
    {
        var totaal = 0;

        foreach (var site in sites)
        {
            if (site.Engine != SiteEngine.Generic || string.IsNullOrWhiteSpace(site.IdPattern)) continue;

            System.Text.RegularExpressions.Regex patroon;
            try { patroon = new System.Text.RegularExpressions.Regex(site.IdPattern); }
            catch (ArgumentException) { continue; }

            var voorvoegsel = site.Name + ":";

            string? NieuweSleutel(string sleutel)
            {
                if (!sleutel.StartsWith(voorvoegsel, StringComparison.Ordinal)) return null;
                var link = sleutel[voorvoegsel.Length..];
                var id = Sources.GenericSource.IdUitLink(patroon, link);
                return id == link ? null : voorvoegsel + id;
            }

            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            List<(long RowId, string Waarde)> Lees(string sql)
            {
                var rijen = new List<(long, string)>();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$voor", voorvoegsel);
                command.Parameters.AddWithValue("$naam", site.Name);
                using var reader = command.ExecuteReader();
                while (reader.Read()) rijen.Add((reader.GetInt64(0), reader.GetString(1)));
                return rijen;
            }

            void Schrijf(string sql, long rowId, string sleutel, string id)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$rij", rowId);
                command.Parameters.AddWithValue("$sleutel", sleutel);
                command.Parameters.AddWithValue("$id", id);
                totaal += command.ExecuteNonQuery();
            }

            const string metVoorvoegsel = "substr(key, 1, length($voor)) = $voor";

            foreach (var (rij, sleutel) in Lees($"SELECT rowid, key FROM seen WHERE {metVoorvoegsel}"))
                if (NieuweSleutel(sleutel) is { } nieuw)
                    Schrijf("UPDATE OR REPLACE seen SET key = $sleutel WHERE rowid = $rij", rij, nieuw, "");

            foreach (var (rij, sleutel) in Lees($"SELECT rowid, key FROM favorites WHERE {metVoorvoegsel}"))
                if (NieuweSleutel(sleutel) is { } nieuw)
                    Schrijf("UPDATE OR REPLACE favorites SET key = $sleutel, externalId = $id WHERE rowid = $rij",
                        rij, nieuw, nieuw[voorvoegsel.Length..]);

            foreach (var (rij, externId) in Lees("SELECT rowid, externalId FROM outcome WHERE source = $naam"))
                if (NieuweSleutel(voorvoegsel + externId) is { } nieuw)
                    Schrijf("UPDATE outcome SET externalId = $id WHERE rowid = $rij", rij, nieuw, nieuw[voorvoegsel.Length..]);

            transaction.Commit();
        }

        if (totaal > 0) Log.Write($"IdPattern: {totaal} sleutel(s) omgezet naar het vaste id van hun zoekertje");
        return totaal;
    }

    /// <summary>
    /// Een site kreeg een andere naam. Alles wat aan die naam hing, gaat mee: de sites van
    /// elke bewaarde zoekopdracht, en de sleutels van "al gezien", de favorieten en de
    /// bewaarde resultaten.
    ///
    /// Waarom dit er is: een site hangt aan zijn weergavenaam, niet aan zijn Id. Een
    /// zoekertje heet "Facebook Marketplace:123", en een zoekopdracht onthoudt "Facebook
    /// Marketplace". Tot september 2026 viel een hernoemde site daardoor stil uit elke
    /// zoekopdracht en telde alles weer als nieuw.
    /// Overschakelen op het Id zou elke bestaande sleutel breken; de naam meenemen niet.
    /// </summary>
    public void RenameSource(string oud, string nieuw)
    {
        if (string.Equals(oud, nieuw, StringComparison.Ordinal)) return;

        using (var connection = Open())
        using (var transaction = connection.BeginTransaction())
        {
            void Voer(string sql)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$oud", oud + ":");
                command.Parameters.AddWithValue("$nieuw", nieuw + ":");
                command.Parameters.AddWithValue("$oudNaam", oud);
                command.Parameters.AddWithValue("$nieuwNaam", nieuw);
                command.ExecuteNonQuery();
            }

            // Een sleutel is "Naam:extern-id". OR REPLACE: stond de nieuwe sleutel er al
            // (de site heette vroeger al eens zo), dan wint de hernoemde.
            const string nieuweSleutel = "$nieuw || substr(key, length($oud) + 1)";
            const string hoortErbij = "substr(key, 1, length($oud)) = $oud";

            Voer($"UPDATE OR REPLACE seen SET key = {nieuweSleutel} WHERE {hoortErbij}");
            Voer($"UPDATE OR REPLACE favorites SET key = {nieuweSleutel}, source = $nieuwNaam WHERE {hoortErbij}");
            Voer("UPDATE outcome SET source = $nieuwNaam WHERE source = $oudNaam");

            transaction.Commit();
        }

        foreach (var search in GetAll())
        {
            var geraakt = false;

            foreach (var setting in search.SiteSettings)
            {
                if (!string.Equals(setting.Site, oud, StringComparison.OrdinalIgnoreCase)) continue;
                setting.Site = nieuw;
                geraakt = true;
            }

            foreach (var lijst in new System.Collections.IDictionary[] { search.LastErrors, search.FailureStreaks, search.LastCounts })
            {
                if (!lijst.Contains(oud)) continue;
                lijst[nieuw] = lijst[oud];
                lijst.Remove(oud);
                geraakt = true;
            }

            if (geraakt) Update(search);
        }

        Log.Write($"site hernoemd: '{oud}' heet nu '{nieuw}'; zoekopdrachten en sleutels meegenomen");
    }

    /// <summary>Bewaart een nieuwe zoekopdracht en geeft het toegekende id terug.</summary>
    public int Add(SavedSearch search)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO searches (name, query, sites, minPrice, maxPrice, config, newCount)
            VALUES ($name, $query, '', NULL, NULL, $config, $new);
            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$name", search.Name);
        command.Parameters.AddWithValue("$query", search.Query);
        command.Parameters.AddWithValue("$config", WriteConfig(search));
        command.Parameters.AddWithValue("$new", search.NewCount);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Schrijft een gewijzigde zoekopdracht terug.</summary>
    public void Update(SavedSearch search)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE searches SET name = $name, query = $query, config = $config
            WHERE id = $id
            """;

        command.Parameters.AddWithValue("$name", search.Name);
        command.Parameters.AddWithValue("$query", search.Query);
        command.Parameters.AddWithValue("$config", WriteConfig(search));
        command.Parameters.AddWithValue("$id", search.Id);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Noteert dat een zoekopdracht gedraaid heeft en hoeveel er nieuw was.
    /// Apart van <see cref="MarkSeen"/>, want die loopt tijdens het zoeken
    /// meermaals; het tijdstip moet pas op het einde vastliggen, anders schuift
    /// de planner zijn eigen volgende beurt telkens vooruit.
    /// </summary>
    public void SetLastRun(int searchId, DateTime when, int newCount)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "UPDATE searches SET lastRun = $now, newCount = $new WHERE id = $id";
        command.Parameters.AddWithValue("$now", when.ToString("o"));
        command.Parameters.AddWithValue("$new", newCount);
        command.Parameters.AddWithValue("$id", searchId);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Je opende de zoekopdracht: wat er nu in staat, is bekeken, en de teller gaat naar nul.
    /// Enkel deze twee kolommen - niet het JSON-blokje, dat een lopende beurt ook schrijft.
    /// </summary>
    public void SetViewed(int searchId, DateTimeOffset when)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "UPDATE searches SET lastViewed = $viewed, newCount = 0 WHERE id = $id";
        command.Parameters.AddWithValue("$viewed", when.ToString("o"));
        command.Parameters.AddWithValue("$id", searchId);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Wanneer de zoekopdracht laatst geopend werd, zoals het nu in de databank staat. Een
    /// beurt vraagt het op het einde opnieuw: wie opende terwijl ze liep, heeft de nieuwe van
    /// daarvoor al gezien.
    /// </summary>
    public DateTimeOffset? GetLastViewed(int searchId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT lastViewed FROM searches WHERE id = $id";
        command.Parameters.AddWithValue("$id", searchId);

        return LeesTijdstip(command.ExecuteScalar() as string);
    }

    public void Delete(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // De bewaarde resultaten ook: die bleven vroeger achter, zonder zoekopdracht om ze te tonen.
        command.CommandText = "DELETE FROM searches WHERE id = $id; DELETE FROM seen WHERE searchId = $id; " +
                              "DELETE FROM outcome WHERE searchId = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // ---------- al gezien ----------

    /// <summary>Geeft de sleutels terug van alles wat voor deze zoekopdracht al gezien is.</summary>
    public HashSet<string> GetSeenKeys(int searchId)
    {
        var keys = new HashSet<string>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM seen WHERE searchId = $id";
        command.Parameters.AddWithValue("$id", searchId);

        using var reader = command.ExecuteReader();
        while (reader.Read()) keys.Add(reader.GetString(0));

        return keys;
    }

    /// <summary>
    /// Alles wat voor deze zoekopdracht al gezien is, met wanneer het voor het eerst opdook.
    /// Dat tijdstip, naast <see cref="SavedSearch.LastViewed"/>, zegt of je het al bekeken hebt.
    /// </summary>
    public Dictionary<string, DateTimeOffset> GetSeen(int searchId)
    {
        var gezien = new Dictionary<string, DateTimeOffset>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, firstSeen FROM seen WHERE searchId = $id";
        command.Parameters.AddWithValue("$id", searchId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Een onleesbaar tijdstip telt als "lang geleden": liever een nieuwe te weinig
            // dan een oude die telkens terugkomt.
            gezien[reader.GetString(0)] = LeesTijdstip(reader.GetString(1)) ?? DateTimeOffset.MinValue;
        }

        return gezien;
    }

    /// <summary>Markeert resultaten als gezien en zet de laatste zoekdatum.</summary>
    public void MarkSeen(int searchId, IEnumerable<string> keys)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT OR IGNORE INTO seen (searchId, key, firstSeen)
                VALUES ($id, $key, $now)
                """;

            var idParam = command.Parameters.Add("$id", SqliteType.Integer);
            var keyParam = command.Parameters.Add("$key", SqliteType.Text);
            var nowParam = command.Parameters.Add("$now", SqliteType.Text);

            idParam.Value = searchId;
            nowParam.Value = DateTime.Now.ToString("o");

            foreach (var key in keys)
            {
                keyParam.Value = key;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    // ---------- favorieten ----------

    /// <summary>
    /// De sleutels van alles wat als favoriet staat. Bij het tonen van
    /// zoekresultaten zetten we daarmee het sterretje goed, zonder per zoekertje
    /// de databank te moeten bevragen.
    /// </summary>
    public HashSet<string> GetFavoriteKeys()
    {
        var keys = new HashSet<string>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM favorites";

        using var reader = command.ExecuteReader();
        while (reader.Read()) keys.Add(reader.GetString(0));

        return keys;
    }

    /// <summary>
    /// Bewaart wat een beurt opleverde, zodat je het later nog kan bekijken.
    ///
    /// Waarom dit op schijf moet: een geplande zoekopdracht draait zonder scherm en
    /// stuurt een melding met wat er nieuw is. Stonden die resultaten enkel in het
    /// geheugen, dan krijg je een mail over driehonderd zoekertjes die de app zelf
    /// niet meer kan tonen zodra ze herstart is. De melding en het scherm horen
    /// hetzelfde te weten.
    ///
    /// Het is bewust een kopie, net als bij de favorieten: de site kan een zoekertje
    /// intussen weggehaald hebben.
    ///
    /// Eén commando voor alle zoekertjes, met enkel nieuwe waarden per rij, zoals
    /// <see cref="MarkSeen"/>. Tot 22 september 2026 kreeg elk zoekertje een nieuw commando,
    /// dat SQLite telkens opnieuw moest ontleden: 4000 zoekertjes - "Cd speler" sinds de rem
    /// op 2000 per site - kostten 50 tot 79 ms, nu 28 tot 40 ms (gemeten op een kopie van de
    /// echte databank). Het scherm roept dit op een achtergronddraad aan (zie
    /// <c>MainWindow.BewaarUitkomstAsync</c>); dat kan, want elke aanroep opent zijn eigen
    /// verbinding.
    /// </summary>
    public void SaveOutcome(int searchId, IReadOnlyList<Listing> listings)
    {
        using var connection = Open();
        using var transactie = connection.BeginTransaction();

        using (var wissen = connection.CreateCommand())
        {
            wissen.CommandText = "DELETE FROM outcome WHERE searchId = $id";
            wissen.Parameters.AddWithValue("$id", searchId);
            wissen.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO outcome
                (searchId, position, source, externalId, title, price, priceLabel,
                 location, url, image, largeImage, isNew)
            VALUES ($id, $pos, $source, $ext, $title, $price, $label,
                    $loc, $url, $img, $large, $new)
            """;

        // Zonder vast type: dan bindt elke waarde zoals ze is, net als AddWithValue per rij
        // vroeger deed. Bij de prijs telt dat, want die is soms leeg.
        SqliteParameter Veld(string naam) => command.Parameters.AddWithValue(naam, DBNull.Value);

        Veld("$id").Value = searchId;
        var pos = Veld("$pos");
        var source = Veld("$source");
        var ext = Veld("$ext");
        var title = Veld("$title");
        var price = Veld("$price");
        var label = Veld("$label");
        var loc = Veld("$loc");
        var url = Veld("$url");
        var img = Veld("$img");
        var large = Veld("$large");
        var nieuw = Veld("$new");

        for (var i = 0; i < listings.Count; i++)
        {
            var l = listings[i];

            pos.Value = i;
            source.Value = l.Source;
            ext.Value = l.ExternalId;
            title.Value = l.Title;
            price.Value = (object?)l.Price ?? DBNull.Value;
            label.Value = l.PriceLabel;
            loc.Value = l.Location;
            url.Value = l.Url;
            img.Value = l.ImageUrls.Count > 0 ? l.ImageUrls[0] : "";
            large.Value = l.LargeImageUrl;
            nieuw.Value = l.IsNew ? 1 : 0;

            command.ExecuteNonQuery();
        }

        transactie.Commit();
    }

    /// <summary>Wat de laatste beurt van deze zoekopdracht opleverde.</summary>
    public List<Listing> GetOutcome(int searchId)
    {
        var uit = new List<Listing>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, externalId, title, price, priceLabel, location, url,
                   image, largeImage, isNew
            FROM outcome WHERE searchId = $id ORDER BY position
            """;
        command.Parameters.AddWithValue("$id", searchId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var listing = new Listing
            {
                Source = reader.GetString(0),
                ExternalId = reader.GetString(1),
                Title = reader.GetString(2),
                Price = reader.IsDBNull(3) ? null : (decimal)reader.GetDouble(3),
                PriceLabel = reader.GetString(4),
                Location = reader.GetString(5),
                Url = reader.GetString(6),
                LargeImageUrl = reader.GetString(8),
                IsNew = reader.GetInt32(9) == 1
            };

            var foto = reader.GetString(7);
            if (foto.Length > 0) listing.ImageUrls.Add(foto);

            uit.Add(listing);
        }

        return uit;
    }

    /// <summary>
    /// De favorieten zelf, nieuwste eerst. Een favoriet bewaart een kopie van het
    /// zoekertje: de site kan het intussen offline gehaald hebben, maar wat je
    /// bewaarde blijft dan toch zichtbaar.
    /// </summary>
    public List<Listing> GetFavorites()
    {
        var list = new List<Listing>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, externalId, title, price, priceLabel, location, url, image, largeImage
            FROM favorites ORDER BY addedAt DESC
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var listing = new Listing
            {
                Source = reader.GetString(0),
                ExternalId = reader.GetString(1),
                Title = reader.GetString(2),
                Price = reader.IsDBNull(3) ? null : (decimal)reader.GetDouble(3),
                PriceLabel = reader.GetString(4),
                Location = reader.GetString(5),
                Url = reader.GetString(6),
                LargeImageUrl = reader.GetString(8),
                IsFavorite = true
            };

            var image = reader.GetString(7);
            if (image.Length > 0) listing.ImageUrls.Add(image);

            list.Add(listing);
        }

        return list;
    }

    public void AddFavorite(Listing listing)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT OR REPLACE INTO favorites
                (key, source, externalId, title, price, priceLabel, location, url, image, largeImage, addedAt)
            VALUES ($key, $source, $id, $title, $price, $label, $location, $url, $image, $large, $now)
            """;

        command.Parameters.AddWithValue("$key", listing.Key);
        command.Parameters.AddWithValue("$source", listing.Source);
        command.Parameters.AddWithValue("$id", listing.ExternalId);
        command.Parameters.AddWithValue("$title", listing.Title);
        command.Parameters.AddWithValue("$price", (object?)listing.Price ?? DBNull.Value);
        command.Parameters.AddWithValue("$label", listing.PriceLabel);
        command.Parameters.AddWithValue("$location", listing.Location);
        command.Parameters.AddWithValue("$url", listing.Url);
        command.Parameters.AddWithValue("$image", listing.Thumbnail);
        command.Parameters.AddWithValue("$large", listing.LargeImageUrl);
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));

        command.ExecuteNonQuery();
    }

    public void RemoveFavorite(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM favorites WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    // ---------- recente zoekopdrachten ----------

    /// <summary>
    /// Houdt bij wat er gezocht is. De zoekterm is de sleutel, dus twee keer
    /// hetzelfde zoeken geeft geen twee regels: de tijd wordt gewoon bijgewerkt.
    /// </summary>
    public void AddRecent(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO recent (query, ranAt) VALUES ($query, $now)
            ON CONFLICT(query) DO UPDATE SET ranAt = $now
            """;

        command.Parameters.AddWithValue("$query", query.Trim());
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    public List<RecentSearch> GetRecent(int limit = 50)
    {
        var list = new List<RecentSearch>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT query, ranAt FROM recent ORDER BY ranAt DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RecentSearch
            {
                Query = reader.GetString(0),
                RanAt = DateTime.Parse(reader.GetString(1))
            });
        }

        return list;
    }

    public void ClearRecent()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM recent";
        command.ExecuteNonQuery();
    }
}
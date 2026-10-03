using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vindioo.Models;

namespace Vindioo.Services;

/// <summary>
/// Bewaart elke site als een apart JSON-bestand in de map van de gebruiker.
/// Eén bestand per site maakt het makkelijk om een site te delen, te back-uppen
/// of los toe te voegen. De oude gedeelde sites.json wordt bij de eerste start
/// automatisch opgesplitst en als .bak bewaard.
/// </summary>
public class SiteStore
{
    private static readonly string Folder = AppPaths.Folder;

    /// <summary>Map met één JSON-bestand per site.</summary>
    private static readonly string SitesFolder = Path.Combine(Folder, "sites");

    /// <summary>Oud formaat: alle sites in één lijst. Wordt eenmalig gemigreerd.</summary>
    private static readonly string LegacyFilePath = Path.Combine(Folder, "sites.json");

    /// <summary>
    /// De opties voor het lezen én schrijven van een sitebestand.
    ///
    /// De enum-converter staat erbij zodat een keuze als "Choice" of "Html" met
    /// naam in het bestand mag staan in plaats van als nummer. Een sitebestand is
    /// bedoeld om met de hand te bewerken en te delen, en "Kind": 0 zegt niemand
    /// iets. Getallen blijven gewoon werken, dus bestaande bestanden veranderen
    /// er niet van.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public List<SiteDefinition> Sites { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(SitesFolder);
        MigrateLegacyIfNeeded();

        var loaded = new List<SiteDefinition>();

        foreach (var file in Directory.EnumerateFiles(SitesFolder, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<SiteDefinition>(File.ReadAllText(file), Options);
                if (def is null || string.IsNullOrWhiteSpace(def.Name)) continue;

                // Zonder Id (bv. een met de hand gemaakt bestand) de bestandsnaam gebruiken.
                if (string.IsNullOrWhiteSpace(def.Id))
                    def.Id = Path.GetFileNameWithoutExtension(file);

                loaded.Add(def);
            }
            catch (Exception ex)
            {
                // Eén kapot bestand mag de rest niet tegenhouden — maar het mag
                // ook niet geruisloos verdwijnen. Een site die zomaar uit de rij
                // weg is, is anders niet te vinden.
                Log.Write($"sitebestand {Path.GetFileName(file)} kon niet gelezen worden - {ex.Message}");
            }
        }

        Sites = loaded.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Add(SiteDefinition site)
    {
        // Een gedeeld bestand mag niet bepalen WAAR er geschreven wordt. De Id gaat
        // rechtstreeks in een bestandspad, en Path.Combine laat "..\..\x" gewoon door - een
        // volledig pad als "C:\ergens" negeert de sitesmap zelfs helemaal. Door de slug halen
        // houdt enkel [a-z0-9-] over. Gevonden in een codeanalyse van 30 september 2026.
        if (!string.IsNullOrWhiteSpace(site.Id)) site.Id = MakeSlug(site.Id);

        // Bestaande site met dezelfde naam vervangen (zoals voorheen).
        var existing = Sites.FirstOrDefault(s => s.Name.Equals(site.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            site.Id = existing.Id;   // zelfde bestand hergebruiken
            Sites.Remove(existing);
        }
        else if (string.IsNullOrWhiteSpace(site.Id) ||
                 Sites.Any(s => s.Id.Equals(site.Id, StringComparison.OrdinalIgnoreCase)))
        {
            // Nieuwe site, of een gedeelde Id die hier al bezet is: maak een unieke.
            site.Id = UniqueSlug(site.Name);
        }

        Sites.Add(site);
        WriteFile(site);
        Sites = Sites.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Slaat één bewerkte site op naar zijn eigen bestand.</summary>
    public void Save(SiteDefinition site)
    {
        if (string.IsNullOrWhiteSpace(site.Id)) site.Id = UniqueSlug(site.Name);
        WriteFile(site);
    }

    public void Remove(string name)
    {
        var site = Sites.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (site is null) return;

        Sites.Remove(site);

        var path = FilePathFor(site.Id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// Leest een gedeeld sitebestand in en voegt het toe (met ontdubbeling).
    /// Geeft de ingelezen site terug, of null bij een ongeldig bestand.
    /// </summary>
    public SiteDefinition? Import(string filePath) => Import(filePath, out _);

    /// <summary>
    /// Dezelfde import, met de reden erbij wanneer het niet lukt. Die hoort op het scherm: een
    /// bestandsnaam alleen zegt niet of het bestand kapot is of geweigerd werd.
    /// </summary>
    public SiteDefinition? Import(string filePath, out string reden)
    {
        reden = "";

        try
        {
            var def = JsonSerializer.Deserialize<SiteDefinition>(File.ReadAllText(filePath), Options);

            if (def is null || string.IsNullOrWhiteSpace(def.Name))
            {
                reden = "geen geldige sitebeschrijving";
                return null;
            }

            // Een sitebestand is bedoeld om te DELEN, en de brug voert zijn zoek-URL uit in
            // jouw eigen Chrome, met jouw cookies. Wat van buiten binnenkomt, wordt dus
            // nagekeken; zie SiteUrlCheck voor waarom dat op twee plaatsen gebeurt.
            if (!SiteUrlCheck.IsVeilig(def, out var waarom))
            {
                reden = waarom;
                Log.Write($"importeren van {Path.GetFileName(filePath)} geweigerd - {waarom}");
                return null;
            }

            Log.Write($"sitebestand {Path.GetFileName(filePath)} ({def.Name}) zoekt op " +
                      string.Join(", ", SiteUrlCheck.Hosts(def)));

            Add(def);
            return def;
        }
        catch (Exception ex)
        {
            // Wie een kapot gedeeld bestand importeert, zag enkel de bestandsnaam; de reden
            // staat nu in het logboek en op het scherm.
            reden = FriendlyError.Describe(ex);
            Log.Write($"importeren van {Path.GetFileName(filePath)} mislukt - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Leest alle sitebestanden uit een map in, elk zoals <see cref="Import"/>. Heeft de
    /// map een submap "sites", dan worden die gelezen: zo mag je ook de map van de
    /// sites-repository zelf kiezen, of je eigen gegevensmap, waar naast de sites ook
    /// instellingen.json staat. Geeft terug hoeveel sites gelukt zijn en welke
    /// bestanden geen geldige sitebeschrijving waren.
    ///
    /// Dit is de eerste stap na het installeren: de app komt zonder sites, die staan in
    /// een aparte repository.
    /// </summary>
    /// <remarks>
    /// <c>Replaced</c> zijn de sites die er al stonden en overschreven werden. Dat gebeurde
    /// altijd al, maar de melding zei enkel "10 site(s) geïmporteerd": wie een site zelf had
    /// bijgestuurd, zag niet dat die aanpassing weg was.
    /// </remarks>
    public (int Imported, List<string> Failed, List<string> Replaced) ImportFolder(string folder)
    {
        var sitesMap = Path.Combine(folder, "sites");
        var bron = Directory.Exists(sitesMap) ? sitesMap : folder;

        var imported = 0;
        var failed = new List<string>();
        var replaced = new List<string>();
        var bestaand = Sites.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Eerst de lijst vastleggen: wie zijn eigen sitesmap kiest, schrijft tijdens het
        // importeren in dezelfde map die gelezen wordt.
        var files = Directory.EnumerateFiles(bron, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            var site = Import(file, out var reden);

            if (site is null)
            {
                failed.Add(Path.GetFileName(file) + (reden.Length > 0 ? $" ({reden})" : ""));
                continue;
            }

            imported++;
            if (bestaand.Contains(site.Name)) replaced.Add(site.Name);
        }

        return (imported, failed, replaced);
    }

    /// <summary>Schrijft een site naar een zelfgekozen bestand om te delen.</summary>
    public void Export(SiteDefinition site, string targetPath) =>
        File.WriteAllText(targetPath, JsonSerializer.Serialize(site, Options));

    // ---------- intern ----------

    /// <summary>Splitst de oude sites.json één keer op in losse bestanden.</summary>
    private void MigrateLegacyIfNeeded()
    {
        var alreadyHasFiles = Directory.EnumerateFiles(SitesFolder, "*.json").Any();
        if (alreadyHasFiles || !File.Exists(LegacyFilePath)) return;

        try
        {
            var list = JsonSerializer.Deserialize<List<SiteDefinition>>(File.ReadAllText(LegacyFilePath))
                       ?? new List<SiteDefinition>();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in list)
            {
                if (string.IsNullOrWhiteSpace(def.Name)) continue;

                var baseSlug = string.IsNullOrWhiteSpace(def.Id) ? MakeSlug(def.Name) : def.Id;
                def.Id = MakeUnique(baseSlug, used);
                used.Add(def.Id);

                WriteFile(def);
            }

            // Oud bestand als back-up bewaren i.p.v. verwijderen.
            var backup = LegacyFilePath + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(LegacyFilePath, backup);
        }
        catch
        {
            // Mislukt de migratie, dan blijft de oude sites.json staan en start de
            // app met de (mogelijk lege) sitesmap; er gaat niets verloren.
        }
    }

    private static void WriteFile(SiteDefinition site)
    {
        Directory.CreateDirectory(SitesFolder);
        File.WriteAllText(FilePathFor(site.Id), JsonSerializer.Serialize(site, Options));
    }

    /// <summary>
    /// Het bestand van een site. De Id gaat ook hier door <see cref="MakeSlug"/>: dit is de
    /// plaats waar het pad werkelijk gemaakt wordt, en dat is het tweede slot.
    /// </summary>
    private static string FilePathFor(string id) => Path.Combine(SitesFolder, MakeSlug(id) + ".json");

    /// <summary>Maakt een sleutel die nog niet door een andere site gebruikt wordt.</summary>
    private string UniqueSlug(string name)
    {
        var used = new HashSet<string>(Sites.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        return MakeUnique(MakeSlug(name), used);
    }

    private static string MakeUnique(string baseSlug, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "site";

        var slug = baseSlug;
        var i = 2;
        while (used.Contains(slug)) slug = $"{baseSlug}-{i++}";
        return slug;
    }

    /// <summary>Maakt een korte, veilige sleutel uit de naam, bv. "AlleVeilingen" -> "alleveilingen".</summary>
    private static string MakeSlug(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "site" : slug;
    }

    /// <summary>Map met de sitebestanden, handig om te tonen of te back-uppen.</summary>
    public static string Location => SitesFolder;
}

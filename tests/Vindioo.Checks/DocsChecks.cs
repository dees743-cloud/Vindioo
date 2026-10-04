using System.IO;
using System.Text.RegularExpressions;

namespace Vindioo.Checks;

/// <summary>
/// Houdt CLAUDE.md klein.
///
/// Claude Code leest dat bestand bij het begin van elke sessie volledig in, en die tekst
/// reist bij elke beurt opnieuw mee. Op 30 september 2026 was het <b>281 kB</b> geworden:
/// 45 500 woorden, zo'n 70 000 tot 90 000 tokens nog voor er iets gevraagd was. Het was een
/// werkdagboek geworden - "nagemeten op 24 september" stond er meer dan honderd keer in.
///
/// De inhoud is niet weggegooid maar verhuisd naar <c>docs/</c>, per onderwerp, en die
/// bestanden worden enkel gelezen wanneer ze nodig zijn. Deze controle bewaakt dat het niet
/// opnieuw dichtslibt: een afspraak die niemand nakijkt, verwatert.
/// </summary>
public static class DocsChecks
{
    /// <summary>De grens. Alles wat bij élke taak nodig is, past hier ruim in.</summary>
    private const int MaxKiloBytes = 15;

    public static void Run()
    {
        Check.Groep($"Documentatie: CLAUDE.md blijft onder de {MaxKiloBytes} kB");

        var wortel = Check.Projectmap();
        if (wortel is null)
        {
            // Draait de controle los van de projectmap (bv. een gepubliceerde kopie), dan
            // valt er niets na te kijken. Zwijgen is dan beter dan falen.
            Check.Overgeslagen("de projectmap is niet gevonden: deze controles zijn overgeslagen");
            return;
        }

        var pad = Path.Combine(wortel, "CLAUDE.md");
        var tekst = File.ReadAllText(pad);
        var kb = new FileInfo(pad).Length / 1024.0;

        Check.Dat(kb <= MaxKiloBytes,
            $"CLAUDE.md is {kb:0.0} kB (hoogstens {MaxKiloBytes}; staat er iets nieuws in, zet het in docs/)");

        // Een verwijzing naar een bestand dat niet bestaat, stuurt de volgende sessie het bos in.
        var genoemd = Regex.Matches(tekst, @"docs/([a-z0-9\-]+\.md)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ontbreekt = genoemd
            .Where(naam => !File.Exists(Path.Combine(wortel, "docs", naam)))
            .ToList();

        Check.Dat(ontbreekt.Count == 0,
            $"de {genoemd.Count} genoemde docs-bestanden bestaan ook echt" +
            (ontbreekt.Count == 0 ? "" : " - ontbreekt: " + string.Join(", ", ontbreekt)));

        // En andersom: een document waar niets naar verwijst, wordt nooit gevonden.
        var aanwezig = Directory.Exists(Path.Combine(wortel, "docs"))
            ? Directory.EnumerateFiles(Path.Combine(wortel, "docs"), "*.md").Select(Path.GetFileName).ToList()
            : new List<string?>();

        var verweesd = aanwezig
            .Where(naam => naam is not null && !genoemd.Contains(naam, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Check.Dat(verweesd.Count == 0,
            $"elk van de {aanwezig.Count} docs-bestanden staat in de tabel in CLAUDE.md" +
            (verweesd.Count == 0 ? "" : " - niet genoemd: " + string.Join(", ", verweesd)));

        // Een @-verwijzing laadt het bestand automatisch mee in, en dan is de winst weg.
        Check.Dat(!Regex.IsMatch(tekst, @"(?m)^\s*@\S+\.md\b"),
            "geen @docs/...-imports in CLAUDE.md (die worden vanzelf mee ingeladen)");

        OudeNamen(wortel);
    }

    /// <summary>
    /// De oude naam staat nog op een aantal plaatsen in de code, en overal met opzet: zonder
    /// die namen vindt de app de gegevensmap van een bestaande installatie niet, blijft Windows
    /// de oude exe opstarten, of wordt de bewaarde API-sleutel onleesbaar.
    ///
    /// <para><c>docs/project.md</c> somt ze op, en die tabel is wat een volgende lezer - of een
    /// volgende sessie - gebruikt om te beslissen wat weg mag. Op 4 oktober 2026 klopte hij
    /// niet: zes rijen, een tekst die van zeven sprak, en tien plaatsen in de code. Wat niet in
    /// de tabel staat, wordt opgeruimd, en sommige van die namen kan je niet ongestraft
    /// weghalen.</para>
    ///
    /// <para>Daarom staat de lijst hier hard, en niet als een telling: zodra er een waarde
    /// bijkomt of wegvalt, valt deze controle om met de naam erbij, en moet wie hem toevoegde
    /// kiezen - in de tabel zetten, of bewust weghalen.</para>
    /// </summary>
    private static void OudeNamen(string wortel)
    {
        Check.Groep("De oude naam staat enkel waar de documentatie zegt dat hij staat");

        var verwacht = new SortedSet<string>(StringComparer.Ordinal)
        {
            @"Local\Zentrix-een-exemplaar",   // App.OudeSloten
            @"Local\Zentrix-toon-venster",    // het sein naar een draaiende oude versie
            "ZENTRIX_SOFTWARE_RENDER",        // de noodrem voor een wit venster
            "Zentrix", "Zoekhulp",            // AppPaths.LegacyNames en Autostart.OudeNamen
            "zentrix.db", "zoekhulp.db",      // AppPaths.DatabaseNames
            "zentrix-log.txt", "zoekhulp-log.txt",
            "ZENTRIX_DATA", "ZOEKHULP_DATA",  // AppPaths.OudeDataVariabelen
            "Zentrix-instellingen",           // DPAPI-entropie: NOOIT wijzigen
            "X-Zentrix-Brug", "X-Zentrix-Sig", "X-Zentrix-Voor"
        };

        var gevonden = new SortedSet<string>(StringComparer.Ordinal);

        var bronnen = Directory.EnumerateFiles(wortel, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(@"\bin\") && !p.Contains(@"\obj\") &&
                        !p.Contains(@"\.vs\") && !p.Contains(@"\tests\"));

        foreach (var bestand in bronnen)
            foreach (var regel in File.ReadLines(bestand))
            {
                // Commentaar telt niet mee: daar mag de oude naam gewoon in staan, en dat
                // hoort ook - de uitleg waarom iets blijft, gaat net over die naam.
                var kaal = regel.TrimStart();
                if (kaal.StartsWith("//") || kaal.StartsWith("*")) continue;

                foreach (Match m in Regex.Matches(regel, "\"([^\"]*)\""))
                {
                    var waarde = m.Groups[1].Value;

                    if (waarde.Contains("zentrix", StringComparison.OrdinalIgnoreCase) ||
                        waarde.Contains("zoekhulp", StringComparison.OrdinalIgnoreCase))
                        gevonden.Add(waarde);
                }
            }

        var erbij = gevonden.Except(verwacht).ToList();
        var weg = verwacht.Except(gevonden).ToList();

        Check.Dat(erbij.Count == 0,
            erbij.Count == 0
                ? $"geen onbekende resten van de oude naam ({gevonden.Count} waarden, allemaal in de tabel)"
                : "nieuw en niet in de tabel van docs/project.md: " + string.Join(", ", erbij));

        Check.Dat(weg.Count == 0,
            weg.Count == 0
                ? "en elke waarde uit de tabel staat ook echt nog in de code"
                : "weg uit de code, dus ook uit de tabel halen: " + string.Join(", ", weg));

        // De entropie apart, want die is van een andere orde: een rename daar maakt de
        // bewaarde API-sleutel onleesbaar zonder dat er ergens een fout verschijnt.
        Check.Dat(gevonden.Contains("Zentrix-instellingen"),
            "de DPAPI-entropie draagt nog de oude naam, en dat moet zo blijven");
    }
}

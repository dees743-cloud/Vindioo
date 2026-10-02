using System.IO;
using System.Text.RegularExpressions;

namespace Zentrix.Checks;

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
    }

}

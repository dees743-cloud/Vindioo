namespace Vindioo.Checks;

/// <summary>Het afdrukken en tellen van de controles.</summary>
public static class Check
{
    private static int _fouten;
    private static int _totaal;

    /// <summary>Met --snel worden de controles overgeslagen die echt moeten wachten (een time-out van 30 s).</summary>
    public static bool Snel { get; set; }

    public static void Groep(string naam) => Console.WriteLine($"-- {naam}");

    public static void Dat(bool ok, string wat)
    {
        _totaal++;
        Console.WriteLine((ok ? "OK    " : "FOUT  ") + wat);
        if (!ok) _fouten++;
    }

    public static void Overgeslagen(string wat) => Console.WriteLine("--    " + wat);

    /// <summary>
    /// De projectmap, gezocht vanaf de map van de exe naar boven. Niet de huidige map:
    /// <c>dotnet run --project</c> laat die staan waar de shell stond.
    ///
    /// Nodig voor de controles die naar een bestand in de broncode kijken in plaats van naar
    /// code die draait - de grootte van CLAUDE.md, of wat het manifest van de extensie vraagt.
    /// </summary>
    public static string? Projectmap()
    {
        var map = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

        while (map is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(map.FullName, "CLAUDE.md")) &&
                System.IO.File.Exists(System.IO.Path.Combine(map.FullName, "Vindioo.csproj")))
                return map.FullName;

            map = map.Parent;
        }

        return null;
    }

    public static int Einde()
    {
        Console.WriteLine();
        Console.WriteLine(_fouten == 0 ? $"ALLES OK ({_totaal} controles)" : $"{_fouten} FOUT(EN) op {_totaal} controles");
        return _fouten == 0 ? 0 : 1;
    }
}

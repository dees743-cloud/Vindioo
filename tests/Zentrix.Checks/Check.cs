namespace Zentrix.Checks;

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

    public static int Einde()
    {
        Console.WriteLine();
        Console.WriteLine(_fouten == 0 ? $"ALLES OK ({_totaal} controles)" : $"{_fouten} FOUT(EN) op {_totaal} controles");
        return _fouten == 0 ? 0 : 1;
    }
}

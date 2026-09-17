using System.IO;

namespace Zentrix.Services;

/// <summary>
/// Eenvoudig logboek in een tekstbestand, zodat achteraf te zien is waar iets
/// misliep — vooral handig bij de brug, waar de helft in Chrome gebeurt en dus
/// niet in de debugger te volgen is. Eén regel per gebeurtenis, met tijdstempel.
/// </summary>
public static class Log
{
    private static readonly object Lock = new();

    /// <summary>Het logbestand; ligt naast de andere gebruikersgegevens.</summary>
    public static string FilePath { get; } = AppPaths.LogFile;

    /// <summary>Boven deze grootte begint het logboek opnieuw.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) File.Delete(FilePath);

                File.AppendAllText(FilePath,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Loggen mag de app nooit tegenhouden.
        }
    }
}

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Zentrix.Services;

/// <summary>
/// Start de Chrome van de gebruiker wanneer die dicht staat. De brug werkt via
/// een extensie in díe browser: draait Chrome niet, dan vraagt niemand om werk
/// en blijft een zoekopdracht op leboncoin of Catawiki eindeloos hangen.
/// </summary>
public static class ChromeLauncher
{
    /// <summary>
    /// Draait er al een Chrome <b>van de gebruiker</b>?
    ///
    /// Niet zomaar "er loopt een chrome.exe": de app start er zelf een met Playwright, op haar
    /// eigen profielmap, en díe heeft de brug-extensie niet. Sinds de rijstroken start die
    /// browserstrook tegelijk met de brug, dus dat valt geregeld samen. Telde zo'n Chrome mee,
    /// dan dacht de app "Chrome draait al, de extensie wordt wel wakker", wachtte ze dertig
    /// seconden op een extensie die er nooit komt, en sloeg ze alle brugsites over met "de
    /// extensie meldde zich niet". Gevonden in een codeanalyse van 30 september 2026.
    /// </summary>
    public static bool IsRunning
    {
        get
        {
            var processen = Process.GetProcessesByName("chrome");

            try
            {
                return processen.Any(p => !IsVanOnsProfiel(p));
            }
            finally
            {
                foreach (var p in processen) p.Dispose();
            }
        }
    }

    /// <summary>
    /// Een Chrome die Playwright voor ons startte, herkenbaar aan onze profielmap in zijn
    /// opdrachtregel. Is die niet te lezen, dan telt hij gewoon mee - zoals vroeger.
    /// </summary>
    private static bool IsVanOnsProfiel(Process proces)
    {
        try
        {
            return BrowserFetcher.LeesOpdrachtregel(proces.Id) is { } regel &&
                   regel.Contains(BrowserFetcher.ProfilePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Het pad naar chrome.exe, of null wanneer Chrome niet gevonden wordt.</summary>
    public static string? FindChrome()
    {
        // Windows houdt zelf bij waar chrome.exe staat.
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = root.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");

            if (key?.GetValue(null) is string path && File.Exists(path)) return path;
        }

        // Terugval op de gebruikelijke plaatsen.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Start Chrome geminimaliseerd; false wanneer Chrome niet te vinden is.</summary>
    public static bool Start()
    {
        var path = FindChrome();
        if (path is null)
        {
            Log.Write("brug: chrome.exe niet gevonden");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,

                // Geminimaliseerd, zodat de browser niet voor het zoekscherm springt.
                WindowStyle = ProcessWindowStyle.Minimized
            });

            Log.Write("brug: Chrome gestart omdat de extensie zich niet meldde");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("brug: Chrome starten mislukte - " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Zorgt dat de extensie bereikbaar is: start Chrome als het moet en wacht
    /// tot ze zich meldt. Geeft terug hoe het afliep, en niet enkel of het lukte:
    /// "geen contact", "verkeerde koppelcode" en "geen Chrome" vragen elk iets
    /// anders van de gebruiker. Tot september 2026 was dit een ja of nee, en toen
    /// de koppelcode niet meer klopte zei de app "meldde zich niet" terwijl de
    /// extensie zich wel degelijk meldde - met de oude code.
    /// </summary>
    public static async Task<BridgeStatus> EnsureBridgeAsync(TimeSpan timeout, IProgress<string>? status = null)
    {
        var bridge = BridgeServer.Instance;
        bridge.Start();

        if (bridge.PortBusy) return BridgeStatus.PortInUse;

        // De extensie vraagt elke 250 ms om werk; meldt ze zich, dan is alles klaar.
        if (bridge.ExtensionAlive) return BridgeStatus.Ready;

        // Meldt ze zich wel, maar met een andere code, dan heeft wachten geen zin.
        if (bridge.WrongCodeRecently) return BridgeStatus.WrongCode;

        if (IsRunning)
        {
            status?.Report("Wachten tot de Zentrix Brug in Chrome wakker wordt...");
        }
        else
        {
            status?.Report("Chrome starten voor de brug...");
            if (!Start()) return BridgeStatus.ChromeNotFound;
        }

        var until = DateTime.Now + timeout;

        while (DateTime.Now < until)
        {
            if (bridge.ExtensionAlive) return BridgeStatus.Ready;

            if (bridge.WrongCodeRecently)
            {
                Log.Write("brug: de extensie meldt zich met een andere koppelcode");
                return BridgeStatus.WrongCode;
            }

            await Task.Delay(250);
        }

        Log.Write("brug: extensie meldde zich niet binnen de wachttijd");
        return BridgeStatus.NoExtension;
    }

    /// <summary>Wat de gebruiker moet doen, in gewone woorden.</summary>
    public static string Describe(BridgeStatus status) => status switch
    {
        BridgeStatus.WrongCode =>
            "de Zentrix Brug in Chrome gebruikt een andere koppelcode. Kies tandwiel > Koppelcode " +
            "en plak de code opnieuw in de extensie.",
        // Heeft de extensie zich sinds de start nooit gemeld, dan is ze misschien niet
        // geïnstalleerd, en nergens in de app stond hoe dat moet.
        BridgeStatus.NoExtension when !BridgeServer.Instance.ExtensionConnected =>
            "Chrome draait, maar de Zentrix Brug meldt zich niet. Nog niet geïnstalleerd? In Chrome: " +
            "chrome://extensions > Ontwikkelaarsmodus aan > Uitgepakte extensie laden > de map 'extension' " +
            "van Zentrix. Staat ze er al, zet ze dan aan en plak de koppelcode (tandwiel > Koppelcode).",
        BridgeStatus.NoExtension =>
            "Chrome draait, maar de Zentrix Brug meldt zich niet. Staat de extensie aan in chrome://extensions?",
        BridgeStatus.ChromeNotFound =>
            "Google Chrome is niet gevonden. Sites via de brug hebben Chrome nodig.",
        BridgeStatus.PortInUse =>
            $"de brug kan niet starten: poort {BridgeServer.Port} is in gebruik door een ander programma " +
            "(of door Zentrix van een andere Windows-gebruiker).",
        _ => ""
    };
}

/// <summary>Hoe het afliep toen de app de brug klaarzette.</summary>
public enum BridgeStatus
{
    /// <summary>De extensie meldt zich; de brug werkt.</summary>
    Ready,

    /// <summary>De extensie meldt zich, maar met een andere koppelcode.</summary>
    WrongCode,

    /// <summary>De server van de brug kan niet starten: de poort is bezet.</summary>
    PortInUse,

    /// <summary>Chrome draait (of is gestart), maar de extensie meldt zich niet.</summary>
    NoExtension,

    /// <summary>Chrome is niet te vinden op deze computer.</summary>
    ChromeNotFound
}

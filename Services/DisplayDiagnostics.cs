using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Zentrix.Services;

/// <summary>
/// Schrijft in het logboek hoe WPF tekent en wat Windows aan het scherm verandert. Gemaakt
/// voor een wit venster bij het opstarten van de pc (22 september 2026): de app liep gewoon, de
/// planner zocht en stuurde meldingen, maar het venster bleef wit tot je Zentrix herstartte.
/// Windows meldde geen fout van de grafische kaart, en het logboek van Zentrix ook niet.
/// Met deze regels zegt de volgende keer het logboek zelf wat er gebeurde:
/// <list type="bullet">
/// <item>bij de start: of er met de grafische kaart getekend wordt of op de processor, en
/// hoelang de pc al aan staat;</item>
/// <item>wanneer Windows het scherm wijzigt (resolutie, schaal, een scherm dat verschijnt of
/// verdwijnt), wanneer de pc slaapt of ontwaakt, en wanneer je vergrendelt of aanmeldt;</item>
/// <item>wanneer het venster getoond wordt, of het daarna ook echt een eerste keer getekend
/// werd, en hoe (zie <see cref="VolgVenster"/>).</item>
/// </list>
/// Staat er "getoond" zonder "getekend", dan is het het tekenen. Gewone vensters, gewone
/// dagen: een paar regels per start.
/// </summary>
public static class DisplayDiagnostics
{
    public static void Start()
    {
        Log.Write($"tekenen: {Niveau()}, {Modus(RenderOptions.ProcessRenderMode)}; " +
                  $"pc aan sinds {Duur(TimeSpan.FromMilliseconds(Environment.TickCount64))}; scherm {Scherm()}");

        // Het niveau kan veranderen terwijl de app draait: een stuurprogramma dat opnieuw
        // start, of een kaart die wegvalt en WPF laat terugvallen op de processor.
        RenderCapability.TierChanged += (_, _) => Log.Write($"tekenen gewijzigd: {Niveau()}");

        // Van Windows zelf. Werkt ook zonder venster: SystemEvents heeft een eigen draad.
        SystemEvents.DisplaySettingsChanged += (_, _) => Log.Write($"Windows wijzigde het scherm: {Scherm()}");
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode != PowerModes.StatusChange) Log.Write($"pc {(e.Mode == PowerModes.Suspend ? "gaat slapen" : "ontwaakt")}");
        };
        SystemEvents.SessionSwitch += (_, e) => Log.Write($"sessie: {e.Reason}");
    }

    /// <summary>
    /// Volgt een venster: wanneer het getoond wordt, of het daarna een eerste beeld tekent, en
    /// of dat met de grafische kaart of op de processor gebeurt.
    /// </summary>
    public static void VolgVenster(Window venster, string naam)
    {
        var getoond = DateTime.MinValue;
        var keer = 0;

        venster.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;

            keer++;
            getoond = DateTime.Now;
            Log.Write($"{naam}: getoond ({(keer == 1 ? "eerste keer" : $"keer {keer}")}), " +
                      $"pc aan sinds {Duur(TimeSpan.FromMilliseconds(Environment.TickCount64))}");
        };

        // Eén keer, na het eerste beeld. Blijft deze regel na "getoond" weg, dan tekende het
        // venster nooit - precies wat een wit venster is.
        venster.ContentRendered += (_, _) =>
        {
            var hoe = PresentationSource.FromVisual(venster) is HwndSource bron
                ? Modus(bron.CompositionTarget.RenderMode)
                : "onbekend";

            Log.Write($"{naam}: eerste beeld getekend, {(DateTime.Now - getoond).TotalMilliseconds:F0} ms na het tonen, {hoe}");
        };
    }

    /// <summary>Hoe WPF kan tekenen op deze pc: het getal zit in de bovenste 16 bits.</summary>
    private static string Niveau() => (RenderCapability.Tier >> 16) switch
    {
        0 => "niveau 0 (geen hulp van de grafische kaart)",
        1 => "niveau 1 (gedeeltelijk met de grafische kaart)",
        _ => "niveau 2 (met de grafische kaart)"
    };

    private static string Modus(RenderMode modus) =>
        modus == RenderMode.SoftwareOnly ? "op de processor" : "met de grafische kaart waar het kan";

    /// <summary>Het hoofdscherm in eenheden van WPF: verandert de resolutie of de schaal, dan verandert dit.</summary>
    private static string Scherm() =>
        $"{SystemParameters.PrimaryScreenWidth:F0}x{SystemParameters.PrimaryScreenHeight:F0}, " +
        $"werkblad {SystemParameters.WorkArea.Width:F0}x{SystemParameters.WorkArea.Height:F0}";

    private static string Duur(TimeSpan t) =>
        t.TotalMinutes < 1 ? $"{t.TotalSeconds:F0} s"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s"
        : $"{(int)t.TotalHours} u {t.Minutes} min";
}

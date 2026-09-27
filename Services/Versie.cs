using System.Reflection;

namespace Zentrix.Services;

/// <summary>
/// Welke versie van Zentrix er draait.
///
/// Het nummer staat op één plaats - <c>&lt;Version&gt;</c> in het csproj - en wordt hier uit de
/// assembly gelezen. Zo kan het niet uit de pas lopen met wat er op het bestand staat.
///
/// Waarom het zichtbaar moet zijn: er staan twee exe's op deze pc, een uit Visual Studio
/// (<c>bin\Debug\...</c>) en een gepubliceerde (<c>C:\Users\davyb\Zentrix</c>), en die delen
/// dezelfde gegevensmap. Aan het scherm was tot 27 september 2026 niet te zien welke van de
/// twee je voor je had, en na het publiceren van een wijziging is dat precies wat je wil weten.
/// </summary>
public static class Versie
{
    /// <summary>"0.9.0", uit het csproj.</summary>
    public static string Nummer { get; } = Lees();

    /// <summary>"Zentrix 0.9.0", voor in het menu en het logboek.</summary>
    public static string Volledig => "Zentrix " + Nummer;

    private static string Lees()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // InformationalVersion is wat <Version> oplevert; die kan een achtervoegsel dragen
        // dat de bouwomgeving erbij zet ("0.9.0+a1b2c3"), en dat hoort niet op het scherm.
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(info))
            return info.Split('+')[0];

        // Terugval: het gewone versienummer, zonder de revisie die altijd 0 is.
        var v = assembly.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}

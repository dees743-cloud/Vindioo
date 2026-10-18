using System.Windows;

namespace Vindioo;

/// <summary>
/// Hoe een venster boven een ander venster opengaat.
/// </summary>
public static class Vensters
{
    /// <summary>
    /// Vraagt of deze sitebestanden erin mogen, met de hosts erbij die ze zullen aanspreken.
    ///
    /// <para>Een sitebestand is bedoeld om door te geven, en het bepaalt waar de app naartoe
    /// gaat - bij een brugsite in jouw eigen Chrome, met jouw cookies. Tot 18 oktober 2026
    /// stonden die hosts alleen in het logboek, en daar kijkt niemand vóór hij op Importeren
    /// duwt. De vraag hoort te staan waar de keuze gemaakt wordt.</para>
    /// </summary>
    public static bool MagImporteren(Window eigenaar, string map) =>
        MagImporteren(eigenaar, Services.SiteStore.Bestanden(map));

    /// <inheritdoc cref="MagImporteren(Window, string)"/>
    public static bool MagImporteren(Window eigenaar, IReadOnlyList<string> bestanden)
    {
        if (bestanden.Count == 0) return true;

        var hosts = Services.SiteStore.HostsVan(bestanden);

        var wat = bestanden.Count == 1
            ? $"'{System.IO.Path.GetFileName(bestanden[0])}' importeren?"
            : $"{bestanden.Count} sitebestanden importeren?";

        var waarheen = hosts.Count == 0
            ? "Er staat geen bruikbaar webadres in."
            : "Vindioo gaat daarmee naar:" + Environment.NewLine +
              string.Join(Environment.NewLine, hosts.Select(h => "    " + h));

        var vraag = wat + Environment.NewLine + Environment.NewLine + waarheen +
                    Environment.NewLine + Environment.NewLine +
                    "Bij een site die via de brug loopt, worden die adressen in jouw eigen " +
                    "Chrome geopend, met jouw cookies.";

        return MessageBox.Show(eigenaar, vraag, "Sitebestanden importeren",
                               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Zet <paramref name="eigenaar"/> als eigenaar, en haalt die naar voren wanneer dit venster
    /// sluit.
    ///
    /// <para>Waarom dat tweede nodig is. Een eigenaar zetten hoort genoeg te zijn: Windows
    /// activeert bij het sluiten van een venster normaal zijn eigenaar. In de praktijk gebeurde
    /// op 2 oktober 2026 iets anders - stond er een Verkenner of een Chrome tussen Vindioo en het
    /// pop-upvenster, dan sprong Vindioo bij het sluiten helemaal naar achter, achter dat andere
    /// programma. Je moest het dan uit de taakbalk terughalen om verder te werken.</para>
    ///
    /// <para><b>Dit is niet nagemeten in een harnas, en dat hoort erbij.</b> Windows laat een
    /// proces dat op de achtergrond gestart is de vensters niet herschikken: in een proefopzet
    /// kwam noch Kladblok noch de Verkenner naar voren, dus de situatie viel er niet na te
    /// bootsen. Wat er wél gemeten is, met het echte detailvenster: zónder een ander programma
    /// ertussen komt het hoofdvenster na het sluiten gewoon terug. De klacht gaat dus over het
    /// geval met iets ertussen, en daar dwingt deze regel het antwoord af in plaats van erop te
    /// vertrouwen.</para>
    ///
    /// <para>Het kan geen aandacht stelen van een ander programma: wie dit venster sluit, had het
    /// net nog vooraan staan. En een eigenaar die intussen weg of geminimaliseerd is, wordt met
    /// rust gelaten - anders zou het sluiten van een venster een geminimaliseerde Vindioo uit het
    /// systeemvak trekken.</para>
    /// </summary>
    public static T Boven<T>(this T venster, Window eigenaar) where T : Window
    {
        venster.Owner = eigenaar;

        venster.Closed += (_, _) =>
        {
            if (eigenaar.IsVisible && eigenaar.WindowState != WindowState.Minimized)
                eigenaar.Activate();
        };

        return venster;
    }
}

using System.Windows;

namespace Vindioo;

/// <summary>
/// Hoe een venster boven een ander venster opengaat.
/// </summary>
public static class Vensters
{
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

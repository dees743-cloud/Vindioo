namespace Vindioo.Models;

/// <summary>
/// Hoe de resultaten over pagina's verdeeld worden.
///
/// <para>Staat in <c>Models</c> en niet bij het raster zelf, om twee redenen. Het is een regel en
/// geen tekenwerk - en de controles compileren <c>Models</c>, <c>Sources</c> en <c>Services</c>
/// mee, maar de vensters niet. Een regel die in een <c>Control</c> zit, valt dus buiten alles wat
/// na te meten is.</para>
/// </summary>
public static class PageLayout
{
    /// <summary>
    /// De paginagrootte afgerond op een <b>heel aantal rijen</b>.
    ///
    /// <para>Waarom dit bestaat: een vaste paginagrootte van 100 geeft bij 8 kolommen twaalf
    /// volle rijen en een laatste rij met vier kaarten. Die halve rij leest als "dit is alles",
    /// terwijl er nog pagina's volgen - gemeten op een schermafbeelding van 4 oktober 2026, met
    /// de pager op pagina 6 van meer dan negen. Hoeveel er naast elkaar passen hangt van de
    /// breedte van het venster af, dus hangt de paginagrootte daar voortaan ook van af.</para>
    ///
    /// <para>Er wordt naar het <b>dichtstbijzijnde</b> hele aantal rijen gegaan, omhoog of
    /// omlaag, en altijd minstens één rij. Zo blijft de gevraagde grootte uit de instellingen het
    /// richtgetal, en wordt ze op een smal venster niet stil gehalveerd.</para>
    /// </summary>
    /// <param name="gevraagd">De paginagrootte uit de instellingen.</param>
    /// <param name="kolommen">Hoeveel kaarten er naast elkaar passen; 1 in de lijstweergave.</param>
    public static int VolleRijen(int gevraagd, int kolommen)
    {
        if (kolommen <= 1 || gevraagd <= 0) return gevraagd;

        var rijen = Math.Max(1, (int)Math.Round(gevraagd / (double)kolommen,
                                                MidpointRounding.AwayFromZero));

        return rijen * kolommen;
    }
}

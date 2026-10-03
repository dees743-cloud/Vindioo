using Vindioo.Models;

namespace Vindioo.Services;

/// <summary>
/// Waarschuwt dat een bewaarde veiling bijna afloopt.
///
/// Waarom dit er is: een favoriet is een kopie, en tot nu keek de app er enkel naar wanneer jij op
/// <i>Nakijken</i> duwde. Een kavel waarop je wou bieden liep dus af terwijl je iets anders deed,
/// en achteraf stond er enkel "Veiling afgelopen op 28 september" op de kaart - precies het
/// moment dat je had willen weten, maar dan te laat.
///
/// <b>Hier gaat geen enkel verzoek de deur uit.</b> Dat is met opzet: de waarschuwing hangt aan
/// het einde dat we al kennen, niet aan een nieuwe ronde langs de sites. Zou dit zelf gaan
/// ophalen, dan deed het dat elke halve minuut voor elke favoriet, op een moment dat jij niet
/// kijkt - en dat is precies wat een site als robotverkeer ziet. Wat dat kost staat onderaan bij
/// "Wat dit niet doet".
/// </summary>
public static class AuctionWatch
{
    /// <summary>
    /// De momenten waaruit je kan kiezen, in minuten voor het einde. Vier is genoeg: een dag
    /// vooraf om te beslissen of je meedoet, vier uur om je dag te plannen, een uur om erbij te
    /// gaan zitten, een kwartier voor het echte werk.
    /// </summary>
    public static readonly int[] Keuzes = { 1440, 240, 60, 15 };

    /// <summary>Wat er in het instellingenscherm bij zo'n keuze staat.</summary>
    public static string Noem(int minuten) => minuten switch
    {
        >= 1440 => minuten == 1440 ? "1 dag vooraf" : $"{minuten / 1440} dagen vooraf",
        >= 60 => minuten == 60 ? "1 uur vooraf" : $"{minuten / 60} uur vooraf",
        _ => $"{minuten} minuten vooraf"
    };

    /// <summary>
    /// Welke waarschuwing is deze veiling nu toe, of null wanneer er niets te melden valt.
    ///
    /// De regel is de <b>kleinste</b> drempel waar we binnen zitten en die nog niet gemeld is.
    /// Dat kleine is belangrijk. Stond de app een nacht uit en kom je terug met nog twintig
    /// minuten te gaan, dan zit je tegelijk binnen "1 dag", "4 uur" en "1 uur". De grootste nemen
    /// zou drie meldingen na elkaar geven, één per tik, want elke volgende blijft dan openstaan.
    /// De kleinste nemen en die onthouden dekt alles wat grover is in één keer.
    /// </summary>
    /// <param name="einde">Wanneer de veiling sluit, zoals wij het kennen.</param>
    /// <param name="nu">Nu.</param>
    /// <param name="momenten">De aangevinkte drempels, in minuten.</param>
    /// <param name="alGemeld">De kleinste drempel die voor deze favoriet al gemeld is.</param>
    internal static int? Aanstaand(DateTime? einde, DateTime nu, IReadOnlyList<int> momenten, int? alGemeld)
    {
        if (einde is null || momenten.Count == 0) return null;

        // Op de echte tijdlijn, want tussen nu en het einde kan de wintertijd vallen - dezelfde
        // rekenfout die de afteltimer een uur verkeerd zette. Zie docs/resultaten.md.
        var over = Listing.Resterend(einde.Value, nu);

        // Voorbij: dan is het geen waarschuwing meer maar een stempel op de kaart.
        if (over <= TimeSpan.Zero) return null;

        int? kleinste = null;

        foreach (var moment in momenten)
        {
            if (over.TotalMinutes > moment) continue;          // nog niet binnen deze drempel
            if (alGemeld is { } g && moment >= g) continue;    // deze of een grovere ging al
            if (kleinste is null || moment < kleinste) kleinste = moment;
        }

        return kleinste;
    }

    /// <summary>
    /// Eén ronde: kijkt alle favorieten na en stuurt wat er te sturen valt.
    ///
    /// Draait mee op de tik van <see cref="SearchScheduler"/>, elke halve minuut, ook wanneer de
    /// app in het systeemvak zit - daar is ze juist voor.
    /// </summary>
    public static async Task TickAsync(HistoryStore history, DateTime nu)
    {
        var instellingen = AppSettings.Current.Notify;
        if (!instellingen.AuctionAlert) return;

        var momenten = instellingen.AuctionAlertMinutes;
        if (momenten.Count == 0) return;

        foreach (var favoriet in history.GetFavorites())
        {
            var moment = Aanstaand(favoriet.EndsAt, nu, momenten, favoriet.AlertedLead);
            if (moment is null) continue;

            // Eerst opschrijven, dan pas sturen. Andersom zou een melding die halverwege
            // vastloopt bij de volgende tik opnieuw vertrekken, en dan elke halve minuut.
            history.SetFavoriteAlerted(favoriet.Key, moment);

            var over = Listing.Resterend(favoriet.EndsAt!.Value, nu);
            Log.Write($"veiling '{Kort(favoriet.Title)}' loopt af over {Hoelang(over)} " +
                      $"(drempel {moment} min)");

            await Notifier.NotifyAuctionAsync(favoriet, over);
        }
    }

    /// <summary>"58 minuten", "3 uur", "1 dag" - zoals je het zelf zou zeggen.</summary>
    internal static string Hoelang(TimeSpan over)
    {
        if (over.TotalMinutes < 1) return "minder dan een minuut";
        if (over.TotalMinutes < 60) return (int)over.TotalMinutes == 1 ? "1 minuut" : $"{(int)over.TotalMinutes} minuten";
        if (over.TotalHours < 24) return (int)over.TotalHours == 1 ? "1 uur" : $"{(int)over.TotalHours} uur";

        return (int)over.TotalDays == 1 ? "1 dag" : $"{(int)over.TotalDays} dagen";
    }

    private static string Kort(string tekst) => tekst.Length <= 60 ? tekst : tekst[..57] + "...";
}

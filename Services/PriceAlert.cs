using Vindioo.Models;

namespace Vindioo.Services;

/// <summary>
/// Waarschuwt dat de prijs van een bewaarde favoriet veranderd is.
///
/// <para><b>Dit is het enige stuk van de app dat uit zichzelf het net op gaat.</b> Overal elders
/// gebeurt er pas iets wanneer jij op een knop duwt of een zoekopdracht gepland staat. De
/// waarschuwing voor een aflopende veiling (<see cref="AuctionWatch"/>) kost met opzet geen enkel
/// verzoek: die hangt aan een sluitingstijd die al bekend is. Een prijs weet je alleen door te
/// gaan kijken, en daar is geen omweg voor.</para>
///
/// <para>Daarom zit de rem in de code en niet in een instelling die iemand per ongeluk op "elke
/// minuut" zet. Er wordt enkel gekeken naar favorieten waarvoor je het zelf aanzette, hoogstens
/// <see cref="HoogstensPerTik"/> tegelijk, en per favoriet niet vaker dan het ritme hieronder.</para>
/// </summary>
public static class PriceAlert
{
    /// <summary>Het gewone ritme: vier keer per dag is genoeg om een prijsdaling op te merken.</summary>
    internal static readonly TimeSpan Gewoon = TimeSpan.FromHours(6);

    /// <summary>
    /// Zes uur is te grof voor een kavel dat vandaag sluit: daar gaat het bod in de laatste uren
    /// omhoog, en dat is net wat je wil weten. Binnen deze marge voor het einde gaat het naar
    /// <see cref="Dringend"/>.
    /// </summary>
    internal static readonly TimeSpan BijnaKlaarVanaf = TimeSpan.FromHours(24);

    /// <summary>Het ritme voor zo'n kavel.</summary>
    internal static readonly TimeSpan Dringend = TimeSpan.FromHours(1);

    /// <summary>
    /// Hoeveel favorieten er hoogstens in één tik nagekeken worden. Niet omdat het er veel zijn,
    /// maar omdat twintig verzoeken in één seconde het patroon is waar een site op let. Wie niet
    /// aan de beurt kwam, is dat bij de volgende tik - die komt na een halve minuut.
    /// </summary>
    internal const int HoogstensPerTik = 3;

    /// <summary>
    /// Is deze favoriet aan een nieuwe meting toe?
    ///
    /// Apart gezet zodat de regel na te meten is zonder één verzoek te versturen: die lus
    /// hieronder is precies het stuk dat een controle niet mag uitvoeren.
    /// </summary>
    /// <param name="favoriet">De favoriet, met zijn eigen keuzes.</param>
    /// <param name="nu">Nu.</param>
    internal static bool AanDeBeurt(Listing favoriet, DateTime nu)
    {
        // Niet aangezet, of nergens heen te sturen: dan valt er ook niets te meten. Wie niets
        // wil weten, hoort geen verkeer te veroorzaken.
        if (!favoriet.AlertPrice || favoriet.AlertChannels == AlertChannels.Geen) return false;

        // Nog nooit gekeken: dan nu. Dat ijkt meteen de prijs waartegen we straks vergelijken.
        if (favoriet.PriceCheckedAt is not { } laatst) return true;

        // Een klok die achteruit ging (zomertijd, of een pc die bijgesteld werd) mag geen reden
        // zijn om een halve dag te zwijgen.
        if (laatst > nu) return true;

        var bijnaKlaar = favoriet.EndsAt is { } einde &&
                         Listing.Resterend(einde, nu) is var over &&
                         over > TimeSpan.Zero && over <= BijnaKlaarVanaf;

        return nu - laatst >= (bijnaKlaar ? Dringend : Gewoon);
    }

    /// <summary>
    /// Is dit een prijswijziging waar een bericht bij hoort?
    ///
    /// <para>Vergeleken wordt er met de prijs waarover je het <b>laatst bericht kreeg</b>, en niet
    /// met de prijs die op de kaart staat. Die kaart toont met opzet nog de prijs van de dag dat
    /// je de favoriet bewaarde - dat is waar "was € 5, nu € 24" op slaat. Zou de waarschuwing
    /// daartegen vergelijken, dan kreeg je bij elke ronde opnieuw bericht over dezelfde
    /// wijziging.</para>
    /// </summary>
    internal static bool IsNieuws(decimal? gemeld, decimal? bewaard, decimal? nu)
    {
        if (nu is not { } prijs || prijs <= 0) return false;

        // Nooit eerder gemeld: dan is de prijs van het bewaren het ijkpunt.
        var vorige = gemeld ?? bewaard;

        return vorige is { } oud && oud > 0 && oud != prijs;
    }

    /// <summary>
    /// Eén ronde. Draait mee op de tik van <see cref="SearchScheduler"/>, net als
    /// <see cref="AuctionWatch"/> - maar deze doet wél verzoeken, dus met de rem erop.
    /// </summary>
    public static async Task TickAsync(HistoryStore history, IReadOnlyList<SiteDefinition> sites,
                                       DateTime nu, CancellationToken ct = default)
    {
        var beurt = history.GetFavorites().Where(f => AanDeBeurt(f, nu)).Take(HoogstensPerTik).ToList();
        if (beurt.Count == 0) return;

        foreach (var favoriet in beurt)
        {
            if (ct.IsCancellationRequested) return;

            // Eerst opschrijven dat we gekeken hebben, dan pas kijken. Andersom zou een site die
            // blijft hangen bij elke tik opnieuw geprobeerd worden - elke halve minuut.
            history.SetFavoriteChecked(favoriet.Key, nu);

            FavoriteStatus status;

            try
            {
                status = await FavoriteWatch.CheckAsync(favoriet, sites, ct);
            }
            catch (Exception ex)
            {
                Log.Write($"prijswacht: '{Kort(favoriet.Title)}' kon niet nagekeken worden - {ex.Message}");
                continue;
            }

            // Het einde meenemen nu de pagina toch gelezen is. Dat kost niets extra en houdt de
            // waarschuwing van AuctionWatch scherp, ook bij een veiling die verlengd werd.
            if (status.Einde is not null && status.Einde != favoriet.EndsAt)
                history.SetFavoriteEnd(favoriet.Key, status.Einde);

            // De laatst bekende prijs onthouden bij ELKE geslaagde meting, niet enkel wanneer
            // er een bericht uitgaat. Anders klopt de kaart wel na een melding, maar niet na
            // een ronde waarin de prijs toevallig gelijk bleef of waarin je zelf geen bericht
            // wilde - en dan staat er alsnog een verouderd bedrag.
            if (status.PrijsNu is > 0 && status.PrijsNu != favoriet.CurrentPrice)
                history.SetFavoriteCurrentPrice(favoriet.Key, status.PrijsNu);

            if (!IsNieuws(favoriet.NotifiedPrice, favoriet.Price, status.PrijsNu)) continue;

            var oud = favoriet.NotifiedPrice ?? favoriet.Price;

            // Weer eerst opschrijven: een melding die halverwege vastloopt mag niet bij de
            // volgende ronde opnieuw vertrekken.
            history.SetFavoriteNotifiedPrice(favoriet.Key, status.PrijsNu);

            Log.Write($"prijswacht: '{Kort(favoriet.Title)}' ging van €{oud:0.##} naar €{status.PrijsNu:0.##}");

            await Notifier.NotifyPriceChangeAsync(favoriet, oud, status.PrijsNu!.Value, favoriet.AlertChannels);
        }
    }

    private static string Kort(string tekst) => tekst.Length <= 60 ? tekst : tekst[..57] + "...";
}

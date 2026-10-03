namespace Vindioo.Models;

/// <summary>Hoe vaak een zoekopdracht vanzelf opnieuw draait.</summary>
public enum ScheduleMode
{
    /// <summary>Nooit vanzelf; enkel wanneer je er zelf op klikt.</summary>
    Off = 0,

    /// <summary>Om de zoveel minuten.</summary>
    Interval = 1,

    /// <summary>Eén keer per dag, op een vast uur.</summary>
    Daily = 2
}

/// <summary>
/// De timing van één zoekopdracht. Losgekoppeld van de zoekopdracht zelf omdat
/// dit het enige stuk is dat de planner nodig heeft: wanneer moet ik draaien?
/// </summary>
public class SearchSchedule
{
    public ScheduleMode Mode { get; set; } = ScheduleMode.Off;

    /// <summary>Bij <see cref="ScheduleMode.Interval"/>: om de hoeveel minuten.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Bij <see cref="ScheduleMode.Daily"/>: op welk uur (0-23).</summary>
    public int DailyHour { get; set; } = 8;

    /// <summary>Bij <see cref="ScheduleMode.Daily"/>: op welke minuut (0-59).</summary>
    public int DailyMinute { get; set; }

    /// <summary>
    /// Enkel zoeken binnen een tijdvenster. 's Nachts elk uur een browser
    /// opstarten heeft weinig zin en houdt de pc wakker.
    /// </summary>
    public bool OnlyBetween { get; set; }

    public int FromHour { get; set; } = 8;
    public int ToHour { get; set; } = 22;

    /// <summary>Meteen bij het opstarten van de app één keer draaien.</summary>
    public bool RunOnStartup { get; set; }

    /// <summary>Een melding sturen wanneer er iets nieuws gevonden is.</summary>
    public bool NotifyOnNew { get; set; } = true;

    /// <summary>
    /// Wanneer deze zoekopdracht voor het eerst weer aan de beurt is, gerekend
    /// vanaf de laatste keer dat hij draaide. Null betekent nooit vanzelf.
    ///
    /// Het tijdvenster wordt hier niet in verrekend: dat blijft een aparte
    /// vraag (<see cref="WithinWindow"/>), zodat de planner het verschil kan
    /// tonen tussen "nog niet aan de beurt" en "buiten de uren".
    ///
    /// <paramref name="now"/> is het moment waarop de planner kijkt. "Meteen aan de beurt"
    /// is dan precies dat moment. Tot september 2026 was dat telkens een nieuwe
    /// <c>DateTime.Now</c>, een fractie later dan het moment waarmee de planner vergeleek, en
    /// "later dan nu" is nooit aan de beurt: een dagelijkse zoekopdracht, en een zoekopdracht
    /// met interval die nog nooit gedraaid had, startten nooit vanzelf.
    /// </summary>
    public DateTime? NextRun(DateTime? lastRun, DateTime? now = null)
    {
        var nu = now ?? DateTime.Now;

        switch (Mode)
        {
            case ScheduleMode.Interval:
                var minutes = Math.Max(1, IntervalMinutes);

                // Nog nooit gedraaid: meteen aan de beurt.
                return lastRun is null
                    ? nu
                    : lastRun.Value.AddMinutes(minutes);

            case ScheduleMode.Daily:
                var today = nu.Date.AddHours(DailyHour).AddMinutes(DailyMinute);

                // Het uur van vandaag is al voorbij én we draaiden vandaag al:
                // dan is morgen aan de beurt.
                if (lastRun is { } run && run >= today) return today.AddDays(1);

                return today <= nu ? nu : today;

            default:
                return null;
        }
    }

    /// <summary>Valt dit moment binnen het toegelaten tijdvenster?</summary>
    public bool WithinWindow(DateTime moment)
    {
        if (!OnlyBetween) return true;

        var hour = moment.Hour;

        // Een venster dat over middernacht loopt (22 tot 6) draait om.
        return FromHour <= ToHour
            ? hour >= FromHour && hour < ToHour
            : hour >= FromHour || hour < ToHour;
    }

    /// <summary>Korte omschrijving voor in de lijst, bv. "elk uur, 8-22u".</summary>
    public string Describe()
    {
        var basis = Mode switch
        {
            ScheduleMode.Interval => IntervalMinutes switch
            {
                < 60 => $"elke {IntervalMinutes} min",
                60 => "elk uur",
                _ when IntervalMinutes % 60 == 0 => $"elke {IntervalMinutes / 60} uur",
                _ => $"elke {IntervalMinutes} min"
            },
            ScheduleMode.Daily => $"dagelijks om {DailyHour:00}:{DailyMinute:00}",
            _ => "handmatig"
        };

        if (Mode != ScheduleMode.Off && OnlyBetween) basis += $", {FromHour}-{ToHour}u";
        if (RunOnStartup) basis += ", ook bij opstarten";

        return basis;
    }
}

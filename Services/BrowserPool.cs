namespace Vindioo.Services;

/// <summary>
/// Eén Chrome voor een hele zoekopdracht in plaats van een per site.
///
/// Waarom: het starten van Chrome kost ruim een seconde, en met eBay, Facebook en
/// AlleVeilingen aangevinkt betaalde de app dat drie keer. Ze kunnen die browser
/// gerust delen — ze gaan toch na elkaar, want twee Playwright-sessies op hetzelfde
/// profiel botsen.
///
/// Waarom niet gewoon één browser die blijft staan: dan houdt een app die in het
/// systeemvak op zijn volgende beurt wacht, uren een Chrome open voor niets. Vandaar
/// een telling: wie hem nodig heeft neemt een <see cref="Lease"/>, en zodra de
/// laatste die teruggeeft gaat de browser dicht.
/// </summary>
public static class BrowserPool
{
    private static readonly object Slot = new();

    private static BrowserFetcher? _fetcher;
    private static int _leases;

    /// <summary>
    /// Meldt dat er een zoekopdracht loopt die de browser mag gebruiken. Geef het
    /// resultaat vrij (met <c>using</c>) zodra die zoekopdracht klaar is.
    /// </summary>
    public static IDisposable Lease()
    {
        lock (Slot) _leases++;
        return new Uitleen();
    }

    /// <summary>
    /// De gedeelde browser. Sluit hem NIET zelf af; dat gebeurt wanneer de laatste
    /// lener klaar is.
    /// </summary>
    public static BrowserFetcher Get()
    {
        lock (Slot) return _fetcher ??= new BrowserFetcher();
    }

    private sealed class Uitleen : IDisposable
    {
        private bool _klaar;

        public void Dispose()
        {
            if (_klaar) return;
            _klaar = true;

            BrowserFetcher? sluiten = null;

            lock (Slot)
            {
                if (--_leases > 0) return;

                sluiten = _fetcher;
                _fetcher = null;
            }

            if (sluiten is null) return;

            // Afsluiten mag op zijn gemak: de zoekopdracht is klaar en niemand
            // wacht erop. Een fout hierbij hoort de app niet te laten vallen.
            _ = Task.Run(async () =>
            {
                try
                {
                    await sluiten.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Log.Write($"browser: afsluiten mislukt - {ex.Message}");
                }
            });
        }
    }
}

using System.IO;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// Welke Chrome de app afsluit voor ze zelf een Chrome start. Enkel de beslissing en het
/// uitlezen van een opdrachtregel; er wordt hier niets afgesloten.
/// </summary>
public static class BrowserChecks
{
    public static void Run()
    {
        Check.Groep("Achtergebleven Chrome: enkel onze eigen, van een vorige sessie");

        var eigen = BrowserFetcher.LeesOpdrachtregel(Environment.ProcessId);
        Check.Dat(eigen is not null && eigen.Contains("Zentrix.Checks", StringComparison.OrdinalIgnoreCase),
            $"de opdrachtregel van een proces uitlezen lukt ('{eigen}')");

        var profiel = @"C:\Users\iemand\AppData\Roaming\Zentrix\browser-profiel";
        var nu = DateTime.Now;
        var oud = nu.AddMinutes(-5);

        Check.Dat(BrowserFetcher.IsAchtergebleven(oud, $"chrome.exe --user-data-dir={profiel} --headless", nu, profiel),
            "ouder dan Zentrix en met ons profiel: afsluiten");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(oud, @"chrome.exe --user-data-dir=C:\Users\iemand\AppData\Local\Google\Chrome\User Data", nu, profiel),
            "je eigen Chrome (ander profiel): blijven");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(nu.AddSeconds(5), $"chrome.exe --user-data-dir={profiel}", nu, profiel),
            "met ons profiel maar gestart na Zentrix (van deze sessie): blijven");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(oud, null, nu, profiel),
            "opdrachtregel niet leesbaar: blijven");

        Check.Groep("Opstarten met Windows: nooit dotnet.exe als pad");
        var pad = Autostart.ExePad();
        Check.Dat(pad is null || !Path.GetFileName(pad).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase),
            $"ExePad geeft geen dotnet.exe ('{pad}')");
    }
}

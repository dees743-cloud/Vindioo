using System.IO;
using Vindioo.Services;

namespace Vindioo.Checks;

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
        Check.Dat(eigen is not null && eigen.Contains("Vindioo.Checks", StringComparison.OrdinalIgnoreCase),
            $"de opdrachtregel van een proces uitlezen lukt ('{eigen}')");

        var profiel = @"C:\Users\iemand\AppData\Roaming\Vindioo\browser-profiel";
        var nu = DateTime.Now;
        var oud = nu.AddMinutes(-5);

        Check.Dat(BrowserFetcher.IsAchtergebleven(oud, $"chrome.exe --user-data-dir={profiel} --headless", nu, profiel),
            "ouder dan Vindioo en met ons profiel: afsluiten");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(oud, @"chrome.exe --user-data-dir=C:\Users\iemand\AppData\Local\Google\Chrome\User Data", nu, profiel),
            "je eigen Chrome (ander profiel): blijven");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(nu.AddSeconds(5), $"chrome.exe --user-data-dir={profiel}", nu, profiel),
            "met ons profiel maar gestart na Vindioo (van deze sessie): blijven");
        Check.Dat(!BrowserFetcher.IsAchtergebleven(oud, null, nu, profiel),
            "opdrachtregel niet leesbaar: blijven");

        Check.Groep("Opstarten met Windows: nooit dotnet.exe als pad");
        var pad = Autostart.ExePad();
        Check.Dat(pad is null || !Path.GetFileName(pad).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase),
            $"ExePad geeft geen dotnet.exe ('{pad}')");

        // ---------------------------------------------------------------------------
        Check.Groep("De versie is leesbaar uit de assembly");
        {
            // Let op wat hier gemeten wordt: dit controleproject compileert de broncode zelf,
            // dus Versie leest hier de assembly van HET CONTROLEPROJECT (1.0.0), niet die van
            // Vindioo. Het nummer van de app is dus niet hier na te meten - wel het mechanisme:
            // dat er een leesbaar nummer uit komt en dat een achtervoegsel als "+a1b2c3" (dat
            // de bouwomgeving erbij zet) eraf gaat. Dat 0.9.0 in het tandwielmenu staat, is
            // buiten beeld nagemeten met het echte hoofdscherm.
            var nummer = Versie.Nummer;

            Check.Dat(System.Text.RegularExpressions.Regex.IsMatch(nummer, @"^\d+\.\d+\.\d+$"),
                $"drie getallen, zonder commit-hash erachter ({nummer})");
            Check.Dat(nummer != "?", "er komt een nummer uit, geen vraagteken");
            Check.Dat(Versie.Volledig == "Vindioo " + nummer, $"met de naam ervoor ({Versie.Volledig})");
        }
    }

    /// <summary>
    /// Eén profielmap, één Chrome. Deze controles starten er géén: ze nemen de map vooraf in
    /// en kijken of de fetcher dat merkt voor hij Playwright aanspreekt.
    /// </summary>
    public static async Task RunAsync()
    {
        Check.Groep("Browser: twee Chrome's op één profielmap kan niet, en dat is nu te lezen");

        var profiel = BrowserFetcher.ProfilePath;

        Check.Dat(BrowserFetcher.NeemProfiel(profiel), "de eerste neemt de profielmap");
        Check.Dat(!BrowserFetcher.NeemProfiel(profiel), "een tweede krijgt ze niet");
        Check.Dat(!BrowserFetcher.NeemProfiel(profiel.ToUpperInvariant()),
            "ook niet geschreven met andere hoofdletters - Windows ziet dezelfde map");

        // En dan de weg die de gebruiker echt loopt: de aanmeldknop in Sites beheren maakt een
        // eigen BrowserFetcher, want hij heeft een zichtbaar venster nodig. Klik je die terwijl
        // er gezocht wordt, dan kwam daar tot 18 oktober 2026 "Target page, context or browser
        // has been closed" uit, met een half scherm browserlogboek erachter: die vensters tonen
        // ex.Message rechtstreeks. Gemeten na 0,5 s, want Chrome ging eerst nog op pad.
        await using (var tweede = new BrowserFetcher())
        {
            var klok = System.Diagnostics.Stopwatch.StartNew();
            Exception? fout = null;

            try { await tweede.GetHtmlAsync("http://127.0.0.1:1/"); }
            catch (Exception ex) { fout = ex; }

            klok.Stop();

            Check.Dat(fout is InvalidOperationException,
                $"een tweede fetcher krijgt een gewone fout, geen Playwright-fout ({fout?.GetType().Name})");

            Check.Dat(fout?.Message == BrowserFetcher.ProfielBezet,
                $"met een zin die zegt wat je eraan doet ('{fout?.Message}')");

            Check.Dat(klok.ElapsedMilliseconds < 2000,
                $"en meteen, zonder eerst een Chrome te starten ({klok.ElapsedMilliseconds} ms)");
        }

        // De map hoort door dat tweede exemplaar niet vrijgegeven te zijn: hij heeft ze nooit
        // gehad. Anders laat een mislukte aanmeldpoging de lopende zoekopdracht in de kou staan.
        Check.Dat(!BrowserFetcher.NeemProfiel(profiel),
            "een fetcher die de map nooit kreeg, geeft ze bij het opruimen ook niet vrij");

        BrowserFetcher.GeefProfielTerug(profiel);

        Check.Dat(BrowserFetcher.NeemProfiel(profiel), "teruggegeven: de volgende mag weer");
        BrowserFetcher.GeefProfielTerug(profiel);
    }
}

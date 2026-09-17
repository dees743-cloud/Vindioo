using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Zentrix.Services;

/// <summary>
/// Haalt pagina's op met een echte Chrome die een eigen profielmap gebruikt.
/// Daardoor blijven cookies en logins bewaard tussen sessies, net als in
/// je gewone browser. Dat maakt de app herkenbaar als terugkerende bezoeker
/// in plaats van als vreemde robot.
/// </summary>
public class BrowserFetcher : IAsyncDisposable
{
    /// <summary>Map met het browserprofiel van de app, los van je eigen Chrome.</summary>
    public static string ProfilePath => AppPaths.BrowserProfile;

    private IPlaywright? _playwright;
    private IBrowserContext? _context;

    /// <summary>Zichtbaar draaien. Aan voor aanmelden, uit voor gewoon zoeken.</summary>
    public bool Visible { get; set; }

    /// <param name="waitSelector">
    /// Waar we op wachten: de selector van een zoekertje. Staat die op de pagina,
    /// dan is er niets meer af te wachten en kunnen we meteen uitlezen.
    /// </param>
    /// <param name="vervolgpagina">
    /// Pagina 2 of verder. Die heeft pagina 1 al achter zich, dus de selector werkt;
    /// staat er na drie seconden nog niets, dan is de pagina leeg en wachten we niet
    /// verder. Vroeger wachtte zo'n lege pagina acht seconden plus nog vier aan pauzes.
    /// </param>
    public async Task<string> GetHtmlAsync(string url, string? waitSelector = null,
                                           CancellationToken ct = default, bool vervolgpagina = false)
    {
        // Waar de seconden blijven. Een browserbron is met afstand de traagste weg,
        // en zonder deze tussenstanden is niet te zien of dat aan het starten van
        // Chrome ligt, aan de pagina, of aan ons eigen wachten.
        var klok = System.Diagnostics.Stopwatch.StartNew();
        var stap = new System.Text.StringBuilder();

        void Meet(string wat)
        {
            stap.Append($" {wat} {klok.ElapsedMilliseconds}ms");
            klok.Restart();
        }

        // Nooit eindeloos wachten op een profiel dat nog vastzit.
        var context = await GetContextAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Meet("chrome");

        var page = await context.NewPageAsync();
        Meet("tabblad");

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 45000
            });
            Meet("laden");

            // Wachten tot het EERSTE zoekertje er staat, niet tot het netwerk stil
            // valt. Dat laatste kostte op eBay elke keer de volle acht seconden: met
            // advertenties en trackers wordt het daar nooit stil, dus liep die wachttijd
            // altijd in zijn limiet — 8 van de 12 seconden per pagina, voor niets. De
            // brug leerde diezelfde les al met het load-event.
            //
            // Zodra het zoekertje er staat is de pagina bruikbaar; komt het er niet,
            // dan vallen we terug op een korte adempauze in plaats van een lange.
            var gevonden = false;

            if (!string.IsNullOrWhiteSpace(waitSelector))
            {
                try
                {
                    await page.WaitForSelectorAsync(waitSelector,
                        new PageWaitForSelectorOptions { Timeout = vervolgpagina ? 3000 : 8000 });
                    gevonden = true;
                }
                catch (TimeoutException)
                {
                    // Geen zoekertjes te zien: misschien geen resultaten, misschien
                    // een blokkade. Hoe dan ook geen reden om langer te wachten.
                }
            }

            // Een lege vervolgpagina is gewoon het einde van de resultaten; daar valt
            // niets meer af te wachten. Enkel bij de eerste pagina nog even kijken of
            // het netwerk stil valt, want daar kan het ook een trage site zijn.
            if (!gevonden && !vervolgpagina)
            {
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new PageWaitForLoadStateOptions { Timeout = 2500 });
                }
                catch (TimeoutException)
                {
                    // Achtergrondverkeer dat blijft doorlopen: niet erg.
                }
            }

            Meet(gevonden ? "zoekertjes" : "netwerkstil");

            await TryDismissCookieBannerAsync(page);
            Meet("cookies");

            // Even scrollen: veel sites laden resultaten pas bij het naar beneden gaan.
            // Stonden de zoekertjes er al, dan wachten we tot er geen meer bijkomen in
            // plaats van een vaste halve seconde (die kostte gemeten 526 ms per pagina,
            // ook bij sites waar niets bijkwam). Een lege vervolgpagina slaat dit over.
            if (gevonden)
            {
                await page.Mouse.WheelAsync(0, 2000);
                await WachtTotStabielAsync(page, waitSelector!);
            }
            else if (!vervolgpagina)
            {
                await page.Mouse.WheelAsync(0, 2000);
                await page.WaitForTimeoutAsync(1500);
            }
            Meet("scrollen");

            var html = await page.ContentAsync();
            Meet("uitlezen");

            Log.Write($"browser:{stap}");
            return html;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Wacht tot er na het scrollen geen zoekertjes meer bijkomen: telt elke 100 ms
    /// en stopt zodra het aantal twee keer gelijk bleef, met anderhalve seconde als
    /// maximum. Een site die alles meteen in de HTML zet, is zo na 200 ms klaar; een
    /// site die bijlaadt (Facebook) krijgt de tijd die ze nodig heeft.
    /// </summary>
    private static async Task WachtTotStabielAsync(IPage page, string selector)
    {
        var tot = DateTime.Now.AddMilliseconds(1500);
        var vorige = -1;
        var gelijk = 0;

        while (DateTime.Now < tot)
        {
            await page.WaitForTimeoutAsync(100);

            int aantal;
            try
            {
                aantal = await page.Locator(selector).CountAsync();
            }
            catch
            {
                return;   // onbruikbare selector of pagina weg: niet blijven proberen
            }

            if (aantal == vorige)
            {
                if (++gelijk >= 2) return;
            }
            else
            {
                gelijk = 0;
                vorige = aantal;
            }
        }
    }

    /// <summary>
    /// Haalt een JSON-API op VANUIT de pagina van die site, met eigen kopregels.
    ///
    /// Waarom niet gewoon met HttpClient: Discogs zit achter Cloudflare, en die
    /// kijkt naar de vingerafdruk van de TLS-handdruk. Python komt erdoor, .NET
    /// niet - elk verzoek van de app kreeg "Just a moment..." terug, ongeacht welke
    /// kopregels we meestuurden. Waarom dan niet gewoon naar die URL navigeren: de
    /// API weigert elk verzoek zonder de kopregel `x-apollo-operation-name`, en die
    /// kan je bij een gewone navigatie niet meegeven.
    ///
    /// Vandaar deze omweg: de API gewoon als pagina openen in Chromium, maar met
    /// een route die de kopregels aan het verzoek toevoegt. Dan legt de browser de
    /// verbinding (Cloudflare is tevreden) en gaat de kopregel toch mee.
    /// </summary>
    public async Task<string> GetJsonAsync(string url, IDictionary<string, string>? headers,
                                           CancellationToken ct = default)
    {
        var context = await GetContextAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var page = await context.NewPageAsync();

        try
        {
            // De kopregels bij het verzoek zetten in plaats van bij ons: zo blijft
            // het Chromium zelf die de verbinding legt, en dat is precies wat nodig
            // is. Een fetch vanuit de pagina werkt niet - die kreeg de wachtpagina
            // van Cloudflare terug.
            if (headers is { Count: > 0 })
            {
                await page.RouteAsync("**/*", async route =>
                {
                    var kop = new Dictionary<string, string>(route.Request.Headers);
                    foreach (var (naam, waarde) in headers) kop[naam] = waarde;

                    await route.ContinueAsync(new RouteContinueOptions { Headers = kop });
                });
            }

            var antwoord = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 45000
            });

            // De ruwe body, niet de HTML eromheen: een browser toont JSON in een
            // <pre>, en die opmaak willen we hier juist niet.
            return antwoord is null ? "" : await antwoord.TextAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Opent de browser zichtbaar op een adres en wacht tot jij het venster sluit.
    /// Gebruik dit om je één keer aan te melden bij een site.
    /// </summary>
    public async Task OpenForLoginAsync(string url, CancellationToken ct = default)
    {
        Visible = true;
        var context = await GetContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(url, new PageGotoOptions { Timeout = 45000 });

        // Wachten tot de gebruiker klaar is en het venster sluit.
        var closed = new TaskCompletionSource();
        page.Close += (_, _) => closed.TrySetResult();
        await closed.Task;
    }

    /// <summary>
    /// Klikt de cookiemelding weg, want die ligt over de resultaten heen.
    ///
    /// Eerst wordt geprobeerd te WEIGEREN en pas daarna te aanvaarden. Dat is de
    /// beleefde volgorde — we zoeken hier alleen maar zoekertjes op — en veel sites
    /// zetten die knop er tegenwoordig gewoon bij. Waar hij ontbreekt moet er toch
    /// iets aangeklikt worden, anders blijft de melding over de pagina liggen.
    ///
    /// Beide reeksen gaan als één zoekopdracht naar de pagina in plaats van als
    /// zes losse: elke losse vraag is een heen-en-weer met de browser, en dat liep
    /// op tot drieënhalve seconde per pagina.
    /// </summary>
    private static async Task TryDismissCookieBannerAsync(IPage page)
    {
        const string weigeren = "^(Alles weigeren|Weigeren|Alleen noodzakelijke|Alleen essenti.le|"
                              + "Doorgaan zonder te accepteren|Tout refuser|Continuer sans accepter|"
                              + "Reject all|Only essential|Necessary only)$";

        const string aanvaarden = "^(Alles accepteren|Accepteren|Akkoord|Tout accepter|Accept all|OK)$";

        foreach (var patroon in new[] { weigeren, aanvaarden })
        {
            try
            {
                var knop = page.GetByRole(AriaRole.Button,
                    new PageGetByRoleOptions { NameRegex = new Regex(patroon, RegexOptions.IgnoreCase) });

                if (await knop.CountAsync() == 0) continue;

                await knop.First.ClickAsync(new LocatorClickOptions { Timeout = 1500 });
                await page.WaitForTimeoutAsync(400);
                return;
            }
            catch
            {
                // Geen banner, of hij laat zich niet aanklikken: doorgaan.
            }
        }
    }

    private async Task<IBrowserContext> GetContextAsync()
    {
        if (_context is not null) return _context;

        Directory.CreateDirectory(ProfilePath);
        _playwright = await Playwright.CreateAsync();

        // LaunchPersistentContext gebruikt een echte profielmap op schijf,
        // waardoor cookies en logins bewaard blijven.
        SluitAchtergeblevenChrome();

        _context = await _playwright.Chromium.LaunchPersistentContextAsync(ProfilePath,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = !Visible,
                Channel = "chrome",   // de echte Chrome, niet de kale Chromium
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "nl-BE",
                TimezoneId = "Europe/Brussels",
                Args = new[] { "--disable-blink-features=AutomationControlled" }
            });

        return _context;
    }

    /// <summary>
    /// Een vorige sessie van Zentrix kan een Chrome hebben laten staan die het profiel
    /// vasthoudt - bijvoorbeeld wanneer de app in Visual Studio gestopt werd terwijl ze
    /// aan het zoeken was. Die moet eerst dicht, anders raakt de nieuwe Chrome niet aan het
    /// profiel.
    ///
    /// Enkel díe, en daarvoor gelden twee voorwaarden: de opdrachtregel noemt ONZE
    /// profielmap, en het proces is ouder dan deze Zentrix. Tot september 2026 ging elk
    /// Chrome-proces zonder venster dicht dat jonger was dan een minuut. Dat trof de gewone
    /// Chrome van de gebruiker: een tabblad dat net openging, en de extensie van de brug
    /// wanneer Zentrix Chrome daarvoor net zelf gestart had - sinds de rijstroken gebeurt
    /// dat tegelijk. Een achtergebleven Chrome was bovendien zelden jonger dan een minuut.
    /// </summary>
    private static void SluitAchtergeblevenChrome()
    {
        var onzeStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("chrome"))
        {
            using (process)
            {
                try
                {
                    // Gestart na deze Zentrix: van de gebruiker, of van onszelf. Dan de
                    // opdrachtregel niet eens lezen.
                    if (process.StartTime >= onzeStart) continue;

                    if (!IsAchtergebleven(process.StartTime, LeesOpdrachtregel(process.Id), onzeStart, ProfilePath))
                        continue;

                    Log.Write($"browser: achtergebleven Chrome van een vorige sessie afgesloten (proces {process.Id})");
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Geen toegang, of het proces is intussen al weg: overslaan.
                }
            }
        }
    }

    /// <summary>
    /// De beslissing zelf, los van Windows, zodat ze na te rekenen is: ouder dan deze
    /// Zentrix, en de opdrachtregel noemt ons profiel.
    /// </summary>
    internal static bool IsAchtergebleven(DateTime procesStart, string? opdrachtregel,
                                          DateTime onzeStart, string profiel) =>
        procesStart < onzeStart &&
        opdrachtregel is not null &&
        opdrachtregel.Contains(profiel, StringComparison.OrdinalIgnoreCase);

    // ---------- de opdrachtregel van een ander proces ----------
    //
    // .NET kan die niet zelf uitlezen. Windows geeft hem via NtQueryInformationProcess met
    // klasse 60 (ProcessCommandLineInformation, sinds Windows 8.1). Daarvoor volstaat het
    // beperkte leesrecht, en dat heb je op elk proces van je eigen account.

    private const int ProcessCommandLineInformation = 60;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
                                                        IntPtr buffer, int length, out int returnLength);

    /// <summary>De volledige opdrachtregel van een proces, of null als dat niet lukt.</summary>
    internal static string? LeesOpdrachtregel(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            // Eerst vragen hoe groot het antwoord is; die vraag zelf mislukt altijd.
            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var nodig);
            if (nodig <= 0) return null;

            var buffer = Marshal.AllocHGlobal(nodig);
            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, nodig, out _) != 0)
                    return null;

                // Het antwoord is een UNICODE_STRING: de lengte in bytes, de maximumlengte, en
                // een wijzer naar de tekst, die zelf verderop in dezelfde buffer staat.
                var lengte = (ushort)Marshal.ReadInt16(buffer, 0);
                var tekst = Marshal.ReadIntPtr(buffer, IntPtr.Size);

                return Marshal.PtrToStringUni(tekst, lengte / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
        _playwright?.Dispose();
        GC.SuppressFinalize(this);
    }
}
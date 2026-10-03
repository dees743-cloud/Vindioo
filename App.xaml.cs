using System.Threading;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Vindioo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Het slot dat zegt "Vindioo draait al"; vastgehouden zolang deze app leeft.</summary>
        private static Mutex? _eenExemplaar;

        /// <summary>Het seintje waarmee een tweede start het draaiende venster naar voren haalt.</summary>
        private static EventWaitHandle? _toonVenster;

        /// <summary>Hoogstens één foutmelding per minuut: een fout die zich herhaalt, geeft anders een lawine.</summary>
        private static DateTime _laatsteFoutmelding = DateTime.MinValue;

        // "Local\": per aangemelde gebruiker, zodat twee gebruikers op dezelfde pc elk hun
        // eigen Vindioo kunnen draaien.
        private const string SlotNaam = @"Local\Vindioo-een-exemplaar";
        private const string SeinNaam = @"Local\Vindioo-toon-venster";

        /// <summary>
        /// Het slot zoals het heette vóór de hernoeming. <b>Niet weghalen.</b>
        ///
        /// Een slot met een nieuwe naam ziet een draaiende oude versie niet, en dan starten er
        /// twee apps tegelijk - elk in de overtuiging dat ze de enige is. De tweede loopt dan
        /// stil vast op poort 8731, precies de fout die dit slot moest verhelpen. Erger nog:
        /// de ene kijkt naar %APPDATA%\Vindioo en de andere naar %APPDATA%\Zentrix.
        /// </summary>
        private static readonly string[] OudeSloten = { @"Local\Zentrix-een-exemplaar" };

        protected override void OnStartup(StartupEventArgs e)
        {
            // Maar één Vindioo tegelijk. Een tweede exemplaar kon vroeger gewoon starten,
            // maar de poort van de brug was dan al bezet: het liep stil vast en er
            // verscheen niets. Voor wie op de snelkoppeling dubbelklikt terwijl de app in
            // het systeemvak staat, leek het alsof die knop niets deed. Nu haalt een
            // tweede start het draaiende venster naar voren en stopt dan meteen.
            _eenExemplaar = new Mutex(true, SlotNaam, out var eerste);

            // En de oude naam erbij, zolang er nog een Zentrix op deze pc kan staan. Draait
            // die, dan is dit niet het eerste exemplaar - ook al is óns slot nog vrij.
            if (eerste && OudeSloten.Any(Bezet)) eerste = false;

            if (!eerste)
            {
                // Het draaiende exemplaar kan een oudere versie zijn, en die luistert naar een
                // ander sein. Allebei proberen, en stoppen bij het eerste dat aankomt.
                foreach (var naam in new[] { SeinNaam, @"Local\Zentrix-toon-venster" })
                {
                    try
                    {
                        using var sein = EventWaitHandle.OpenExisting(naam);
                        sein.Set();
                        break;
                    }
                    catch
                    {
                        // Dat sein bestaat niet, of het eerste exemplaar is net aan het
                        // stoppen. Dan is er niets te tonen.
                    }
                }

                Environment.Exit(0);
                return;
            }

            _toonVenster = new EventWaitHandle(false, EventResetMode.AutoReset, SeinNaam);

            // (Bezet staat onderaan deze klasse.)

            new Thread(() =>
            {
                while (_toonVenster.WaitOne())
                    Dispatcher.InvokeAsync(() => (MainWindow as Vindioo.MainWindow)?.BrengNaarVoren());
            })
            {
                IsBackground = true,
                Name = "Vindioo: tweede start"
            }.Start();

            base.OnStartup(e);

            // Eerst de gegevensmap. Bij de eerste start na het hernoemen verhuist die
            // van %APPDATA%\Zoekhulp naar %APPDATA%\Zentrix, en dat moet gebeurd zijn
            // voor iets anders een bestand opent. Het logboek kan pas daarna.
            _ = Services.AppPaths.Folder;
            if (Services.AppPaths.MigrationNote.Length > 0)
                Services.Log.Write(Services.AppPaths.MigrationNote);

            // Welke exe dit is. Zonder deze regel staat er in het logboek van twee weken
            // niet bij welke versie een fout maakte, en er draaien er twee op deze pc.
            Services.Log.Write($"{Services.Versie.Volledig} gestart vanuit " +
                               System.IO.Path.GetDirectoryName(Environment.ProcessPath));

            // Noodrem voor een stuurprogramma dat niet meer presenteert. Gaat de
            // grafische kaart in een rare toestand (na slaapstand, na een reset
            // van het stuurprogramma), dan tekent WPF wel maar komt er niets op
            // het scherm: je kijkt naar een spierwit venster terwijl de app
            // gewoon draait. Alles op de processor laten tekenen lost dat op.
            // Standaard staat het uit, want het kost vloeiendheid bij het
            // schuiven door lange lijsten.
            //     set VINDIOO_SOFTWARE_RENDER=1
            //
            // De oude naam telt ook nog mee: wie dit ooit zette omdat zijn scherm wit bleef,
            // zou anders bij het hernoemen zonder waarschuwing weer naar een wit venster
            // kijken - en dat is nu net het probleem dat deze schakelaar oplost.
            var zacht = Environment.GetEnvironmentVariable("VINDIOO_SOFTWARE_RENDER")
                        ?? Environment.GetEnvironmentVariable("ZENTRIX_SOFTWARE_RENDER");

            if (zacht == "1")
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode =
                    System.Windows.Interop.RenderMode.SoftwareOnly;

                Services.Log.Write("tekenen op de processor (VINDIOO_SOFTWARE_RENDER=1)");
            }

            // Vangnet: zonder dit verdwijnt een fout in de interface zonder
            // spoor - de app staat er dan als een leeg venster bij en er is
            // niets terug te vinden. De gebruiker draait deze app buiten de
            // debugger, dus het logboek is de enige plaats waar het kan staan.
            DispatcherUnhandledException += (_, args) =>
            {
                Services.Log.Write("ONVERWACHTE FOUT (scherm): " + args.Exception);

                // Vroeger sloot de app hier zonder één woord, en met haar de planner. Staat
                // het hoofdvenster, dan werkt de app verder en zegt ze het. Gebeurt het al bij
                // het opstarten, dan stopt ze wel - een onzichtbare app die het slot van
                // "Vindioo draait al" vasthoudt, is erger - maar eerst met een melding.
                // "Draait": het venster staat, of de app draait zonder venster in het
                // systeemvak (een start door Windows). Vroeger enkel het eerste; zonder het
                // tweede zou een fout in het systeemvak de hele app laten stoppen.
                var draait = MainWindow is { IsLoaded: true } or Vindioo.MainWindow { AchtergrondGestart: true };
                if (draait) args.Handled = true;

                if (DateTime.Now - _laatsteFoutmelding < TimeSpan.FromMinutes(1)) return;
                _laatsteFoutmelding = DateTime.Now;

                MessageBox.Show(
                    (draait
                        ? "Er ging iets mis in Vindioo. De app probeert gewoon verder te werken."
                        : "Vindioo kon niet starten.") +
                    $"\n\n{args.Exception.Message}\n\nDetails staan in het logboek:\n{Services.Log.FilePath}",
                    "Vindioo", MessageBoxButton.OK, draait ? MessageBoxImage.Warning : MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                Services.Log.Write("ONVERWACHTE FOUT: " + args.ExceptionObject);
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Services.Log.Write("ONVERWACHTE FOUT (achtergrond): " + args.Exception);
                args.SetObserved();
            };

            // Voor het hoofdvenster er is: dat leest meteen de bewaarde weergave
            // en de instellingen voor het systeemvak.
            Services.AppSettings.Load();

            // Wijst opstarten met Windows nog naar de oude exe, zet het dan recht.
            Services.Autostart.RefreshPath();

            // WPF-UI kleurt zijn eigen besturingselementen (vinkjes, selectie in
            // lijsten, schuifbalken) met een accentkleur die het uit Windows haalt.
            // Hier zetten we die op het indigo van het designsysteem, anders staat
            // de systeemkleur van de gebruiker tussen onze eigen kleuren.
            var accent = (Color)Current.Resources["AccentColor"];
            ApplicationAccentColorManager.Apply(accent, ApplicationTheme.Dark);

            // Wat er aan het tekenen en aan het scherm gebeurt, in het logboek. Zie
            // Services.DisplayDiagnostics: zo is een wit venster na te gaan.
            Services.DisplayDiagnostics.Start();

            // Het hoofdvenster. Dat stond vroeger als StartupUri in App.xaml, en dan toont WPF
            // het altijd, ook bij een start door Windows. Daar werd het meteen weer verborgen,
            // maar het vlak waarop de grafische kaart tekent, was dan al gemaakt - een minuut na
            // het aanmelden, terwijl Windows nog opstartte - en bleef soms wit. Nu wordt het bij
            // zo'n start niet getoond, en pas getekend wanneer je het opent (MainWindow.StartOpAchtergrondAsync).
            var stil = Services.AppSettings.Current.StartMinimized ||
                       e.Args.Any(a => string.Equals(a, "--systeemvak", StringComparison.OrdinalIgnoreCase));

            var venster = new MainWindow();
            MainWindow = venster;

            if (stil)
            {
                // Niet afwachten: OnStartup hoort niet te blijven staan terwijl de planner en
                // het systeemvakpictogram opstarten. Maar de taak wordt wél nagekeken - bij een
                // async void zou een fout hierin als onbehandelde uitzondering op de dispatcher
                // belanden, en dat is precies het soort fout dat spoorloos verdwijnt.
                _ = venster.StartOpAchtergrondAsync().ContinueWith(
                    taak => Services.Log.Write("starten in het systeemvak mislukt - " +
                                               taak.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                venster.Show();
            }
        }

        /// <summary>
        /// Draait er al iets achter dit slot? Kijkt zonder het slot te nemen.
        ///
        /// <c>OpenExisting</c> gooit wanneer het slot niet bestaat - dat is het gewone geval,
        /// en daarom is dat hier geen fout maar het antwoord "nee".
        /// </summary>
        private static bool Bezet(string naam)
        {
            try
            {
                using var slot = Mutex.OpenExisting(naam);
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Het bestaat wél, maar van een andere gebruiker. Dan is het niet het onze.
                return false;
            }
        }
    }
}

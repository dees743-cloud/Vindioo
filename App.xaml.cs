using System.Threading;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Zentrix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Het slot dat zegt "Zentrix draait al"; vastgehouden zolang deze app leeft.</summary>
        private static Mutex? _eenExemplaar;

        /// <summary>Het seintje waarmee een tweede start het draaiende venster naar voren haalt.</summary>
        private static EventWaitHandle? _toonVenster;

        /// <summary>Hoogstens één foutmelding per minuut: een fout die zich herhaalt, geeft anders een lawine.</summary>
        private static DateTime _laatsteFoutmelding = DateTime.MinValue;

        // "Local\": per aangemelde gebruiker, zodat twee gebruikers op dezelfde pc elk hun
        // eigen Zentrix kunnen draaien.
        private const string SlotNaam = @"Local\Zentrix-een-exemplaar";
        private const string SeinNaam = @"Local\Zentrix-toon-venster";

        protected override void OnStartup(StartupEventArgs e)
        {
            // Maar één Zentrix tegelijk. Een tweede exemplaar kon vroeger gewoon starten,
            // maar de poort van de brug was dan al bezet: het liep stil vast en er
            // verscheen niets. Voor wie op de snelkoppeling dubbelklikt terwijl de app in
            // het systeemvak staat, leek het alsof die knop niets deed. Nu haalt een
            // tweede start het draaiende venster naar voren en stopt dan meteen.
            _eenExemplaar = new Mutex(true, SlotNaam, out var eerste);

            if (!eerste)
            {
                try
                {
                    using var sein = EventWaitHandle.OpenExisting(SeinNaam);
                    sein.Set();
                }
                catch
                {
                    // Het eerste exemplaar is net aan het stoppen: niets meer te tonen.
                }

                Environment.Exit(0);
                return;
            }

            _toonVenster = new EventWaitHandle(false, EventResetMode.AutoReset, SeinNaam);

            new Thread(() =>
            {
                while (_toonVenster.WaitOne())
                    Dispatcher.InvokeAsync(() => (MainWindow as Zentrix.MainWindow)?.BrengNaarVoren());
            })
            {
                IsBackground = true,
                Name = "Zentrix: tweede start"
            }.Start();

            base.OnStartup(e);

            // Eerst de gegevensmap. Bij de eerste start na het hernoemen verhuist die
            // van %APPDATA%\Zoekhulp naar %APPDATA%\Zentrix, en dat moet gebeurd zijn
            // voor iets anders een bestand opent. Het logboek kan pas daarna.
            _ = Services.AppPaths.Folder;
            if (Services.AppPaths.MigrationNote.Length > 0)
                Services.Log.Write(Services.AppPaths.MigrationNote);

            // Noodrem voor een stuurprogramma dat niet meer presenteert. Gaat de
            // grafische kaart in een rare toestand (na slaapstand, na een reset
            // van het stuurprogramma), dan tekent WPF wel maar komt er niets op
            // het scherm: je kijkt naar een spierwit venster terwijl de app
            // gewoon draait. Alles op de processor laten tekenen lost dat op.
            // Standaard staat het uit, want het kost vloeiendheid bij het
            // schuiven door lange lijsten.
            //     set ZENTRIX_SOFTWARE_RENDER=1
            if (Environment.GetEnvironmentVariable("ZENTRIX_SOFTWARE_RENDER") == "1")
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode =
                    System.Windows.Interop.RenderMode.SoftwareOnly;

                Services.Log.Write("tekenen op de processor (ZENTRIX_SOFTWARE_RENDER=1)");
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
                // "Zentrix draait al" vasthoudt, is erger - maar eerst met een melding.
                // "Draait": het venster staat, of de app draait zonder venster in het
                // systeemvak (een start door Windows). Vroeger enkel het eerste; zonder het
                // tweede zou een fout in het systeemvak de hele app laten stoppen.
                var draait = MainWindow is { IsLoaded: true } or Zentrix.MainWindow { AchtergrondGestart: true };
                if (draait) args.Handled = true;

                if (DateTime.Now - _laatsteFoutmelding < TimeSpan.FromMinutes(1)) return;
                _laatsteFoutmelding = DateTime.Now;

                MessageBox.Show(
                    (draait
                        ? "Er ging iets mis in Zentrix. De app probeert gewoon verder te werken."
                        : "Zentrix kon niet starten.") +
                    $"\n\n{args.Exception.Message}\n\nDetails staan in het logboek:\n{Services.Log.FilePath}",
                    "Zentrix", MessageBoxButton.OK, draait ? MessageBoxImage.Warning : MessageBoxImage.Error);
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
            // zo'n start niet getoond, en pas getekend wanneer je het opent (MainWindow.StartOpAchtergrond).
            var stil = Services.AppSettings.Current.StartMinimized ||
                       e.Args.Any(a => string.Equals(a, "--systeemvak", StringComparison.OrdinalIgnoreCase));

            var venster = new MainWindow();
            MainWindow = venster;

            if (stil) venster.StartOpAchtergrond();
            else venster.Show();
        }
    }
}

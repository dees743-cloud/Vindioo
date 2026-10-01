// Controles voor Zentrix.
//
// Draaien, vanuit de projectmap, terwijl Visual Studio gewoon open mag blijven:
//
//     dotnet run --project tests\Zentrix.Checks
//     dotnet run --project tests\Zentrix.Checks -- --snel      (zonder de trage controles)
//
// Waarom geen xUnit: dat pakket staat niet in de lokale NuGet-cache, en dit hier heeft
// niets nodig wat de app zelf niet al gebruikt. Elke controle drukt OK of FOUT af; de
// uitvoer eindigt met "ALLES OK" of het aantal fouten, en de exitcode is 0 of 1.
//
// Drie regels waar alles hier aan vastzit:
//
// 1. Nooit aan de echte gegevens komen. ZENTRIX_DATA wijst naar een nieuwe, lege map,
//    en dat moet gebeuren voor iets anders AppPaths aanraakt: SiteStore en Log vragen
//    hun pad al bij het laden van hun klasse. Daarna wordt nagekeken dat het gelukt is.
// 2. Geen netwerk naar echte sites. Een site die iets moet teruggeven, is een lokale
//    proefsite op 127.0.0.1 (zie Proefsite.cs). Een echte site verandert van dag tot
//    dag, en dan zegt een rode controle niets over de code.
// 3. Geen Chrome en geen Playwright. Wat de brug doet, wordt nagebootst met verzoeken
//    zoals de extensie of een webpagina ze stuurt.

using System.IO;
using Zentrix.Checks;
using Zentrix.Services;

var data = Path.Combine(Path.GetTempPath(), "zentrix-checks", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
Directory.CreateDirectory(data);
Environment.SetEnvironmentVariable("ZENTRIX_DATA", data);

if (!string.Equals(AppPaths.Folder, data, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"STOP: de gegevensmap is {AppPaths.Folder} en niet {data}. Er is niets gecontroleerd.");
    return 2;
}

Check.Snel = args.Contains("--snel");
Console.WriteLine($"Gegevensmap: {data}");

await PlannerChecks.RunAsync();
await PaginaChecks.RunAsync();
await StilFalenChecks.RunAsync();
await SitesChecks.RunAsync();
await PrijsChecks.RunAsync();
PricewatchChecks.Run();
await NieuwChecks.RunAsync();
await FotoChecks.RunAsync();
await FavorietChecks.RunAsync();
await PrijslezerChecks.RunAsync();
DocsChecks.Run();
ImportChecks.Run();
BrowserChecks.Run();
await BrugChecks.RunAsync();

return Check.Einde();

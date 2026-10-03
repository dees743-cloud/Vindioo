# Vindioo

Een Windows-desktopapp die meerdere tweedehands- en veilingsites tegelijk doorzoekt. Doel: het
dagelijkse zoekwerk dat anders een voormiddag kost, terugbrengen tot één minuut. De app is bedoeld
om ooit door anderen gebruikt te worden, dus alles werkt per gebruiker apart en moet er verzorgd
uitzien.

## Werkafspraken

- Commentaar in het Nederlands. Namen van klassen, methodes en variabelen in het Engels.
- UI-teksten (labels, knoppen, meldingen) in het Nederlands — die zijn voor de eindgebruiker.
- Eén stap tegelijk, en wachten op bevestiging voor de volgende. Niet vijf dingen tegelijk.
- Bij elke wijziging een concrete vindaanwijzing geven: een zoekterm voor Ctrl+F of een
  regelnummer.
- Uitleggen wat een stuk code doet en waarom, niet alleen wat er verandert. De eigenaar heeft
  basiskennis programmeren en wil bijleren.
- Geen commando's of instellingen voorstellen die niet bestaan.
- **Meten in plaats van geloven.** Een bewering over een site, een snelheid of een formaat hoort
  met een getal onderbouwd te worden. Dat is de rode draad door de hele documentatie.
- **Dit bestand blijft klein** — zie "Documentatie" onderaan.

## Stack

C# / .NET 10 (`net10.0-windows`), `Nullable` en `ImplicitUsings` aan, namespace `Vindioo`.

| | |
|---|---|
| WPF + **WPF-UI** 4.3 | Gebruik de `ui:`-varianten (`ui:Button`, `ui:TextBox`, `ui:ListView`). Donker thema in `App.xaml`, Mica uit |
| AngleSharp | HTML-parsing met CSS-selectors |
| Microsoft.Playwright | sites die een echte browser nodig hebben |
| Microsoft.Data.Sqlite | bewaarde zoekopdrachten, "al gezien", favorieten |
| MailKit | e-mail — **niet** `System.Net.Mail.SmtpClient`, zie `docs/zoeken.md` |
| CommunityToolkit.Mvvm | enkel als basisklasse (`ObservableObject`) |
| Claude API | gewone `HttpClient`; de sleutel staat beschermd door Windows (DPAPI) in `instellingen.json`, nooit in de code |
| Ollama | lokaal op de pc (RTX 4060), voor de AI-controle op een foto |

**`System.IO` is hier geen globale using** (door `UseWindowsForms`, zie `docs/zoeken.md`): schrijf
`using System.IO;` zelf bovenaan, anders bestaan `Path` en `File` niet.

## Opbouw

```
Models/
  Listing.cs         één zoekertje, los van de site waar het vandaan komt
  SiteDefinition.cs  beschrijving van een site: puur data, geen code
  SearchFilters.cs   de filterwaarden van één site (prijs, postcode, straal…)
  SiteTab.cs         één tabblad: de site, of hij meezoekt, en zijn eigen filters
  SiteChooser.cs     het chipje achteraan de tabstrip om sites aan te vinken
  SiteSetting.cs     de bewaarbare tegenhanger daarvan, per zoekopdracht
  SiteEditor.cs      dezelfde gegevens als invulvelden, voor het instellingenscherm
  SavedSearch.cs     een zoekopdracht: zoekterm, sites, schema, melding
  SearchSchedule.cs  wanneer een zoekopdracht vanzelf draait
  CustomFilter.cs    een filter dat maar op één site bestaat, uit het sitebestand
  LinkTextOptions.cs de instellingen van de linkmotor, uit het sitebestand
  ListingSort.cs     de volgorde van de resultaten, met een eigen vergelijking
  PriceIndication.cs wat een prijsindicatie opleverde
  EndTimeApiOptions.cs  een API met het sluitingstijdstip van veel veilingen tegelijk
  RecentSearch.cs    een eerder gebruikte zoekterm, met wanneer
Sources/
  ISearchSource.cs      contract waar elke bron aan voldoet
  SourceFactory.cs      kiest per site de motor (Generic of LinkText)
  SearchUrlBuilder.cs   zoek-URL met {query} en de filters uit de site
  PriceParser.cs        de prijs uit de tekst van een site, voor allebei de motoren
  GenericSources.cs     voert een SiteDefinition uit (HTML of JSON)
  LinkTextSource.cs     de linkmotor: volgt links in plaats van selectors
Services/
  SiteStore.cs       sitesmap: één JSON-bestand per site, met migratie/import/export
  HistoryStore.cs    SQLite: zoekopdrachten, "al gezien", favorieten en recent
  SiteAnalyzer*.cs   laat Claude de selectors van een onbekende site bepalen, en telt na
                     (partial, per onderwerp: Fetch, Meten, Paginering, Filters, Advertentie,
                     Prompts)
  BrowserFetcher.cs  Playwright met een eigen Chrome-profielmap
  BrowserPool.cs     één Chrome voor alle sites van dezelfde zoekopdracht
  BridgeServer.cs    lokale server waarmee de browserextensie praat
  ChromeLauncher.cs  vindt en start Chrome, en zegt waarom de brug niet werkt
  SearchRunner.cs    voert élke zoekopdracht uit — de enige zoeklus
  SearchScheduler.cs kijkt elke halve minuut wie aan de beurt is
  Notifier.cs        melding via het systeemvak, Telegram of e-mail
  TrayIcon.cs        het pictogram naast de klok (het enige stuk WinForms)
  Autostart.cs       mee opstarten met Windows, via HKCU
  AppPaths.cs        waar de gegevens staan, en de verhuis uit de oude map
  AppSettings.cs     instellingen van de app zelf (venster, weergave, meldingen)
  Log.cs             logboek in een tekstbestand
  Versie.cs          welk versienummer er draait, uit de assembly
  FriendlyError.cs   zet een fout om in een zin die de gebruiker iets zegt
  HttpFactory.cs     de User-Agent en hoe een HttpClient hier gemaakt wordt
  PriceIndicator.cs  wat is een toestel ongeveer waard
  FavoriteWatch.cs   staat een bewaarde favoriet nog te koop, en tegen welke prijs nu
  Pricewatch.cs      wat kost het nieuw bij Tweakers
  PhotoAnalyzer.cs   laat een AI op deze pc naar een foto kijken
  DetailFetcher.cs   haalt van de pagina van een zoekertje wat niet op de zoekpagina staat
  DisplayDiagnostics.cs  zet in het logboek hoe er getekend wordt, voor een wit venster
Converters/Converters.cs   zichtbaarheid van NIEUW-label, prijsopmaak, afgeronde uitsnede
Controls/
  PhotoThumbnail.xaml       miniatuur, gedeeld door beide weergaven
  VirtualizingWrapPanel.cs  raster dat enkel opbouwt wat in beeld staat
  SmoothScroll.cs           vloeiend schuiven met het muiswiel
  ScrollingText.cs          een regel die te lang is: vervaagt, en schuift bij zweven
  CountdownBadge.cs         de timer op een veilingkaart, met één gedeelde klok
  CustomFilterControls.cs   de invoer voor sitegebonden filters
Vensters (root):
  MainWindow*.cs        zoeken, filters, bewaarde zoekopdrachten (partial: Tabs, Filters,
                        Tabbladen, Zoekopdrachten, Menu, Zoeken)
  AddSiteWindow         nieuwe site toevoegen met AI-analyse en testen
  SettingsWindow        tabs met een bewerkbare kaart per site
  SearchSettingsWindow  alles van één zoekopdracht
  NotifySettingsWindow  waar meldingen heen gaan
  PriceIndicationWindow de prijsindicatie van één zoekertje
  LotPriceWindow        wat er in een partij zit: elke gelezen titel apart opgezocht
  PhotoInsightWindow    wat de AI op de foto van een zoekertje ziet
  ListingDetailWindow   alles van één zoekertje (dubbelklik)
  Vensters.cs           hoe een venster boven een ander opengaat, en wie daarna vooraan komt
extension/   de brug: een Chrome-extensie die pagina's ophaalt in je eigen browser
tools/       hulpmiddelen om buiten de app om na te meten (zie docs/fouten-opsporen.md)
tests/Vindioo.Checks/   controles zonder testframework
docs/        de uitgebreide documentatie, zie onderaan
```

## Gegevens, bouwen en nameten

Gebruikersgegevens staan in **`%APPDATA%\Vindioo`**: de map `sites\` (één JSON-bestand per site),
`vindioo.db`, `brug-code.txt`, `instellingen.json` en `browser-profiel\`. Met de
omgevingsvariabele `VINDIOO_DATA` wijs je een andere map aan — zo boots je een lege eerste start
na zonder aan de echte gegevens te komen.

```bash
dotnet build Vindioo.csproj
dotnet run --project tests\Vindioo.Checks -- --snel
dotnet publish Vindioo.csproj -c Release -r win-x64 --self-contained true -o C:\Vindioo
```

De controles drukken per stuk OK of FOUT af en eindigen met "ALLES OK" en het aantal. Draait
Vindioo of Chrome-met-de-brug, dan vallen er controles weg en zeggen ze dat zelf (663 met Vindioo
dicht en Chrome open, 616 met Vindioo erbij — gemeten 3 oktober 2026). Publiceren kan enkel met
Vindioo dicht.

## Regels die schade voorkomen

- **Kom niet aan de sitebestanden in `%APPDATA%\Vindioo\sites`.** De eigenaar beheert die zelf en
  importeert ze uit `vindioo-sites`. Lezen of kopiëren mag; schrijven niet.
- **`vindioo-sites` blijft privé**, `Vindioo` is openbaar (`dees743-cloud`, MIT). Die bestanden
  beschrijven per site hoe je zijn robotbeveiliging omzeilt, en `facebook.json` houdt een
  regionummer dat bij benadering een woonplaats is.
- **Er draait maar één Vindioo tegelijk** (een benoemde `Mutex`). Een proef met `VINDIOO_DATA` kan
  dus enkel terwijl de gewone Vindioo dicht staat.
- **Maakt een wegwerpprojectje een `App` aan, sla dan `App.OnStartup` over**: zet het private
  statische veld `Application._isShuttingDown` op `true`, pomp de dispatcher één keer, en zet het
  terug. Anders vindt die opstart het slot "Vindioo draait al" en stopt je proef.
- **Een shell binnen de Claude-desktopapp ziet een omgeleide `%APPDATA%` en `HKCU`.** Wie de echte
  map of het echte register wil lezen, doet dat buiten die omleiding (`Win32_Process.Create`).
- **De controles mogen geen netwerk en geen echte gegevens gebruiken**: een lokale proefsite op
  127.0.0.1 (`Proefsite.cs`), een nagebootste extensie, en `VINDIOO_DATA` naar `%TEMP%`.
- **Het versienummer staat op één plaats**: `<Version>` in `Vindioo.csproj`. Wie een versie tagt,
  brengt die ook uit, of nummert opnieuw.
- **Commits gaan naar GitHub na elke commit**, met het afgeschermde noreply-adres als afzender.

## Documentatie

De uitleg per onderwerp staat in `docs/`. Lees enkel wat je nodig hebt — deze bestanden worden
**niet** vanzelf ingeladen, en dat is de bedoeling.

| Bestand | Lees dit wanneer je werkt aan |
|---|---|
| `docs/project.md` | de naam, het versienummer, de gegevensmap, publiceren, GitHub, en wat er al werkt |
| `docs/scherm.md` | `MainWindow`: de kop, de tabstrip, de tabs onderaan |
| `docs/zoeken.md` | `SearchRunner`, `SearchScheduler`, `Notifier`, `TrayIcon`, paginering, stoppen |
| `docs/resultaten.md` | de volgorde, de plaats bij een zoekertje, de veilingtijd en de timers |
| `docs/prijs.md` | `PriceIndicator`, `Pricewatch`, `LotPriceWindow` |
| `docs/favorieten.md` | `FavoriteWatch`: staat een favoriet er nog, en wat kost het nu |
| `docs/ai-foto.md` | `PhotoAnalyzer`, `PhotoInsightWindow`, Ollama |
| `docs/zoekertje.md` | `ListingDetailWindow`, `DetailFetcher`, de foto's van een advertentie |
| `docs/brug.md` | `BridgeServer`, `ChromeLauncher`, `extension/`, Cloudflare, toestemmingsmuren |
| `docs/sites.md` | `SiteDefinition`, de motoren, `SearchUrlBuilder`, selectors, eigen filters |
| `docs/sites-toevoegen.md` | `SiteAnalyzer` en `AddSiteWindow`: hoe de AI-analyse werkt |
| `docs/prestaties.md` | snelheid, `BrowserPool`, `VirtualizingWrapPanel`, `SmoothScroll` |
| `docs/fouten-opsporen.md` | het logboek, de controles, de hulpmiddelen in `tools/` |
| `docs/weergave.md` | `App.xaml`, kleuren, en de UI-conventies van WPF |
| `docs/volgende-stappen.md` | wat er nog open staat |

**Hoe dit bestand klein blijft.** Op 30 september 2026 was CLAUDE.md 281 kB: 45 500 woorden, zo'n
70 000 tot 90 000 tokens die bij **elke** sessie meegaan voor er iets gevraagd is. Het was een
werkdagboek geworden - "nagemeten op 24 september" stond er meer dan honderd keer in - en dat is
waardevolle kennis, maar zelden nodig bij een gewone taak. Daarom:

- **Hier staat enkel wat bij élke taak nodig is**: de werkafspraken, de stack, de mappenstructuur,
  de commando's en de regels die schade voorkomen. De grens is **15 kB**, en `DocsChecks` laat de
  controles falen zodra dat overschreden wordt - een afspraak die niemand nakijkt, verwatert.
- **Nieuwe kennis gaat naar het `docs/`-bestand van dat onderwerp**, niet hierheen. Hier komt
  hoogstens een regel in de tabel bij, en dat enkel bij een nieuw onderwerp.
- **Geen `@docs/...`-imports.** Die worden door Claude Code automatisch mee ingeladen, en dan ben
  je precies waar je begon. Gewone verwijzingen dus.
- **Het verhaal hoort in git, niet in een bestand.** Wat de code doet staat in de code en de
  commentaren; hoe we het gevonden hebben staat in de commit.

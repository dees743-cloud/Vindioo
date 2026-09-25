# Zentrix

Een Windows-desktopapp die meerdere tweedehands- en veilingsites tegelijk
doorzoekt. Doel: het dagelijkse zoekwerk dat anders een voormiddag kost,
terugbrengen tot één minuut. De app is bedoeld om ooit door anderen gebruikt
te worden, dus alles moet per gebruiker apart werken en er verzorgd uitzien.

## Werkafspraken

- Commentaar in het Nederlands. Namen van klassen, methodes en variabelen in
  het Engels.
- UI-teksten (labels, knoppen, meldingen) in het Nederlands — die zijn voor de
  eindgebruiker.
- Eén stap tegelijk, en wachten op bevestiging voor de volgende. Niet vijf
  dingen tegelijk aanpassen.
- Bij elke wijziging een concrete vindaanwijzing geven: een zoekterm voor
  Ctrl+F of een regelnummer. Grote bestanden zijn anders lastig te navigeren.
- Uitleggen wat een stuk code doet en waarom, niet alleen wat er moet
  veranderen. De eigenaar heeft basiskennis programmeren en wil bijleren.
- Geen commando's of instellingen voorstellen die niet bestaan.

## Stack

- C# / .NET 10 (`net10.0-windows`), met `Nullable` en `ImplicitUsings`
  ingeschakeld
- WPF, namespace `Zentrix` (tot september 2026 heette dat `zoekhulp`, zie
  "De naam, de gegevensmap en GitHub")
- WPF-UI voor de Fluent-look (`FluentWindow`, donker thema — ingesteld in
  `App.xaml`; Mica staat uit, zie Designsysteem). Gebruik de `ui:`-varianten (`ui:Button`,
  `ui:TextBox`, `ui:ListView` …) in plaats van de standaard
  WPF-besturingselementen.
- AngleSharp voor HTML-parsing met CSS-selectors
- Microsoft.Playwright voor sites die een echte browser nodig hebben
- Microsoft.Data.Sqlite voor bewaarde zoekopdrachten en "al gezien"
- MailKit voor het versturen van e-mail — **niet** `System.Net.Mail.SmtpClient`,
  zie hieronder bij Automatisch zoeken
- CommunityToolkit.Mvvm
- Claude API via gewone `HttpClient` (`https://api.anthropic.com/v1/messages`);
  sleutel uit de omgevingsvariabele `ANTHROPIC_API_KEY`, staat nooit in de code.
  Het NuGet-pakket `Anthropic` stond een tijd in het project zonder gebruikt te
  worden; het is eruit gehaald.
- Ollama draait lokaal op de pc (RTX 4060) — nog niet gebruikt in de app

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
  SavedSearch.cs     een zoekopdracht: zoekterm, verfijning, sites, schema, melding
  SearchSchedule.cs  wanneer een zoekopdracht vanzelf draait
  CustomFilter.cs    een filter dat maar op één site bestaat, uit het sitebestand
  LinkTextOptions.cs de instellingen van de linkmotor, uit het sitebestand
  ListingSort.cs     de volgorde van de resultaten, met een eigen vergelijking
  PriceIndication.cs wat een prijsindicatie opleverde: marktwaarde, varianten, verbreding
  EndTimeApiOptions.cs  een API met het exacte sluitingstijdstip van veel veilingen tegelijk
  RecentSearch.cs    een eerder gebruikte zoekterm, met wanneer
Sources/
  ISearchSource.cs      contract waar elke bron aan voldoet
  SourceFactory.cs      kiest per site de motor (Generic of LinkText)
  SearchUrlBuilder.cs   zoek-URL met {query} en de filters uit de site
  GenericSources.cs     voert een SiteDefinition uit (HTML of JSON)
  LinkTextSource.cs     de linkmotor: volgt links in plaats van selectors, zie hieronder
Services/
  SiteStore.cs       sitesmap: één JSON-bestand per site, met migratie/import/export
  HistoryStore.cs    SQLite: zoekopdrachten, "al gezien", favorieten en recent
  SiteAnalyzer.cs    laat Claude de selectors van een onbekende site bepalen, en telt
                     ze na met de echte motor
  BrowserFetcher.cs  Playwright met een eigen Chrome-profielmap
  BrowserPool.cs     één Chrome voor alle sites van dezelfde zoekopdracht
  BridgeServer.cs    lokale server waarmee de browserextensie praat
  ChromeLauncher.cs  vindt en start Chrome wanneer de brug hem nodig heeft, en zegt
                     waarom de brug niet werkt (BridgeStatus)
  SearchRunner.cs    voert élke zoekopdracht uit, met of zonder scherm — de enige
                     zoeklus — en bepaalt welke sites tegelijk mogen
  SearchScheduler.cs kijkt elke halve minuut wie aan de beurt is
  Notifier.cs        melding via het systeemvak, Telegram of e-mail
  TrayIcon.cs        het pictogram naast de klok (het enige stuk WinForms)
  Autostart.cs       mee opstarten met Windows, via HKCU
  AppPaths.cs        waar de gegevens staan, en de verhuis uit de oude map
  AppSettings.cs     instellingen van de app zelf (venster, weergave, meldingen)
  Log.cs             logboek in een tekstbestand
  FriendlyError.cs   zet een fout om in een zin die de gebruiker iets zegt
  PriceIndicator.cs  wat is een toestel ongeveer waard: zoeken, opschonen, rekenen
  PhotoAnalyzer.cs   laat een AI op deze pc naar een foto kijken en erover vertellen
  DetailFetcher.cs   haalt van de pagina van een zoekertje wat niet op de zoekpagina staat
  DisplayDiagnostics.cs  zet in het logboek hoe er getekend wordt en wat Windows aan het
                     scherm verandert, voor een wit venster
Converters/
  Converters.cs      zichtbaarheid van NIEUW-label en tellers
Controls/
  PhotoThumbnail.xaml  miniatuur met de grote foto ernaast, gedeeld door beide weergaven
  VirtualizingWrapPanel.cs  raster dat enkel opbouwt wat in beeld staat
  SmoothScroll.cs      vloeiend schuiven met het muiswiel, voor allebei de weergaven
  ScrollingText.cs     een regel die te lang is voor haar vak: vervaagt, en schuift als je
                       er met de muis op gaat staan
  CountdownBadge.cs    de timer rechtsonder op een veilingkaart, met één gedeelde klok
  CustomFilterControls.cs  de invoer voor sitegebonden filters, gedeeld door
                       het zoekscherm en het instellingenvenster
Vensters (root):
  MainWindow           zoeken, filters, bewaarde zoekopdrachten
  AddSiteWindow        nieuwe site toevoegen met AI-analyse en testen
  SettingsWindow       tabs met een bewerkbare kaart per site
  SearchSettingsWindow alles van één zoekopdracht: woorden, sites, filters, schema
  NotifySettingsWindow waar meldingen heen gaan en hoe de app op de achtergrond doet
  PriceIndicationWindow de prijsindicatie van één zoekertje (rechtsklik op de foto)
  PhotoInsightWindow   wat de AI op de foto van een zoekertje ziet (rechtsklik)
  ListingDetailWindow  alles van één zoekertje: de foto's, de verkoper, hoelang online (dubbelklik)
extension/
  background.js      de brug: haalt pagina's op in je eigen Chrome
  manifest.json      naam en versie zoals Chrome ze toont
  popup.html/.js     het venstertje waarin je de brug-code plakt
tools/
  verifieer-sites.py  controleert de sitebestanden buiten de app om (zie Fouten opsporen)
  meet-filter.py      meet of een filterparameter op een site echt iets doet
  vensterfoto.py      fotografeert een venster zonder het naar voren te halen
  klik.py             klikt op een plek binnen een venster, gemeten bij het klikken
  typ.py              typt tekst, ongeacht de toetsenbordindeling
  scroll.py           scrolt in een venster
  cursortest.py       zegt welke muisaanwijzer Windows op een plek toont
tests/
  Zentrix.Checks/     controles zonder testframework (zie "Controles" bij Fouten opsporen)
```

De databank `zentrix.db` houdt de bewaarde zoekopdrachten, wat je al gezien hebt,
je favorieten, je recente zoektermen en **wat de laatste beurt van elke zoekopdracht
opleverde** (`outcome`). Een oudere databank heeft nog een tabel `hidden`, van het
wegklikken dat er niet meer is (zie "Wat je te zien krijgt"); die wordt niet meer gelezen.

Gebruikersgegevens staan in `%APPDATA%\Zentrix`: de map `sites\` (één
JSON-bestand per site; het oude gedeelde `sites.json` is bij de eerste start
opgesplitst en als `sites.json.bak` bewaard), `zentrix.db`, `brug-code.txt`,
`instellingen.json` en de map `browser-profiel`.

Elke site staat in zijn eigen bestand (`sites\<id>.json`) zodat je een site
los kan delen, back-uppen of importeren. Toevoegen, bewerken, testen,
exporteren en importeren gebeurt in het instellingen-scherm.

## De naam, de gegevensmap en GitHub

Tot september 2026 heette de app in de code **zoekhulp**; in de interface was ze al
Zentrix. Nu heet alles zo: de namespace `Zentrix`, het project `Zentrix.csproj`, de exe
`Zentrix.exe`, de gegevensmap `%APPDATA%\Zentrix` met `zentrix.db` en
`zentrix-log.txt`, en de extensie "Zentrix Brug". Alleen de projectmap
`source\repos\zoekhulp` heet nog zo; die hernoem je met Visual Studio dicht, of bij het
aanmaken van de repository op GitHub.

**De gegevensmap verhuist vanzelf.** `AppPaths` is de enige plaats die weet waar de
gegevens staan; vroeger schreven zeven klassen `%APPDATA%\Zoekhulp` elk zelf uit. Bij de
eerste start na het hernoemen wordt de oude map hernoemd naar de nieuwe, en daarin
`zoekhulp.db` en `zoekhulp-log.txt`. Een naamswijziging op dezelfde schijf, geen kopie:
ook de 540 MB van het browserprofiel gaan mee zonder wachten. Lukt het niet - een bestand
in gebruik, bijvoorbeeld door een achtergebleven Chrome van Playwright - dan blijft de app
de oude map gebruiken en probeert ze het bij de volgende start opnieuw. Wat er gebeurde,
staat in het logboek.

Twee valkuilen die daarbij horen:

- `AppPaths` mag het logboek **niet** gebruiken. Het logboek vraagt zijn eigen pad aan
  `AppPaths`; een verhuis die zelf wil loggen, wacht dan op zichzelf. Daarom zet ze wat
  er gebeurde in `MigrationNote`, en `App` schrijft dat weg.
- Opstarten met Windows bewaart het **volledige pad** van de exe. Na het hernoemen wees
  dat naar `zoekhulp.exe`, die niet meer bestaat, en dan start Windows stilletjes niets.
  `Autostart.RefreshPath` zet het bij elke start gelijk.

**Wat niet op GitHub hoort** staat in `.gitignore`: `bin`, `obj`, `.vs`, de
`.user`-bestanden en `.claude` (de adviseurs, met persoonlijke paden). Sinds 22 september 2026
ook wat andere hulpmiddelen achterlieten: `.codex` (een kopie van die adviseurs), `AGENTS.md` (een
kopie van dit bestand) en `chatgpt_tips.md`. De sites staan sowieso niet in het project maar in de gegevensmap.

**De app komt zonder sites.** Die staan in een aparte repository, `zentrix-sites`: een
algemene zoekmotor publiek delen is iets anders dan kant-en-klare bestanden die op
bepaalde sites gericht zijn, en een deel daarvan omzeilt bewust de beveiliging tegen
robots. Wie de app voor het eerst start, ziet daarom geen lege lijst met "typ hierboven
wat je zoekt", maar **"Nog geen sites"** met twee knoppen: *Sites importeren uit map* en
*Site toevoegen* (`NoSitesPanel`, getoond vanuit `UpdateEmptyHints`). Importeren staat ook in
het tandwielmenu.

`SiteStore.ImportFolder` leest alle sitebestanden uit een map, elk met dezelfde
ontdubbeling als een losse import. Heeft de gekozen map een submap `sites`, dan leest hij
die: zo mag je de map van de repository zelf kiezen, of je eigen gegevensmap, waar naast
de sites ook `instellingen.json` staat. Nagemeten in een lege gegevensmap: 0 naar 10
sites, met elke sleutel, motor en eigen filter intact, en een tweede import gaf geen
dubbels.

**Een andere gegevensmap** kan met de omgevingsvariabele `ZENTRIX_DATA`. Zo is een lege
eerste start na te bootsen zonder aan je eigen sites en zoekopdrachten te komen:

```bash
set ZENTRIX_DATA=C:\ergens\leeg
```

Er wordt dan niets verhuisd; `AppPaths` gebruikt die map gewoon.

**De brug-extensie staat in `extension\`.** Chrome laadt een uitgepakte extensie vanaf een
vaste map; sinds 15 september 2026 is dat deze. Wie nog een oude kopie heeft (bv.
`C:\zoekhulp-brug`): `chrome://extensions`, de oude extensie verwijderen, "Uitgepakte
extensie laden", deze map kiezen en de koppelcode opnieuw plakken - een andere map is voor
Chrome een andere extensie, met een lege opslag.

## Starten zonder Visual Studio

Een versie die je gewoon dubbelklikt, staat in **`C:\Users\davyb\Zentrix\Zentrix.exe`**
(18 september 2026). Ze is **zelfstandig**: .NET zit erin, dus ze start ook op een pc zonder
Visual Studio of .NET. Opnieuw maken na een wijziging, met Zentrix dicht (anders zijn de
bestanden in gebruik):

```bash
dotnet publish Zentrix.csproj -c Release -r win-x64 --self-contained true -o C:\Users\davyb\Zentrix
```

Wat daarbij hoort:

- **Snelkoppelingen** "Zentrix" staan op het bureaublad (`G:\ONEDRIVE\Bureaublad`) en in het
  startmenu. Die laatste staat onder `%APPDATA%`, dus vanuit de Claude-app aangemaakt via
  `Win32_Process.Create` - anders belandt hij in de omgeleide kopie en verschijnt hij nooit.
  Verhuist de map, dan wijzen ze nergens meer naar.
- **De hele map hoort bij elkaar**, niet enkel de exe: ruim zeshonderd bestanden, samen zo'n
  290 MB. Het grootste deel is .NET zelf en het stuk van Playwright dat Chrome aanstuurt
  (`.playwright\node`). Daarom geen "enkel bestand": Playwright zoekt die map naast de exe.
  Browsers hoeven er niet bij, want Playwright gebruikt de gewone Chrome (`Channel = "chrome"`).
- **Dezelfde gegevens als vanuit Visual Studio**: `%APPDATA%\Zentrix`, met dezelfde sites,
  favorieten en zoekopdrachten. En er draait er maar één tegelijk: sluit de ene voor je de
  andere start.
- **Opstarten met Windows volgt de exe die het laatst draaide.** `Autostart.RefreshPath` zet het
  pad bij elke start gelijk. Start je vanuit Visual Studio, dan wijst het naar
  `bin\Debug\...\Zentrix.exe`; start je daarna de gepubliceerde, dan naar die. Wie wil dat
  Windows de gepubliceerde start, start die dus één keer na het werken in Visual Studio.
- **Proefstarten vanuit de Claude-app** gebeurt met `ZENTRIX_DATA` naar een lege map (zie "Fouten
  opsporen" over de omgeleide gegevensmap). Het register hoeft daarbij niet bewaakt te worden:
  ook dat is vanuit de Claude-app omgeleid, dus de proef kan het echte "opstarten met Windows"
  niet wijzigen. Zo nagemeten: proces draaiend na 7 s, "hoofdscherm opgebouwd" in het logboek,
  databank aangemaakt, brug op 8731.

## De indeling van het hoofdscherm

Het scherm is opgebouwd zoals een app op een telefoon, met vier lagen:

1. **Blauwe kop** met de zoekbalk links en daarnaast **één** tandwiel. Daarachter
   zit alles: bovenaan *Instellingen van deze zoekopdracht* (welke sites meezoeken,
   met welke filters, en wanneer hij vanzelf draait), daaronder *Sites beheren*,
   *Site toevoegen*, *Sites importeren uit map*, *Koppelcode*, *Meldingen en
   achtergrond*, *Logboek openen* en *Weergave*. Elk begrip heeft één
   naam en elk pictogram staat er één keer: "Cards" (Engels), "Sitemap" (in webtaal een
   XML-bestand) en "Brug-code" (de extensie zegt "koppelcode") zetten de gebruiker op het
   verkeerde been, en hetzelfde oogje stond bij twee menu-items.

   *Sites beheren* opent meteen het venster met een tab per site (`ManageSitesMenu_Click`), op de
   site waarvan de tab openstaat. Tot 18 september 2026 hing daar een submenu met elke site apart,
   dat meegroeide met het aantal sites - een extra stap, want in het venster staan ze al als tabs.

   Er stonden hier een tijd **twee** tandwielen: een naast de zoekbalk voor de
   zoekterm en een rechtsboven voor de app. Ze zagen er identiek uit en stonden in
   dezelfde balk, dus niemand kon zien welke welke was. Nu is er één, en het
   onderscheid zit in het menu zelf — waar het te lezen valt in plaats van te raden.
   De titelbalk van Windows (`ui:TitleBar`) houdt enkel nog de knoppen van Windows.

2. **Zoekbalk**, wit op het blauw, met er vlak naast het tandwiel uit punt 1.
   De instellingen van de zoekterm staan bovenaan in dat menu: ze horen bij het
   woord dat ernaast staat, niet bij de app in het algemeen, en dus staan ze
   bovenaan en niet ergens tussen de rest.
   Bestaat er al een zoekopdracht met precies die zoekterm, dan bewerk je die;
   anders wordt er een nieuwe gemaakt met wat er op dat moment op het scherm
   staat, aangevinkte sites en filters inbegrepen.

   **De geopende bewaarde zoekopdracht hoort bij haar zoekterm** (`_activeSearch`,
   `IsActieveZoekterm`). Wie na het openen van "marantz" een ander woord zoekt, zoekt los
   daarvan. Die controle stond tot september 2026 enkel bij het vergrootglas: met Enter
   kwamen de resultaten van "fiets" in de zoekopdracht "marantz" terecht, als "al gezien",
   met een nieuw tijdstip dat de planner verzette en met de fouten van "fiets" over die van
   "marantz" heen. Nu staat ze vooraan in `RunSearchAsync`, zodat elke weg naar zoeken ze
   doorloopt, en opent het tandwiel de geopende zoekopdracht enkel als de zoekterm nog klopt.
   Hoofdletters en spaties aan de rand tellen niet.

3. **Eén tab per site die meezoekt**, met vooraan het tabblad **Alles** en
   achteraan het chipje **Sites**.
   Dat laatste heeft geen site en dus geen vinkje: het toont alles wat er binnen
   is, van elke bron samen. `SiteTab.Alles()` maakt het aan; `Def` en `Source`
   zijn daar leeg, en `IsAll` zegt het. De zoekinstellingen staan er uit — die
   horen bij één site — maar de volgorde en de weergave blijven bruikbaar.
   Op "Alles" geldt per zoekertje nog steeds de prijsgrens van zíjn eigen site.

   **De strip toont enkel de sites die meezoeken.** Vroeger stond er een tab per
   site mét een vinkje erin, en dan groeide de rij mee met het aantal sites
   terwijl je er doorgaans twee of drie gebruikt: acht sites vulden de volle
   breedte en braken al af naar een tweede rij, terwijl bij het opstarten alles
   uitgevinkt staat en die tabs dus geen van alle iets konden tonen. Nu groeit de
   strip mee met wat je gebruikt, en dat blijft altijd een handvol — hoeveel
   sites er ook bijkomen. Voor drie sites scheelde dat ongeveer 1950 naar 715
   beeldpunten.

   Het aan- en uitvinken zit daardoor in het **chipje achteraan** (`SiteChooser`),
   dat toont hoeveel sites meezoeken ("3 van 8 sites") en een lijst met vinkjes
   opent. Zoekt er niets mee, dan staat er "Sites kiezen" in de accentkleur: dan
   is dat het enige wat er nog te doen valt, en zonder die nadruk kijk je bij het
   opstarten naar één tab "Alles" zonder aanwijzing waar de rest gebleven is.

   Twee dingen die daarbij horen:

   - Het chipje zit **in dezelfde `WrapPanel`** als de tabs, via een
     `CompositeCollection` met een `CollectionContainer` rond de gefilterde
     weergave van de tabs. Zo loopt het gewoon mee achter de laatste tab, ook als
     de rij afbreekt. Het chipje als een extra `SiteTab` in de lijst stoppen lijkt
     eenvoudiger, maar dan krijgt elke plek die over "alle tabs behalve Alles"
     gaat er een uitzondering bij, en dat zijn er een stuk of tien.
   - Welke tab in de strip staat, volgt uit een filter op de weergave
     (`_tabsView`), en die weergave wordt ververst vanuit `PropertyChanged` op de
     `SiteTab` zelf — niet vanuit het vinkje. Anders moet élke plek die
     `IsEnabled` omzet eraan denken, en een bewaarde zoekopdracht die zijn sites
     oplegt (`PasToe`) doet dat ook. Vink je de site uit waarvan de tab openstaat,
     dan valt hij terug op "Alles" in plaats van op een lege pagina.

   Een site mag in zijn bestand een **kortere naam voor de tab** zetten
   (`ShortName`): "Facebook" in plaats van "Facebook Marketplace". In de strip
   staat alles naast elkaar en is breedte het schaarse goed; overal elders — de
   kaarten, de instellingen, de meldingen, de lijst achter het chipje — blijft de
   volledige naam staan, want daar is die de identiteit. Leeg laten betekent:
   gewoon de volledige naam. Het veld is bewerkbaar in het instellingenscherm.

   **Een site die mislukte, krijgt een waarschuwingsteken op zijn tab**, met de melding
   als tooltip (`SiteTab.ErrorText`). De statusregel onderaan noemt enkel nog hoeveel sites
   mislukten. Daar stonden vroeger alle fouten achter elkaar, en omdat die regel in een
   horizontale `StackPanel` stond, liep hij voorbij de vensterrand en viel het einde weg.
   Nu staat hij in een `Grid`, wordt hij afgekapt met een beletselteken en toont zijn
   tooltip de volledige tekst. Een lege lijst zegt ook wat er aan de hand is (`LeegTekst`):
   nog niets gezocht, niets gevonden, alles buiten je filters, of de site mislukte.

   De naam aanklikken opent de tab van die site. Je ziet dan **alleen de
   resultaten van die site** — dat gebeurt met een filter op de
   `ICollectionView` van de resultatenlijst, niet met een tweede lijst, zodat
   er één plaats blijft waar resultaten binnenkomen.
   Daaronder staat een rij met vooraan de **pager** — waar je in de resultaten zit —
   en daarnaast de **zoekinstellingen van díe site**: locatie, prijs, aantal per
   pagina, de sitegebonden filters, de volgorde, de weergaveknop en een knop naar
   zijn kaart.

   De pager stond eerst naast het logo in de kopbalk. Dat is een heel scherm
   verwijderd van waar je aan het kijken bent; vlak links van het locatiespeldje zit
   hij bij de resultaten waar hij over gaat. Elke site heeft dus
   zijn eigen postcode, straal en prijsgrenzen (`SiteTab.Filters`); er is geen
   gedeelde filterbalk meer.
   De tabstrip en de pagina worden getekend als **één doorlopende figuur**: een
   `Path` met de naam `TabFrame`, waarvan `UpdateTabFrame` in de code de vorm
   berekent uit de gemeten plaats van de open tab. Dat is de kern van de zaak:
   waar twee losse vormen tegen elkaar moeten aansluiten krijg je altijd een
   sprongetje of een puntje van een lijnuiteinde, hoe nauwkeurig je de
   coördinaten ook kiest. Binnen één figuur bestaat die naad niet.

   De vorm kent drie gevallen: geen open tab op de onderste rij (gewoon een
   afgerond vlak), de open tab helemaal links (de linkerlijn loopt door tot boven
   de tab en rondt daar af, zonder uitloop) en de open tab ergens in het midden
   (links en rechts een uitloop die naar buiten in de lijn buigt). `UpdateTabFrame`
   hangt aan `LayoutUpdated` en slaat over wanneer de maten niet veranderd zijn —
   zonder die controle zet elke toekenning van `Data` een nieuwe lay-outronde in
   gang en draai je in een lus.

   De open tab tekent zelf geen rand meer; de gesloten tabs hebben wel hun eigen
   randje en zweven tien pixels boven de lijn.

   Wat NIET werkt, en wat we achtereenvolgens geprobeerd hebben:

   - Een `Border` met `CornerRadius` voor de tab en een aparte `Border` voor de
     pagina. Een `Border` kan enkel naar **binnen** afronden; de tab wordt daar
     onderaan smaller van, terwijl de lijn juist naar buiten de paginarand in moet
     buigen.
   - Losse `Path`-vormpjes voor die uitloop, over de paginarand geschoven. Werkt
     op het oog, maar laat bij elke boog een puntje achter (het uiteinde van de
     lijn) en een sprongetje waar de boog de rechte lijn raakt, omdat beide op een
     andere halve pixel vallen.
   - De eerste tab laten inspringen zodat zijn uitloop op een recht stuk lijn
     landde. Lost de naad op maar zet de tab scheef; met één figuur is het niet
     meer nodig.

   Rond de tabs staat een `WrapPanel` en géén `ScrollViewer`: die laatste knipt af
   wat buiten zijn kader valt. Bij een smal venster breken de tabs af naar een
   tweede rij; staat de open tab dan niet op de onderste rij, dan tekent
   `UpdateTabFrame` een gewoon afgerond vlak zonder inkeping.
   Alle sites staan bij het opstarten **uitgevinkt**: elke extra site kost tijd,
   zeker die via de brug. Het veld "Standaard aangevinkt" op een kaart wordt
   daardoor niet gebruikt. De strip bestaat bij het opstarten dus uit "Alles" en
   het chipje.
4. **Balk onderaan** met vier tabbladen: *Zoeken*, *Favorieten*, *Recent* en
   *Zoekopdrachten*. Die wisselen enkel de zichtbaarheid van vier panelen.
   Op *Zoekopdrachten* staat per regel het schema en wanneer hij laatst liep, met
   een knop om hem nu te laten draaien, een vuilbakje en een knop naar zijn instellingen.
   Het vuilbakje vraagt eerst of het mag, net als de Delete-toets op een geselecteerde regel.
   Tot 19 september 2026 stond daarvoor een knop *Verwijderen* bovenaan, die pas werkte na
   het selecteren van een regel. *Nieuwe* en *Huidige vastzetten* staan vlak naast de titel,
   boven de kaarten; ze stonden eerst helemaal rechts, een schermbreedte van de lijst.
   Dubbelklikken toont de resultaten van de laatste beurt, zonder opnieuw te zoeken -
   bij sites via de brug scheelt dat al gauw een halve minuut wachten. De **teller**
   ervoor zegt hoeveel je daarvan nog niet bekeek; erop klikken toont enkel die (zie
   "3d. Nieuw is wat je nog niet bekeek").

   **Een geplande beurt neemt het scherm enkel over als het vrij is**
   (`Scheduler_Started`): je drukte zelf op haar afspeelknop, het scherm toont die
   zoekopdracht al, of er is nog niets gezocht en niets ingevuld. Vroeger nam ze het
   scherm altijd over zodra het venster in beeld stond, en dan waren je zoekterm, je
   vinkjes en je filters weg zonder dat je iets gevraagd had. De resultaten gaan niet
   verloren: de statusregel zegt wat de planner doet, en dubbelklikken toont ze.

   **Annuleren in het venster van een zoekopdracht annuleert** (`SearchSettingsWindow`).
   Wat je wijzigt, staat enkel op het scherm; pas *Bewaren* zet het in de zoekopdracht
   (`Neem(doel)`), en eerst na de controle op het zoekwoord. *Nu uitvoeren* draait op een
   kopie met hetzelfde `Id`. Tot september 2026 schreven *Nu uitvoeren* en een geweigerde
   *Bewaren* al in het echte object, hetzelfde object dat de planner gebruikt en bij zijn
   volgende beurt wegschrijft.

   Beide nagemeten met een testprojectje dat de vensters echt opbouwt (in een lege
   gegevensmap, het hoofdscherm buiten beeld getoond), en met de tegenproef: op de oude
   code faalden de geweigerde *Bewaren*, *Nu uitvoeren* en Enter.

## Automatisch zoeken

De app kan blijven zoeken terwijl er niemand kijkt. Dat is opgebouwd uit vier
stukken die los van elkaar te begrijpen zijn.

**1. Een zoekopdracht is een compleet object.** `SavedSearch` bevat alles wat
nodig is om te zoeken: het zoekwoord, per site zijn eigen filters
(`SiteSetting`), het schema (`SearchSchedule`) en of er een melding hoort te
komen.

Een zoekopdracht heeft **één zoekwoord en geen aparte naam**. Er was een tijd
een naamveld plus twee velden om op woorden na te filteren ("moet bevatten",
"mag niet bevatten"); dat is er bewust weer uit gehaald. Het vroeg drie
beslissingen waar er één nodig is, en twee namen voor hetzelfde ding bijhouden
geeft alleen verwarring. `SavedSearch.Name` is nu afgeleid: het zoekwoord met
een hoofdletter. De kolom `name` in de databank blijft bestaan om op te sorteren. Niets daarvan zit in het scherm. Daardoor kan dezelfde zoekopdracht
draaien of je nu op het vergrootglas klikt of de app in het systeemvak staat.

Dat object gaat als **één blokje JSON** naar de kolom `config` van de tabel
`searches`. Per veld een kolom zou betekenen dat elke nieuwe instelling weer een
migratie vraagt; naam, zoekterm en `lastRun` blijven wel echte kolommen, want
daarop wordt gesorteerd en gezocht. Oude zoekopdrachten — een lijstje sitenamen
plus één gedeelde prijs — worden bij het inlezen omgezet
(`SavedSearch.MigrateLegacySites`).

**2. Zoeken zonder scherm.** `SearchRunner` voert een `SavedSearch` uit en geeft
een `SearchOutcome` terug: alles wat gevonden is, wat daarvan nieuw is, en welke
sites faalden.

**En sinds 23 september 2026 is het de enige zoeklus.** Het hoofdscherm had er een eigen, en dan
moest elke regel twee keer geschreven worden: dat kostte in september 2026 al twee keer werk
("nieuw tot je kijkt", de nieuwe rem op het aantal) en gaf stille verschillen tussen zelf zoeken
en een geplande beurt - de prijsgrens, en filters die je wijzigde en die de planner niet kende.
`MainWindow.RunSearchAsync` zet nu enkel klaar waarmee gezocht wordt en roept de runner aan; wat
overblijft in het scherm is tonen. Zie "Het scherm zoekt niet meer zelf" hieronder.

**Een zoekopdracht zonder `Id` is niet bewaard** (23 september 2026). Zo zoekt het zoekscherm
los: iemand typt een woord en klikt op het vergrootglas, zonder dat daar een zoekopdracht bij
hoort. `SearchRunner` herkent dat aan `Id == 0` en houdt dan twee dingen apart:

- **Er wordt niets in de databank geschreven**, ook niet met `markSeen` aan: er is geen rij om
  in te schrijven. Vroeger kwamen daar rijen met `searchId 0` van.
- **"Nieuw" bestaat niet.** Dat gaat over wat je bij díe zoekopdracht nog niet bekeek, en zonder
  geschiedenis zou alles nieuw zijn - het NIEUW-label op elke kaart, en een melding over de hele
  lading. `IsNew` blijft dus uit en `outcome.New` blijft leeg.

Nagemeten in `PlannerChecks`, met de tegenproef: op de oude code was alles nieuw, stonden er vijf
klaar om te melden, en schreef een losse beurt wél in de databank.

**Elke site meldt zelf dat ze klaar is** (23 september 2026, `SiteKlaar` en de parameter
`siteKlaar`): haar naam, hoeveel ze gaf, hoelang ze deed en haar fout in gewone taal, of null
wanneer het lukte. Daarmee kan het scherm de teller op haar tab, de tijd in de statusregel en het
waarschuwingsteken meteen bijwerken, in plaats van te wachten tot de hele beurt klaar is; tot dan
gaf de runner de fouten pas op het einde mee, in `outcome.SiteErrors`.

Ook een site die helemaal niet gezocht heeft, komt langs: een brugsite die overgeslagen werd
omdat de brug niet werkt, en een aangevinkte site die niet meer bestaat. Zonder dat zou het scherm
die stil laten vallen, en daar gaan de vangnetten van 3c juist over.

Nagemeten in `PlannerChecks` met drie sites tegelijk: een die lukt (5 zoekertjes, geen fout, een
gemeten tijd), een die meteen mislukt ("is niet bereikbaar"), en een die niet meer in Sites beheren
staat - die laatste met dezelfde melding als in de uitkomst.

**De planner levert ook tussentijds, als iemand meekijkt** (23 september 2026, de parameter
`tussentijds`). Vroeger gaf hij pas door wanneer een hele site klaar was: druk je op het
driehoekje van een zoekopdracht terwijl het venster openstaat, dan bleef het scherm leeg tot de
eerste site helemaal binnen was. Nu komt elke pagina meteen door, en bij de brug zelfs terwijl de
pagina nog laadt - hetzelfde gevoel als zelf zoeken. Drie dingen die daarbij horen:

- **Enkel wanneer er echt iemand kijkt.** `Delivered` heeft altijd een luisteraar (het
  hoofdscherm), ook als die de lading weggooit, dus daar valt het niet aan af te lezen. Het
  scherm beslist het in `Scheduler_Started` en de planner vraagt het op met
  `SearchScheduler.WordtGetoond`. Staat de app in het systeemvak, dan blijft het bij één levering
  per site - en vraagt de app de brug ook geen tussentijdse versies, wat in september 111 van de
  159 miljoen gekopieerde tekens scheelde.
- **Wat al binnen was, komt niet twee keer door.** Het samenvoegen zit in één plaats (`Lever`),
  die zowel de tussentijdse als de laatste lading verwerkt en enkel het nieuwe doorgeeft.
- **De NIEUW-vlag staat er meteen op**, want een kaart leest die één keer, bij het tekenen.

Nagemeten in `PlannerChecks` met een proefsite van drie pagina's: met een toeschouwer 3
leveringen (30+30+10), zonder 1, in beide gevallen samen precies de 70 uit de uitkomst en geen
enkel zoekertje twee keer.

### Het scherm zoekt niet meer zelf

**De verhuizing zelf** (23 september 2026, punt 9 van Volgende stappen). `MainWindow.RunSearchAsync`
bouwt nu een `SavedSearch` - de geopende bewaarde, of een tijdelijke zonder `Id` uit de aangevinkte
tabs - en roept daarmee `SearchRunner.RunAsync` aan. Wat overblijft in het scherm is tonen: de
lijst, de tabs, de statusregel, *Recent* en het bewaren van de uitkomst. De teller, het tijdstip,
"al gezien", wat er per site misliep en het wegschrijven van de zoekopdracht doet de runner, precies
zoals bij een geplande beurt.

Wat het scherm daarvoor nodig had, staat in vier parameters van `RunAsync`:

| Parameter | Waarvoor |
|---|---|
| `delivered` + `tussentijds` | elke pagina meteen tonen, in plaats van pas als een site klaar is |
| `siteKlaar` | de tijd in de statusregel en de fout op het waarschuwingsteken van die tab |
| `slotGenomen` | het scherm neemt `Gate` zelf, en houdt het vast tot de resultaten bewaard zijn |
| `logNaam` | "zoeken" in het logboek voor je eigen beurt, "planner" voor een geplande |

Drie dingen die daarbij hoorden:

- **De filters van het scherm gaan vooraf in de zoekopdracht** (`NeemSchermfiltersOver`), niet
  achteraf. Anders zou de runner met de oude prijsgrens tellen en bewaren, en de statusregel met
  de nieuwe.
- **Het scherm houdt zijn drie controles vooraf**: geen site aangevinkt, en een lege zoekterm die
  nergens kan. Die zeggen wat je eraan doet ("Kies eerst welke sites meezoeken"), en een
  zoekopdracht die bij de runner niet kan draaien krijgt wél een tijdstip - dat verzet de volgende
  geplande beurt terwijl er met de zoekopdracht zelf niets mis is.
- **`slotGenomen` moet ook het vrijgeven overslaan**, niet enkel het nemen. Het scherm gaf het
  daarna nog eens vrij, en een semafoor van één gooit dan `SemaphoreFullException` - de hele
  zoekopdracht viel om. Gevonden door de controle die erbij hoort, niet door de app te draaien.

**Nagemeten met het hoofdscherm buiten beeld**, op een lokale proefsite met prijzen van € 1 tot
€ 20: zoeken zonder bewaarde zoekopdracht (20 gevonden, niets nieuw, niets in de databank), met een
bewaarde zoekopdracht en een grens van € 10 (20 in het geheugen, 10 in beeld, teller 10, bewaard 10,
gezien 10), een prijsgrens die je wijzigt en die in de zoekopdracht belandt, een site die niet
bereikbaar is (waarschuwingsteken op háár tab, "1 site mislukte" in de statusregel), en een
aangevinkte site die niet meer bestaat. Met de tegenproef op de vorige versie: die zweeg over de
verdwenen site en liep niet door de runner.

**Wat er intussen is rechtgezet**, van de vier kleine verschillen die de inventaris van 22 september
overhield: een verdwenen site komt nu ook op het scherm (in de statusregel, want ze heeft geen tab),
"al gezien" wordt pas op het einde geschreven - dus een site die eerst twintig zoekertjes gaf en
daarna faalde, laat die niet meer als gezien achter - en `LastViewed` wordt op het einde opnieuw
gelezen. Het vijfde, annuleren, staat hieronder.

### Een zoekopdracht stoppen

**Het vergrootglas wordt een stopknop zodra er gezocht wordt** (24 september 2026). Dat kon tot dan
niet: je drukte op zoeken, en dan wachtte je het uit - bij een site die de verbinding aanneemt en
zwijgt, dertig seconden lang, en met "Cd speler" over acht sites al gauw een minuut. Het laatste
stuk van punt 9: de runner kende zijn `CancellationToken` al, het scherm gaf er geen mee.

- **Eén knop op één plaats.** Dezelfde knop naast de zoekbalk, met een ander pictogram
  (`Stop24`, in amber). Daar staat je muis al, en een tweede knop ernaast zou twee
  zichtbaarheden overal gelijk moeten houden. De stand zit in `Tag` van de knop, zodat het
  uiterlijk in het sjabloon blijft en de code enkel "stop" zet of weghaalt.
- **Het wachten op het slot hoort erbij.** Draait er een geplande beurt, dan sta je daarop te
  kijken zonder dat er iets van jou gebeurt; daarom is `Gate.WaitAsync(stop)` ook afbreekbaar.
  Stop je dan, dan is het slot nooit van jou geweest en wordt het dus ook niet vrijgegeven.
- **Een halve beurt is geen beurt.** Na het afbreken komt de runner niet meer aan het
  wegschrijven toe, dus het tijdstip, de teller en "al gezien" blijven die van de vorige beurt.
  Wat al binnen was, blijft wél op het scherm staan: die sites waren klaar.
- **Er loopt er maar één tegelijk.** `RunSearchAsync` stopt meteen wanneer er al een beurt van het
  scherm loopt. Enter, een filterpopup die sluit en het openen van een zoekopdracht komen daar
  allemaal binnen, en tot nu startten die een tweede beurt die op het slot van de eerste bleef
  wachten - dezelfde sites nog eens af, zonder dat je erom vroeg. Nu is dat ook nodig: er is één
  stopknop en één `CancellationTokenSource`.

**De valkuil zit in het slot**, en het is dezelfde als bij `slotGenomen` zelf: geeft de runner het
bij het afbreken tóch vrij, dan geeft het scherm het daarna een tweede keer vrij en gooit een
semafoor van één `SemaphoreFullException` - de hele zoekopdracht valt om. `PlannerChecks` kijkt dat
apart na.

Nagemeten met het hoofdscherm buiten beeld, op twee proefsites (een die meteen antwoordt en een die
de verbinding aanneemt en zwijgt): 20 zoekertjes binnen, dan op de knop, en **0,2 s later gestopt**
met "Gestopt. 20 resultaten van de sites die wel klaar waren." De tegenproef op de vorige versie:
daar liep dezelfde beurt na 15,6 s nog altijd, op weg naar de time-out van 30 s.

Wat ze wél delen is `SearchRunner.Gate`, een semafoor van één. Twee
zoekopdrachten tegelijk gaat niet: de brug heeft één wachtrij, en twee
Playwright-sessies op hetzelfde browserprofiel botsen. Draait er iets op de
achtergrond terwijl je zelf zoekt, dan zegt de statusregel dat je even wacht.

Welke sites tegelijk mogen, bepaalt `SearchRunner.RunInLanesAsync`, ook voor het
hoofdscherm: drie **rijstroken** die tegelijk lopen. De rechtstreekse sites gaan allemaal
samen, de browsersites na elkaar (één Chrome-profiel; de linkmotor telt altijd als
browsersite) en de brugsites na elkaar (één wachtrij). Tot september 2026 wachtten een
browsersite en een brugsite op elkaar terwijl ze niets delen, en de planner deed zelfs de
rechtstreekse sites na elkaar. Volgens de tijden in het logboek gaat "marantz" over acht
sites zo van 53,7 naar ongeveer 27 seconden; dat is nog live na te meten.

**3. De planner.** `SearchScheduler` tikt elke 30 seconden en pakt telkens
hooguit één zoekopdracht op die aan de beurt is. Eén per tik, met opzet: zo
blijft er tussen twee zoekopdrachten ruimte voor iets anders.

Een schema is `Off`, `Interval` (om de zoveel minuten) of `Daily` (elke dag op
een uur). Daar bovenop kan een tijdvenster ("alleen tussen 8 en 22 uur", ook over
middernacht heen) en "ook uitvoeren bij het opstarten". Het venster is met opzet
een aparte vraag van "is hij aan de beurt", zodat het verschil tussen *nog niet*
en *buiten de uren* zichtbaar blijft.

`lastRun` wordt pas op het **einde** van een beurt gezet (`SetLastRun`), niet
tijdens. Zette `MarkSeen` dat mee — zoals vroeger — dan schuift de planner zijn
eigen volgende beurt telkens vooruit terwijl hij bezig is.

**Elke beurt krijgt een tijdstip, ook een beurt die niet kon draaien.** De planner neemt
de eerste zoekopdracht die aan de beurt is. Bleef er een aan de beurt, dan nam hij elke
halve minuut weer díe, en kwamen de zoekopdrachten erna in de lijst nooit meer aan bod.
Dat gebeurde op drie manieren, die alle drie dicht zijn sinds september 2026:

- **Geen site aangevinkt**, of geen zoekterm en geen site die zonder kan (bijvoorbeeld
  een site die intussen hernoemd werd): `SearchRunner` stopte vóór `SetLastRun`. Nu
  krijgt zo'n zoekopdracht toch haar tijdstip, met de reden in
  `SearchOutcome.NotRunReason`, in de statusregel en in het logboek. Haar vorige
  resultaten blijven staan: een lege lijst is dan geen uitkomst.
- **Een time-out van één site.** Die van `HttpClient` (30 s) is ook een
  `OperationCanceledException`, en die werd doorgegooid als annulering: de hele beurt
  viel om, met de resultaten van de sites die wél lukten. Nu gooit enkel een echte
  annulering door (`catch (OperationCanceledException) when (ct.IsCancellationRequested)`),
  en is een time-out een fout van die ene site: "gaf geen antwoord binnen de tijd".
- **Iets onverwachts.** Loopt een beurt toch vast, dan noteert `SearchScheduler` het
  tijdstip zelf, in zijn `catch`.

**"Nu aan de beurt" is het moment waarop de planner kijkt.** `NextRun(lastRun, nu)` krijgt
dat moment mee. Tot september 2026 gaf het in dat geval een nieuwe `DateTime.Now` terug,
een fractie later dan het moment waarmee de planner vergeleek, en "later dan nu" is nooit
aan de beurt. Gevolg: **een dagelijkse zoekopdracht startte nooit vanzelf**, en een
zoekopdracht met interval pas nadat ze één keer met de hand gedraaid had. In de databank
stond de dagelijkse "Cd speler" nog op 13 september, drie dagen later. Een gemiste beurt
wordt nu dezelfde dag ingehaald: start de app om 10u, dan draait "dagelijks om 8u" meteen.

Nagemeten met een testprojectje dat de tik van de planner zelf oproept, met twee lokale
proefsites (een die antwoordt en een die de verbinding aanneemt en zwijgt): de
onuitvoerbare zoekopdracht wordt genoteerd en de volgende draait bij de volgende tik, de
time-out laat de resultaten van de andere site staan, en een echte annulering gooit nog
door. Voor de NextRun-fout faalden de dagelijkse en de nooit gedraaide zoekopdracht, erna
niet meer.

**3b. Wat een beurt opleverde blijft bewaard.** Een geplande zoekopdracht draait
zonder scherm. Stonden haar resultaten enkel in het geheugen — zoals eerst — dan
kreeg je een mail over driehonderd zoekertjes die de app zelf niet meer kon tonen
zodra ze herstart was, of nadat er een kwartier voorbij was. De melding en het
scherm hoorden hetzelfde te weten.

Daarom gaan ze naar de tabel `outcome` in de databank (`SaveOutcome` / `GetOutcome`),
als kopie — net als bij de favorieten, want de site kan een zoekertje intussen
weggehaald hebben. Dubbelklikken op een zoekopdracht toont die lijst meteen, met
erbij van wanneer ze is en hoeveel er nieuw was. Er wordt niet meer op de klok
gekeken: een lijst van een half uur oud is nog altijd beter dan niets, en wie verse
resultaten wil klikt op het vergrootglas.

**Het bewaren loopt op een achtergronddraad** (22 september 2026, een tip uit een beoordeling
door ChatGPT; `MainWindow.BewaarUitkomstAsync`). Sinds de rem op 2000 per site levert "Cd
speler" zo'n 4000 zoekertjes, en die bewaren kostte op de schermdraad 50 tot 79 ms: een
hapering precies wanneer de resultaten klaar waren. Gemeten op een kopie van de echte databank.
Twee dingen:

- `SaveOutcome` gebruikt nu één commando voor alle rijen, zoals `MarkSeen`: 32 ms in plaats van 50.
- Het hoofdscherm geeft het slot van de zoekopdrachten pas vrij als het bewaren klaar is, zodat
  de volgende zoekopdracht nooit tegelijk schrijft. De planner wacht niet. Schrijft er intussen
  toch iets, dan wacht SQLite tot het vorige klaar is in plaats van "database is locked" te geven
  (nagemeten in `NieuwChecks`).

De rest op het einde van een zoekopdracht (`MarkSeen`, `SetLastRun`, `Update`) kost samen zo'n
20 ms, en blijft op de schermdraad.

**3c. Wat er misliep, blijft ook bewaard.** Een geplande zoekopdracht faalde vroeger
zonder dat iemand het zag: de fouten stonden enkel in het logboek. Een verlopen aanmelding
bij Facebook leek zo wekenlang op "niets nieuws te koop". Nu houdt `SavedSearch` per site
bij wat er bij de laatste beurt misliep (`LastErrors`) en hoeveel beurten op rij
(`FailureStreaks`), allebei in het JSON-blokje `config`. De lijst met zoekopdrachten toont
"· 1 site mislukt" met een waarschuwingsteken, en de melding als tooltip.

Mislukt een site **twee beurten op rij**, dan komt er één melding via dezelfde kanalen
(`Notifier.NotifyProblemAsync`), en daarna niet opnieuw tot de site weer lukt. Eén
mislukte beurt kan een haperende verbinding zijn; twee op rij wijst op iets dat de
gebruiker moet oplossen. De regel zit in `SavedSearch.RecordRun`. Staat melden aan zonder
dat er een kanaal kan versturen, dan waarschuwt het venster van de zoekopdracht daarvoor.

**Een site die stukgaat, geeft meestal geen fout maar een lege lijst.** De opmaak veranderde,
de selector vindt niets, en de beurt "lukt". Daarom drie vangnetten, sinds september 2026:

- **Nul na veel.** Elke zoekopdracht onthoudt per site hoeveel zoekertjes die bij de laatste
  beurt zonder fout gaf (`SavedSearch.LastCounts`, in het JSON-blokje). Vindt een site nu
  niets terwijl het vorige keer tien of meer waren, dan is dat een fout van die site
  (`VerdachtLeeg`): op de tab, in de lijst, en na twee beurten een melding. Een zoekterm kan
  ook echt uitverkocht raken, dus na drie verdachte beurten op rij geldt nul als het nieuwe
  normaal. Werkt ook bij zelf zoeken, zolang er een bewaarde zoekopdracht openstaat.
- **De zoekmotor herkent een pagina die geen resultatenpagina is.** Geen enkel zoekertje op
  pagina 1, en de pagina is een controlepagina (`SiteAnalyzer.LooksBlocked`) of kleiner dan
  20 000 tekens: dan is het een fout, geen "niets gevonden". Een echte resultatenpagina, ook
  zonder resultaten, is groot. Bij JSON: ontbreekt al de eerste stap van het pad naar de
  lijst, dan is het een ander soort antwoord - de fout van de site komt mee in de melding
  (Discogs: een verlopen opgeslagen zoekopdracht). Ontbreekt pas een dieper stuk, dan kan het
  een API zijn die een lege lijst weglaat, en blijft het "niets gevonden".
- **Een aangevinkte site die niet meer bestaat** (verwijderd, of een bestand met een andere
  naam) is een fout van die site. Vroeger viel ze stil weg.

**Een melding zegt of ze vertrok.** `Notifier` telt welke kanalen lukten. Lukte er geen, dan
staat er in het logboek "melding NIET verstuurd" en zegt de statusregel het; vroeger stond er
altijd "melding verstuurd", ook met een ingetrokken Telegram-token. Een ballon zonder pictogram
in het systeemvak telt als mislukt.

**Foutmeldingen in gewone taal** (`FriendlyError.Describe`): een 403 wordt "weigert de app",
een time-out "gaf geen antwoord binnen de tijd", een JSON-fout "gaf iets anders terug dan
resultaten". Dat komt op de tab, in de lijst, in de melding op je telefoon en bij Testen. Wat
al Nederlands is, blijft staan; het Engelse origineel staat in het logboek.

**3d. Nieuw is wat je nog niet bekeek** (19 september 2026). Tot dan betekende "nieuw" *nieuw
bij de laatste beurt*, en dan wiste elke volgende beurt de vorige nieuwe. Het logboek van die dag
toont het zo: om 12:07:19 vond de planner 48 nieuwe bij "Cd speler" (de beurt van 8u, ingehaald bij
het opstarten), om 12:07:45 drukte de eigenaar op het driehoekje, en die beurt vond er 0 - de 48
waren "gezien" zonder dat iemand ze zag, en de bewaarde lijst was overschreven. Bovendien was er
geen manier om enkel de nieuwe te tonen.

Nu blijft een zoekertje nieuw **tot je de zoekopdracht opent** (dubbelklik, Enter of de teller), zo
koos de eigenaar het. Het driehoekje, het vergrootglas en de planner laten de nieuwe staan; de
teller telt op over de beurten heen. Hoe dat werkt:

- **Geen nieuwe vlag per zoekertje, maar twee tijdstippen.** De tabel `seen` had al per sleutel
  `firstSeen`. Er kwam één kolom bij in `searches`: `lastViewed`, wanneer je de zoekopdracht laatst
  opende. Nieuw is wat voor het eerst opdook ná dat moment (`SavedSearch.IsUnviewed`). Zo is het
  ook juist voor een zoekertje dat één beurt ontbrak - een site die mislukte - en daarna terugkomt:
  een vlag in de bewaarde lijst was dan verloren gegaan.
- **Een eigen kolom, niet in het JSON-blokje `config`.** Dat blokje schrijft ook een beurt die al
  liep voor je keek (`Update` op het einde), en die zou het tijdstip terugzetten. `SetViewed` schrijft
  enkel `lastViewed` en zet `newCount` op 0.
- **Een beurt leest `lastViewed` op het einde opnieuw** (`SearchRunner.RunAsync`). Opende je de
  zoekopdracht terwijl ze liep, dan zag je de nieuwe van daarvoor al.
- **Twee soorten nieuw.** De melding gaat over `SearchOutcome.New`: wat deze beurt voor het eerst
  zag. Anders kreeg je elk uur een melding over dezelfde zoekertjes. De teller en het NIEUW-label
  gaan over wat je nog niet bekeek (`Listing.IsNew`). De statusregel zegt allebei: "niets nieuws
  sinds de vorige beurt. Nog 48 van eerder niet bekeken; klik op de teller."
- **Een beurt die niet kon draaien**, laat de teller staan. Vroeger zette ze hem op 0.
- **Bij het openen wordt opnieuw bepaald wat nieuw is**, uit `seen` en `lastViewed`, in plaats van
  de vlag uit de bewaarde lijst te geloven. Het NIEUW-label blijft staan zolang die lijst op het
  scherm staat; pas de volgende keer tellen ze als bekeken.

**Tonen.** Een klik op de teller opent de zoekopdracht met de schakelaar **Enkel nieuwe (54)** aan, in
de knoppenrij boven de resultaten, vooraan (`NewOnlyButton`, `_enkelNieuw`). Die filter komt bovenop
die van de tab en de prijs (`HoortInHuidigeTab`); het getal telt de nieuwe op de open tab. De
schakelaar staat er enkel als er iets nieuws is, of zolang hij aan staat - anders kan je hem niet meer
uitzetten. Hij gaat uit bij elke nieuwe zoekopdracht. Dubbelklikken opent met de schakelaar uit: alles,
met de nieuwe bovenaan.

**De overstap** (`HistoryStore.StartpuntBekeken`) gebeurt één keer, wanneer de kolom er net bijkomt.
Zonder startpunt zou alles wat ooit gezien werd als nieuw tellen. Het startpunt is het laatste
`firstSeen` van wat bij de laatste beurt al gekend was; wat daarna opdook, is precies wat die beurt
nieuw vond. Nagemeten op een kopie van de echte databank, met het hoofdscherm buiten beeld: "Computer"
hield **54** van zijn 64 nieuwe. De andere 10 zijn AlleVeilingen-kavels die al op 13 september gezien
waren, maar op 16 september nog met het paginanummer in hun link (`IdPattern` kwam er pas op 17
september), en dus toen ten onrechte nieuw. De 48 van "Cd speler" waren al overschreven en zijn niet
terug te halen. Een klik op de teller toonde de 54, schakelaar aan; uit gaf alle 274 met de nieuwe
bovenaan; de teller stond daarna op 0, ook in de databank; opnieuw openen gaf niets nieuws meer, en
de schakelaar verdween.

**4. Melding.** `Notifier` stuurt naar alle kanalen die aanstaan; een kanaal dat
mislukt houdt de rest niet tegen, want een melding is een extra en geen
voorwaarde om te blijven zoeken.

| Weg | Instellen | Waarvoor |
|---|---|---|
| Ballon in het systeemvak | niets | je zit aan de pc |
| Telegram | @BotFather → `/newbot` → token; daarna één bericht naar je bot en "Chat-id ophalen" | je wil het op je telefoon |
| E-mail | mailserver, poort, gebruiker, wachtwoord | je wil het in je postvak |

Telegram is de aangewezen weg voor onderweg: er gaat geen wachtwoord over de
lijn, alleen een token dat je altijd weer kan intrekken, en het chatnummer haalt
de app zelf op uit het laatste bericht dat je naar de bot stuurde.

Bij Telegram gaat de **foto** mee: per zoekertje één bericht met de foto, de
titel, de prijs en een link, tot hoogstens zes. Op een telefoon is het de foto
die bepaalt of je erop klikt. Telegram haalt die foto zelf op bij de URL die wij
doorgeven, en dat lukt niet altijd — sommige sites weigeren een verzoek zonder
de juiste herkomst. Wat niet lukt, belandt onderaan alsnog in een gewoon
tekstbericht, zodat je nooit een resultaat mist door een weerbarstige foto. Wie
het rustiger wil, zet het vinkje uit en krijgt één bericht met alles erin.

**E-mail gaat via MailKit en niet via `System.Net.Mail.SmtpClient`.** Die laatste
meldt zich bij sommige servers gewoon niet aan. Zo ging het in de praktijk: de server biedt na STARTTLS enkel `AUTH LOGIN` en `PLAIN` aan, en .NET
stuurt dan helemaal geen AUTH. De server antwoordt op MAIL FROM met
`530 5.1.0 must authenticate first`, en dat leest als een fout wachtwoord terwijl
er nooit een wachtwoord verstuurd is. Het verschil tussen de twee is meetbaar:

| Antwoord van de server | Wat er aan de hand is |
|---|---|
| `530 5.1.0 must authenticate first` | de client heeft geen AUTH gestuurd |
| `535 5.7.0 authentication failed` | de client heeft het geprobeerd, maar naam of wachtwoord klopt niet |

Met MailKit komt er wél een AUTH, en krijg je dus een eerlijke foutmelding.
Microsoft raadt `SmtpClient` overigens zelf al jaren af.

Let ook op de **poort**: 465 is versleuteld vanaf de eerste byte
(`SslOnConnect`), 587 begint gewoon en schakelt over met STARTTLS. Door elkaar
halen geeft een verbinding die blijft hangen, dus `SendEmailAsync` leidt het af
uit het poortnummer. En de **gebruikersnaam is bijna altijd het volledige
e-mailadres**, niet de naam van de persoon: `Jan Peeters` werkt dan niet en
`jan.peeters@voorbeeld.be` wel.

Server nakijken zonder de app, en zonder een wachtwoord te gebruiken (vervang
`smtp.voorbeeld.be` door de mailserver die je nakijkt):

```bash
python -c "import smtplib,ssl; s=smtplib.SMTP('smtp.voorbeeld.be',587); s.ehlo(); s.starttls(context=ssl.create_default_context()); s.ehlo(); print(s.esmtp_features.get('auth'))"
```

**Het mailwachtwoord en het Telegram-token staan beschermd door Windows** in `instellingen.json`
(22 september 2026, een tip uit een beoordeling door ChatGPT). `AppSettings` gebruikt daarvoor DPAPI
(`ProtectedData`, voor de huidige gebruiker, met het merkteken `dpapi:`): het bestand is enkel
leesbaar voor jouw Windows-account op deze pc. Een kopie in een back-up, op OneDrive of op een
andere pc is waardeloos; een programma dat onder jouw account draait, kan het wel lezen. Daarvoor
stond het wachtwoord er als base64 in (`b64:`), wat geen bescherming is, en het token gewoon
leesbaar - terwijl wie het token heeft, als jouw bot schrijft en leest wat jij hem stuurt.

- **De omzetting gebeurt bij het inlezen**, en het bestand wordt meteen herschreven.
- **Niet te openen** (een bestand van een andere pc of een ander account): de waarde is leeg, het
  logboek zegt waar je ze opnieuw invult, en de beschermde vorm blijft in het bestand staan tot je
  iets nieuws invult. Een ander vinkje bewaren wist ze dus niet.
- **Een oudere Zentrix** kent `dpapi:` niet, leest de beschermde vorm als het wachtwoord zelf (de
  mail mislukt dan) en pakt ze bij het bewaren in als `b64:`. De nieuwe code pakt dat weer uit.
  Start na de omzetting dus liever geen oude versie meer: publiceer de exe opnieuw.
- **Bewaren wijzigt het object niet meer.** Vroeger werd het wachtwoord even vervangen door de
  versluierde vorm en daarna teruggezet; een mail die op dat moment op een andere draad vertrok,
  meldde zich aan met die vorm. Nu gaat het via een JSON-boom.

Nagemeten in `StilFalenChecks`, met de tegenproef op de oude code. En op het echte bestand: op
23 september 2026 zette de eerste start met de nieuwe exe het om ("instellingen: wachtwoord en
token (wat ingevuld was) staan nu beschermd door Windows"), en de melding 50 seconden later
vertrok via systeemvak én e-mail - dus het wachtwoord kwam er ongeschonden weer uit.
Elk kanaal heeft een testknop: wachten tot er 's nachts iets gevonden wordt om te
ontdekken dat je token niet klopt, is geen manier van werken.

**Hoeveel zoekertjes er in een melding staan:** de ballon 3, Telegram 15 (plus hoogstens zes
met foto), een e-mail 50. Telkens met eronder hoeveel er nog zijn; de e-mail zegt ook waar: de
teller van die zoekopdracht in Zentrix. Een e-mail zette er tot 22 september 2026 alle nieuwe in,
en sinds een site tot 2000 zoekertjes levert, kon dat er 1700 zijn.

**Een bericht op Telegram wordt nooit midden in de HTML afgeknipt** (22 september 2026). Telegram
weigert een bericht met kapotte HTML in zijn geheel ("can't parse entities"), dus een schaar die
in een `<a href="...` of een `&amp;` valt, kost de hele melding. En dat gebeurde: tot dan werd de
HTML op 4000 tekens geknipt, en vijftien lange links van eBay zijn al gauw vijfduizend tekens.
Telegram telt bovendien de **zichtbare** tekst (4096 voor een bericht, 1024 onder een foto): een
link van vijfhonderd tekens telt enkel met zijn titel. Nu:

- `TelegramTekst` neemt hele zoekertjes zolang ze passen, gemeten in zichtbare tekens
  (`ZichtbareLengte`), en zegt eerlijk hoeveel er nog zijn. Vroeger stond er "(afgekapt)" zonder
  te zeggen hoeveel er ontbraken.
- Een titel boven de 200 tekens wordt ingekort, als tekst en vóór ze HTML wordt (`Kort`, dat ook
  geen emoji doormidden knipt).
- Het vangnet (`PastOpTelegram`) laat regels van onderen vallen, en knipt nooit binnen een regel.
  Daarom moet elke regel van een Telegram-bericht op zichzelf geldige HTML zijn.
- Het bijschrift bij een foto kort enkel de titel in.

Nagemeten in `StilFalenChecks` met een controle die nabootst wat Telegram aanvaardt: vijftien links
van duizend tekens gaven 16 807 tekens HTML, en de oude schaar maakte daar een ongeldig bericht van.
Nu gaan ze er alle vijftien in: zichtbaar zijn dat 744 tekens.

Er komt niets centraal samen. Telegram en e-mail gaan rechtstreeks van deze pc
naar de dienst die de gebruiker zelf koos.

### In het systeemvak blijven draaien

Een schema van "elk uur" heeft geen betekenis wanneer de app dicht staat. Daarom
zit er een pictogram naast de klok (`TrayIcon`), met een menu: openen, de eerstvolgende
zoekopdracht nu uitvoeren, afsluiten. Standaard verbergt het kruisje het venster in plaats
van de app af te sluiten; de eerste keer - en alleen de eerste keer
(`AppSettings.CloseToTrayExplained`) - legt een ballon uit waar ze gebleven is.
Minimaliseren gaat standaard gewoon naar de taakbalk (`MinimizeToTray` staat uit): het
kruisje verbergt de app al, en wie minimaliseert, verwacht haar in de taakbalk terug.

**Een start door Windows toont geen venster** (22 september 2026). Opstarten met Windows geeft
`--systeemvak` mee (`Autostart`), en dan maakt `App.OnStartup` het hoofdvenster wel, maar toont
het niet: `MainWindow.StartOpAchtergrond` start enkel het pictogram en de planner. Het venster
wordt pas getoond, en dus pas voor het eerst getekend, wanneer je het opent. Daarom staat er ook
geen `StartupUri` meer in `App.xaml`: die toont het venster altijd. `MainWindow_Loaded` en
`StartOpAchtergrond` lopen samen via `StartAchtergrondAsync`, dat maar één keer iets doet
(`AchtergrondGestart`). Hetzelfde bij de instelling "meteen in het systeemvak" (`StartMinimized`).

Waarom: na het opstarten van de pc bleef het venster soms spierwit, tot de eigenaar Zentrix
herstartte. De app liep gewoon - om 17:05 zocht de planner en stuurde ze een melding - enkel het
tekenen faalde, zonder één fout in het logboek of in dat van Windows, en zonder een hapering van
het stuurprogramma van de grafische kaart. Tot dan werd het venster ook bij een start door Windows
getoond en in `Loaded` meteen weer verborgen, een minuut na het aanmelden, terwijl Windows en de
grafische kaart nog opstartten; het vlak waarop de kaart tekent, werd op dat moment gemaakt. Een
start met `--systeemvak` zonder pc-start gaf het witte venster nooit, dus het moment is de
verdachte en niet de code. Of het hiermee weg is, zegt de volgende pc-start: zie "Een spierwit
venster" bij Fouten opsporen. Nagemeten zonder pc-start: geen venster na een start zoals Windows
die doet, het venster er na een tweede start (en 226 ms later getekend), een gewone start, en het
kruisje met opnieuw openen.

**De eerste echte pc-start erna ging goed** (23 september 2026): pc aan om 16:39, Zentrix om 16:40
in het systeemvak zonder venster, en bij het openen om 16:44:36 stond het beeld er 251 ms later.
Eén meetpunt - het witte venster kwam ook vroeger niet elke keer - maar precies het geval dat
misging. Blijft het bij volgende pc-starts goed, dan is het hiermee weg.

**Er draait maar één Zentrix tegelijk.** `App.OnStartup` neemt een benoemd slot (`Mutex`);
een tweede start vindt dat bezet, geeft het draaiende exemplaar een seintje
(`EventWaitHandle`) en stopt meteen, en dat exemplaar haalt zijn venster naar voren
(`MainWindow.BrengNaarVoren`). Vroeger liep een tweede exemplaar stil vast op de bezette
poort van de brug, en leek de snelkoppeling niets te doen. Gevolg voor wie test: een lege
gegevensmap proberen met `ZENTRIX_DATA` kan enkel terwijl de gewone Zentrix dicht is.

Dit is het **enige stuk WinForms** in de app: WPF heeft geen eigen pictogram voor
het systeemvak en WPF-UI 4.3 levert er ook geen (de naam `NotifyIcon` komt niet
eens voor in `Wpf.Ui.dll`). `UseWindowsForms` zet echter `System.Drawing` en
`System.Windows.Forms` als globale using over het hele project, en dan botst
alles met WPF: `Application`, `UserControl`, `TextBox`, `CheckBox` en
`KeyEventArgs` bestaan in allebei. Vandaar in het csproj:

```xml
<Using Remove="System.Drawing" />
<Using Remove="System.Windows.Forms" />
```

De verwijzing naar de assembly blijft, de globale usings zijn weg, en
`TrayIcon.cs` zet zijn eigen `using` — het enige bestand dat het nodig heeft.
Binnenin staat `System.Drawing` voluit geschreven om diezelfde botsing te
vermijden.

Het menu op het pictogram hoort bij Windows en niet bij de app, dus het volgt de
stand van Windows (lezen uit `SystemUsesLightTheme` in het register) in plaats van
ons eigen designsysteem. Zonder dat staat er een spierwit menu midden op een
donker bureaublad.

Windows 11 verstopt een nieuw pictogram standaard achter het pijltje in het
systeemvak. Wie het vast wil zien staan, sleept het er één keer uit.

**Opstarten met Windows bewaart nooit `dotnet.exe`.** Wordt de app gestart als
`dotnet Zentrix.dll` - zo doet een testprogramma het - dan is het proces dotnet.exe, en schreef
`Autostart.RefreshPath` dat in het register: `"dotnet.exe" --systeemvak`, waarmee Windows niets
start. Op 16 september 2026 gebeurde dat echt, door een testprojectje dat `new App()` deed:
**`App.OnStartup` loopt ook dan**, zodra de dispatcher berichten verwerkt. `Autostart.ExePad`
neemt nu de `Zentrix.exe` naast de dll, en anders niets.

**Wie in een testprojectje `App` aanmaakt, moet `App.OnStartup` overslaan.** `new App()` zet
de opstart klaar, en die loopt zodra de dispatcher de eerste keer berichten verwerkt. Draait
Zentrix intussen gewoon, dan vindt die opstart het slot "Zentrix draait al", haalt ze het
venster van de gebruiker naar voren en stopt ze het testprojectje. Zo sla je ze over: zet
voor het eerste pompen het private statische veld `Application._isShuttingDown` op `true`,
laat de dispatcher één keer pompen, en zet het terug op `false`. (`_startupUri` op null zetten
hoeft sinds 22 september 2026 niet meer: `App.xaml` heeft geen `StartupUri`, `OnStartup` maakt
het hoofdvenster zelf.) Nagemeten op 17 september 2026 met een eigen `Application` en een tegenproef:
met het veld aan liep `OnStartup` niet, zonder wel. Het `Loaded` van het hoofdvenster
loskoppelen voorkomt daarnaast een pictogram in het systeemvak en een planner die start.

*Eerstvolgende zoekopdracht nu uitvoeren* neemt de zoekopdracht die het eerst aan de beurt is,
niet de eerste in de alfabetische lijst. De tekst bij het pictogram ("3 nieuw bij ...") gaat
weg zodra je het venster opent.

## Resultaten over pagina's

Er stond lang een **rem op wat er binnenkwam**: honderd zoekertjes per site, in te
stellen achter het `#`. Dat was de verkeerde knop. Je wilde niet mínder ophalen, je
wilde er niet vijfhonderd tegelijk op je scherm. Die twee dingen staan nu los van
elkaar:

- **De app haalt op wat de sites geven**, tot een rem die afhangt van het soort site (zie
  hieronder). Voor "cd" over vijf sites was dat al in september 1124 zoekertjes in plaats
  van de 500 van vroeger: Catawiki gaf er 240 in plaats van 100, eBay 500.
- **Het `#` bepaalt enkel hoeveel je er tegelijk ziet**: 50, 100, 150 of 200. Het is
  een instelling van de app (`AppSettings.PageSize`), niet van een site, dus die knop
  werkt ook op het tabblad "Alles".

**De rem hangt af van hoe een site binnenkomt** (22 september 2026,
`SiteDefinition.ResultLimit` en `PageLimit`). Het is een keuze van de app, geen grens van de site:

| Soort site | Hoogstens | Pagina's | Waarom |
|---|---|---|---|
| rechtstreeks (`AnswersDirectly`: gewone motor, geen browser, geen brug) | 2000 | 20 | 100 zoekertjes kosten bij 2dehands 0,3 tot 0,8 s |
| via de browser of de brug | 500 | 10 | 2 tot 8 s per pagina, en veel pagina's na elkaar valt een robotbeveiliging op |
| de linkmotor (Facebook) | 300 | - | geen pagina's maar scrollen, als jouw aangemelde account (zie bij de linkmotor) |

Tot dan was het 500 voor elke site, en onthield elke bewaarde zoekopdracht per site zelf een
maximum, uit het veld "Max. resultaten" in haar venster: 100 bij de oudste, 500 bij de rest. Dat
veld is weg (`SiteSetting.MaxResults`); anders was de hogere rem nooit bij "Computer" of
"Commodore" aangekomen. Een oude zoekopdracht met dat veld in haar JSON-blokje leest gewoon verder.

Wat de hogere rem losmaakte: 2dehands heeft voor "cd speler" **3961** zoekertjes (het zoekt ook in
de beschrijving), en de app haalde er 76 van op - de eerste 100, min 24 van Catawiki. Ook verderop
zijn het nog echte cd-spelers: vanaf het 500e 70 van de 100 met "cd" en "speler" in de titel,
vanaf het 2000e 51, pas helemaal achteraan (3800) auto's met een cd-speler in de beschrijving.
Gemeten op 22 september 2026, met de motor van de app: **2dehands 1816 in 10,5 s, Marktplaats
1750 in 16,0 s**, telkens 20 pagina's (de rem, niet het einde: pagina 20 gaf nog 87 nieuwe) en
zo'n 110 advertenties van Catawiki overgeslagen. Een site met kleine pagina's haalt de 2000 niet:
de rem van 20 pagina's geldt ook daar, want tachtig verzoeken na elkaar aan dezelfde site valt op.

Let op bij een bestaande zoekopdracht: wat voorbij de eerste 100 staat, heeft ze nooit gezien.
De eerste beurt na deze wijziging telt dat dus als nieuw - bij "Cd speler" zo'n 1700 op 2dehands.
Een melding blijft daarbij klein: de ballon toont er 3, Telegram 15, en een e-mail sinds dezelfde
dag hoogstens 50 (`Notifier.MailMaximum`), telkens met "en nog ..." eronder.

**`{offset}`: pagina's die zeggen vanaf het hoeveelste zoekertje ze beginnen.** De API van
2dehands en Marktplaats nummert zijn pagina's niet: de derde pagina is `limit=100&offset=200`.
In de zoek-URL of in `PageTemplate` wordt `{offset}` (pagina - 1) maal `PageSize` uit het
sitebestand; zonder `PageSize` telt het niet als paginering (anders vraagt de app twintig keer
dezelfde pagina). Meer dan 100 per keer weigert die API (`limit=200` gaf een 400).

**Een korte pagina is de laatste.** De app vraagt geen volgende pagina meer wanneer een
pagina minder dan de helft van de eerste opleverde (`GenericSource.SearchAsync`). Zonder die
regel vroeg ze bijna altijd een pagina voorbij het einde, en die wachtte acht seconden op
zoekertjes die nooit kwamen: in het logboek waren 23 van de 27 keer "netwerkstil" zo'n lege
vervolgpagina, bij AlleVeilingen 10 van de 13 seconden. De helft en niet "minder dan de
eerste", want een gewone vervolgpagina levert vaak net iets minder op. Nagemeten met een
lokale proefsite: 30-29-10 stopt na pagina 3, 30-12 na pagina 2, en volle pagina's lopen
gewoon door tot de rem van tien. Een lege vervolgpagina die toch gevraagd wordt, wacht nog
maar drie seconden, en zonder het netwerkstil- en scrollwachten erbij.

**Een eerste pagina met minder dan tien zoekertjes is de enige**
(`GenericSource.MinimumVoorVervolgpagina`). Geen enkele site toont er minder per pagina, en
een zoekterm met twee resultaten vroeg anders nog een lege pagina 2 (bij AlleVeilingen 10 s).

**Hoe het verdelen werkt.** Er zijn al twee lagen die bepalen wat je ziet — de filter
op de weergave (tab, prijs) en de sortering. De pagina is een **derde**
laag, en die kan geen filter zijn: een filter beslist per zoekertje los van de rest,
terwijl "zit dit op pagina drie" juist een vraag is over de volgorde van alles samen.
Die is pas te beantwoorden nadat filter en sortering gedraaid hebben. Dus: eerst
filteren en sorteren zoals altijd, en dan er een schijf uit nemen (`ToonPagina`). De
resultatenlijst hangt daarom aan `_zichtbaar` — één pagina — en niet meer aan alle
resultaten.

**Je kijkt al terwijl de rest binnenkomt.** `ToonPagina` draait ook bij elke levering
van een bron, dus pagina één staat er zodra de eerste site klaar is en het aantal
pagina's groeit daarna onder je handen mee. Gemeten op vijf sites: na 5 seconden twee
pagina's, na 20 vier, na 30 zeven.

Ook **pagina 1 zelf** wordt meteen gemeld (`progress`). Tot september 2026 kwam die pas met
de returnwaarde terug, dus na de laatste pagina: bij AlleVeilingen stond pagina 1 10,6 s later
in beeld dan nodig, en achter pagina 2 tot 10 in "zoals de site ze geeft".

`ToonPagina` wist de zichtbare lijst daarbij niet meer, maar past enkel aan wat veranderde.
Wissen gaf een Reset, en daarop gooide het raster al zijn kaarten weg en sprong het naar
boven - bij elke levering van een site, dus terwijl je al aan het scrollen was. Een nieuw
zoekertje bovenaan is nu één toevoeging en één verwijdering; nagekeken op 3000
willekeurige wissels, zonder één Reset.

**De pager staat er twee keer**: vooraan in de knoppenrij, links van het
locatiespeldje, en nog eens onder de resultaten. Wie naar beneden gescrold heeft,
staat met de muis al bij de tweede en hoeft niet terug naar boven. `BouwPager` vult
ze allebei in dezelfde lus. Hij toont **hoogstens zeven nummers** rond de pagina
waar je staat. Ga je
vooruit, dan schuift dat venster mee: eerst 1 tot 7, dan valt 1 weg en komt 8 erbij.
Alle pagina's tonen zou bij vijftien pagina's een lint dwars over de kopbalk geven.
De pager verdwijnt vanzelf wanneer alles op één pagina past.

Bij een nieuwe zoekopdracht, een andere tab of een ander aantal per pagina begin je
weer op pagina één — op pagina vier van een vorige site staan slaat nergens op.

## Wat je te zien krijgt

Naast de filters bepaalt vooral de **volgorde** hoe de zoekertjes op het scherm staan. Die
werkt op de **weergave** en niet op de lijst zelf: wat binnenkwam blijft staan, er wordt
enkel anders naar gekeken. Zo is alles omkeerbaar zonder opnieuw te zoeken.

**Volgorde** (`ListingSort`). Zoals de site ze geeft, op prijs in beide
richtingen, nieuwste eerst, of de veiling die het eerst afloopt. Dat gebeurt met `ListCollectionView.CustomSort`
en niet met een `SortDescription`, want die laatste zet zoekertjes zonder prijs
of zonder datum vooraan — precies waar je niet naar wil kijken. Ze horen
achteraan, in beide richtingen, en gelijke waarden worden op hun sleutel uit
elkaar gehouden zodat de lijst niet danst bij elke verversing.

**Veiling die het eerst afloopt** (18 september 2026, `ListingSort.EndingSoonest`): bovenaan wat
het eerst sluit, over de sites heen. Achteraan komen gewone zoekertjes (die lopen niet af) en
veilingen die al voorbij zijn (daar kan je niet meer op bieden). Waar het einde vandaan komt:

- bij **AlleVeilingen** de echte datum van de kavelpagina (`EndsAt`, zie hieronder);
- bij **Catawiki en eBay** een schatting uit hun aftelklok (`Listing.SchatEinde`): "Nog 3 dagen" is
  over drie dagen, "Nog 9d 12u" over negen dagen en twaalf uur, "01:23:45" over zoveel tijd.
  Gerekend vanaf het moment dat de pagina gelezen werd, niet vanaf de klik op de volgorde. Een
  klokuur als "19:30" telt niet: dat is een tijdstip, geen resterende tijd.

`Listing.EndsAtOrEstimate` geeft het ene of het andere, en daarop sorteert `ListingComparer`, met
**één vast "nu" per beurt** - anders kan een veiling halverwege het sorteren voorbij raken.

**Bij deze volgorde haalt de app de einddatum van álle AlleVeilingen-kavels op**, niet enkel van
wat in beeld staat. Zonder dat zou het niet werken: de kavels zonder datum komen achteraan,
raken dus nooit in beeld, en krijgen daardoor nooit een datum. De eigenaar koos het zo, met de
cijfers uit het logboek erbij: meestal 60 tot 100 kavels per zoekopdracht, hoogstens 300. Zodra
die datums binnen zijn, gaat de lijst **één keer** opnieuw op volgorde (niet bij elke datum,
anders springt ze voortdurend); `DetailFetcher.FillAsync` zegt daarvoor hoeveel zoekertjes er
een datum bijkregen, en de ronde daarna vindt alles in het geheugen, dus dat loopt niet rond.

Nagemeten op het hoofdscherm buiten beeld, met tien zoekertjes per pagina en acht echte
AlleVeilingen-kavels waarvan er vijf buiten pagina 1 stonden: met de gewone volgorde kregen er
3 van de 8 een datum (wat in beeld stond), met "eerst afloopt" alle 8, en pagina 1 liep van
"Nog 2 uur" (Catawiki) over "Nog 4d 3u" (eBay) en de AlleVeilingen-kavels tot "Nog 12 dagen".

Een prijs van **nul telt niet als prijs**. Bij 2dehands staat er nul in het
prijsveld wanneer er "bieden" of "zie beschrijving" bedoeld wordt: van de honderd
zoekertjes voor "iphone 13" hadden er negen een nul, waarvan er maar twee echt
gratis waren (`priceType` is dan `FREE`, tegenover `FAST_BID` en
`SEE_DESCRIPTION` voor de rest). Zonder die regel staat die hele hoop bovenaan
zodra je op prijs sorteert.

**Wegklikken bestaat niet meer.** Tot 17 september 2026 stond er een oogje op elke kaart:
"niet meer tonen", met de sleutel in de tabel `hidden`, een menu-item om alles terug te
zetten, en een planner die weggeklikte zoekertjes niet als nieuw telde. De eigenaar gebruikte
het niet: je zoekt om dingen te zien, niet om ze te verbergen. Op de plaats van het oogje staat
nu het **vergrootglas**: de muis erop toont de grote foto (`PhotoThumbnail.ShowPreview`, in
het sjabloon gekoppeld aan `IsMouseOver` van `ListZoom` of `GridZoom`). Vroeger verscheen die
grote foto zodra de muis ergens op de miniatuur stond, en dan sprong er bij elke beweging over
de lijst een foto uit. Wat eerder weggeklikt was, staat gewoon weer in de lijst; de tabel
`hidden` blijft in een bestaande databank staan, maar wordt niet meer gelezen.

**De koopjesmarkering is eruit** (17 september 2026), en dat is met opzet geen kleine voetnoot:
er zat veel werk in. Een groen label "-99%" op een kaart betekende "zoveel procent onder de
mediaan van deze zoekopdracht", met een uitschietersgrens (het eerste kwartiel min een half maal
de kwartielafstand) zodat een brede zoekterm geen labels gaf. Dat rekenwerk klopte, maar de
**invoer** niet: de goedkoopste zoekertjes in een lading zijn bijna altijd **veilingen waarop nog
geboden moet worden**. Catawiki adverteert zijn kavels op 2dehands en Marktplaats met het huidige
bod als prijs, en een bod begint bij € 1. Zo hing het label precies bij wat géén koopje is, en
trokken die biedingen ook de mediaan omlaag. De eigenaar: "dit vind ik geen goede richtlijn, weg
ermee." Meegegaan: `PriceInsight`, `BargainBrush`, het label in beide sjablonen, "· 2 koopjes" in
de statusregel, en in het venster van een zoekopdracht "alleen melden als het minstens X % onder
de mediaan zit" (`NotifyOnlyBargains`, `BargainPercent`). Een oude zoekopdracht met die vinkjes
meldt voortaan gewoon alles wat nieuw is; de velden in het JSON-blokje worden genegeerd.

Wat de vraag "is dit een goede prijs" wél beantwoordt, staat hieronder: de prijsindicatie, die
per model vergelijkt en veilingen apart houdt.

**Naast de prijs staat de stad, en anders het land** (17 september 2026). Dat is wat je bij een
zoekertje wil weten: kan ik het gaan halen, of komt het van ver. Wat de sites aanleveren is daar
niet naar. AlleVeilingen zet het volledige adres van het veilinghuis in één tekstje
("Rijksweg 2, 9681 Maarkedal, België"), eBay geeft in zijn kaarten van september 2026 helemaal
geen stad meer maar wel het land ("van Nederland"), en de selector `.s-item__location` uit het
sitebestand bestond daar niet meer - op 62 kaarten nul treffers, dus stond er niets.

Dat is opgelost in de **sitebestanden** en niet in de app, want hoe een site zijn adres opschrijft
hoort bij die site. De nieuwe regel `::match(patroon)` achter een selector (zie "Sites toevoegen")
houdt enkel over wat in de eerste haakjes van het patroon past:

| Site | wat de pagina geeft | wat er nu staat |
|---|---|---|
| 2dehands, Marktplaats | `Aalter` | ongewijzigd |
| AlleVeilingen | `Rijksweg 2, 9681 Maarkedal, België` | `Maarkedal` |
| eBay | `van Nederland` | `Nederland` |
| Kleinanzeigen | `72461 Albstadt` | `Albstadt` |
| AutoScout24 | `BE-1160 Auderghem` | `Auderghem` |
| leboncoin | `Photo, audio & vidéo` (de **categorie**) | `Argenteuil` |
| Discogs | het land van de verkoper | ongewijzigd: geen stad beschikbaar |
| Catawiki | niets | nog niets, zie hieronder |

Bij AlleVeilingen is het patroon `(?:^|,)\s*(?:\d{4,6}\s+)?([^,]+?)\s*,\s*[^,]+$`: het komma-stuk
vlak voor het land, zonder de postcode die er soms voor staat. Dat ene patroon vangt alle vormen die
de site geeft - met straat, met een gehucht ervoor, met en zonder postcode, en enkel de stad.
Nagemeten op de echte zoekpagina: 13 verschillende adressen, 13 juiste steden. Bij eBay is het
`^(?:van|uit)\s+(.+)$` op de laatste `.s-card__attribute-row`: 60 van de 62 kaarten geven zo hun
land. De twee andere zijn advertenties zonder land, en die blijven leeg - wat het patroon niet
herkent, komt er niet in, dus daar verschijnen niet de verzendkosten die in diezelfde rij staan.

Bij het nameten bleken er nog drie sites meer te tonen dan een stad, en die zijn in dezelfde
beweging meegegaan - het is telkens één regel in het sitebestand:

- **Kleinanzeigen** en **AutoScout24** zetten de postcode voor de stad (`72461 Albstadt`,
  `BE-1160 Auderghem`). Patroon: `^(?:[A-Z]{2}-)?(?:\d{4,6}\s+)?(.+)$`, nagemeten 27 van de 27
  en 20 van de 20.
- **leboncoin** toonde de **categorie** in plaats van de plaats. Een kaart heeft daar twee keer
  `p.text-caption.text-neutral` - eerst de categorie, dan de plaats - en de motor neemt het eerste
  element dat past. De selector wijst nu met buren (`p... + p.sr-only + p...`) de tweede aan, en
  het patroon `^(.+?)(?:\s+\d{4,5}\b.*)?$` knipt de postcode en de wijk eraf: `Argenteuil 95100
  Centre-ville` wordt `Argenteuil`. Nagemeten: 35 van de 35 kaarten een Franse stadsnaam.

**Discogs** geeft het land van de verkoper en **Facebook** "Kortrijk, VLG"; die blijven zoals ze
waren. Facebook loopt trouwens via de linkmotor, en die haalt de plaats uit de tekstregels van de
kaart in plaats van uit `LocationSelector` - `::match` doet daar dus niets.

**Bij een veiling staat achter de plaats hoelang er nog geboden kan worden** (17 september 2026):
"Nederland · Nog 9d 12u". Dat is precies wat er bij een kavel ontbrak, want daar is de tijd
belangrijker dan de prijs van dit moment. Het sitebestand wijst die tekst aan met
`TimeLeftSelector`, en `Listing.PlaceLine` plakt de twee aan elkaar: is er geen plaats - Catawiki
geeft er geen - dan blijft de tijd alleen over, zonder los scheidingsteken.

| Site | Waar het staat | Wat er komt |
|---|---|---|
| Catawiki | `time` op de kaart | `Nog 3 dagen`, `Nog 21 uur` |
| eBay | `.s-card__time-left` | `Nog 9d 12u` (enkel bij een veiling; bij "Nu kopen" bestaat dat element niet) |
| AlleVeilingen | niet op de zoekpagina, wel op de **kavelpagina** | `Nog 11 dagen`, uitgerekend uit `29/09/2026 19:00` |

**Het is tekst en geen datum**, met opzet. Catawiki zet geen `datetime` bij zijn aftelklok - dat
was nog een open vraag, en nu is ze beantwoord - en wat de site zelf toont, klopt altijd met wat een
bezoeker daar ziet. Daarom gaat het ook **niet mee** in een favoriet of in de bewaarde resultaten
van een vorige beurt: "nog 3 dagen" van vorige week zou een leugen zijn. Bij Catawiki komt de klok,
net als de prijs, pas met JavaScript in de pagina, dus `Listing.MergeFrom` vult ze aan uit een
latere levering van de brug.

**En het kost niets extra.** De vraag lag voor de hand: als je die tijd toch moet ophalen, haal dan
meteen het land op. Maar de tijd wordt niet opgehaald - ze staat al op de zoekpagina, in dezelfde
kaart als de titel en de prijs. Het land staat daar niet, en enkel daarvoor zou je per kavel een
pagina moeten laden.

**Behalve bij AlleVeilingen**, en daar wordt ze wél opgehaald (18 september 2026). Op de kaart
staan enkel het huidige bod, het aantal biedingen en het kavelnummer; de sluitingsdatum staat op de
pagina van het kavel, in `div[title='Einddatum']`: "Einde op 29/09/2026 19:00". Wat dat mogelijk
maakte, was een meting: die pagina komt binnen met een **gewoon verzoek** - geen browser, geen brug -
en gemeten over acht echte kavels duurde dat **191 ms voor alle acht samen**, zes tegelijk.

`DetailFetcher` doet dat, en de regel eromheen is belangrijker dan de code: **enkel voor de
zoekertjes die op dat moment op het scherm staan**. Een zoekopdracht levert tot vijfhonderd
zoekertjes per site, en dan zouden dat vijfhonderd verzoeken aan die site zijn. Nu is het er
één per kavel dat je ziet - bij een pagina van vijftig ongeveer een seconde, op de
achtergrond, terwijl de resultaten al in beeld staan. Zo koos de eigenaar het. Verder:

- **Behalve bij de volgorde "Veiling die het eerst afloopt"**: die haalt de datum van álle kavels in
  de lijst op, zie "Wat je te zien krijgt".
- **Wat opgehaald is, blijft onthouden** zolang de app draait (op de sleutel van het zoekertje), dus
  heen en weer bladeren kost niets. Nagemeten: de tweede ronde over dezelfde acht kavels deed 0 ms.
- **Bij een nieuwe pagina wordt het vorige stilgelegd** (`VulEinddatumsAan` in `MainWindow`): die
  kavels staan dan niet meer in beeld.
- **Een zoekertje dat zijn tijd al toont, wordt overgeslagen**, en een link die geen webadres is ook.
- Het is een **echt tijdstip** en geen tekst, dus de app rekent er zelf "Nog 11 dagen" uit
  (`Listing.TijdTot`) en dat blijft kloppen - ook morgen, en ook bij een favoriet.
- **De melding aan het scherm moet op de schermdraad.** Het ophalen gebeurt op
  achtergronddraden, en een `PropertyChanged` van daar bereikt de binding niet: het zoekertje
  stond goed in het geheugen, maar op de kaart bleef enkel de plaats staan. `DetailFetcher.Zet`
  gaat daarom via `Application.Current.Dispatcher` - en zonder venster, zoals in de controles,
  gewoon meteen. Dit is precies het soort fout dat je niet ziet in een controle zonder scherm:
  het kwam pas boven met het hoofdscherm buiten beeld.

Het veld heet `DetailEndDateSelector` en staat in *Sites beheren* als "Einddatum (op de
kavelpagina)". Vul het enkel in wanneer het echt niet op de zoekpagina staat: het is een verzoek per
zoekertje.

**Past de regel niet, dan schuift ze** (`Controls/ScrollingText.cs`). In het raster is een kaart
264 breed, en daar gaat "Saint-Rémy-lès-Chevreuse · Nog 9d 12u" (193 beeldpunten) niet
naast de prijs in. In rust dooft het einde uit over de laatste 22 beeldpunten - dat zegt "er staat
meer" zonder een beletselteken - en zodra de muis erop komt, schuift de tekst heen en weer zodat je
het einde kan lezen. Na een aanloop van 0,4 seconde, zodat een muis die over de lijst passeert niets
in gang zet, en met 45 beeldpunten per seconde (minstens 1,2 s heen, zodat een regel die net niet
past niet zit te trillen).

Drie dingen die daarbij horen, en die alle drie uit een meting kwamen:

- **Een TextBlock met een animatie volstaat niet.** Een TextBlock krijgt van zijn ouder nooit meer
  breedte dan er beschikbaar is, dus valt er ook niets te schuiven. Vandaar een eigen `Decorator`
  die zijn tekst méét met oneindige breedte en op die natuurlijke breedte plaatst; het vak
  eromheen knipt af.
- **Een `OpacityMask` met een verloop van 0 tot 1 wordt uitgerekt over het element én zijn
  kinderen.** De tekst is hier juist breder dan het vak, dus viel de hele vervaging in het stuk dat
  toch al weggeknipt was - op het scherm was er niets te zien. Met
  `MappingMode="Absolute"` en een eindpunt in beeldpunten staat ze wel waar ze hoort.
- **Het vak moet zelf de muis aannemen.** Zonder een doorzichtige rechthoek in `OnRender` reageert
  enkel de tekst zelf, en moet je precies op een letter mikken.

Nagemeten met twee wegwerpprojectjes: één dat het besturingselement los opbouwt (aanloop,
snelheid, terugspringen, en stoppen wanneer een kaart uit beeld gaat - in het raster worden kaarten
hergebruikt, en een animatie die blijft lopen schuift daarna de tekst van een ánder zoekertje),
en één dat het hoofdscherm buiten beeld opbouwt en de twee sjablonen vult. Let op bij dat
laatste: het raster bouwt buiten beeld **geen enkele kaart** op (nul rijen), dus daar is het
sjabloon met de hand gevuld. En een `VisualBrush` toont de `OpacityMask` van zijn wortelelement
niet, dus fotografeer de ouder wanneer je zoiets wil zien.

**Rechtsonder op een veilingkaart staat een timer** (18 september 2026, `Controls/CountdownBadge.cs`):
een klokje met hoelang er nog geboden kan worden. Op de lijstkaart in de rechterbenedenhoek, op de
rasterkaart aan het einde van de prijsregel - daar is dat de onderste regel. De tijd achter de stad
blijft ook staan; zo koos de eigenaar het.

De timer **telt enkel echt af als het tijdstip dat toelaat** (`Listing.TimerEinde`):

| Site | Waar het tijdstip vandaan komt | De timer |
|---|---|---|
| AlleVeilingen | de kavelpagina (`DetailEndDateSelector`) | telt af, op de seconde |
| Catawiki | hun API, na het zoeken (`EndTimeApi`, zie hieronder) | telt af; tot de API antwoordde de tekst van de site ("3 dagen") |
| eBay | de aftelklok van de kaart | "9d 12u" als tekst; telt af zodra eBay op de minuut of seconde schrijft ("Nog 6s") |

Aftellen vanaf "Nog 3 dagen" zou een precisie tonen die er niet is: de echte sluiting kan evengoed
23 uur later zijn. Daarom onthoudt `Listing` bij een geschatte tijd of de tekst tot op de minuut
ging; enkel dan telt de timer ervan af. eBay schrijft zijn klok fijner naarmate het einde nadert -
gemeten op 18 september 2026: "Nog 6s" bij een veiling die zes seconden later sloot - dus daar telt
hij af precies wanneer het ertoe doet. eBay zet ernaast ook een tijdstip, maar in wisselende vormen
("(Vandaag 19:46)", "(27/09, 11:24)"); dat lezen we niet.

Opmaak (`Listing.TimerTekst`): "3d 04u", "4u 12m", "12m 34s", "Afgelopen". In het laatste uur kleurt
hij amber (`TimerUrgentBrush` en `WarningOnCardBrush`, `Listing.IsDringend`). De tooltip zegt het
exacte uur ("Sluit zondag 20 september om 23:09"), of dat de tekst van de site komt.

**Alle timers delen één klok.** Een timer meldt zich aan bij `Loaded` en af bij `Unloaded`; het
raster bouwt enkel de kaarten in beeld op, dus er tikken er hoogstens een paar tientallen, en staat
er geen enkele in beeld, dan staat de klok stil. Nagemeten met de twee sjablonen buiten beeld: de
timer van AlleVeilingen "2d 03u", die van Catawiki "3 dagen" en na twee seconden nog steeds, die van
eBay van "0m 44s" naar "0m 42s" en amber, een gewoon zoekertje zonder timer, en na het sluiten
luisterde er niemand meer naar de klok.

**Het exacte tijdstip van Catawiki komt uit hun API** (`EndTimeApi` in het sitebestand,
`DetailFetcher.FillFromApiAsync`). De zoekpagina van Catawiki vraagt het zelf op bij
`/buyer/api/v3/bidding/lots?ids=...`, voor alle 24 kavels van een pagina samen, met per kavel
`bidding_end_time` (in UTC). Wij doen hetzelfde: één verzoek per 24 kavels, voor álle kavels van de
zoekopdracht, dus meestal één tot zes. Een gewoon verzoek krijgt daar een 403, dus het loopt via de
brug, zoals de zoekpagina zelf - en **pas na het zoeken**: zolang er gezocht wordt, heeft de
zoekopdracht de brug nodig (`SearchRunner.Gate`), en na het zoeken komt er vanzelf een beurt. Werkt
de brug niet, dan wordt het overgeslagen en toont de timer de tekst van de site. Het blok is
algemeen: een andere site met zo'n API zet er haar eigen adres en puntpaden in.

**Catawiki heeft geen plaats op zijn zoekpagina**, en ook niet in de API's erachter. Wat er op
17 september 2026 nagekeken is, zodat niemand het nog eens hoeft af te tasten:

| Waar | Wat het geeft |
|---|---|
| de kaart (`article.c-lot-card__container`) | titel, prijs, foto, aftelklok - geen plaats |
| `__NEXT_DATA__` van de zoekpagina | dezelfde velden plus de verkoper (`sellerShopName`) - geen land |
| `/buyer/api/v1/search?q=` en `/buyer/api/v2/search?q=` | een echte zoek-API met 25 kavels, maar per kavel geen land (en ook geen prijs) |
| `/buyer/api/v1/lots?ids=<lijst>` | meerdere kavels tegelijk: titel, foto, link - geen land |
| `/buyer/api/v2/lots/<id>/shipping` | verzendprijzen naar elk land, niet het land van herkomst |
| `__NEXT_DATA__` van de **pagina van het kavel** | `"country":{"name":"Nederland","shortCode":"nl"}` |

Het land bestaat dus wel, maar enkel per kavel, en enkel achter een paginabezoek - via de brug zo'n
vier seconden per kavel, en een zoekopdracht geeft er honderd. Zichtbaar op het scherm staat er
bovendien vaak "Verzending vanuit de EU" in plaats van een land; het land zit enkel in het JSON-blok.
Dezelfde afweging dus als bij "Grote foto's op aanvraag" (Volgende stappen 2): iets voor op aanvraag,
niet tijdens het zoeken. Wat vandaag wel kan: Catawiki heeft een eigen filter *Land van de verkoper*,
en wie dat aanvinkt weet het land van elk resultaat zonder het op te halen.

### Prijsindicatie: wat is dit ongeveer waard

De **prijsindicatie** beantwoordt de vraag per zoekertje in plaats van per lading:
rechtsklik op de foto van een zoekertje (in de lijst, het raster of bij de favorieten) en kies
*Prijsindicatie*. Er gaat een venster open (`PriceIndicationWindow`) dat een zoekterm voorstelt,
meteen zoekt, en de marktwaarde toont met alle vergelijkingen eronder, zodat je zelf ziet waar
het getal vandaan komt. Klikken op een vergelijking opent dat zoekertje. Gebouwd op 17 september
2026; de eigenaar wil dit later ook voor favorieten gebruiken om te zien of iets nog te koop is en
of er iets gelijkaardigs bestaat.

**Hoe het werkt** (`PriceIndicator`):

1. **De zoekterm.** Merk en model uit de titel: "Denon - DCD-520 - Lecteur de CD" wordt
   "Denon DCD-520" (`SuggestTerm`). Een modelnummer is letters plus cijfers ("DCD-520", "PMA 525R"),
   ook met twee groepjes letters ("SL-PJ22"); "Lot 63", "uit 1983" en "32-bit" zijn het niet. Die
   twee groepjes kwamen erbij nadat "Technics - SL-PJ22" het voorstel "SL PJ22" gaf, zonder merk.
   Je kan de term aanpassen en opnieuw zoeken.
2. **Zoeken** op de sites met het vinkje *Telt mee voor prijsindicatie* (`PriceReference` in het
   sitebestand), met hetzelfde slot als een gewone zoekopdracht, in **twee schrijfwijzen** waarvan
   de treffers samengaan (`ZoekVormen`): zoals getypt ("Technics SL-PJ22") en met losse letters
   ("Denon dcd 520", "Technics sl pj22"). Gemeten: met "DCD-520" ontbrak "DCD 520 AE", maar met
   "sl pj 22" vond 2dehands 1 onbruikbare treffer in plaats van 2 juiste, want het leest "SL-PJ22"
   als één woord. Verbreden gaat met losse letters: "Technics SL PJ" vond op Marktplaats 7 andere
   SL-PJ's, "Technics SL-PJ" geen enkele.
3. **Opschonen.** De titel wordt streng nagekeken, want de sites zoeken ruim:
   - elk woord uit de zoekterm moet in de titel staan, en het modelnummer ook;
   - **een ander achtervoegsel is een ander model**: de DCD-520AE is nieuwer en duurder dan de
     DCD-520, en staat apart met een eigen prijsvork. Zo koos de eigenaar het;
   - **een bod is geen prijs**: een veilingsite (`IsAuction`) telt niet mee. Een veilinghuis dat op
     een gewone site adverteert (`AuctionSellers`) komt hier niet eens meer voorbij: die zoekertjes
     worden al bij het uitlezen overgeslagen, zie "Werkende bronnen";
   - niet mee tellen ook: iemand die zelf zoekt, toebehoren of een onderdeel vóór het model
     ("Afstandsbediening Denon rc-203 dcd-800": één verkoper had er vijftien), defect of voor
     onderdelen, een set (het woord "set", of een tweede toestel met "+", "/", komma of "en"
     ertussen) en geen prijs;
   - hetzelfde zoekertje op twee sites telt één keer (zelfde titel en prijs).
4. **Rekenen** (`Reeks`): de mediaan, en als vork het eerste tot het derde kwartiel. Vanaf vier
   prijzen vallen de uitschieters eruit (anderhalve kwartielafstand); daaronder is de vork de laagste
   tot de hoogste prijs. **Onder de drie prijzen** zegt de app "te weinig gegevens".
5. **Verbreden** bij te weinig: naar de reeks, "Denon DCD", met dezelfde regels, en duidelijk
   gelabeld als geen exact model. Zonder modelnummer kan dat niet, en dat staat er dan. **Geen AI**:
   de eigenaar koos voor enkel verbreden.

Gemeten op 17 september 2026 voor de DCD-520 op 2dehands en Marktplaats, in 0,8 seconden: twee
vraagprijzen (€ 80 en € 90), dus te weinig; de DCD-520AE apart met € 80, € 110 en € 125 (en € 250
als uitschieter); twee Catawiki-veilingen; en verbreed naar Denon DCD: meestal € 75 tot € 194 uit
46 vraagprijzen, waarbij 49 van de 100 treffers wegvielen (veilingen, afstandsbedieningen, sets).
Voor een DCD-1450AR: ± € 200, meestal € 135 tot € 250 uit 4 vraagprijzen.

**Waarom de vergelijkingen vaak van Marktplaats komen:** het is gewoon groter. "technics cd speler"
gaf er 248 tegenover 88 op 2dehands, en van de SL-PJ-reeks stond op 2dehands enkel de Catawiki-kavel
zelf. **eBay en Facebook tellen (nog) niet mee:** ze lopen via de browser (enkele seconden per
pagina), eBay gaf bij de laatste beurt 0 resultaten, eBay mengt veilingen met "Nu kopen" (een bod
zou dan als vraagprijs tellen), en Facebook zoekt enkel in je eigen regio. Het vinkje kan in Sites
beheren, maar dat is nog niet nagemeten.

**Wat het niet is:** een verkoopprijs. Het zijn vraagprijzen van vandaag, en een vraagprijs is wat
een verkoper hoopt. Verkochte prijzen (eBay heeft een filter "verkochte artikelen") zouden sterker
zijn, maar eBay weigert een gewoon verzoek en loopt in Zentrix via de browser; dat is een volgende
stap. De woordenlijsten (toebehoren, defect, geen achtervoegsel) staan in `PriceIndicator` en zijn
gemaakt op echte titels; een nieuwe soort rommel vraagt daar een woord bij. Nagemeten in
`PrijsChecks` met de titels van die dag, en met een lokale proefsite van begin tot einde.

### AI-controle op een foto: wat zie ik hier niet

De **AI-controle** kijkt met een model op je eigen pc naar de foto van een zoekertje en vertelt er
in gewone taal over. Bedoeld voor wat je met het blote oog niet ziet: een doos vol dvd's waarvan
de titels te klein zijn, of het typenummer op het label achteraan een oude radio. Zo vroeg de
eigenaar het op 24 september 2026.

**Het draait lokaal.** Ollama op `127.0.0.1:11434`, op de grafische kaart (een RTX 4060); er gaat
geen foto de deur uit. Het model staat in `AppSettings.AiModel` en is `qwen3.5:9b` - 9,7 miljard
parameters, Q4_K_M, 6,6 GB, en het kan "vision". Gemeten: **ongeveer een seconde per foto**, plus
veertig seconden de eerste keer, want dan wordt het model in de kaart geladen. Daarom staat
`keep_alive` op tien minuten: anders betaal je die veertig seconden bij elke foto opnieuw.

**Drie dingen kwamen uit het meten en bepalen de hele opzet** (`PhotoAnalyzer`):

- **De foto moet in stukken.** Een vision-model verkleint zijn invoer naar een vast formaat, en op
  een hele foto zijn kleine labels dan een paar beeldpunten hoog. Op een echte kavelfoto met elf
  tijdschriften en zo'n dertig diskettehoesjes gaf de hele foto **14 namen** - enkel de grote
  koppen - en gaven zes stukken er **57**, de kleine labels inbegrepen. De stukken overlappen een
  kwart, anders valt een titel die op de snijlijn ligt in twee onleesbare helften uiteen.
- **Het antwoord moet een vaste vorm hebben.** Met vrije tekst liep het model vast in herhaling:
  34 seconden om 200 keer "SuperDisk" te zeggen. Met een afgedwongen JSON-vorm (`format` in de
  vraag aan Ollama) gebeurde dat geen enkele keer meer.
- **Lezen is betrouwbaar, weten niet.** Wat het van de foto leest klopt grotendeels; wat het
  eromheen bedenkt niet - bij een laptop verzon het toetsen die niet bestaan ("F34"). Daarom leest
  het eerst enkel namen, en vertelt het pas daarna, met het uitdrukkelijke verbod om iets over
  staat, kleur of ouderdom te schrijven dat het niet gelezen heeft. Met dat verbod erbij bleef de
  alinea eerlijk.

**In stukken lezen, in één keer vertellen.** Elk stuk geeft enkel namen terug; op het einde gaat de
hele foto nog één keer mee, samen met alles wat gelezen is, en daar komt de alinea uit. Dat zet
meteen leesfouten recht, want dan ziet het model de losse stukken in hun verband: PORTEX stond in
een los stuk als "FORTEX", MOUSE MANAGER als "HOUSE MANAGER" en HAIKU als "AIKU", en in de alinea
stonden ze alle drie goed.

**Wat het niet kan, en dus ook niet belooft: tellen.** "Ongeveer twintig tot dertig" voor elf
tijdschriften en dertig diskettes. Het aantal staat er als een schatting, en dat is het ook.

Nagemeten met de echte Ollama op de echte kavelfoto: **17,8 s** voor de grondige lezing (6 stukken,
34 namen), en een alinea die er 28 van opsomt. Twee leesfouten bleven staan (DIETPLAN werd
"Dietpotatoes", ROBOPOST werd "Robopod"), dus ongeveer vijf op de zes klopt - genoeg om te zeggen
*wat er ongeveer ligt*, niet genoeg om blind op te varen. Een gewone foto van één toestel gaat
zonder stukken: **1,8 s** voor "een tweedehands ThinkPad-laptop met Intel-processors".

De logica eromheen staat in `FotoChecks`, met een nagebootste Ollama (`NepOllama`): het knippen,
het ontdubbelen, wat er in de vraag meegaat, een stuk dat onzin teruggeeft, en Ollama dat niet
draait. De grafische kaart hoort niet in de controles - die moeten overal draaien en in een
seconde klaar zijn.

**Het venster** (`PhotoInsightWindow`, stap 2, 24 september 2026) gaat open met een rechtsklik op
de foto: *AI-controle op deze foto*, naast *Prijsindicatie*. Een eigen venster en geen dialoog, om
dezelfde reden als bij de prijsindicatie: het kijken duurt een halve minuut, en intussen wil je
verder kunnen. Vier dingen die het eerlijk moet zeggen, want anders lijkt het stuk of te mooi:

- **Het model laden kost de eerste keer zo'n veertig seconden.** Het venster vraagt vooraf aan
  Ollama of het model al in de kaart staat (`PhotoAnalyzer.ModelStaatKlaarAsync`, `/api/ps`), en
  zegt het dán - niet achteraf. Anders staar je naar een venster dat niets lijkt te doen.
- **Grondig lezen duurt langer.** Het vinkje staat aan, en zegt in zijn tooltip waarom je het zou
  uitzetten: in stukken lezen maakt kleine tekst leesbaar, maar kost een halve minuut in plaats
  van een paar seconden.
- **Wat er letterlijk gelezen is, staat eronder**, als losse chipjes, met erbij dat ongeveer vijf
  op de zes klopt. De alinea erboven is wat je leest; die lijst is waarmee je het kan nagaan.
- **Onderaan staat wat het niet kan**: het draait op je eigen kaart, er gaat geen foto de deur
  uit, en een aantal is altijd een schatting.

De **grote foto** gaat voor op de miniatuur (`Listing.LargeImage`): hoe meer beeldpunten, hoe meer
er te lezen valt. *Kopiëren* zet de alinea én de gelezen namen op het klembord - bij een doos vol
dvd's is die lijst juist het ding dat je ergens anders wil plakken.

Nagemeten met het venster buiten beeld, met het echte zoekertje, de echte foto van Catawiki en de
echte Ollama: **12 s**, zes stukken, 34 namen, en de panelen vulden zich zoals het hoort. Dat de
menu-items goed gekoppeld zijn, bewijst de build zelf: de XAML-compiler zoekt `Click=` op in de
code-behind, dus een verkeerde naam komt er niet door.

**Alle foto's van één zoekertje** (stap 3, 24 september 2026). De zoekpagina geeft er één; een
advertentie heeft er vijf of tien, en juist op die andere staat vaak wat je zoekt - het label
achteraan, de doos van binnen, de krassen. Het tweede menu-item *AI-controle op alle foto's van
dit zoekertje* haalt ze op van de pagina van het zoekertje zelf, en elke foto krijgt zijn eigen
blok in het venster. Dat blok verschijnt zodra die foto klaar is: bij vijf foto's duurt het geheel
meer dan een minuut, en dan wil je niet naar een leeg venster kijken.

- **Waar die foto's staan, zegt het sitebestand**: `DetailImagesSelector`, met dezelfde notatie als
  elk ander veld, dus gerust met `::replace` erachter om de grote variant te krijgen. Ontbreekt het
  veld, dan blijft het bij de foto van de zoekpagina. Ingevuld op 25 september 2026 voor 2dehands,
  Marktplaats en AlleVeilingen; welke selector en wat er gemeten is, staat in `SITES.md` van
  `zentrix-sites`.
- **De foto die we al hebben staat vooraan.** Die is er zeker, en zo kan de AI-controle beginnen
  ook als de pagina niets extra's geeft.
- **Dubbels vallen weg**, want een site zet dezelfde foto vaak twee keer op de pagina: klein in het
  rijtje eronder en groot bovenaan. Een pad zonder domein wordt aangevuld tegen `BaseUrl`.
- **Via de brug bij een brugsite**, en dan enkel als de extensie zich net nog meldde - hiervoor
  start de app geen Chrome, net als bij de einddatum via een API.
- **Onthouden op het adres van de pagina**, niet op `Listing.Key`. Die is `Source:ExternalId`, en
  bij een leeg id zouden twee zoekertjes van dezelfde site elkaars foto's krijgen. Gevonden door de
  controle die er juist voor staat.

`GenericSource.ReadFieldsAsync` is de meervoudsvorm van `ReadFieldAsync`: alles wat past in plaats
van het eerste. Zo blijft er één plaats waar de notatie van een selector uitgelegd wordt. Twee
dingen kwamen daarbij boven, en allebei zijn het fouten die je enkel op een echte pagina vindt:

- **"Alles wat past" betekent ook élke treffer binnen één element.** Een site zet zijn foto's vaak
  niet als losse `img` neer maar samen in één blok, en dan is één element genoeg. Met `::match`
  levert dat blok nu elke treffer op in plaats van de eerste.
- **Er wordt in de hele pagina gezocht, niet enkel in de `<body>`.** 2dehands zet zijn foto's in
  het `application/ld+json`-blok van de **`<head>`**, en met alleen de body vond de selector daar
  nul elementen terwijl de foto's er gewoon stonden. Dat gold ook voor `ReadFieldAsync`, dus die
  is meteen mee rechtgezet; in de praktijk verandert er niets voor de einddatum van AlleVeilingen,
  want een `div` staat nooit in de `<head>`.

Nagemeten met het venster buiten beeld, de echte Ollama en twee echte Catawiki-foto's achter een
lokale proefpagina die de advertentiepagina speelt: twee blokken, elk met eigen miniatuur en een
ánder antwoord, samen **28 s**. En op 25 september met de echte sitebestanden en echte zoekertjes,
via dezelfde import als in het tandwielmenu: 2dehands gaf **3 extra foto's in 425 ms**,
AlleVeilingen **2 in 136 ms**.

**Dit is stap 1 tot 3 van vier**; zie Volgende stappen 3 voor de rest.

### Dubbelklikken: alles van één zoekertje

**Een dubbelklik opent niet meer de webpagina, maar een eigen venster** (25 september 2026,
`ListingDetailWindow`, gevraagd door de eigenaar). Daarin staan de foto's van de advertentie, wie
het verkoopt en hoelang het er al staat - precies wat je wil weten om te beslissen of je verder
kijkt. De browser openen, de cookiemelding wegklikken en de pagina laten laden was daarvoor een
omweg van een tien seconden per zoekertje. *Openen op de site* staat als knop in dat venster, dus
die weg blijft; ernaast staat *AI-controle*, die doorstuurt naar `PhotoInsightWindow`.

**De foto's: miniaturen boven, één grote eronder.** De muis over een miniatuur wisselt de grote
foto, en een randje in het accent toont welke dat is. Geen klik: er valt hier niets te kiezen dat
blijft staan, en zo blader je met één beweging door alle foto's. Zo koos de eigenaar het.

**Wat er meteen staat en wat opgehaald wordt.** De titel, de prijs, de plaats en de site komen uit
het zoekertje zelf, en de foto van de zoekpagina staat er meteen groot - het venster is dus nooit
leeg. De andere foto's, de verkoper en "online sinds" staan op de pagina van het zoekertje, en die
wordt opgehaald zodra het venster opengaat.

**In één verzoek, niet drie** (`DetailFetcher.DetailsAsync`). Het zijn drie gegevens van dezelfde
pagina; die drie keer ophalen zou bij een brugsite twaalf seconden kosten. `FotosAsync` (de
AI-controle) loopt sindsdien over dezelfde weg, met enkel de foto's eruit. Wat opgehaald is, blijft
onthouden op het adres van de pagina.

Waar het staat, zegt het sitebestand - drie velden, alle drie ook in *Sites beheren*:

| Veld | Waarvoor |
|---|---|
| `DetailImagesSelector` | alle foto's van de advertentie (bestond al voor de AI-controle) |
| `DetailSellerSelector` | de verkoper, wanneer die niet al op de zoekpagina staat |
| `DetailPostedSelector` | sinds wanneer het online staat |

Twee dingen die daarbij horen:

- **"Online sinds" is tekst en geen datum**, om dezelfde reden als de tijd tot het einde van een
  veiling: elke site schrijft het anders op ("Eergisteren", "24 sep. '26", "Vandaag"), en wat de
  site zelf toont klopt altijd met wat een bezoeker daar ziet. Er wordt dus niets uitgerekend.
- **De verkoper hoeft meestal niet opgehaald te worden.** 2dehands en Marktplaats zetten hem al op
  hun zoekpagina (`SellerSelector`, dat er toch al staat voor "veilinghuizen overslaan"), dus daar
  blijft `DetailSellerSelector` leeg en staat de naam er meteen.

**Heeft een site geen van de drie velden, dan zegt het venster dat** ("Het sitebestand van Discogs
zegt nog niet waar de foto's en de verkoper staan") en wordt er niets opgehaald. Beter dan een leeg
vak waarvan niemand weet of het aan het laden is.

**De prijs komt uit `PriceTextConverter`**, dezelfde als op de kaart. Rekende dit venster zelf, dan
stond hetzelfde zoekertje hier op "€ 40" en in de lijst op "€ 39,95".

Nagemeten met het venster buiten beeld en een vers zoekertje uit de zoek-API van 2dehands (een
advertentie van gisteren kan al weg zijn): **4 foto's in 356 ms**, verkoper "Japoto", "24 sep. '26",
de muis op miniatuur 2 wisselt de grote foto en verzet het randje, en een site zonder die velden
toont de ene foto die we wel hebben met de reden erbij. De logica eromheen staat in `FotoChecks`,
met een proefsite die telt hoeveel verzoeken er komen - dat één verzoek is het hele punt.

**Wat het nog niet doet:** de beschrijving van de advertentie, en "3 dagen online" uitrekenen uit
"24 sep. '26" (dat vraagt een datumlezer per site). 2dehands zet er trouwens ook "7x bekeken" en
"0x bewaard" bij; dat is er met dezelfde selector uit te halen.

## Hoe een site binnenkomt — drie wegen

1. **Rechtstreeks** (`HttpClient`). Snelst. Werkt bij 2dehands en Marktplaats,
   die allebei op de `lrp` JSON-API zitten.
2. **Playwright** (`NeedsBrowser = true`). Eigen Chrome-profiel zodat logins
   bewaard blijven. Nodig bij Facebook en AlleVeilingen. eBay liep hier ook, tot zijn
   robotbeveiliging de onzichtbare Chrome van de app in september 2026 niet meer
   betrouwbaar doorliet; sindsdien gaat het via de brug (zie `SITES.md`).
3. **De brug** (`UseBridge = true`). De eigen Chrome van de gebruiker, via een
   extensie. Enige weg voor sites met zware bot-detectie.

### De brug

Een Chrome-extensie ("Zentrix Brug", uitgepakt geladen uit de map `extension\` van dit project)
vraagt elke 250 ms aan `http://127.0.0.1:8731/job` of er werk is, opent de URL
in een tabblad op de achtergrond en stuurt de HTML terug naar `/result`. De
koppeling gebeurt met een code uit `brug-code.txt`, zichtbaar via tandwiel >
*Koppelcode* in de app.

Twee dingen die de wachttijd bepalen, en waar we lelijk op vastliepen:

- De extensie wacht op **`document.readyState`**, niet op het load-event. Dat
  laatste wacht ook op advertenties en trackers: op leboncoin stonden de
  resultaten er na 1 seconde, maar het load-event kwam pas na 23 seconden.
- Terwijl de pagina laadt stuurt de extensie elke 0,75 s een **tussentijdse
  versie** (`partial: true`). `BridgeServer` geeft die door aan de bron, die ze
  meteen uitleest, zodat de eerste zoekertjes al na ~1,5 s in beeld staan. De
  opdracht blijft openstaan tot de laatste, volledige levering.

**Een latere levering kan vollediger zijn.** Sommige sites vullen hun pagina in
stukken: bij Catawiki staat de prijs in de eerste HTML als een leeg vakje
(`<p class="c-lot-card__price placeholder"></p>`) en zet JavaScript hem er pas
daarna in. Een zoekertje dat uit zo'n vroege versie komt, mist die gegevens.
Daarom worden dubbels niet zomaar weggegooid: `Listing.MergeFrom` vult aan wat
nog ontbrak. Enkel aanvullen, nooit overschrijven. Zonder dat zag je bij Catawiki
alle kavels zonder prijs.

Let op bij wijzigingen aan `BridgeServer`: `Content-Length` telt **bytes**,
niet tekens. De body werd ooit als tekens gelezen, waardoor Franse pagina's
(é, è) de server lieten wachten op tekens die nooit kwamen — de POST hing dan
ruim twee minuten en de app gaf op met "geen antwoord van de browserextensie".

**Drie dingen die de snelheid bepalen.** De extensie wacht op het eerste zoekertje
(de app stuurt zijn `ItemSelector` mee als `waitSelector`) in plaats van een vaste
2,5 seconde; ze neemt tot **drie opdrachten tegelijk** aan, elk in een eigen
tabblad; en de app vraagt de vervolgpagina's van een brugsite samen op in plaats van
een voor een. Zie "Waarom meer cores niets oplossen" verderop.

Die rem van drie werkte tot september 2026 **niet**: de controle stond pas na het
aannemen van een opdracht, en na de korte pauze nam de extensie er toch een bij. Bij
Catawiki liepen er zo negen tegelijk. Nu vraagt ze geen werk meer zolang er drie lopen.
Verder in dezelfde ronde:

- **Streamen enkel als iemand leest.** De opdracht zegt `stream: false` wanneer er geen
  `onPartial` is (een vervolgpagina, een geplande zoekopdracht). Daarvoor kopieerde de
  extensie elke 0,75 s de hele pagina voor niets: 111 van de 159 miljoen tekens. Let op: de
  ontvanger moet dan ook echt null zijn. Eerst was hij er altijd en keek hij pas binnenin of
  iemand meelas, en dan streamde de planner toch.
- **Vervolgpagina's in golven van drie**, zoveel als de extensie tegelijk aanneemt, met na elke
  golf de stopregel van "een korte pagina is de laatste". Eerst gingen pagina 2 tot 10 in één
  keer de deur uit, ook als pagina 1 er twee gaf: bij leboncoin twaalf seconden aan lege
  pagina's, en negen verzoeken vanuit je eigen browser op een paar seconden.
- **Een bezette poort** laat de app niet meer vallen: `BridgeServer.PortBusy`, en de brug zegt
  `BridgeStatus.PortInUse`. De rest van de app werkt dan gewoon.
- **Een vervolgpagina wacht korter.** `itemTimeoutMs` in de opdracht (3 s in plaats van
  8 s), en zonder zoekertje valt de pauze van 2,5 s weg: die pagina is gewoon leeg.
- **Na het scrollen wachten tot er niets meer bijkomt** (`waitUntilStable` in de extensie,
  `WachtTotStabielAsync` in de app) in plaats van een vaste halve seconde: tellen om de
  100 ms, stoppen zodra het aantal twee keer gelijk bleef, hoogstens 1,5 s.

**Twee soorten opdrachten.** Naast een pagina kan de brug ook een **JSON-API**
ophalen. Dat gaat anders: de extensie opent geen tabblad om uit te lezen, maar
opent een gewone pagina van díe site en doet van daaruit een `fetch` met de
kopregels die de app meestuurt (`rawText: true` en `headers` in de opdracht). De
ruwe tekst komt terug in hetzelfde veld als anders de HTML.

Waarom die omweg nodig is, staat bij "Een site achter Cloudflare" hieronder: Cloudflare weigert de app
zelf op de vingerafdruk van zijn TLS-handdruk, en naar een API navigeren kan niet
omdat je dan geen kopregels kan meegeven. De brug is de enige weg die allebei
oplost — het is de echte Chrome van de gebruiker die de verbinding legt.

**Chrome moet draaien.** De extensie leeft in Chrome; staat die dicht, dan
vraagt niemand om werk en blijft een opdracht hangen tot ze afloopt. Daarom
kijkt de app voor elke zoekopdracht met een brugsite of de extensie zich de
laatste drie seconden gemeld heeft (`BridgeServer.ExtensionAlive` — anders dan
`ExtensionConnected`, dat blijft staan zodra ze zich ooit meldde). Zo niet, dan
start `ChromeLauncher` Chrome geminimaliseerd en wacht de app tot 30 seconden
tot de extensie zich meldt. Chrome zelf wordt gevonden via de registersleutel
`App Paths\chrome.exe`, met de gebruikelijke installatiemappen als terugval.

**Waarom de brug niet werkt, zegt `ChromeLauncher.EnsureBridgeAsync` met een reden**
(`BridgeStatus`) in plaats van waar of onwaar: `Ready`, `WrongCode` (de extensie meldt
zich, maar met een andere koppelcode), `NoExtension` (Chrome draait, de extensie zwijgt) of
`ChromeNotFound`. Dat onderscheid kwam uit een echt voorval: na het verhuizen van de
gegevensmap klopte de koppelcode niet meer, de app zei "de extensie meldde zich niet", en de
extensie zelf toonde "verbonden" - want die telde het foutantwoord van de app als contact.
Nu:

- `BridgeServer` onthoudt een verkeerde code (`WrongCodeRecently`) en logt ze eens per minuut;
- de extensie telt een foutantwoord niet als contact, en de popup kent vier toestanden
  (verbonden, verkeerde code, geen code, Zentrix draait niet);
- na "Code opslaan" vraagt de popup meteen `/ping`, dus je ziet bij het plakken of de code klopt;
- werkt de brug niet, dan **slaat de zoekopdracht de brugsites meteen over** met die reden,
  in plaats van per site 90 seconden te wachten;
- een opdracht waarop niemand meer wacht, slaat `/job` over, zodat Chrome geen tabbladen
  meer opent voor zoekopdrachten die allang voorbij zijn.

**Een verkeerde code telt enkel als ze van de extensie komt.** Elke webpagina in elke browser
op deze pc kan `127.0.0.1:8731` aanspreken. De koppelcode houdt haar buiten, maar tot september
2026 kon ze met een verzonnen code wel `WrongCodeRecently` aanzetten, en dan sloeg de app alle
brugsites over. Nu stuurt de extensie de kopregel `X-Zentrix-Brug` mee, en telt een foute code
enkel met die kopregel. Een webpagina kan zo'n eigen kopregel niet sturen zonder eerst
toestemming te vragen (een CORS-voorvraag), en die toestemming krijgt enkel nog een
`chrome-extension://`-herkomst: de brug antwoordde vroeger met `Access-Control-Allow-Origin: *`
en `Access-Control-Allow-Headers: *`. Nagemeten met verzoeken zoals een webpagina en de extensie
ze sturen. **Na deze wijziging moet de extensie herladen worden**; een oude extensie werkt
verder, maar een verkeerde code heet dan weer "de extensie meldt zich niet".

**De brug neemt niet alles aan** (22 september 2026, een tip uit een beoordeling door ChatGPT).
Tot dan las ze elke kop en elke body tot het einde, en reserveerde ze meteen de maat die een
verzoek aankondigde: `Content-Length: 1500000000` legde 1,5 GB vast nog voor er één byte binnen
was, en een verbinding die zweeg, bleef open tot Zentrix stopte. Nu, in `HandleClientAsync`:

- **De koppelcode wordt nagekeken voor de body.** Ze staat in het adres, dus dat kan. Wie de code
  niet kent - elke webpagina - krijgt nooit een body gelezen, hoe groot die ook zegt te zijn.
- **Hoogstens 128 MB per levering** (`MaxBodyBytes`), met de juiste code; daarboven een 413 en
  een regel in het logboek. De grootste levering in het logboek tot dan was een zoekpagina van
  Vinted van 8 miljoen tekens. Een kop van meer dan 64 kB krijgt een 431.
- **Tien seconden om een verzoek volledig te sturen** (`ReadTimeout`). De extensie doet het in een
  fractie van een seconde.
- **Hoogstens 32 verbindingen tegelijk** (`MaxConnections`), geteld bij het aannemen. De extensie
  heeft er een handvol open. Drukte en stilte staan hoogstens eens per minuut in het logboek.

De extensie hoeft daarvoor niet herladen te worden: ze leest het antwoord op `/result` niet.
Nagemeten met ruwe verzoeken in `BrugChecks`, en met de tegenproef: op de oude brug faalden de
zes grenzen, en een gewone levering ging bij allebei door.

Waarom dit werkt waar Playwright faalt: het is de echte browser van de
gebruiker, met zijn eigen geschiedenis en cookies, zonder
automatiseringsprotocol. Voor Datadome is dat niet te onderscheiden van een
gewone bezoeker. De server luistert alleen op 127.0.0.1 — bij een andere
gebruiker is dat zijn eigen machine en zijn eigen logins. Er komt niets
centraal samen.

Bekende beperking: de extensie scrolt maar één keer halverwege, waardoor
sites met lazy loading binnen één pagina te weinig resultaten geven. Meer
resultaten haal je nu vooral via de paginering (`PageTemplate`), niet via
langer scrollen — al kost elke extra pagina bij de brug wel een nieuwe ronde
van ~4 seconden.

## Werkende bronnen en hun eigenaardigheden

De eigenaardigheden **per site** - welke selectors, welke parameters, wat er bij het
inregelen misging - staan niet meer hier maar in `SITES.md` van de aparte repository
`zentrix-sites` (naast deze map: `..\zentrix-sites`). De app kent geen enkele site bij
naam; wat hieronder volgt, geldt voor elke site.

**Server-side zoekfilters.** Elke site-beschrijving heeft een `Filters`-mapping:
per bekende filternaam (`priceMin`, `priceMax`, `priceRangeCents`, `priceRangeEuro`,
`postcode`/`location`, `radius`, `radiusMeters`) een stukje URL met `{value}`, bv.
`radiusMeters` → `&distanceMeters={value}`. `SearchUrlBuilder` plakt alleen
de ingevulde filters aan de URL; een regel zonder `{value}` (bv. een vaste
sortering) komt er altijd bij. Dat geldt voor élke motor, dus ook voor Facebook.
Wat een site niet in zijn mapping heeft, filtert de app zelf. Een provincie is geen
vaste filternaam meer maar een eigen filter van de site, zie "Filters die maar op één
site bestaan".

**Zoeken met een lege zoekbalk** kan, maar enkel op de sites die het aankunnen
(`SiteDefinition.AllowsEmptyQuery`). Staan er meer sites aan, dan draaien enkel
die en zegt de statusregel welke overgeslagen zijn — anders lijkt het alsof die
sites niets gevonden hebben. Kan geen enkele aangevinkte site het, dan gebeurt er
niets en zegt de app wélke site een zoekterm nodig heeft. Dezelfde regel staat in
`SearchRunner`, want een zoekopdracht moet hetzelfde doen of je nu kijkt of niet.
Een lege term komt niet in *Recent* terecht: daar zou een leeg regeltje staan.

**Een veilinghuis dat op een gewone site adverteert, wordt overgeslagen.** Catawiki zet zijn
kavels als gewone zoekertjes op 2dehands en Marktplaats, met het huidige bod als "vaste prijs".
Gemeten op 17 september 2026 was dat bij "marantz" **25 van de 100** resultaten op 2dehands en
**32 van de 100** op Marktplaats; bij "cd speler" 23 en 8. Dezelfde kavels staan bovendien op de
veilingsite zelf, die je apart kan aanvinken. Wie ze in het sitebestand bij `AuctionSellers` zet
(samen met `SellerSelector`, zodat de app de verkoper kan lezen), ziet ze nergens meer terug: niet
in de resultaten, niet in de meldingen en niet in de prijsindicatie. Het logboek zegt per pagina
hoeveel er wegvielen. In *Sites beheren* staat het veld als "Veilinghuizen overslaan".

**Filters werken meteen op wat er al staat.** Er zijn twee soorten:

- De **prijs** kan de app zelf afdwingen. Die filter zit daarom in de
  weergave (`ZichtbaarInHuidigeTab`), niet in wat er binnenkomt: alle resultaten
  worden bewaard en er wordt enkel bepaald wat je te zien krijgt. Zo werkt de
  filter in twee richtingen — een ruimere prijs toont meteen weer meer, zonder
  opnieuw te zoeken. Vroeger werd er gefilterd bij het toevoegen, en dan was wat
  buiten de grens viel gewoon weg.

  **Maar buiten je prijsgrens bestaat niet voor de zoekopdracht** (22 september 2026,
  `SearchRunner.BinnenPrijs`): het telt niet mee in de teller "nieuw", het wordt niet
  bewaard bij de resultaten en het geldt niet als gezien. Zo koos de eigenaar het, en zo deed
  de planner het al. Het scherm deed het anders, en dat was te
  zien: bij Zoekopdrachten stond "12 nieuw" terwijl de schakelaar erna "Enkel nieuwe (8)" zei,
  en wat je nooit te zien kreeg, gold toch als bekeken - verruimde je later je prijs, dan was
  het niet meer nieuw. In het geheugen blijft alles staan, dus een ruimere prijs toont nog
  altijd meteen meer.

  Sinds de twee zoeklussen samengebracht zijn, is er nog één regel: die van de runner. Wat het
  scherm erover weet, is enkel welke zoekertjes het mag tónen
  (`MainWindow.HoortBijZoekopdracht`, gebruikt door `HoortInHuidigeTab`).

  Dit speelt enkel bij sites die zelf niet op prijs filteren: AlleVeilingen, Facebook en
  Kleinanzeigen. Bij de rest staat de grens in de zoek-URL en komt zo'n zoekertje niet binnen.

  Nagemeten met het hoofdscherm buiten beeld, op een proefsite met prijzen van € 1 tot € 20 en
  een grens van € 10: 20 gevonden, 10 in beeld, teller 10, bewaard 10, gezien 10, en met de
  grens weg weer 20 in beeld zonder opnieuw te zoeken. Op de oude code: teller 20, bewaard 20,
  gezien 20.
  **Wat je wijzigt terwijl een bewaarde zoekopdracht openstaat, gaat mee in die zoekopdracht**
  (22 september 2026, `MainWindow.NeemSchermfiltersOver`), en de statusregel zegt het: "De
  gewijzigde filters zijn bewaard in 'Cd speler'." Tot dan was dat een stil verschil tussen de
  twee zoeklussen: je eigen beurt zocht met de nieuwe waarde - en bewaarde de resultaten, de
  teller en het tijdstip onder die zoekopdracht - terwijl de volgende geplande beurt nog met de
  oude waarden liep. Stil bewaren is even verwarrend als stil vergeten, vandaar die regel in de
  statusregel; wie het ongedaan wil maken, gebruikt het tandwiel. Een site die in de zoekopdracht
  staat maar geen tab heeft (verwijderd of hernoemd) blijft staan, anders verdwijnt de
  waarschuwing van de planner. Nagemeten met het hoofdscherm buiten beeld: de grens van € 10 naar
  € 5 gezet, opnieuw gezocht, en de zoekopdracht in de databank stond op € 5 met die regel erbij;
  op de oude code bleef ze op € 10 staan.

- **Locatie, straal, provincie en het maximum** kan alleen de site zelf: die
  waarden zitten in de zoek-URL. Sluit je zo'n popup nadat je er iets aan
  gewijzigd hebt, dan start de app vanzelf een nieuwe zoekopdracht
  (`FilterPopup_Closed`), in plaats van je terug naar het vergrootglas te sturen.

## Motoren: hoe een site wordt uitgelezen

Er zijn geen ingebouwde bronnen meer — **elke site is een bestand**. Het veld
`Engine` bepaalt enkel wie dat bestand uitleest:

- `Generic` (0) — de standaard: leest de pagina met de selectors uit het bestand.
- `LinkText` (1) — de **linkmotor**, voor sites met versleutelde klassenamen die bij
  elke update veranderen. In plaats van een selector per veld volgt die de links naar
  zoekertjes (`ItemSelector`) en leidt prijs, titel en plaats af uit de tekstregels van
  zo'n link: de regel met een prijsteken is de prijs, de eerste andere regel de titel,
  de laatste de plaats. De andere selector-velden doen daar niets en worden in de
  instellingen verborgen. Facebook Marketplace gebruikt hem.

  Wat bij één site hoort, staat in het blok `LinkText` van het sitebestand:

  | Veld | Bij Facebook | Waarvoor |
  |---|---|---|
  | `IdPattern` | `/marketplace/item/(\d+)` | het id uit de link; een link die er niet op past, is geen zoekertje |
  | `UrlTemplate` | `https://www.facebook.com/marketplace/item/{id}/` | een nette link zonder de volgcodes die bij elke keer laden anders zijn — anders telt elk zoekertje bij elke beurt als nieuw |
  | `PriceMarkers` | `€`, `Gratis` | welke regel de prijs is |
  | `LocationPattern` | `,\s*(VLG\|WAL\|BRU)$` | staat het einde van een plaatsnaam in de titel en niet in de plaats, dan zijn die twee omgewisseld |
  | `LoginMarkers` | `log in to facebook`, `aanmelden bij facebook` | tekst waaraan je ziet dat je niet aangemeld bent |

  **Scrollen in plaats van pagina's** (22 september 2026). Facebook heeft geen volgende pagina;
  het laadt bij terwijl je naar beneden gaat. Met één keer scrollen bleven er 25 over, terwijl
  je in je eigen browser er veel meer ziet. Nu scrolt `BrowserFetcher.ScrolTotAsync` tot er 300
  staan (`ResultLimit` van de linkmotor), telkens door het **laatste zoekertje** in beeld te
  brengen - het muiswiel werkt op wat onder de muis staat, en de pagina heeft meer dan één deel
  dat schuift. Het stopt zodra een ronde niets bracht, en na hoogstens 20 rondes: dit gebeurt met
  je eigen aangemelde account, en eindeloos scrollen is wat Facebook als een robot ziet. Testen
  in Sites beheren vraagt 20, en scrolt dan niet.

  **Live gemeten op 23 september 2026** ("cd speler", een geplande beurt): `(126 zoekertjes na 7
  keer scrollen)`, goed voor **98 resultaten in 18,0 s**, waarvan 12,8 s scrollen. Daags ervoor,
  zonder scrollen: 25 resultaten in 4,3 s. Het stopte vanzelf op 126, ruim onder de rem van 300 -
  een ronde bracht niets meer, dus dat is wat Facebook daar heeft. Geen controlevraag van Facebook.
  De beurt erna meldde 106 nieuwe: alles voorbij die eerste 25 had de zoekopdracht nooit gezien.

  **Waarom niet elke kaart een zoekertje wordt**, zegt het logboek sinds 23 september 2026
  (`LinkTextSource.ReadPage`), want anders weet je enkel dat het scrollen 126 kaarten telde en de
  motor er 98 overhield:

  ```
  Facebook Marketplace: 32 kaarten -> 30 zoekertjes (0 dubbel, 2 zonder titel, 0 geen zoekertje)
  ```

  Drie redenen: hetzelfde zoekertje staat twee keer op de pagina, de kaart heeft geen titel, of de
  link is geen zoekertje (bijvoorbeeld "Iets te koop aanbieden"). Nagemeten op een bewaarde pagina
  van 15 september: 32 kaarten, 30 zoekertjes, en die twee hebben **echt geen titel** - ook het
  `alt` van de foto (`" in Hemiksem, VLG"`) en het `aria-label` (`", € 80, Hemiksem, VLG, …"`)
  beginnen leeg. De verkoper vulde er geen in. **Zo'n kaart valt weg**, ook al heeft ze een foto,
  een prijs en een plaats; zo koos de eigenaar het op 23 september 2026.

  **Live nagemeten op 24 september 2026**, en het antwoord is een ander dan verwacht. De regel
  stond intussen twee keer in het logboek, allebei bij een echte beurt:

  | Beurt | Kaarten -> zoekertjes | Weg |
  |---|---|---|
  | 23 sept 19:56 | 78 -> 75 | 3 zonder titel, **0 dubbel** |
  | 24 sept 16:50 | 102 -> 101 | 1 zonder titel, **0 dubbel** |

  De motor laat dus vrijwel niets vallen, en dubbels bestaan er niet. **Het gat zit ergens
  anders**: het scrollen telde bij allebei **102** zoekertjes, en de motor kreeg er de ene keer
  102 te lezen en de andere keer maar 78. Dezelfde selector (`ScrolTotAsync` krijgt de
  `ItemSelector` mee als `waitSelector`), dezelfde pagina, twee momenten: tijdens het scrollen en
  wanneer de HTML opgehaald wordt. Tussen die twee verdwenen er 24, en op 22 september 28.
  Facebook ruimt zijn kaarten blijkbaar op naarmate je verder scrolt, en dan lees je een pagina
  waar een deel al weg is. Eén van de drie keer ging het wél goed, dus het is geen vaste regel -
  en dat maakt het lastiger te vangen.

  **Daarom telt de app het nu twee keer** (24 september 2026, `BrowserFetcher.GetHtmlAsync`): één
  keer zoals altijd tijdens het scrollen, en één keer vlak voor de pagina opgehaald wordt.
  Allebei in dezelfde zin in het logboek:

  ```
  browser: ... scrollen 2039ms (50 zoekertjes na 4 keer scrollen, 50 bij het ophalen) uitlezen 3ms
  ```

  Samen met de regel van de motor (`kaarten -> zoekertjes`) zijn er zo drie meetpunten op één
  rij, en die zeggen elk iets anders:

  | Wat je ziet | Waar de kaarten bleven |
  |---|---|
  | scrollen 102, ophalen 102, kaarten 78 | in het ophalen of in het uitlezen, niet in de pagina |
  | scrollen 102, ophalen 78, kaarten 78 | uit de pagina: de site ruimt op wat je voorbij gescrold bent |

  De telling gebeurt enkel bij een site die scrolt - ze kost een heen-en-weer naar de browser, en
  de andere sites hebben er niets aan. Nagemeten met een wegwerpprojectje en een lokale pagina die
  per scroll tien zoekertjes bijlaadt: 50 na 4 rondes, 50 bij het ophalen, en 50 in de opgehaalde
  HTML. Een pagina die opruimt in precies dat ene ogenblik tussen de telling en het ophalen is
  lokaal niet na te bootsen; daarvoor is de eerstvolgende echte Facebook-beurt nodig.

  Tot september 2026 heette dit de Facebook-motor (`FacebookSource.cs`), met al die
  waarden in de code. Na de keuzelijsten van AlleVeilingen was dat de laatste plek waar
  de app een site bij naam kende. Het nummer is gebleven, dus `"Engine": 1` blijft
  werken.

  **Nagemeten op exact dezelfde HTML.** De Facebook-pagina voor "fiets" is één keer
  opgehaald en bewaard, en daarna gelezen door de oude code en door de linkmotor: 25
  zoekertjes, en id, link, titel, prijstekst, prijs, plaats, foto en grote foto waren
  bij alle 25 **identiek**. Een pagina bewaren en twee keer lezen vergelijkt beter dan
  twee keer zoeken: tussen twee zoekopdrachten verschuift de voorraad, en dan weet je
  niet of een verschil aan de code ligt. Daarvoor heeft de motor `ReadPage`.

  **De linkmotor leest de losse tekststukken van een kaart** (`Regels`), niet de
  aaneengeplakte tekst. Facebook zet elk stuk in een eigen element: `Zojuist geplaatst | € 50 |
  € 70 | Meisjes fiets 26 | Kortrijk, VLG`. De oude manier knipte de samengeplakte tekst op
  hoofdletters, en gaf prijslabels als "Zojuist geplaatst€ 300", plaatsen als "GB
  SSDRoeselare, VLG", en liet een kaart als "€ 1.234Giant fiets Ronse, VLG" helemaal vallen.
  Nu is de prijs een regel die enkel een prijs is; wat ervoor staat is een label van de site,
  een tweede prijs erna de oude, doorgestreepte. Op dezelfde bewaarde pagina voor "fiets": de
  oude manier 25 zoekertjes, de nieuwe 30, alle met een schone titel, prijs en plaats (de
  twee kaarten zonder titel vallen nog altijd weg). Staat alles in één tekststuk, dan geldt
  de oude manier nog.

- **Een vast id uit de link** (`IdPattern`, voor de gewone motor). De link is wat een
  zoekertje uniek maakt, maar AlleVeilingen zet het paginanummer in de link en Catawiki de
  zoekterm. Een kavel die van pagina 2 naar 3 schoof, was zo "nieuw": 14 van de 140 kavels
  stonden er twee keer in. Met `"IdPattern": "/kavel/(\\d+)"` is de eerste groep de
  identiteit; de link zelf blijft volledig. Een ongeldig patroon valt terug op de link.

  **Bestaande sleutels gaan mee** (`HistoryStore.ApplyIdPatterns`), bij het opstarten en na
  Sites beheren of een import: "al gezien", favorieten en bewaarde resultaten.
  Zonder dat telt elke kavel één keer opnieuw als nieuw, met een melding over honderd
  zoekertjes die je al kende. Het mag elke keer draaien: een sleutel die al een id is, past
  niet meer op het patroon. Droog getest op een kopie van de echte databank: AlleVeilingen
  van 164 naar 149 sleutels, Catawiki 334 gebleven, 833 rijen omgezet, een tweede keer 0.

`SourceFactory` maakt op basis van dat veld de juiste bron. Heeft een nieuwe site een
opbouw die met geen van beide motoren te beschrijven is, dan is de weg dus: een motor
toevoegen aan `SiteEngine` en aan de fabriek — niet een nieuwe ingebouwde bron naast het
systeem. En zet wat bij die ene site hoort in zijn bestand, niet in de motor.

### URL-stijlen

Los daarvan bepaalt `UrlStyle` hoe de zoek-URL *gebouwd* wordt. Dat is een andere
laag dan de motor: het uitlezen kan gewoon met selectors terwijl de URL bijzonder is.

- `Standard` (0) — `{query}` in de URL, filters en paginanummer erachter geplakt.
  Staat `{page}` in de zoek-URL zelf, dan komt het paginanummer daar in plaats van
  `PageTemplate` achteraan.
- `Base64Json` (1) — de hele zoekopdracht zit als JSON in één base64-parameter. De
  JSON staat gewoon in de zoek-URL, na het isgelijkteken, met `{query}`, `{page}` en
  `{filters}` erin; de app vult hem in en versleutelt hem. AlleVeilingen werkt zo:
  `?fi={"s":"{query}",…,"pg":{page},…,"Type":2{filters}}`. Losse parameters aan de
  URL plakken wordt daar genegeerd.

  Drie dingen gaan in zo'n JSON-blok anders dan in een gewone URL. Waarden worden
  als JSON-tekst ontsnapt en niet als URL, want de URL-versleuteling komt pas na de
  base64. Bij meerdere keuzes gebeurt dat per keuze, zodat een scheiding als `","`
  zelf blijft staan. En `{filters}` staat achter de laatste vaste eigenschap: de
  komma ervoor zet de app er enkel bij als er ook echt filters zijn.

  **Niets gekozen is niet hetzelfde als niets vermelden.** Een filter mag een
  `EmptyFragment` hebben: wat er in de URL komt wanneer er niets gekozen is. Bij
  AlleVeilingen bleek dat nodig, en dat was enkel met een meting te zien. Zonder
  `"r":[]` in het blok gaf "stoel" **0 van de 30** dezelfde kavels als de oude code,
  die die lege lijst altijd meestuurde; met een gekozen provincie waren het er 29 van
  de 30. Daarom staat bij de provincies `"r":[]` als `EmptyFragment` en bij de
  veilinghuizen `"a":null` - precies wat de site zelf stuurt. Na die rechtzetting:

  | combinatie op "stoel" | zelfde kavels als met de oude code |
  |---|---|
  | niets gekozen | 27 van 30 |
  | West-Vlaanderen | 29 van 30 |
  | veilinghuis Vavato | 30 van 30 |
  | twee provincies en twee huizen | 24 van 30 |

  De verschillen zijn kavels die in de dag tussen beide metingen bijkwamen of
  afliepen. Dat de filters echt iets doen, blijkt uit de andere richting: elke
  combinatie had 0 van de 30 kavels gemeen met de zoekopdracht zonder filter.

  Wie een site met een eigen URL-vorm omzet, meet dus op deze manier: vóór de
  wijziging per combinatie de links van de eerste pagina bewaren, erna opnieuw, en
  tellen hoeveel er overeenkomen.

  Deze stijl heette tot september 2026 `AlleVeilingen`, met het JSON-blok in de code
  en de keuzelijsten in `SiteChoices.cs`. Het nummer is gebleven, zodat een
  sitebestand met `"UrlStyle": 1` gewoon blijft werken.

**Waar `{query}` staat, bepaalt hoe hij versleuteld wordt.** Staat de
plaatshouder vóór het vraagteken — dus in het pad — dan blijft een schuine streep
een scheidingsteken tussen padstukken. Dat is nodig sinds AutoScout24: die zoekt
met `/lst/bmw/x5`, en versleuteld wordt dat `/lst/bmw%2Fx5` waarop de site met
404 antwoordt. Alle andere sites zetten hun zoekterm in de querystring, en daar
wordt alles gewoon versleuteld. Zie `EncodeQuery`.

**Staat `{query}` tussen aanhalingstekens** (`"query":"{query}"`), dan zit hij in een JSON-blok
in de URL, zoals bij de GraphQL-API van Discogs. Dan wordt hij eerst als JSON-tekst ontsnapt en
pas daarna als URL. Anders maakte een aanhalingsteken in de zoekterm - `12"` is gewoon op
Discogs - de JSON ongeldig.

`SearchUrlBuilder` kent beide stijlen, plus `Supports(def, key)` — daarmee weet het
hoofdscherm welke filters het moet tonen of verbergen zonder ergens een lijstje bij
te houden.

### Een API die zich voor de gek laat houden

Tel niet enkel het totaal dat een API teruggeeft, en kijk of hij je filter echt begreep.
Bij de `lrp`-API van 2dehands en Marktplaats telde het totaal de zoekterm en niet de
filter, en werd een verkeerd gevormde filter aanvaard maar genegeerd. Het volledige
verhaal staat in `SITES.md` van `zentrix-sites`.

### Eigen kopregels per site

`Headers` in het sitebestand zet extra kopregels op het verzoek. De meeste sites
hebben er geen nodig; een API wel. De GraphQL-API van Discogs weigert elk verzoek
zonder `x-apollo-operation-name` met een CSRF-fout. Zet daar enkel in wat de site
vraagt en nooit iets dat geheim moet blijven — een sitebestand is bedoeld om te
delen. Een kopregel die .NET niet aanvaardt wordt overgeslagen en belandt in het
logboek, zodat één fout adres de zoekopdracht niet laat vallen.

**Een link opent enkel als het een webadres is** (`OpenSelected`, `AlsWebadres`). De link van
een zoekertje komt uit de pagina of uit het sitebestand (`UrlTemplate` bij de linkmotor), en
een sitebestand kan van een vreemde komen. Windows opent met `UseShellExecute` alles wat je
het geeft, ook een programma op schijf of op een netwerkmap. Enkel `http` en `https` gaan
door; de rest meldt de statusregel en het logboek.

### Filters die maar op één site bestaan

De vaste filters — prijs, locatie, aantal, volgorde — kent de app van binnenuit,
want die betekenen overal hetzelfde. Maar AutoScout24 heeft brandstof,
kilometerstand, carrosserie, euronorm en nog een pak meer, en een veilingsite
heeft daar niets aan. Zulke filters horen dus **bij de site en niet in de code**.

In het sitebestand staat daarvoor `CustomFilters`, een lijst van
`CustomFilter`-blokjes:

```json
{ "Key": "fuel", "Label": "Brandstof", "Kind": "Choice", "Multiple": true,
  "Fragment": "&fuel={value}",
  "Options": [ { "Value": "B", "Label": "Benzine" },
               { "Value": "D", "Label": "Diesel" } ] }
```

Drie soorten volstaan: **Choice** (een keuze uit een lijst, met `Multiple` voor
meerdere tegelijk), **Number** (een vrij getal) en **Toggle** (aan of uit).
`Fragment` werkt net als bij de vaste filters: het stukje URL met `{value}` erin.
De knop met de schuifregelaars staat uit bij sites zonder eigen filters.

De popup erachter wordt **in code opgebouwd** (`BuildSpecialFilters`) en niet in
de XAML, want elke site heeft iets anders. Wie er een filter bij wil, zet een
regel in een JSON-bestand — er hoeft niets aan de app te veranderen. De waarden
gaan mee in een bewaarde zoekopdracht (`SiteSetting.Custom`) en dus ook in de
planner, en ze tellen mee in `SiteFilterFingerprint`: wijzig je de brandstof, dan
gaat de app opnieuw zoeken in plaats van enkel de lijst te herschikken.

**Filters die samen in één blok horen.** Sommige sites aanvaarden geen losse
parameters. Catawiki propt zijn hele filterset in één parameter `filters`, met een
versleutelde ampersand ertussen; twee losse `filters=`-parameters worden gewoon
genegeerd (nagemeten: dan krijg je het ongefilterde aantal terug). Discogs verwacht
zijn filters binnen het JSON-blok `variables` van zijn GraphQL-API.

Daarvoor staat de plaatshouder **`{filters}`** in de zoek-URL: staat die er, dan
worden alle fragmenten daar tussengezet in plaats van achteraan geplakt. Waarmee ze
aan elkaar geplakt worden zegt **`FilterJoin`** (standaard een komma; Catawiki
gebruikt `%26`). Niets aangevinkt geeft een leeg blok, en dat is bij allebei
onschadelijk — nagemeten.

**De scheiding tussen meerdere waarden moet ONVERSLEUTELD in het bestand.**
`SearchUrlBuilder` versleutelt de samengevoegde waarde als geheel, dus een `&` wordt
vanzelf `%26` en `[]` wordt `%5B%5D`. Zet je de versleutelde vorm in het bestand, dan
wordt het procentteken nóg eens versleuteld (`%2526`) en negeert de site je filter.
Daar zijn we bij Catawiki ingetrapt: de URL zag er bijna goed uit, en het aantal
klopte niet. Bij eBay werkt dezelfde regel de goede kant op: scheiding `|` wordt
`%7C`, en dat is precies wat eBay wil.

**Enum-namen mogen in een sitebestand.** `SiteStore` leest en schrijft met een
`JsonStringEnumConverter`, zodat `"Kind": "Choice"` en `"Kind": 0` allebei werken.
Een sitebestand is bedoeld om met de hand te bewerken en te delen, en een nummer
zegt niemand iets. Ging dat mis, dan **verdween de hele site geruisloos** uit de
rij — de `catch` bij het inlezen slikte alles. Die schrijft nu naar het logboek.

**Filters met meerdere keuzes.** Een `Choice` met `Multiple` wordt een reeks vinkjes.
De provincies en veilinghuizen van AlleVeilingen zijn zo gewone filters in
`alleveilingen.json`, met als fragment `"r":["{value}"]` en als scheiding `","` — zie
`Base64Json` bij de URL-stijlen. Tot september 2026 stonden die lijsten vast in de
code (`SiteChoices.cs`), met eigen velden in `SearchFilters`, `SiteTab`, `SiteSetting` en
`SiteEditor` en eigen vinkjes op drie schermen. Dat was de laatste plek waar de app een
site bij naam kende, en die moest weg om de app los van de sites op GitHub te zetten.

**Waar een filter staat** zegt `Section`: standaard `Site`, in de popup met de
schuifregelaars; `Location` zet het in de popup voor locatie en afstand. De provincies
staan daar, want die zoek je waar je naar een plaats zoekt — en een site zonder postcode
of straal kan er zo toch mee op locatie zoeken.

De invoer voor die filters bouwt `CustomFilterControls`, voor het zoekscherm én voor het
instellingenvenster van een zoekopdracht. Dat laatste kende de sitegebonden filters eerst
helemaal niet, en dat was een echte fout: `SiteEditor.ToSetting` nam ze niet mee, dus
**Bewaren in dat venster wiste de eigen filters van elke site** in die zoekopdracht. Nu
staan ze er, en blijven ze. De popups worden bij het openen opnieuw opgebouwd, zodat ze
tonen wat een bewaarde zoekopdracht er intussen in zette.

Wat een site **niet** kent, wordt in de popup verborgen in plaats van gedimd: een
uitgegrijsd invoerveld ziet eruit als iets dat stuk is. AlleVeilingen toont dus
enkel provincies, 2dehands enkel postcode en straal.

Dat gold een tijd ook voor de **knop** met het gebouwtje (veilinghuizen): die
verscheen enkel bij AlleVeilingen en was elders verborgen. Dat is nog een stap
verder getrokken — **de knop is helemaal weg**. Een pictogram dat maar op één van de
negen sites iets doet, verdient geen vaste plaats in een rij die je overal ziet. De
veilinghuizen staan nu gewoon bij de andere filters van die site, achter de
schuifregelaars, samen met alles wat alleen daar bestaat.

De vaste filters — locatie, prijs, aantal per pagina, volgorde — blijven wel altijd
staan en dimmen enkel: die betekenen overal hetzelfde, en een rij knoppen die per tab
van lengte verspringt leest onrustig.

## Een site achter Cloudflare

Cloudflare kan de app weigeren op de vingerafdruk van zijn TLS-handdruk: .NET en een
headless Playwright krijgen "Just a moment...", een echte browser niet. Dan is de brug
de weg, en voor een API de tweede soort brugopdracht (`rawText`, zie "Twee soorten
opdrachten"). Discogs is daar het voorbeeld van; het hele verhaal, met wat we
probeerden en waarom het niet lukte, staat in `SITES.md` van `zentrix-sites`.

## Sites toevoegen

De gebruiker plakt een gewone zoek-URL met het gezochte woord erin. De app
zet dat woord om naar `{query}` (probeert `%20`, `+` en de kale vorm). Daarna
haalt hij de pagina op en laat Claude bepalen waar titel, prijs, plaats, link
en foto staan. De gevonden selectors komen in bewerkbare velden zodat de
gebruiker kan bijsturen, met een testknop die meteen echte resultaten toont.

### Hoe de AI-analyse werkt

`SiteAnalyzer` is bijgewerkt met wat het inregelen van de negen sites opleverde. Ze
vraagt niet langer "wat zijn de selectors" om het antwoord daarna te geloven; ze
**meet**.

**1. De weg wordt gemeten, niet gevraagd.** Eerst een gewoon verzoek. Staat het
zoekwoord daar minstens drie keer in de zichtbare tekst, of is het JSON, dan is dat de
weg. Anders een browser, en pas wanneer die op een controlepagina stuit de brug. Welke
weg lukte, bepaalt `NeedsBrowser` en `UseBridge`. Vroeger ging alles via de browser of
de brug, en kreeg élke nieuwe site "browser nodig" mee — ook een API die in een halve
seconde antwoordt. Het vinkje "Meteen via mijn eigen Chrome" slaat de eerste twee over,
voor een site waarvan je al weet dat ze blokkeert. Een JSON-API die in een tabblad als
`<pre>` verschijnt, wordt als JSON herkend (`FromPre`).

**2. De pagina wordt opgeschoond voor ze vertrekt.** Scripts, stijlen, svg, commentaar
en lange attribuutwaarden gaan eruit; wat overblijft is de opbouw met klassen en
attributen. Past het dan nog niet, dan gaat het stuk mee waar het zoekwoord het dichtst
op elkaar staat, want daar zit de lijst. Vroeger werd rond het tweede voorkomen van het
woord geknipt, en dat landde vaak in het menu of in een script. Bij JSON worden de
lijsten ingekort tot vier elementen. Uit de scripts worden eerst **sporen van een API**
gevist (`/api/`, `graphql`, `__NEXT_DATA__`), zodat Claude die in de notities kan
noemen — een API is stabieler dan HTML, zie 2dehands en Discogs.

**3. Het antwoord wordt nageteld met de echte motor.** `GenericSource.ReadPageAsync` en
`CountItems` voeren het voorstel uit op de volledige pagina, niet op het ingekorte stuk.
Te weinig zoekertjes, links die niet uniek zijn, weinig prijzen of foto's, een
`::replace` die niets vervangt: dan gaat die telling terug naar Claude, met de eerste
drie zoekertjes zoals de app ze leest, en mag hij verbeteren. Hoogstens drie rondes, en
er wordt gestopt zodra een ronde niets bijbrengt — dan ligt het aan de pagina (een
veilingsite zonder prijs) en kost opnieuw vragen enkel geld. Het venster toont die
telling boven de velden. Dit is exact wat we met de hand deden bij AutoScout24 (18 van
de 20 plaatsen) en Discogs (73 van de 100 zoekertjes).

**4. De prompt kent de motor.** `SystemPrompt` beschrijft wat de motor werkelijk kan:
`@attribuut`, `.`, komma-lijsten, `::replace`, `::match`, de terugval tussen `src` en `data-src`,
hoe een prijs uit tekst gehaald wordt, waarom de link de identiteit is, stabiele
selectors boven klassen met willekeurige achtervoegsels, grote foto's via een formaat in
het pad, geen AVIF, puntpaden in JSON en `PageTemplate`. **Leer je bij een site iets dat
voor élke site geldt, zet het dan ook daar**, anders weet de analyse het niet.

De AI vult nu ook de **korte naam**, de **grote foto** en de **volgende pagina** in, en
het venster toont die als bewerkbare velden.

Gemeten op "cd":

| Site | Weg | Tijd | Rondes | Resultaat |
|---|---|---|---|---|
| Kleinanzeigen (nieuw) | rechtstreeks | 33 s | 1 | 27 zoekertjes, 24 met prijs; grote foto zelf gevonden (`$_2.AUTO` → `$_59.AUTO`) |
| eBay (bekend) | browser, want rechtstreeks gaf 403 | 34 s | 1 | 62 zoekertjes; dezelfde selectors als het bestaande bestand, `::replace(s-l500…)` inbegrepen |

Voor wie eraan werkt:

- Model `claude-opus-5`, via gewone `HttpClient`, met **gestructureerde uitvoer**
  (`output_config.format` met een JSON-schema). Vroeger werd er met een reguliere
  expressie een JSON-blok uit de tekst gevist.
- **Het antwoord begint met een blok nadenken.** `content[0]` lezen, zoals vroeger, geeft
  dan een lege tekst. Lees de blokken van het type `text`.
- Bij een verbeterronde gaat het vorige antwoord **ongewijzigd** terug, nadenken
  inbegrepen. Nagemeten dat de API dat aanvaardt; de pagina komt dan uit de cache.
- `fallbacks: "default"` met de kopregel `anthropic-beta: server-side-fallback-2026-07-01`:
  weigert het model een pagina, dan neemt de API zelf een ander model.
- Tijdslimiet vijf minuten. De oude zestig seconden liep met nadenken erbij in zijn limiet.
- Het logboek toont per verzoek vier tellers: invoer, naar cache, uit cache, uitvoer. Bij
  een eerste verzoek staat bijna alles onder "naar cache" — zonder die teller lijkt het
  gratis.
- **Wat een analyse kost.** HTML telt ongeveer twee tekens per token, niet vier. Een
  volle pagina van 150 000 tekens (`MaxHtmlChars`) is zo'n 75 000 tokens. Gemeten bij
  Kleinanzeigen: 73 526 tokens naar de cache en 2 068 uitvoer, **ongeveer 50 cent**. Een
  verbeterronde leest de pagina uit de cache (een tiende van de prijs) en betaalt vooral
  het nieuwe antwoord. Wil het goedkoper, dan is `MaxHtmlChars` de knop — maar meet dan
  of de resultatenlijst nog volledig meegaat.
- De laatst geanalyseerde pagina staat in `%APPDATA%\Zentrix\laatste-analyse.html` (of
  `.json`). Dat bestand kwam vroeger op het bureaublad terecht.
- De Services-map heeft geen globale `using System.IO` in dit WPF-project: schrijf die
  zelf bovenaan, of `Path` en `File` bestaan niet.
- **Zonder API-sleutel** toont het venster bovenaan meteen een veld om de sleutel te
  plakken. Die gaat naar de omgevingsvariabele `ANTHROPIC_API_KEY` van het Windows-account
  én van het lopende proces, zodat herstarten niet nodig is. Vroeger kwam de melding pas na
  het klikken op Analyseren, met de raad een omgevingsvariabele te zetten. Weigert de API de
  sleutel (401), dan verschijnt dat veld opnieuw; daarvoor kon je een verkeerde sleutel enkel
  in de omgevingsvariabelen van Windows vervangen. Het veld zegt ook waar je een sleutel maakt,
  en dat de volledige zoekpagina naar Claude gaat.
- **Aanmelden bij een site** kan op de kaart van die site: tandwiel > Sites beheren > tab van de
  site > Aanmelden (`SettingsWindow.LoginButton_Click`), enkel bij sites die de browser van de app
  gebruiken. Dat stond vroeger enkel in "Site toevoegen", en dan maakte je een lege nieuwe
  site aan om je bij een bestaande aan te melden.

Wat de analyse **niet** doet, en waar je dus zelf aan moet: `Filters`, `CustomFilters`,
`Headers`, `AllowsEmptyQuery`, de velden voor de prijsindicatie (`PriceReference`, `IsAuction`,
`SellerSelector`, `AuctionSellers`), `TimeLeftSelector`, `DetailEndDateSelector`,
`DetailImagesSelector`, `DetailSellerSelector`, `DetailPostedSelector`, `EndTimeApi`,
een eigen `UrlStyle`
en paginering die in het pad zit
(Kleinanzeigen: `/s-seite:2/cd/k0`). Dat vraagt meten, zie `tools/meet-filter.py`.

Selectors gebruiken een eigen notatie: `a.title@href` neemt een attribuut in
plaats van tekst, `.` betekent het resultaat zelf. Bij JSON-bronnen zijn het
puntpaden zoals `priceInfo.priceCents`.

**`::replace(oud,nieuw)`** vervangt achteraf iets in de gevonden waarde. Dat
lijkt op een CSS-pseudo-element maar bestaat daar niet, dus het botst nergens
mee; het werkt zowel bij HTML als bij JSON, en meerdere regels achter elkaar mag.

Waarvoor het dient: verschillende sites zetten het **formaat van een foto in het
pad van de URL**. De zoekpagina toont een miniatuur, maar dezelfde URL met een
ander stukje erin geeft de grote versie. Zonder deze regel konden de selectors
enkel een attribuut uitlezen en was die grote foto onbereikbaar — vier sites
hadden daardoor geen grote foto:

| Site | miniatuur | groot |
|---|---|---|
| AutoScout24 | `250x188` (8 kB) | `1024x768` (124 kB) |
| eBay | `s-l500` (37 kB) | `s-l1600` (200 kB) |
| Catawiki | `cw_lot_card_ext` (66 kB) | `cw_large` (365 kB) |
| AlleVeilingen | `_S.webp` (16 kB) | `_L.webp` (217 kB) |

Bijvoorbeeld:
`img[data-testid='list-item-image']@src::replace(250x188,1024x768)`

Let op dat het stukje dat je vervangt uniek genoeg is. Bij AlleVeilingen is `_S`
te kort — dat komt ook elders in het pad voor — dus vervangen we `_S.webp`.

**`::match(patroon)`** houdt enkel over wat in **groep 1** van dat patroon staat - de eerste
haakjes. Zonder haakjes blijft de hele treffer over, en past het patroon niet, dan blijft het veld
**leeg**. Ook dit werkt bij HTML en bij JSON, en het mag samen met `::replace` (eerst vervangen, dan
knippen).

Waarvoor het dient: soms staat het gezochte middenin een langere tekst, en is er geen apart element
voor. Een vervangregel helpt daar niet, want er staat elke keer iets anders:

| In de pagina | Selector | Resultaat |
|---|---|---|
| `Rijksweg 2, 9681 Maarkedal, België` | `p::match((?:^\|,)\s*(?:\d{4,6}\s+)?([^,]+?)\s*,\s*[^,]+$)` | `Maarkedal` |
| `van Nederland` | `.rij:last-child::match(^(?:van\|uit)\s+(.+)$)` | `Nederland` |

Dat "leeg als het niet past" is de halve reden om het te gebruiken: bij eBay staan de verzendkosten
en het land in rijen die er hetzelfde uitzien, en zo pik je er precies één soort uit.

**En met een patroon neemt de motor het eerste element waar dat patroon ook echt op past**, in
plaats van botweg het eerste dat de selector vindt (zonder patroon blijft het het eerste, zoals in
CSS). Dat bleek nodig op een kavelpagina van AlleVeilingen: `div[title='Einddatum']` staat er
**twee keer**, en de eerste bevat het kavelnummer - een foutje in hun HTML. De datum kwam dus nooit
binnen. Zo hoef je daar geen bange selector als `:last-child` of een rij buren voor te verzinnen,
die bij de volgende opmaakwijziging omvalt.

Twee dingen om te onthouden. Een patroon mag komma's en haakjes bevatten, dus `::match` loopt tot
het **laatste** haakje en staat dus altijd achteraan. En in een sitebestand is het JSON: een
backslash schrijf je er dubbel (`\\s`, `\\d`). Een ongeldig patroon laat de zoekopdracht niet
vallen - de waarde blijft dan zoals ze was, en het logboek zegt het één keer.

Als de AI geen bruikbare selectors vindt, is de beproefde werkwijze: de
pagina in Chrome inspecteren en de juiste klassenaam zelf opzoeken. Zo zijn
eBay, Facebook en Leboncoin opgelost. Let op selectors met willekeurige
achtervoegsels (`styles_adCard__9GEgr`) — die veranderen bij elke update;
kies liever `data-`attributen of `aria-label`.

### Werkwijze voor een nieuwe site

1. **Zoek eerst op de site zelf**, met een woord dat herkenbaar in de URL
   terechtkomt. Vermijd woorden met spaties: het omzetten naar `{query}` wordt dan
   onbetrouwbaar. Neem een **gewoon** woord ("cd", "dvd", "computer", "fiets") en
   geen merknaam: een site die niets te bieden heeft voor jouw nichewoord lijkt
   stuk terwijl er niets mis is. Plak die URL in de app.
2. **Kijk of de site een JSON-API heeft.** F12 → tabblad Network → filter
   Fetch/XHR → opnieuw zoeken. Levert een verzoek JSON met de zoekertjes op,
   gebruik dan díe URL met `Kind: Json`. Puntpaden breken bijna nooit, CSS-
   klassen wel. Dit is precies waarom 2dehands (API) stabieler en sneller is
   dan Marktplaats (HTML) — dezelfde site, andere weg.
3. **Begin zonder browser en zonder brug.** Werkt dat niet, zet "Browser
   gebruiken" aan. Pas bij een blokkade (Datadome, "Access denied") de brug —
   die kost ~4 seconden per pagina.
4. **Controleer de selectors zelf** in plaats van de AI te geloven. In de
   console van de site:
   `document.querySelectorAll("li article[aria-label]").length`
   Komt dat getal overeen met wat je op het scherm ziet, dan klopt de
   `ItemSelector`.
5. **Test per site** in de instellingen; die knop toont meteen echte
   resultaten met prijs en plaats.
6. **Vul de zoekfilters in** door ze op de site zelf toe te passen en te kijken
   wat er in de URL verandert. Zo vonden we `_udlo`/`_udhi` (eBay) en
   `postcode` + `distanceMeters` (2dehands). Let op eenheden: die laatste is in
   meters, vandaar de filternaam `radiusMeters`.
7. **Vul de paginering in** op dezelfde manier: klik op pagina 2 en kijk wat er
   in de URL verandert (`PageTemplate`, bv. `&page={page}`).
7b. **Heeft de site eigen filters?** Zet ze in `CustomFilters`. Meet elke
   parameter na met een telling, en stuur er één onzin-parameter bij als
   controle: verandert die het aantal ook, dan meet je iets anders dan je denkt.
   Meet ook wat **meerdere waarden** doen — dat is per filter anders. Bij
   AutoScout24 tellen `fuel`, `gear`, `body`, `bcol` en `ustate` netjes op (een
   OF), maar `eq=20,23` gaf 253 tegenover 266 voor `20` alleen (een EN), en
   `emclass=5,6` gaf nul, want een wagen heeft maar één euronorm. Dat laatste
   moet dus een enkele keuze zijn. Staan de keuzelijsten ergens in de pagina —
   bij AutoScout24 in `props.pageProps.taxonomy` — neem dan die waarden én die
   labels over; dan verzin je niets en staat het in de taal van de site.
7c. **Is de naam lang?** Zet dan een `ShortName` voor de tabstrip, bv. "Facebook"
   voor "Facebook Marketplace". Vanaf een stuk of twaalf tekens gaat het tellen;
   daaronder laat je het leeg en blijft de volledige naam staan.
8. **Exporteer de site** als hij goed staat, zodat je hem kan bewaren of delen.

Valkuilen die we in de praktijk tegenkwamen:

- **Niet elke site kent een vrije tekstzoekfunctie.** AutoScout24 zoekt enkel op
  merk en model, in het pad van de URL. Voor je daar iets op bouwt: probeer de
  voor de hand liggende parameters uit en tel de resultaten. Bij AutoScout24
  gaven `q`, `query`, `keyword`, `search`, `fulltext`, `designation`,
  `modelversion` en `mv` alle acht hetzelfde ongefilterde totaal (121907), en dat
  is het bewijs dat er geen trefwoordzoekfunctie is. Zet dat dan in de `Notes`,
  want de gebruiker moet weten dat "commodore" daar niets oplevert.
- **Een lege zoekterm is soms de bedoeling.** Op AutoScout24 is het zoekwoord het
  merk, dus wie niet merkgebonden wil zoeken laat het weg en stelt enkel filters
  in — dat is daar de gewone manier van werken en niet een randgeval. Zulke sites
  krijgen `AllowsEmptyQuery` in hun bestand (een vinkje "Mag zonder zoekterm" in
  de instellingen). Let op de **schuine streep**: staat `{query}` in het pad, dan
  blijft er bij een lege term een streep te veel staan, en `/nl/lst/?atype=C`
  antwoordt met een 308 naar `/nl/lst?atype=C`. `SearchUrlBuilder.TrimLegePadstuk`
  haalt die er zelf af. Gemeten zonder merk: 121851 wagens, met `fuel=D` en een
  prijsvork van 5000 tot 8000 nog 4292 — de filters werken dus gewoon door.
- **Een link die er niet staat.** Bij AutoScout24 heeft de titel-link in de
  server-HTML geen `href`; JavaScript zet die er pas in. Kijk dan of het resultaat
  zelf een id draagt (`data-guid`) en of een URL met enkel dat id doorverwijst —
  bij AutoScout24 geeft dat een 308 naar het juiste zoekertje, dus `BaseUrl` plus
  `.@data-guid` volstaat en er is geen browser nodig.
- **Eén veld, twee selectors.** Sites tonen hetzelfde gegeven soms anders per
  soort verkoper. AutoScout24 zet de plaats op `dealer-address` bij een handelaar
  en op `private-seller-address` bij een particulier. Een CSS-lijst met een komma
  vangt beide op. Tel het na op een volle pagina: met alleen de eerste selector
  haalden we 18 van de 20, en de twee die ontbraken waren juist de goedkoopste
  kavels.
- **Kies het juiste landdomein.** eBay stond op `.com` en gaf dollars;
  `benl.ebay.be` geeft euro's, en ook passender locatie en verzending.
- **Prijs in centen** komt vaak voor bij API's (149900 in plaats van 1499).
- **Het eerste resultaat is soms een advertentie of dummy** (eBay).
- **Grote foto:** kijk of de site een grotere variant aanbiedt
  (2dehands: `pictures.extraExtraLargeUrl`). Niet elke site doet dat.
- Loopt iets mis of traag, kijk dan in het logboek (zie hieronder).

### Twee dingen die bij het inregelen misgingen

**Een selector met de verkeerde veldnaam faalt stil, en het is niet te zien waar.**
Het veld heet `UrlSelector`, niet `LinkSelector`. Met de verkeerde naam bleef de URL
leeg, en dan valt `ExternalId` terug op de **titel** als identiteit. Gevolg: van 100
zoekertjes bleven er 73 over, want dezelfde plaat die door veertien verkopers wordt
aangeboden werd één zoekertje. Niets in het logboek wees daarop — het aantal klopte
gewoon niet. Bij een onverwacht laag aantal is dit het eerste om na te kijken:

```
edges 100 · unieke ids 100 · unieke titels 73   <- dan is de URL leeg
```

**De prijs wordt nu getoond met centen wanneer die er zijn, en anders zonder**
(`PriceTextConverter`). Vaste opmaak voldeed niet meer: twee decimalen maakt van een
aanhangwagen "€ 2.999,00", nul decimalen maakte van een plaat van € 0,76 doodleuk
"€ 1". Bij Discogs ligt veel onder de vijf euro, dus daar viel dat meteen op. De twee
resultaatsjablonen gebruikten bovendien elk een andere opmaak; nu allebei dezelfde.

## Hoe snel is het, en waar gaat de tijd heen

Gemeten op **"cd"**, acht sites aangevinkt: **621 resultaten in 35 seconden**.
De sites zijn de bestanden uit `zentrix-sites`; ze staan hier als meetpunten, want wat
er sneller werd zit in de app. Per bron, met waar het vandaan kwam:

| Bron | Weg | Tijd | Was | Resultaten |
|---|---|---|---|---|
| 2dehands | rechtstreeks | 0,6 s | 0,7 s | 100 |
| Discogs | brug (API) | 2,6 s | 2,5 s | 100 |
| Facebook | Playwright | 4,1 s | 4,7 s | 21 |
| AlleVeilingen | Playwright | 4,8 s | 6,0 s | 100 |
| Catawiki | brug | **6,2 s** | 20,7 s | 100 |
| leboncoin | brug | **6,3 s** | 17,7 s | 100 |
| eBay | Playwright | 10,3 s | 10,4 s | 100 |
| AutoScout24 | rechtstreeks | — | — | 404: geen automerk |

De hele zoekopdracht ging van **63 naar 35 seconden**, en eBay apart van 31,4 naar
7,8 seconden in een eerdere ronde.

**Kies een gewoon woord om te meten.** De eerste meting gebeurde op "commodore", en
die gaf een scheef beeld: AlleVeilingen leek kapot (0 resultaten in 11,5 s) terwijl
er gewoon geen Commodore-kavels waren. Met "cd" geeft diezelfde site 100 resultaten
in 6,0 s — de snelste van alle browsersites. Een merknaam of een nichewoord meet de
voorraad van de site, niet de app. Neem "cd", "dvd", "computer", "fiets" of iets
anders wat overal bestaat.

De uitzondering is **AutoScout24**: die kent geen vrije tekst, alleen automerken, dus
daar geeft elk gewoon woord een 404. Dat is geen storing en de app zegt dat nu ook
met zoveel woorden in plaats van "Response status code does not indicate success".
Wie daar wil zoeken laat de zoekbalk leeg en gebruikt de filters.

**Drie rijstroken lopen tegelijk.** De rechtstreekse sites delen niets en gaan allemaal
samen. De browsersites gaan na elkaar, want twee Playwright-sessies op hetzelfde profiel
botsen, en de brugsites ook, want de brug heeft één wachtrij. Maar een browsersite en een
brugsite delen niets, dus die twee stroken lopen naast elkaar. Zie
`SearchRunner.RunInLanesAsync`.

**Het uitlezen gebeurt op een achtergronddraad** (`GenericSource.ParseAsync`,
`LinkTextSource.SearchAsync`). De zoekopdracht start op de schermdraad, en zonder
`Task.Run` liep het ontleden van een pagina van een miljoen tekens daar ook: het scherm
haperde terwijl je scrolde, en de "tegelijk" zoekende sites lazen in feite na elkaar uit.
Het samenvoegen blijft op de schermdraad, want wat na de `await` komt, loopt terug op de
draad van de aanroeper.

**Het logboek zegt per pagina waar de seconden blijven.** Een regel als

```
browser: chrome 702ms tabblad 26ms laden 2084ms zoekertjes 119ms cookies 129ms scrollen 589ms uitlezen 307ms
```

splitst het opstarten van Chrome, het laden van de pagina, het wachten op de
zoekertjes, de cookiemelding, het scrollen en het uitlezen. Zonder die splitsing is
niet te zien of een trage bron aan de site ligt of aan ons eigen wachten — en dat
laatste bleek het geval.

### Twee wachttijden die niets opleverden

eBay deed er 31 seconden over. Na het meten bleek dat **9,5 van de 12 seconden per
pagina puur wachten** was:

- **`NetworkIdle` liep altijd in zijn limiet.** Acht seconden per pagina, elke keer.
  Op een site met advertenties en trackers wordt het nooit stil, dus die wachttijd
  liep altijd vol. Nu wachten we tot het **eerste zoekertje** op de pagina staat
  (`ItemSelector`); dat is er meestal binnen een tiende seconde. Staat het er na acht
  seconden nog niet, dan valt hij terug op een korte adempauze. Dit is dezelfde les
  die de brug eerder leerde met het load-event — de browserkant had hem nog niet.
- **De cookiemelding kostte 3,5 seconde.** Zes labels werden één voor één aan de
  pagina gevraagd, elk een heen-en-weer. Nu is het één zoekopdracht met een patroon,
  en de klik krijgt 1,5 s in plaats van 3 s. Meteen ook rechtgezet: er wordt nu eerst
  geprobeerd te **weigeren** en pas daarna te aanvaarden. We komen er enkel
  zoekertjes lezen.

Resultaat voor eBay: **31,4 s → 12,0 s → 7,8 s.**

### De brug: twee keer hetzelfde probleem

Een brugpagina kostte **consequent ~4,2 seconden**, en daar zat dezelfde fout in als
bij Playwright: een **vaste pauze van 2,5 seconde** na het scrollen. De extensie
wacht nu op het eerste zoekertje (de app stuurt zijn `ItemSelector` mee in de
opdracht) en neemt daarna nog maar een halve seconde.

Daarbovenop haalde de app zijn pagina's **een voor een** op. Catawiki geeft 24 kavels
per pagina, dus voor honderd zijn dat vijf rondes achter elkaar — ruim twintig
seconden, terwijl die pagina's niets met elkaar te maken hebben. Na de eerste pagina
weet de app hoeveel er op een pagina passen en dus hoeveel er nog nodig zijn; die
worden nu **samen** opgevraagd (`HaalPaginasSamenAsync`). De extensie neemt daarvoor
tot drie opdrachten tegelijk aan, elk in een eigen tabblad.

Enkel bij de brug: Playwright deelt één browserprofiel, en een rechtstreekse site
levert doorgaans alles in één keer.

### De versie van de extensie aflezen

Chrome toont op zijn extensiepagina het veld **`version` uit `manifest.json`** — niet
het versiestempel dat in de console verschijnt. Wie na het herladen naar dat scherm
kijkt, ziet dus altijd hetzelfde getal staan tenzij dat veld meegroeit. Verhoog het
daarom bij elke wijziging aan de extensie; het staat nu op 1.7. De `console.log`
bovenaan `background.js` blijft er als tweede controle, want die zegt welke code er
werkelijk draait.

### Waarom meer cores niets oplossen

De vraag ligt voor de hand — zestien logische processors, waarom duurt een
zoekopdracht dan een halve minuut? Gemeten tijdens een echte zoekopdracht over vier
sites:

| | |
|---|---|
| verstreken tijd | 45,2 s |
| gebruikte processortijd | **6,9 s** |
| bezetting van één core | 15% |
| bezetting van alle zestien | **1%** |

De app *rekent* dus nauwelijks; hij **wacht**. Achtendertig van die vijfenveertig
seconden gingen op aan wachten op websites. Zou al het rekenwerk — het uitlezen van
HTML, het ontcijferen van foto's, het tekenen van het scherm — oneindig snel worden,
dan ging de zoekopdracht van 45 naar 38 seconden.

Daarom zit elke winst in **minder wachten** en niet in meer rekenkracht:

- wachten op het zoekertje in plaats van op een klok (eBay 31,4 → 7,8 s);
- drie rijstroken tegelijk laten lopen;
- de vervolgpagina's van een brugsite samen opvragen;
- geen pagina voorbij het einde meer vragen.

Wat we om die reden **niet** doen:

- **ServerGC of extra werkdraden.** Die zijn er voor werk dat op de processor wacht;
  hier wacht alles op het netwerk. Het zou alleen geheugen kosten.
- **ReadyToRun of AOT.** Dat versnelt het *opstarten*, niet het zoeken, en het raakt
  enkel een gepubliceerde build.
- **De miniaturen kleiner ontcijferen** (`DecodePixelWidth`). Dit is geprobeerd én
  gemeten, en het maakte het **slechter**: 381 MB tegenover 327 MB zonder. WPF deelt
  namelijk één ontcijferde foto per URL tussen alle bindingen, en een eigen
  `BitmapImage` per zoekertje gooit dat weg. De foto's van deze sites zijn bovendien
  al klein. Teruggedraaid — en hier genoteerd zodat niemand het nog eens probeert.

### Eén Chrome voor de hele zoekopdracht

Elke browsersite maakte vroeger zijn eigen `BrowserFetcher`, en dus zijn eigen
Chrome: met eBay, AlleVeilingen en Facebook aangevinkt betaalde de app dat starten
drie keer. Ze kunnen hem gerust delen — ze gaan toch na elkaar, want twee
Playwright-sessies op hetzelfde profiel botsen.

`BrowserPool` houdt daarom één browser bij met een **telling van leners**. De
zoekopdracht neemt er een (`BrowserPool.Lease()`) en geeft hem terug als ze klaar
is; bij de laatste gaat Chrome dicht. Dat laatste is niet overbodig: een app die in
het systeemvak op zijn volgende beurt wacht, zou anders uren een Chrome openhouden
voor niets.

In het logboek is het te zien als `chrome 0ms` bij elke pagina behalve de
allereerste.

**Een achtergebleven Chrome** van een vorige sessie (bijvoorbeeld wanneer de app in Visual
Studio gestopt werd terwijl ze zocht) houdt het profiel vast. `BrowserFetcher` sluit die
af voor hij Chrome start (`SluitAchtergeblevenChrome`), maar enkel een Chrome-proces dat
aan twee voorwaarden voldoet: zijn opdrachtregel noemt **onze** profielmap, en het is
ouder dan deze Zentrix. .NET kan de opdrachtregel van een ander proces niet lezen; die
komt van `NtQueryInformationProcess` (klasse 60), waarvoor het beperkte leesrecht volstaat.

Tot september 2026 ging elk Chrome-proces zonder venster dicht dat jonger was dan een
minuut. Dat trof de gewone Chrome van de gebruiker: een tabblad dat net openging, en de
extensie van de brug wanneer Zentrix Chrome daarvoor net zelf gestart had. Sinds de
rijstroken start de browserstrook tegelijk met de brug, dus dat viel samen. Nagemeten met
nep-Chromes (een kopie van `cmd.exe` met de naam `chrome.exe`): de oude met ons profiel
ging dicht, een oude zonder ons profiel en een jonge met ons profiel bleven, en de 22
echte Chrome-processen die op dat moment draaiden ook.

### Het raster virtualiseert

De lijstweergave gebruikte al een `VirtualizingStackPanel`, maar het raster een
gewone `WrapPanel` — en die bouwt élke kaart op, ook de honderden die buiten beeld
vallen. Gemeten met vijfhonderd zoekertjes:

| | geheugen |
|---|---|
| gewone `WrapPanel` | **1102 MB** |
| `VirtualizingWrapPanel` | **368 MB** |

WPF levert geen virtualiserend `WrapPanel`, dus die staat nu in
`Controls/VirtualizingWrapPanel.cs`. Hij gaat ervan uit dat alle kaarten **even
groot** zijn — bij ons is dat zo (de rasterkaart heeft een vaste breedte van 264 en
een vaste hoogte) en het scheelt een hoop: met gelijke maten is uit te rekenen welke
kaarten bij een schuifstand in beeld vallen zonder ze eerst allemaal te meten. Een
paneel dat met wisselende maten overweg kan, moet dat wél en wordt een veelvoud
ingewikkelder.

Drie dingen waar het op stuk ging, en die bij zo'n paneel altijd terugkomen:

- **Genereer altijd vooruit.** `GeneratorPositionFromIndex` geeft voor een nog niet
  opgebouwde kaart `Offset = 1`, en daaruit afleiden dat je achteruit moet werken is
  verkeerd. Het is altijd `GeneratorDirection.Forward`; de offset zegt enkel waar je
  kind in de rij komt.
- **Zet een kaart op ZIJN plaats tussen de kinderen** (`InsertInternalChild`), niet
  achteraan. `ArrangeOverride` leidt uit de plaats van een kind af welk zoekertje het
  is; voeg je alles achteraan toe, dan klopt die koppeling niet meer en blijft er één
  kaart over — precies wat er gebeurde.
- **`CanContentScroll` moet AAN.** Het paneel regelt zijn schuiven zelf via
  `IScrollInfo`. Staat het uit, dan wikkelt de `ScrollViewer` er zijn eigen laag
  omheen, geeft hij het paneel oneindige hoogte, en bouwt het alsnog alles op. Dat is
  het omgekeerde van wat er bij een gewone `WrapPanel` moest, en die oude regel staat
  nog bij de UI-conventies.

De omhulling van een kaart krijgt in het raster een eigen stijl
(`GridItemContainerStyle`): zonder dat rekent het paneel met de maat die WPF-UI zijn
`ListViewItem` standaard geeft — met opvulling en al — en passen er drie kolommen
waar er vier horen. Let bij het narekenen op de **schaal van het scherm**: 264
eenheden zijn op 150% bijna 400 echte beeldpunten.

### Waarom het scrollen schokte

Het schuiven voelde hortend, en dat had twee oorzaken die allebei niets met traagheid
te maken hadden.

**De stap was geen zinnige maat.** WPF neemt de Windows-instelling *"hoeveel regels
per klik van het wiel"* en vermenigvuldigt die met een tekstregel van zestien
beeldpunten. Op deze pc staat die instelling op **1**. Voor de lijst betekende dat
zestien beeldpunten per klik, terwijl een kaart er tweehonderd hoog is: acht klikjes
per kaart. Een regel tekst is nu eenmaal geen maat voor een fotokaart.

In het raster was het omgekeerde aan de hand: daar had ik de stap eerst op een halve
kaarthoogte gezet, wat met de vermenigvuldiging neerkwam op **anderhalve rij per
klik** — zo'n zeshonderd beeldpunten in één sprong.

**En een klik verzette de inhoud ineens.** WPF schuift in één keer naar de nieuwe
plaats; er zit geen beweging tussen.

`SmoothScroll` lost allebei op, op één plaats voor allebei de weergaven. Een klik
verzet **een vijfde van wat je ziet** — dat volgt de hoogte van het venster en niet
de maat van een tekstregel, dus het klopt voor brede lijstkaarten net zo goed als
voor een raster met foto's. En de inhoud schuift er in een paar frames naartoe: elk
frame wordt een kwart van de resterende afstand weggenomen, dus het vertrekt vlot en
loopt zacht uit. Gemeten met een klik van het wiel:

| na | verschoven |
|---|---|
| 30 ms | 41 px |
| 60 ms | 134 px |
| 100 ms | 157 px |
| 160 ms | 164 px |

Beide weergaven komen nu op ~163 beeldpunten per klik uit, in plaats van 16 (lijst)
en 600 (raster).

Twee dingen die daarbij horen:

- De lijst heeft `VirtualizingPanel.ScrollUnit` op **Pixel** nodig. Met
  `CanContentScroll` aan schuift een `VirtualizingStackPanel` standaard **per kaart**,
  en dan springt elke klik een hele kaart. `Pixel` geeft schuiven per beeldpunt en
  houdt de virtualisatie.
- Het raster hergebruikt zijn kaarten nu (`IRecyclingItemContainerGenerator.Recycle`
  in plaats van `Remove`) en houdt twee rijen extra boven en onder aan. Een kaart
  opbouwen — foto, schaduw, afgeronde uitsnede — bij elke schuifstap opnieuw doen is
  precies wat écht haperen veroorzaakt.

  **En daar zit een valkuil in die de halve lijst deed verdwijnen.** `GenerateNext`
  geeft een vlag terug die je makkelijk leest als "moet nog geplaatst worden", maar
  ze betekent **"vers gemaakt"**. Een hergebruikte kaart komt uit de voorraad terug
  met die vlag op `false` — terwijl wij hem bij het opruimen net uit de boom hadden
  gehaald. Sla je hem dan over, dan bestaat hij wel maar staat hij nergens: het
  scherm bleef onderaan leeg terwijl de schuifbalk gewoon doorliep. Kijk dus of de
  kaart op zijn plaats tussen de kinderen staat en zet hem er anders alsnog in —
  ongeacht wat die vlag zegt.

  Zo controleer je het: scrol allebei de weergaven tot onderaan. Ze horen op
  hetzelfde zoekertje te eindigen. De lijst gebruikt WPF's eigen paneel en is dus
  het ijkpunt.

**Let op bij het wisselen van weergave.** Het paneel wordt al gemeten voor het aan
zijn lijst hangt, en dan is `ItemContainerGenerator` nog `null`. Zonder die controle
valt de app om zodra je tussen lijst en raster wisselt. Elke methode die de generator
gebruikt, slaat die ronde nu over.

### Wat er nog te halen valt

- **Een site zonder resultaten kost 8 seconden** op de eerste pagina, want dan loopt het
  wachten op het zoekertje in zijn limiet. Een lege vervolgpagina kost nog 3 seconden, en
  meestal wordt die niet meer gevraagd (zie "Een korte pagina is de laatste").
- **Playwright-vervolgpagina's** gaan nog een voor een. Binnen dezelfde browsercontext
  kunnen ze in tabbladen naast elkaar (eBay: drie pagina's van 3,7 s elk). Niet gedaan,
  want een site kan dat als robotgedrag zien; doe het dan met een uitschakelaar per site.
- **eBay is met ~8 s de traagste**, en dat is bijna helemaal het laden van de pagina
  zelf. Daar valt met wachten niets meer te winnen.
- **De browserstrook gaat na elkaar**, terwijl de sites al één Chrome delen: tabbladen in
  dezelfde context mogen tegelijk. Vraagt wel eerst een slot rond `GetContextAsync` (twee
  aanroepers starten anders elk een Chrome), en een uitschakelaar per site. Op "cd" naar
  schatting 19 naar 10 s; niet gemeten.
- **Chrome gaat dicht na elke zoekopdracht** en start bij de volgende opnieuw (mediaan 0,7 s,
  60 keer in het logboek). Een paar minuten laten openstaan scheelt dat.
- **`WaitForSelectorAsync` wacht op een zichtbaar zoekertje**; een aanwezig zoekertje
  (`State = Attached`) volstaat om uit te lezen. Kan bij eBay 2 tot 3 s per pagina schelen,
  maar een site die haar kaarten eerst leeg tekent, leest dan te vroeg. Eerst meten.
- **Een filter wijzigen op één tab zoekt alle sites opnieuw af.** Enkel de actieve tab
  opnieuw doorzoeken vraagt `AddBatch` en de lijst van getoonde sleutels als veld.

## Fouten opsporen

De app schrijft een logboek naar `%APPDATA%\Zentrix\zentrix-log.txt`
(`Services/Log.cs`): per zoekopdracht welke bron start, de opdrachten van de
brug, elke tussentijdse levering met grootte, hoeveel resultaten daaruit
gelezen zijn, en de tijd per bron. Dat is de snelste weg naar de oorzaak bij
"traag" of "geen resultaten". Elke beurt schrijft daar ook in: welke zoekopdracht
start, wat elke site opleverde en hoeveel er nieuw was.

**Wie er aan het werk was, staat vooraan de regel**: `zoeken:` wanneer je zelf op het
vergrootglas klikt, `planner:` bij een geplande beurt. Het is dezelfde code (`SearchRunner`,
zie "Het scherm zoekt niet meer zelf"), dus zonder dat onderscheid is in het logboek niet meer
te zien wie er zocht.

`App.OnStartup` hangt zich aan `DispatcherUnhandledException`,
`AppDomain.UnhandledException` en `TaskScheduler.UnobservedTaskException`, en
schrijft die naar hetzelfde logboek. Zonder dat verdwijnt een fout in de
interface zonder spoor: de app staat er dan bij als een leeg venster en er is
niets terug te vinden. De eigenaar draait deze app buiten de debugger, dus het
logboek is de enige plaats waar zoiets kan staan.

**Een app die vanuit een verpakte app start, ziet een andere gegevensmap.** Een terminal
of script binnen de Claude-desktopapp (een MSIX-pakket) leest en schrijft `%APPDATA%` via
een omgeleide kopie (`%LOCALAPPDATA%\Packages\Claude_...\LocalCache\Roaming`). Een Zentrix
die van daaruit gestart wordt, gebruikt dus andere sites, een andere databank, een andere
koppelcode en een ander browserprofiel dan dezelfde exe vanuit Visual Studio - en ook wat
zo'n script "in de gegevensmap" leest, komt uit die kopie. Zo stonden in september 2026
alle gegevens ruim een week in de kopie, en startte Visual Studio met een lege map. Wie de
echte map wil lezen of de app met echte gegevens wil starten, doet dat buiten die omleiding
(bv. via `Win32_Process.Create`) of start ze gewoon vanuit Visual Studio.

**Het register wordt net zo omgeleid** (`HKCU`). Op 18 september 2026 toonde een terminal in de
Claude-app bij "opstarten met Windows" een Zentrix-regel naar `bin\Debug\...\Zentrix.exe`, terwijl
het echte register er geen had - die regel was ooit geschreven door een Zentrix die vanuit de
Claude-app gestart was. Wie wil weten of Zentrix echt mee opstart, leest het register buiten de
omleiding, met hetzelfde `Win32_Process.Create`.

**Controles.** `tests\Zentrix.Checks` is een gewoon consoleprogramma dat de logica nameet,
zonder testframework en zonder netwerk:

```bash
dotnet run --project tests\Zentrix.Checks -- --snel
```

Zonder `--snel` komt er één controle bij die 30 seconden op een time-out wacht. Het drukt per
controle OK of FOUT af en eindigt met "ALLES OK" en het aantal, of met het aantal fouten. Met
`--snel` waren dat er 306 op 25 september 2026, met Zentrix open - dus 329 met alles dicht (de
bruggroep is er 23, waarvan 2 wegvallen zodra Chrome draait). Twee dingen op deze pc laten
controles wegvallen, en allebei zeggen ze dat ook:

- **Draait Zentrix zelf**, dan is de poort van de brug bezet en valt de hele brug-groep weg (23).
- **Draait Chrome met de brug-extensie**, dan klopt die elke 250 ms aan met de échte koppelcode.
  Het controleproject heeft een eigen gegevensmap en dus een andere code, dus voor zijn brug is
  dat een verkeerde - en dan staat `WrongCodeRecently` altijd aan. De twee controles die juist
  nakijken dat een webpagina die vlag niet kan zetten, vallen dan weg (327 in plaats van 329).

Drie regels waar het aan vastzit:

- **Het compileert de broncode zelf mee** (`Models`, `Sources`, `Services`, zonder
  `TrayIcon.cs`) en verwijst niet naar `Zentrix.csproj`. Het heeft dus zijn eigen `bin` en
  `obj`, en botst niet met Visual Studio. Daarom staat in `Zentrix.csproj`
  `<Compile Remove="tests\**" />`: anders neemt de app die bestanden mee.
- **Nooit aan de echte gegevens.** `ZENTRIX_DATA` wijst naar een nieuwe map in `%TEMP%`, gezet
  voor iets anders `AppPaths` aanraakt, en nagekeken.
- **Geen echte sites.** Een site is een lokale proefsite op 127.0.0.1 (`Proefsite.cs`), de
  extensie is nagebootst (`NepExtensie`), en een Facebook-kaart is zelfgeschreven HTML. Een
  bewaarde echte pagina hoort niet in deze repository: die kan een aangemelde sessie bevatten.

Wat aan een venster hangt (`MainWindow`, de dialoogvensters), zit er niet in. Dat is met
wegwerpprojectjes nagemeten die de vensters echt opbouwen; zie bij "In het systeemvak
blijven draaien" hoe die `App.OnStartup` overslaan.

**Sitebestanden controleren zonder de app.** `tools/verifieer-sites.py` bouwt per
site exact dezelfde zoek-URL als `SearchUrlBuilder` (ook bij URL-stijl
`Base64Json`), haalt de rechtstreeks bereikbare sites op en leest ze uit met de
puntpaden uit het bestand. Zo zie je in één oogopslag of een configuratie klopt —
handig na het wijzigen van selectors of filters:

```bash
python tools/verifieer-sites.py
```

**Doet een filterparameter echt iets?** `tools/meet-filter.py` bouwt dezelfde
zoek-URL als de app, hangt er jouw parameters aan en telt het **totale** aantal
treffers — niet het aantal op de pagina, want dat ligt vast en verandert nooit,
ook niet als een filter werkt:

```bash
python tools/meet-filter.py autoscout24 porsche "fuel=D" "gear=A"
```

Er gaat altijd een **onbestaande parameter mee als controle**. Verandert die het
aantal ook, dan meet je iets anders dan je denkt en zegt de meting niets. Kleine
verschillen worden genegeerd: op een drukke site komen en gaan er zoekertjes
tussen twee verzoeken door, en een filter dat werkt halveert doorgaans.

Sites die een browser of de brug nodig hebben worden overgeslagen, en ook sites
met HTML: dat script leest enkel JSON uit, want buiten de app om is er geen
CSS-selectormotor. Van die sites toont het wel de zoek-URL — de helft van wat er
mis kan gaan — en verder controleer je ze met de Testen-knop in de instellingen.

Wil je de **selectors** van een HTML-site buiten de app om nameten, dan is een
wegwerpprojectje met AngleSharp de snelste weg: dezelfde parser, dezelfde `Pick`,
en je kan meteen tellen hoeveel van de twintig resultaten een veld invullen. Zo
kwam bij AutoScout24 aan het licht dat de plaats bij particulieren op een andere
`data-testid` staat.

**Het venster fotograferen zonder het naar voren te halen.** `tools/vensterfoto.py`
laat het venster zichzelf in een bitmap tekenen (PrintWindow), zodat je het
uiterlijk kan nakijken terwijl er met iets anders gewerkt wordt:

```bash
python tools/vensterfoto.py Zentrix venster.png
```

`vensterfoto.py` kiest het **grootste** venster met die titel. Zonder die keuze
pak je soms een tooltip van 237x39 die toevallig dezelfde titel draagt, en dan
fotografeer je niets.

Het script zet zichzelf eerst op DPI-bewust. Zonder dat geeft `GetWindowRect`
verkleinde maten terwijl `PrintWindow` op echte beeldpunten tekent, en krijg je
enkel de linkerbovenhoek van het venster te zien. Datzelfde geldt voor élk script
dat op beeldpunten werkt: dit scherm is 3840×2160 op 150%, dus een proces dat
niet DPI-bewust is ziet 2560×1440 en klikt er structureel naast.

`tools/klik.py`, `typ.py` en `scroll.py` horen daarbij. Ze meten het venster
telkens opnieuw op het moment van de handeling, want het venster verschuift;
`klik.py` en `scroll.py` nemen de venstertitel als argument, zodat een dialoog
niet op de coördinaten van het hoofdvenster wordt aangeklikt. `typ.py` typt
cijfers via het numerieke klavier en gewone tekst via het klembord: op het
AZERTY-toetsenbord van deze pc geeft de toetspositie van `2` anders een `é`.

**Een spierwit venster terwijl de app gewoon draait.** Gebeurt dat, dan is er
niets stuk aan de code: WPF tekent wel, maar de grafische kaart presenteert niets
meer. Zo herken je het — de vensterrand en de afgeronde hoeken staan er wél, de
boom is compleet (na te gaan met UI Automation), en het venster laat zich netjes
naar een `RenderTargetBitmap` tekenen. Het komt voor na een slaapstand of een
reset van het stuurprogramma. Herstarten van de pc lost het op.

Bij het **opstarten van de pc** kwam het ook voor, en dan loste een herstart van Zentrix het
op; daarom toont een start door Windows het venster niet meer (zie "In het systeemvak blijven
draaien"). Het logboek zegt sinds 22 september 2026 wat er gebeurt (`DisplayDiagnostics`):

```
tekenen: niveau 2 (met de grafische kaart), ...; pc aan sinds 1 min 29 s; scherm 2560x1440, ...
gestart in het systeemvak, zonder venster: dat wordt pas getekend als je het opent
hoofdvenster: getoond (eerste keer), pc aan sinds 12 min 4 s
hoofdvenster: eerste beeld getekend, 226 ms na het tonen, met de grafische kaart waar het kan
```

Staat er "getoond" zonder "eerste beeld getekend" erna, dan tekende het venster nooit. Een lager
niveau dan 2, "Windows wijzigde het scherm" of "pc ontwaakt" vlak ervoor zeggen waar het aan lag.
Het scherm staat in eenheden van WPF: 3840x2160 op 150% is 2560x1440. Werkt het venster weer na
het groter of kleiner trekken, dan was het het tekenvlak.

Als noodrem kan alles op de processor getekend worden:

```bash
set ZENTRIX_SOFTWARE_RENDER=1
```

Dat staat standaard uit, want het kost vloeiendheid bij het schuiven door lange
lijsten. Laat je hierdoor niet op een dwaalspoor zetten: een leeg venster lijkt
op de valkuilen hieronder bij het designsysteem, maar die geven een fout of een
verkeerde kleur — geen wit venster mét zichtbare rand.

De helft van de brug draait in Chrome. Die code staat in de map `extension\`
van dit project; zijn console open je via `chrome://extensions`
→ "service worker". **Na elke wijziging aan de extensie moet je die daar
herladen (🔄)**, anders blijft Chrome de oude versie draaien. Dat is niet te
automatiseren: `chrome://`-pagina's laten zich niet aansturen, en Chrome afsluiten
om hem opnieuw te laten inlezen kost de gebruiker zijn tabbladen.

Zo zie je welke versie draait: bovenaan `background.js` staat een `console.log` met
een versienummer. Staat die regel niet in de console van de service worker, dan
draait Chrome nog de oude. Het huidige stempel is **versie 6** (de kopregel
`X-Zentrix-Brug`, zodat enkel de extensie een verkeerde koppelcode kan melden).

## Designsysteem

De app heet overal **Zentrix**, ook in de code en in de gegevensmap; zie "De naam,
de gegevensmap en GitHub" voor hoe dat hernoemen ging.

Alle kleur, vorm en typografie staat in `App.xaml` en nergens anders. Vensters
en sjablonen verwijzen enkel naar die namen, nooit naar een losse kleurcode:

- **Accent** `AccentBrush` indigo #4F46E5, met `AccentGradientBrush` naar violet
  #7C3AED. Dat verloop is voor primaire knoppen en voor wat actief staat, niet
  voor grote vlakken.
- **Achtergrond** `AppBackgroundBrush`: verloop #3B3470 → #2E2859 → #1D193A, met daarboven
  de achtergrondfoto (zie Afbeeldingen).
- **Oppervlakken** `SurfaceBrush` (wit op 6%), `SurfaceHoverBrush` (10%),
  `SurfaceBorderBrush` (10%), hoeken `SurfaceRadius` (12). `PanelBrush` is de
  ondoorzichtige variant, voor balken en popups die leesbaar moeten blijven
  boven de achtergrondfoto.
- **Kaarten** zijn iets anders dan oppervlakken, en dat onderscheid is bewust.
  Een *kaart* is een stuk inhoud — een zoekertje, een bewaarde zoekopdracht, een
  regel in Recent — en is licht: `CardBrush` (#F5F3FF op 94%, zodat de
  achtergrond er net doorheen schemert) met `CardTextBrush` (#1E1B2E) en
  `CardTextSubtleBrush` (60%) erop, en de prijs in het accent. Een *oppervlak*
  is een donker paneel of knopvlak dat op de achtergrond meedeint.
  Alles buiten de kaarten — de zoekbalk, de chips, de statusregel — blijft wit
  op de achtergrond.
- **Tekst** `TextPrimaryBrush` #F1EFF7 en `TextSubtleBrush` (60%), met de stijlen
  `TitleText` (20 semibold), `HeadingText` (14 semibold), `BodyText` (13) en
  `CaptionText` (12, gedempt) in Segoe UI Variable.
- **Accent als tekst op donker** `AccentOnDarkBrush` (#A5B4FC). Het gewone indigo haalt als
  tekst of pictogram op de donkere achtergrond maar 2:1 - de actieve tab onderaan was
  daardoor slechter leesbaar dan de inactieve. Dit lichte indigo haalt ongeveer 6:1.
  `AccentBrush` blijft voor vlakken en voor tekst op de lichte kaarten (ongeveer 5,7:1).
- **Waarschuwing** `WarningBrush` (amber #FBBF24) op de donkere achtergrond en
  `WarningOnCardBrush` (#B45309) op de kaarten.
- **Knopjes boven een foto** `OverlayBrush` (#80000000) en `OverlayHoverBrush` (#A6000000). Op
  een witte productfoto haalde een wit pictogram op het vroegere #59000000 maar 2,4:1.
- **De achtergrondfoto is in het midden en rechts lichtpaars.** Tekst die daar rechtstreeks op
  staat, haalde 2 à 3:1, ook in `AccentOnDarkBrush`. De teksten bij een lege lijst en "Nog geen
  sites" staan daarom op een vlak in `PanelBrush`; tellers en knoppen in de koppen zijn
  `TextPrimaryBrush`. Lijnen en pictogrammen in het accent op donker (het draaiende wieltje, de
  rand van het lege sitechipje, de open tab in Sites beheren) zijn `AccentOnDarkBrush`.

Twee valkuilen die de app bij het opstarten lieten crashen of leeg lieten:

- Zet **geen `<StaticResource x:Key="A" ResourceKey="B" />`** als regel in een
  resourcewoordenboek om een naam door te verwijzen. Dat breekt het woordenboek:
  andere sleutels worden dan niet meer gevonden ("Cannot find resource named …").
  Definieer liever een echt penseel met `Color="{StaticResource ...Color}"`.
- Geef **`ui:FluentWindow` geen impliciete stijl** (`<Style TargetType="{x:Type
  ui:FluentWindow}">`). WPF-UI levert zijn venstersjabloon als thema-stijl; een
  eigen stijl neemt die plaats in en dan tekent het venster niets meer — je
  krijgt een leeg wit kader. Zet lettertype, tekstkleur, achtergrond en
  `WindowBackdropType` per venster.

Nog twee dingen over de achtergrond:

- Het verloop staat als **eigen `Border` achter de inhoud** (`Grid.RowSpan` over
  alle rijen), niet als `Background` van het venster. WPF-UI zet die laatste bij
  het laden zelf terug op de effen themakleur (#202020); je verloop verdwijnt dan
  zonder foutmelding. De sleutel `ApplicationBackgroundBrush` overschrijven helpt
  evenmin. Dat geldt voor **elk** venster, niet alleen het hoofdscherm: elk nieuw
  `ui:FluentWindow` heeft die Border nodig, anders staat het er vlak grijs bij
  tussen de rest.
- Het verloop loopt van links licht naar rechts donker met de middelste stop op
  0,65, zodat het grootste deel van het scherm aan de lichte kant blijft.

Mica staat uit: die vlekt door het verloop heen. `ApplicationAccentColorManager`
krijgt in `App.xaml.cs` het indigo mee, zodat de vinkjes en de selectie in
lijsten van WPF-UI dezelfde kleur hebben als de rest.

### Afbeeldingen

`Assets/` bevat `logo.png`, `background.png` en `zentrix.ico`, alle drie als
`Resource` in het csproj — ze zitten dus in de exe. `zentrix.ico` staat als
`ApplicationIcon` en verschijnt in de titelbalk, de taakbalk en op het bestand.
De iconen worden gemaakt met `tools/afbeeldingen.py` uit de aangeleverde
bestanden; draai dat script opnieuw wanneer er een nieuw logo komt.

Wat dat script doet en waarom:

- De aangeleverde afbeeldingen dragen rechtsboven een badge "Made with AI". Bij
  het logo wordt die weggesneden, bij de achtergrond opgevuld met de kleuren
  eromheen (daar is het verloop glad genoeg om dat onzichtbaar te doen).
- De ondergrond van het logo wordt weggerekend naar doorzichtigheid, zodat het
  op onze eigen achtergrond staat. **Let op de richting**, want die hangt af van
  de ondergrond:
  - licht kunstwerk op **zwart** (het huidige neonlogo): `pixel = a*K`, dus de
    dekking komt uit het **lichtste** kanaal. De gloed wordt daardoor vanzelf een
    half-doorzichtige waas — precies wat je wil.
  - donker kunstwerk op **wit** (het vorige logo): `pixel = a*K + (1-a)*255`, dus
    de dekking komt uit het **donkerste** kanaal.

  Wie die twee verwisselt, krijgt een logo dat bijna helemaal doorzichtig wordt.
- Uitsnijden gebeurt op een drempel in de alfa, niet op "alles wat niet helemaal
  doorzichtig is". De gloed rond dit logo reikt tot ver in het beeld en zou het
  uitsnijden anders zinloos maken.
- Het icoon gebruikt enkel het merkteken links (het vergrootglas met de ring),
  op 0,78 van de hoogte uitgesneden: de naam erbij zou op 16 px onleesbaar zijn.

De achtergrondfoto wordt in code gezet (`ApplyBackgroundPhoto`), niet in de XAML,
zodat een ontbrekend bestand de app niet laat vallen: dan blijft het verloop uit
`App.xaml` staan. Belangrijk daarbij: `BitmapCacheOption.OnLoad`, anders leest
WPF de afbeelding pas bij het tekenen in en valt de fout buiten de `catch`.
Boven de foto ligt `ScrimBrush` (35% van de donkerste achtergrondkleur) zodat
witte tekst leesbaar blijft.

## UI-conventies

- Vermijd horizontale `StackPanel`s voor werkbalken die kunnen overlopen; een
  `StackPanel` klipt wat buiten beeld valt. Gebruik een `WrapPanel` of
  `*`-kolommen zodat alles zichtbaar blijft als het venster wordt verkleind.
- Let op: een `WrapPanel` breekt alleen af als hij een begrensde breedte heeft.
  In een horizontale `StackPanel` krijgt hij oneindige breedte en breekt hij
  nooit af. Voor een vast aantal per rij (los van de vensterbreedte) is een
  `UniformGrid` met een vast `Columns` de juiste keuze.
- WPF-UI stijlt de koppen van een `TabControl` niet vanzelf tot herkenbare
  tabs. Geef `TabItem` een eigen `ControlTemplate` (afgeronde bovenhoeken,
  rand, accentkleur bij `IsSelected`) — zie de stijl in `SettingsWindow.xaml`.
- Voor een preview die met de muis mee komt en gaat: gebruik een `Popup` met
  `IsOpen` gekoppeld aan `IsMouseOver` van het doel, niet een `ToolTip`. Een
  tooltip blijft openstaan zodra de muis zijn eigen popup raakt. Zet de inhoud
  op `IsHitTestVisible="False"`.
- Een `Image` tekent op zijn "natuurlijke" grootte, en die hangt af van de
  DPI-metadata in het bestand — een grote foto kan daardoor klein uitvallen.
  Wikkel hem in een `Viewbox` met `MaxWidth`/`MaxHeight` om echt op maat te
  schalen. Let ook op: het standaardsjabloon van een WPF-UI-tooltip legt een
  maximumbreedte op.
- WPF klipt niet op `CornerRadius`; `ClipToBounds` knipt rechthoekig. Voor een
  foto met afgeronde hoeken: een `Clip` met een `RectangleGeometry` (zie
  `RoundedClipConverter`, die meeschaalt met de werkelijke maat). Zet een
  schaduw op een búitenste laag, anders knipt de clip hem weg.
- Wil je een **gewone** `WrapPanel` als `ItemsPanel` van een lijst, zet dan
  `ScrollViewer.CanContentScroll` op `False` én
  `ScrollViewer.HorizontalScrollBarVisibility` op `Disabled`. Bij het standaard
  "per regel scrollen" krijgt het paneel geen echte breedte mee en breekt het
  nooit af — dezelfde valkuil als een `WrapPanel` in een `StackPanel`.

  Voor ons `VirtualizingWrapPanel` geldt precies het **omgekeerde**:
  `CanContentScroll` moet juist AAN, want dat paneel regelt zijn schuiven zelf via
  `IScrollInfo`. Staat het uit, dan krijgt het oneindige hoogte en bouwt het alsnog
  alle kaarten op. De resultatenlijst zet het daarom op `True` voor allebei de
  weergaven.
- Deelt een stuk beeld zich over twee sjablonen, maak er dan een `UserControl`
  van in `Controls/` in plaats van het te kopiëren. De miniatuur met zijn
  grote foto zit vol subtiliteiten (popup, clip, centreren); die wil je
  maar op één plaats onderhouden.
- Een `RadioButton` met `IsChecked="True"` in de XAML vuurt zijn `Checked` af
  **tijdens** het inlezen van die XAML, dus voor de velden in de code-behind
  bestaan. Vandaar de vlag `_ready` in `MainWindow`: zonder die vlag crasht de
  app bij het opstarten op een lege verwijzing.
- XML-commentaar mag geen twee streepjes achter elkaar bevatten. Een
  scheidingsregel als `<!-- ---------- kop ---------- -->` laat de XAML-compiler
  struikelen op `MC3000`; gebruik `=` als je zo'n balk wil.
- Wil je een pictogram vol laten lopen zodra iets actief is, geef de
  `ui:SymbolIcon` dan een stijl met een `DataTrigger` op
  `{Binding IsChecked, RelativeSource={RelativeSource AncestorType=RadioButton}}`
  en zet `Filled`. Vanuit een `ControlTemplate.Triggers` lukt dat niet: de
  inhoud van de knop staat in een andere namescope.
- Kleuren staan als penselen in `App.xaml` (`HeaderBrush`, `AccentBrush`,
  `CardBrush`, `TextSubtleBrush` …). Wil je de app een andere tint geven, dan
  is dat de enige plaats die je hoeft aan te passen.
- Wil je een dropdown onder een knop, hang er dan een `ContextMenu` aan en open
  die in de klik met `PlacementTarget` + `Placement="Bottom"`.
- **`ui:TitleBar` eist zijn hele rechthoek op als sleepgebied.** Ligt er een
  invoerveld onder — zoals de zoekbalk, sinds de kop tot één rij is
  teruggebracht — dan toont de muis daar een pijl, neemt het veld geen invoer
  aan, en maximaliseert een dubbelklik het venster in plaats van tekst te
  selecteren. Precies tot waar de titelbalk reikt, dus "de onderste helft van
  het vak werkt wel".
  `WindowChrome.IsHitTestVisibleInChrome` helpt daar **niet** tegen: WPF-UI doet
  zijn eigen hit-test en kent die vlag niet (de naam komt niet eens voor in
  `Wpf.Ui.dll`). De oplossing is de titelbalk **rechts uitlijnen**, zodat hij
  enkel de strook bij zijn eigen knoppen beslaat, en het slepen van het venster
  zelf te regelen met een `MouseLeftButtonDown` op de kopbalk die `DragMove()`
  aanroept (dubbelklik daar maximaliseert of herstelt). Zie
  `Header_MouseLeftButtonDown`.
- Twijfel je of een plek invoer aanneemt, meet het dan in plaats van te kijken:
  `tools/cursortest.py` loopt een reeks punten af en zegt welke muisaanwijzer
  Windows er toont. Een I-balk betekent gewone inhoud, een pijl betekent
  sleepgebied. Een `Menu` met
  één `MenuItem` erin doet hetzelfde, maar tekent een balk mee.
- **Een uitgeschakelde knop toont geen tooltip**, tenzij `ToolTipService.ShowOnDisabled` aan
  staat. De filterknoppen zetten in hun tooltip waarom ze gedimd zijn; zonder die regel
  kwam die uitleg nooit in beeld.
- **Een `Popup` die dicht is, voert zijn bindings toch uit.** Hij hoort bij de logische
  boom en erft de gegevens. Een `Image` met een webadres als `Source` begint dan meteen te
  downloaden, ook als niemand de popup opent. Zet zo'n bron pas in `Opened` en maak ze leeg
  in `Closed` - zie `PhotoThumbnail.Preview_Opened`.
- **Een `OpacityMask` rekent met de omhullende van het element én zijn kinderen.** Steekt een
  kind buiten het vak - een tekst die breder is dan haar kader - dan valt een verloop van 0 tot 1
  grotendeels in het weggeknipte stuk. Gebruik dan `MappingMode="Absolute"` met een eindpunt in
  beeldpunten. En let op bij het nakijken: een `VisualBrush` neemt de `OpacityMask`, de `Clip` en de
  `Transform` van zijn wórtelelement niet mee, wel die van de kinderen.
- **Een `ObservableCollection` wissen geeft een Reset**, en daarop gooit een lijst of raster
  al zijn containers weg en springt het naar boven. Pas aan wat veranderde (zie
  `ToonPagina`) in plaats van `Clear()` en alles opnieuw toevoegen.
- **Esc en Enter in een dialoogvenster** komen van `IsCancel` en `IsDefault` op de knoppen.
  Zet `IsDefault` niet in een venster waar je lange codes of adressen plakt (zoals
  Meldingen): daar sluit een Enter het venster te makkelijk.
- Een `TextBox` in een zelfgetekend kader (de zoekbalk) krijgt een eigen, kaal sjabloon met
  enkel `PART_ContentHost`. Het standaardsjabloon van WPF-UI tekent anders zijn eigen
  onderlijn, ook met `BorderThickness="0"`.
- **Een eigen stijl voor een knop of tekstvak zonder `BasedOn`** valt terug op het sjabloon
  van Windows: vierkant, lichtblauw bij zweven, en een zwarte cursor in een donker veld. Zo
  stonden `PrimaireKnop`, `StilleKnop` en `Veld` in de vensters Meldingen en Zoekopdracht; nu
  zijn het `ui:Button` (Primary, Secondary, Transparent) en `ui:TextBox`, zoals in Sites
  beheren. Een klein getalveld is een gewone `TextBox` zonder `Style`: dan geldt de stijl van
  WPF-UI, en in een `ui:TextBox` past het wisknopje niet in 46 beeldpunten.
- **Een vlak rond een tekst die de code toont of verbergt**: laat het vlak de zichtbaarheid van
  de tekst volgen (`Visibility="{Binding Visibility, ElementName=EmptyHint}"`), dan hoeft de
  code niets te weten van dat vlak.
- **Een statusregel of melding naast knoppen hoort in een `Grid`** met een `*`-kolom, met
  `TextTrimming` en een tooltip met de volledige tekst, of met `TextWrapping`. In een rechts
  uitgelijnde `StackPanel` verdween het begin van een lange melding links buiten beeld.
- **Het logo verdwijnt onder 1040 beeldpunten breed.** Het staat gecentreerd in dezelfde rij als
  de zoekbalk, en schoof anders over het vergrootglas en het tandwiel.

## Wat er al werkt

- Zoeken over meerdere aangevinkte sites tegelijk, met voortgang en tijd per
  bron. De app komt zonder sites: welke er zijn en wat elk kan, staat in `SITES.md`
  van `zentrix-sites`. Tijdens het zoeken wordt het vergrootglas een **stopknop**: wat al
  binnen was blijft staan, en de zoekopdracht telt die halve beurt niet mee
- Sites importeren uit een map (ook meteen bij een lege eerste start), toevoegen
  met de AI-analyse, en verwijderen in het instellingen-scherm (de knop
  "Verwijderen" op de kaart van die site)
- Vastgezette zoekopdrachten op hun eigen tabblad, met een teller en een NIEUW-markering
  voor wat je nog niet bekeek - dat stapelt op over de beurten heen tot je de zoekopdracht
  opent. Een klik op de teller toont enkel de nieuwe (schakelaar "Enkel nieuwe"), dubbelklikken
  alles. Overleeft een herstart; "Huidige vastzetten" voegt er een toe
- **Favorieten**: het sterretje op elke foto zet een zoekertje apart. Wat je
  bewaart, wordt als kopie opgeslagen, dus het blijft zichtbaar ook als de site
  het zoekertje intussen weghaalt
- **Recent**: elke zoekterm komt in een lijst met wanneer je hem gebruikte;
  dubbelklikken herhaalt de zoekopdracht
- **Automatisch zoeken**: elke zoekopdracht heeft zijn eigen schema (om de zoveel
  minuten of dagelijks op een uur), eventueel binnen een tijdvenster, eventueel
  ook bij het opstarten. De app blijft daarvoor in het systeemvak draaien. Is er
  iets nieuws, dan komt er een melding — ballon, Telegram of e-mail
- **Filters per site binnen één zoekopdracht**: zoeken op "commodore" mag op
  de ene site een postcode en straal hebben en op de andere een keuze uit
  provincies. Het instellingenscherm toont per site enkel wat díe site kent
- **Filters die maar op één site bestaan**, beschreven in het sitebestand zelf:
  een keuze uit een lijst (ook meerdere tegelijk), een getal of aan/uit, in de
  filterpopup of bij de locatie. Een filter bijzetten is een regel JSON, geen code
- **Zoeken zonder zoekterm** op sites die enkel op filters werken
  (`AllowsEmptyQuery`), zoals een autosite waar het zoekwoord het merk is: met een
  lege balk zoek je dan niet merkgebonden en bepalen de filters wat je krijgt
- Filters per site: minimum- en maximumprijs, postcode, straal en maximum
  aantal. Wat een site niet kan, wordt gedimd of verborgen — afgeleid
  uit zijn `Filters`-mapping, dus zonder lijstje "welke site kan wat"
- **Resultaten over pagina's**, met de pager links van het locatiespeldje en nog
  eens onder de resultaten (hoogstens zeven nummers, schuivend). Het aantal per pagina (50/100/150/200) staat achter het `#`;
  de app haalt intussen op wat de sites geven: tot 2000 bij een site die rechtstreeks antwoordt,
  300 bij Facebook, 500 bij de rest
- Een tabblad **Alles** vooraan, met de resultaten van alle sites samen. De
  tabstrip toont verder enkel de sites die meezoeken; kiezen welke dat zijn
  gebeurt in het chipje achteraan de rij. Een site mag met `ShortName` een
  kortere naam voor zijn tab opgeven
- **Sorteren** op prijs (beide richtingen), op nieuwste of op de veiling die het eerst
  afloopt (over Catawiki, eBay en AlleVeilingen heen); de keuze wordt onthouden tussen twee starts
- **Prijsindicatie**: rechtsklik op een foto geeft de marktwaarde van dat model uit de
  vraagprijzen op de sites met het vinkje, met varianten apart, zonder veilingen, sets en
  toebehoren, en verbreed naar de reeks als er te weinig zijn
- **AI-controle op een foto**: rechtsklik op een foto laat een model op je eigen grafische kaart
  lezen wat er op de voorwerpen staat, en erover vertellen in gewone taal - voor een doos vol
  dvd's waarvan de titels te klein zijn, of het typenummer op een label. Lokaal, dus er gaat geen
  foto de deur uit. Ook voor alle foto's van een zoekertje tegelijk, zodra het sitebestand zegt
  waar die staan (`DetailImagesSelector`)
- Naast de prijs de **stad** (en anders het land), en bij een veiling erachter **hoelang er nog
  geboden kan worden**. Past die regel niet, dan vervaagt het einde en schuift ze zodra je er met
  de muis op gaat staan. Staat die tijd niet op de zoekpagina van de site maar wel op de pagina van
  het kavel, dan haalt de app ze daar op - enkel voor de kavels die je op dat moment ziet
- Rechtsonder op een veilingkaart een **timer** die echt aftelt waar het tijdstip exact is
  (AlleVeilingen, Catawiki via zijn API, eBay op het einde), in het laatste uur in amber
- Miniaturen in de resultatenlijst. **Dubbelklikken opent een venster met alles van dat zoekertje**:
  de foto's van de advertentie (miniaturen boven, één grote eronder die meewisselt met de muis), de
  verkoper en hoelang het online staat, met knoppen naar de site en naar de AI-controle
- *Sites beheren* met een tab per site: alle velden bewerkbaar, per site testen,
  aanmelden bij sites die dat vragen, en exporteren/importeren van losse sitebestanden
- Server-side zoekfilters via de `Filters`-mapping
- Resultatenlijst: prijs staat links naast de foto; de muis op het vergrootglas
  van een foto toont een grote foto (`Listing.LargeImage`), die pas dan wordt
  opgehaald zodat de lijst snel blijft. Grote foto komt uit `LargeImageSelector`
  (een puntpad naar een groter formaat, of `::replace` op de URL van de
  miniatuur), met terugval op de miniatuur
- Miniaturen in een vast vak (260×220, bijgesneden),
  met afgeronde hoeken en schaduw; de grote foto staat rechts ernaast en
  ligt met zijn midden op dezelfde hoogte
- Twee weergaven voor de resultaten: **Lijst** (brede kaart: foto, titel,
  prijs — om te lezen) en **Raster** (foto's naast en onder elkaar zoals
  Facebook Marketplace — om te overzien). Wisselen kan met de weergaveknop bij
  de zoekinstellingen of via het tandwiel bij *Weergave*. Alleen het sjabloon en
  het paneel wisselen, dus de resultaten blijven staan. De keuze geldt voor de
  hele app en wordt onthouden tussen twee starts
- Alle sites zijn bestanden. Er is geen ingebouwde bron meer: een site met een
  afwijkende opbouw krijgt een andere motor via het `Engine`-veld
- Resultaten verschijnen per bron zodra die klaar is, en bij de brug zelfs al
  tijdens het laden van de pagina (tussentijdse leveringen)
- Paginering via `PageTemplate`, `{page}` of `{offset}` in de zoek-URL: de app haalt extra
  pagina's op tot de rem van die soort site (20 of 10 pagina's). Geeft een site bij
  pagina 2 hetzelfde terug, dan stopt hij vanzelf. Eén browser wordt hergebruikt
  over alle pagina's. Een API die meteen genoeg teruggeeft, heeft het niet nodig
- Prijsfilter op de site zelf wanneer het sitebestand er een heeft; anders filtert
  de app achteraf
- **Fouten bij de site zelf**: een waarschuwingsteken op de tab, en bij een bewaarde
  zoekopdracht "· 1 site mislukt" in de lijst. Mislukt een geplande zoekopdracht twee keer
  op rij op dezelfde site, dan komt er een melding
- **De brug zegt waarom ze niet werkt** (verkeerde koppelcode, extensie uit, geen Chrome),
  zowel in de app als in de popup van de extensie, en slaat de brugsites dan meteen over
- Maar **één Zentrix tegelijk**; een tweede start haalt het open venster naar voren

## Volgende stappen

Wat er per site nog ontbreekt, staat bij "Nog open" in `SITES.md` van `zentrix-sites`.
Hieronder enkel wat aan de app zelf te doen valt.

1. **Facebook verliest soms een kwart van zijn kaarten tussen het scrollen en het lezen.**
   Het scrollen zelf is nagemeten en werkt (23 september 2026: 126 zoekertjes na 7 keer scrollen,
   98 resultaten in 18,0 s, zonder controlevraag). Wat er wegviel, is op 24 september uitgeplozen
   met de nieuwe logboekregel, en het bleek niet te liggen aan dubbels of aan kaarten zonder
   titel - daarvan zijn er maar 1 tot 3. Het scrollen telde 102 zoekertjes, en de motor kreeg er
   de ene beurt 102 te lezen en de andere maar 78; zie de linkmotor voor de cijfers. **De extra
   telling ligt er sinds 24 september**: het logboek zegt nu ook hoeveel zoekertjes er nog in de
   pagina staan vlak voor ze opgehaald wordt. Wat er nu nodig is, is een echte Facebook-beurt
   waarin het weer misgaat - dan wijst dat getal aan of de kaarten uit de pagina verdwenen of pas
   bij het uitlezen. De tabel in de linkmotor zegt wat welke uitkomst betekent.
2. Grote foto's op aanvraag. Bij sommige sites geeft de zoekpagina enkel kleine,
   bijgesneden foto's en staat de grote pas op de pagina van het zoekertje. Die
   ophalen kost een volledige browsersessie (5–10 s), dus niet tijdens het
   zoeken maar pas wanneer je erom vraagt.
3. **AI-controle op een foto**, lokaal op de grafische kaart. Gevraagd op 24 september 2026:
   de AI moet zien wat de eigenaar zelf niet ziet - een doos vol dvd's waarvan de titels te klein
   zijn, of wat voor toestel er staat en welk typenummer erop staat. Vier stappen:

   - ~~De motor.~~ Gedaan op 24 september 2026: `PhotoAnalyzer` knipt, leest, ontdubbelt en
     vertelt. Zie "AI-controle op een foto" bij Wat je te zien krijgt.
   - ~~Rechtsklik op één foto.~~ Gedaan op 24 september 2026: *AI-controle op deze foto*, naast
     *Prijsindicatie*, met `PhotoInsightWindow`.
   - ~~Alle foto's van dat zoekertje.~~ Gedaan op 24 september 2026: `DetailImagesSelector` in het
     sitebestand, opgehaald door `DetailFetcher.FotosAsync`. Ingevuld op 25 september voor
     2dehands, Marktplaats en AlleVeilingen. **Nog open**: Catawiki en eBay lopen via de brug, en
     dat vraagt een draaiende Zentrix met de extensie om na te meten; Facebook, Discogs,
     leboncoin, Kleinanzeigen en AutoScout24 zijn nog niet bekeken.
   - **Meerdere zoekertjes tegelijk**, met vooraf een schermpje dat zegt hoeveel foto's en
     hoelang. Vraagt dat de lijst en het raster meervoudige selectie aankunnen; nog na te kijken.

   **Google Lens via de brug** is apart besproken en bewust achteraan gezet: het is níet offline -
   je stuurt dan een foto naar Google - er is geen officiële weg naartoe, en een Google-pagina
   besturen breekt bij de eerste opmaakwijziging. Wat het wél goed kan is precies wat het lokale
   model niet kan: een typenummer opzoeken en gelijkaardige items vinden. Dus: de vier stappen
   hierboven eerst en helemaal lokaal, en Lens daarna als een aparte knop waarvan je weet dat er
   iets de deur uit gaat.
4. De brug verder laten scrollen zodat sites met lazy loading meer
   resultaten geven
5. Het automatisch zoeken kan verder:
   - Een gemiste beurt wordt enkel dezelfde dag ingehaald (zie "3. De planner"). Stond de
     app drie dagen dicht, dan draait "dagelijks" één keer, niet drie - en dat is
     vermoedelijk ook wat je wil.
   - Per zoekopdracht toont de teller wat je nog niet bekeek. Een lijstje "dit kwam er
     sinds gisteren bij" over alle zoekopdrachten samen bestaat nog niet.
5b. **Favorieten opvolgen** (gevraagd op 17 september 2026, in drie stappen; de derde is gedaan):
   - of een favoriet **nog te koop** is, en tegen welke prijs nu (een favoriet is een kopie; bij
     Catawiki op 2dehands was het bod intussen van € 5 naar € 24 gestegen);
   - **iets gelijkaardigs** vinden: dezelfde zoektocht als de prijsindicatie levert die al op;
   - **de prijsindicatie** (klaar, zie "Prijsindicatie"). Nog open daarin: verkochte prijzen van
     eBay als sterkere bron, en meer sites met het vinkje (Kleinanzeigen gaf een gewoon verzoek op
     17 september een 403, en moet eerst nagemeten worden).
6. Losse eindjes: `country` en `photosOnly` staan nog als ongebruikte kolommen
   in de SQLite-tabel van bewaarde zoekopdrachten (`photosOnly` zit nu in het JSON-blokje `config`).
   De ballon in het systeemvak toonde "zoekhulp" als afzender, de naam van het
   proces. Sinds de exe `Zentrix.exe` heet is dat vermoedelijk opgelost - nog na te
   kijken; anders vraagt het een AppUserModelID.
7. Uit de adviesronde van 15 september 2026, nog niet gedaan:
   - **Tekststijlen opruimen.** `TitleText`, `HeadingText`, `BodyText` en `CaptionText`
     staan in `App.xaml` maar worden bijna niet gebruikt; de XAML telt veertien
     lettergroottes, en `SettingsWindow` regelt tekstniveaus met `Opacity`. Veel, maar
     mechanisch werk.
   - **Playwright-vervolgpagina's naast elkaar**, zie "Wat er nog te halen valt".
   - Het veld `Enabled` ("Standaard aangevinkt") in het sitebestand doet niets en staat niet
     meer in *Sites beheren*. ("Max. resultaten" in het venster van een zoekopdracht botste
     daar ook mee; dat veld is weg sinds 22 september 2026, zie "Resultaten over pagina's".)
   - De koppelcode-controle, de rijstroken en de kortere wachttijden zijn met een build en
     een testprojectje nagekeken, maar nog niet gemeten tijdens een echte zoekopdracht.
8. Uit de adviesronde van 16 september 2026 (zes adviseurs, zie `.claude\agents`). Gedaan:
   de planner die bleef hangen of nooit startte, het afsluiten van andermans Chrome,
   Enter die in de vorige bewaarde zoekopdracht schreef, de planner die het scherm
   overnam, Annuleren dat niet annuleerde, dubbelklikken dat elke link opende, een
   webpagina die de brugsites kon laten overslaan, stil falen (nul na veel, controlepagina's,
   verdwenen sites, meldingen die niet vertrokken, Engelse foutmeldingen), een hernoemde site,
   de brug in golven, pagina 1 meteen, gecomprimeerde antwoorden, IdPattern, de linkmotor,
   Discogs met een aanhalingsteken, de weergave (contrast, afknippen, smal venster,
   pictogrammen, knopstijlen) en het controleproject. Nog te doen:
   - **Enkel live na te gaan**: of eBay via de brug werkt (de 0 resultaten van toen waren zijn
     robotbeveiliging, zie `SITES.md`), of Kleinanzeigen pagineert met `s-seite:{page}` in het pad, of Catawiki een datum
     heeft in `time@datetime`, en of de brug met extensie 1.7 werkt.
   - **Tekststijlen** (punt 7) en het opruimen van ongebruikte sleutels in `App.xaml`.
   - **Snelheid**: zie "Wat er nog te halen valt".
   - **Toetsenbord**: het sitechipje en de tabs zijn niet met Tab te bereiken.
   - **Zonder extensie** wacht elke zoekopdracht met een brugsite 30 seconden; de melding zegt
     nu wel hoe je de extensie installeert.
   - **GitHub** (beslist op 17 september 2026): `Zentrix` en `zentrix-sites` als privé-repositories
     op het account `dees743-cloud`, commits met het afgeschermde noreply-adres, `.claude/` niet
     mee, geen postcode of mailprovider van de eigenaar in de bestanden. Gaat `zentrix-sites`
     ooit openbaar: eerst het nummer van de Marketplace-regio in `facebook.json` vervangen en
     `opdracht-sitefilters.md` (een interne notitie) nakijken.
9. ~~**De twee zoeklussen samenbrengen**~~ (uit de beoordeling door ChatGPT van 20 september 2026).
   **Gedaan op 23 september 2026**, in vier stappen met bij elke stap een proef: tussentijdse
   leveringen per site, zoeken zonder bewaarde zoekopdracht, voortgang en fouten per site, en dan
   de verhuizing zelf. `SearchRunner` is nu de enige die zoekt en het scherm toont enkel; zie
   "Het scherm zoekt niet meer zelf" bij Automatisch zoeken.

   Waarom het moest: `MainWindow.RunSearchAsync` en `SearchRunner.RunAsync` voerden allebei een
   volledige zoekopdracht uit, en elke regel moest dus twee keer geschreven worden. Dat kostte in
   september 2026 al twee keer werk ("nieuw tot je kijkt", de nieuwe rem) en gaf echte verschillen:
   de prijsgrens en de gewijzigde filters, allebei rechtgezet op 22 september (zie "Filters werken
   meteen op wat er al staat").

   **De inventaris** (22 september 2026, door Codex, daarna punt per punt nagekeken in de code)
   somde de verschillen op. Bedoeld zoals ze waren, en dus geen werk: los zoeken zonder bewaarde
   zoekopdracht, een onuitvoerbare beurt die toch een tijdstip krijgt, tussentijdse resultaten
   enkel op het scherm, het wachten op het bewaren, meldingen enkel van de planner, en *Recent*
   enkel bij zelf zoeken. Rechtgezet bij de verhuizing: een verdwenen site die het scherm stil
   liet vallen, "al gezien" bij een site die halverwege faalt, `LastViewed` dat op het einde
   opnieuw gelezen hoort te worden, en de volgorde van het bewaren. Annuleren kwam er op
   24 september bij, en daarmee is er van die lijst niets meer open; zie "Een zoekopdracht
   stoppen" bij Automatisch zoeken.

   Ook nuttig uit die ronde: **de afspeelknop van een zoekopdracht start de planner**
   (`_scheduler.RunAsync`), en niet `RunSearchAsync`. Dat laatste loopt bij het vergrootglas, bij
   Enter, bij het sluiten van een filterpopup, en bij het openen van een zoekopdracht zonder
   bewaarde resultaten.

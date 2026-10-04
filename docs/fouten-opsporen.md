# Fouten opsporen: logboek, controles en hulpmiddelen

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## Fouten opsporen

De app schrijft een logboek naar `%APPDATA%\Vindioo\vindioo-log.txt`
(`Services/Log.cs`): per zoekopdracht welke bron start, de opdrachten van de
brug, elke tussentijdse levering met grootte, hoeveel resultaten daaruit
gelezen zijn, en de tijd per bron. Dat is de snelste weg naar de oorzaak bij
"traag" of "geen resultaten". Elke beurt schrijft daar ook in: welke zoekopdracht
start, wat elke site opleverde en hoeveel er nieuw was.

**Wie er aan het werk was, staat vooraan de regel**: `zoeken:` wanneer je zelf op het
vergrootglas klikt, `planner:` bij een geplande beurt. Het is dezelfde code (`SearchRunner`,
zie "Het scherm zoekt niet meer zelf"), dus zonder dat onderscheid is in het logboek niet meer
te zien wie er zocht.

**De zoek-URL staat erin, maar zonder je postcode** (`Log.Url`, sinds 4 oktober 2026). Die
volledige URL is het nuttigste wat er in het logboek staat - je ziet precies welke pagina
gevraagd werd en je kan ze in een browser plakken. Maar de filters zitten erin, en daar hoort
een postcode bij. Dit logboek is net waar de app je bij een fout naartoe stuurt, en Vindioo is
een openbare repo die issues uitnodigt: de eerste die zijn logboek in een issue plakt, geeft
bij benadering zijn woonplaats weg. Dat is hetzelfde gegeven waarvoor `vindioo-sites` privé
staat.

Nagemeten in een logboek van 12 105 regels: negen regels droegen zoiets - drie van 2dehands
(`postcode`, `distanceMeters`) en zes van AutoScout24 (`zip`, `zipr`). Enkel de **waarde** gaat
weg; de naam van de parameter blijft staan, want dát de URL een postcode droeg, wil je net
weten. De lijst namen staat in `Log.PlaatsFilter` en dekt ook een paar die nog geen enkel
sitebestand gebruikt - een maskeerder die pas werkt nadat er iets gelekt is, komt te laat.

**Lange getallen blijven wél staan, op één uitzondering na.** Een algemene cijferregel zou
juist het bruikbare slopen: van de 22 URL's met een lang getal in hun pad waren het er 22 een
zoekertje-id (`/itm/128096739486`), en zonder dat nummer is zo'n regel niet meer na te spelen.
De uitzondering is een getal dat in het **sjabloon** van de site staat (`SearchUrlTemplate`):
dat is een instelling en geen zoekertje. Facebook draagt zijn regionummer in het pad van zijn
zoek-URL, en dat benadert een woonplaats. Van de dertien sitebestanden is Facebook het enige
met zo'n getal in zijn sjabloon, dus die regel raakt verder niets.

`App.OnStartup` hangt zich aan `DispatcherUnhandledException`,
`AppDomain.UnhandledException` en `TaskScheduler.UnobservedTaskException`, en
schrijft die naar hetzelfde logboek. Zonder dat verdwijnt een fout in de
interface zonder spoor: de app staat er dan bij als een leeg venster en er is
niets terug te vinden. De eigenaar draait deze app buiten de debugger, dus het
logboek is de enige plaats waar zoiets kan staan.

**Een app die vanuit een verpakte app start, ziet een andere gegevensmap.** Een terminal
of script binnen de Claude-desktopapp (een MSIX-pakket) leest en schrijft `%APPDATA%` via
een omgeleide kopie (`%LOCALAPPDATA%\Packages\Claude_...\LocalCache\Roaming`). Een Vindioo
die van daaruit gestart wordt, gebruikt dus andere sites, een andere databank, een andere
koppelcode en een ander browserprofiel dan dezelfde exe vanuit Visual Studio - en ook wat
zo'n script "in de gegevensmap" leest, komt uit die kopie. Zo stonden in september 2026
alle gegevens ruim een week in de kopie, en startte Visual Studio met een lege map. Wie de
echte map wil lezen of de app met echte gegevens wil starten, doet dat buiten die omleiding
(bv. via `Win32_Process.Create`) of start ze gewoon vanuit Visual Studio.

**Het register wordt net zo omgeleid** (`HKCU`). Op 18 september 2026 toonde een terminal in de
Claude-app bij "opstarten met Windows" een Vindioo-regel naar `bin\Debug\...\Vindioo.exe`, terwijl
het echte register er geen had - die regel was ooit geschreven door een Vindioo die vanuit de
Claude-app gestart was. Wie wil weten of Vindioo echt mee opstart, leest het register buiten de
omleiding, met hetzelfde `Win32_Process.Create`.

**Controles.** `tests\Vindioo.Checks` is een gewoon consoleprogramma dat de logica nameet,
zonder testframework en zonder netwerk:

```bash
dotnet run --project tests\Vindioo.Checks -- --snel
```

Zonder `--snel` komt er één controle bij die 30 seconden op een time-out wacht. Het drukt per
controle OK of FOUT af en eindigt met "ALLES OK" en het aantal, of met het aantal fouten. Met
`--snel` waren dat er op 27 september 2026 **399** met alles dicht (gemeten); met Chrome erbij
397, en met Vindioo er ook nog bij 376. Op 30 september kwamen er 22 bij voor het opvolgen van
favorieten, en toen is het opnieuw gemeten: **419** met Vindioo dicht en Chrome open (de twee
koppelcode-controles vallen dan weg, zoals hieronder beschreven), en 398 met Vindioo er ook bij. Twee dingen op deze pc laten controles wegvallen, en allebei zeggen ze dat ook:

- **Draait Vindioo zelf**, dan is de poort van de brug bezet en valt de hele brug-groep weg (23).
- **Draait Chrome met de brug-extensie**, dan klopt die elke 250 ms aan met de échte koppelcode.
  Het controleproject heeft een eigen gegevensmap en dus een andere code, dus voor zijn brug is
  dat een verkeerde - en dan staat `WrongCodeRecently` altijd aan. De twee controles die juist
  nakijken dat een webpagina die vlag niet kan zetten, vallen dan weg (397 in plaats van 399).

**Ze draaien ook op GitHub, bij elke push** (`.github/workflows/controles.yml`, 1 oktober 2026).
Een controle die enkel draait wanneer iemand eraan denkt, is geen controle - dat was het punt in
de codeanalyse van 30 september. De workflow doet precies wat je hier zelf zou doen: de app
bouwen in Release (de XAML-compiler vangt wat de controles niet zien) en dan
`dotnet run --project tests/Vindioo.Checks -- --snel`. `Check.Einde` geeft exitcode 1 zodra er
iets FOUT is, dus GitHub ziet het vanzelf.

**En de twee node-metingen van de extensie** (`tools/meet-brug-handtekening.mjs` en
`tools/meet-extensie-toegang.mjs`, erbij op 1 oktober 2026). Die stonden in `tools/` omdat je ze
met de hand kan draaien, en dat was precies het bezwaar: de extensie is JavaScript en komt in de
C#-controles niet voor, terwijl ze met jouw cookies werkt. `windows-latest` heeft Node al staan,
dus er is geen `setup-node` voor nodig. Ze zetten zelf exitcode 1 bij een fout.

Op `windows-latest`, want het is een WPF-project en een deel van de controles leest de
opdrachtregel van een proces uit via een Windows-API. Twee dingen zijn daar anders dan thuis, en
allebei zijn ze goed:

- **Er draait geen Vindioo en geen Chrome**, dus de brug-groep en de twee koppelcode-controles
  draaien er juist allemaal mee - thuis vallen er dan 23 weg.
- **De tijdzone is UTC**, en die kent geen zomertijd. De controle op de overgang naar de
  wintertijd merkt dat zelf (ze vergelijkt de verschuiving van de twee momenten) en slaat het
  stuk over dat een uur verschil verwacht. Schrijf een controle die van de klok afhangt dus
  altijd zo dat ze haar eigen tijdzone nakijkt.

Drie regels waar het aan vastzit:

- **Het compileert de broncode zelf mee** (`Models`, `Sources`, `Services`, zonder
  `TrayIcon.cs`) en verwijst niet naar `Vindioo.csproj`. Het heeft dus zijn eigen `bin` en
  `obj`, en botst niet met Visual Studio. Daarom staat in `Vindioo.csproj`
  `<Compile Remove="tests\**" />`: anders neemt de app die bestanden mee.
- **Nooit aan de echte gegevens.** `VINDIOO_DATA` wijst naar een nieuwe map in `%TEMP%`, gezet
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
python tools/vensterfoto.py Vindioo venster.png
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

Bij het **opstarten van de pc** kwam het ook voor, en dan loste een herstart van Vindioo het
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
set VINDIOO_SOFTWARE_RENDER=1
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
`X-Vindioo-Brug`, zodat enkel de extensie een verkeerde koppelcode kan melden).

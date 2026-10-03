# Snelheid: waar de tijd heen gaat, en het virtualiserende raster

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## Hoe snel is het, en waar gaat de tijd heen

Gemeten op **"cd"**, acht sites aangevinkt: **621 resultaten in 35 seconden**.
De sites zijn de bestanden uit `vindioo-sites`; ze staan hier als meetpunten, want wat
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
ouder dan deze Vindioo. .NET kan de opdrachtregel van een ander proces niet lezen; die
komt van `NtQueryInformationProcess` (klasse 60), waarvoor het beperkte leesrecht volstaat.

Tot september 2026 ging elk Chrome-proces zonder venster dicht dat jonger was dan een
minuut. Dat trof de gewone Chrome van de gebruiker: een tabblad dat net openging, en de
extensie van de brug wanneer Vindioo Chrome daarvoor net zelf gestart had. Sinds de
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

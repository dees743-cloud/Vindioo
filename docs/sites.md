# Sitebestanden: motoren, URL-stijlen, filters en kopregels

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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
hebben er geen nodig; een API wel.

**Ze gaan ook mee naar de pagina van een zoekertje** (27 september 2026). De brug deed dat al,
de gewone weg niet - en dan komt die pagina anders binnen dan de zoekpagina van diezelfde site.
Vinted maakte het zichtbaar: met de kopregel `Cookie: anonymous-iso-locale=nl-BE` antwoordt hij
in het Nederlands ("Goed", "20 uur geleden") en zonder in het Frans, dus stond er in het
detailvenster "Ajouté" bij *online sinds* terwijl de zoekpagina Nederlands was. Nagemeten in
`FotoChecks` met een proefsite die de kopregels van elk verzoek onthoudt (`Proefsite.Koppen`),
met de tegenproef: op de vorige code faalde die controle. De GraphQL-API van Discogs weigert elk verzoek
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


## Een gedeeld sitebestand is niet te vertrouwen

Een sitebestand is bedoeld om door te geven. Dat is de kracht ervan - en de reden dat er sinds
1 oktober 2026 naar gekeken wordt bij het **importeren** (`Services/SiteUrlCheck.cs`,
`SiteStore.Import`). Twee dingen kwamen uit de codeanalyse van 30 september 2026:

- **Waar het bestand de app naartoe stuurt.** De brug voert de zoek-URL uit in jouw eigen Chrome,
  met jouw cookies; zie `docs/brug.md` voor wat daar misgaat. Bij het importeren worden daarom de
  zoek-URL, het basisadres én de einddatum-API nagekeken: enkel `https`, en geen adres op je eigen
  netwerk of pc. Wordt een bestand geweigerd, dan zegt het scherm **waarom** - vroeger zag je
  enkel een bestandsnaam. Wat er wél binnenkomt, zet zijn hosts in het logboek, zodat na te lezen
  is waar een gedeeld bestand heen gaat (`SiteUrlCheck.Hosts`).

  Let op: dit geldt enkel bij **importeren**. Een site die je zelf toevoegt gaat via `Add` en mag
  gerust naar een lokale testsite wijzen - het gaat erom wat er van buiten binnenkomt.

- **De `Id` bepaalde waar er geschreven werd.** Die ging ongefilterd in `Path.Combine`, en dus
  schreef `"Id": "..\\..\\..\\..\\Temp\\ontsnapt"` buiten de sitesmap. Nagemeten met
  `Path.GetFullPath`: `%APPDATA%\Zentrix\sites` werd
  `%USERPROFILE%\Temp\ontsnapt.json`, en met een volledig pad (`C:\Windows\Temp\boos`) werd de
  sitesmap zelfs helemaal genegeerd. De `Id` gaat nu door `MakeSlug` - in `Add`, en nog eens in
  `FilePathFor` waar het pad werkelijk gemaakt wordt. Bestaande sites merken er niets van: hun
  Id's zijn al slugs.

Nagemeten in `ImportChecks`: veertien controles, van `http://` tot `file:///` tot een Id die uit
de sitesmap probeert te breken.

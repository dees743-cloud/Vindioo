# Een site toevoegen, en hoe de AI-analyse werkt

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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

**5. De pagina van één zoekertje wordt ook bekeken** (29 september 2026). De zoekpagina geeft
één foto en zelden meer dan een titel en een prijs; alles wat je daarna wil weten staat op de
advertentie zelf. Dat bleek het grootste gat van de analyse: van de dertien sitebestanden die met
de hand ingeregeld zijn, heeft **`DetailImagesSelector` er dertien**, de beschrijving tien, en de
verkoper en de datum elk negen - en de analyse keek daar niet eens naar. Nu opent ze na de
zoekpagina het eerste zoekertje en stelt ze een **tweede vraag**, met een eigen opdracht
(`DetailPrompt`) en een eigen schema van vier velden. Een aparte vraag, want het is een andere
pagina: de AI heeft die advertentie nooit gezien, en een galerij ziet er bij elke site anders uit.

**Wat dat mogelijk maakte, is niet de vraag maar het opschonen.** `Clean` gooit élk `<script>`
weg en geeft enkel de `<body>` terug - prima voor een zoekpagina, maar precies fout hier: bij
2dehands en Marktplaats staat **elke** foto in een `application/ld+json`-blok in de **`<head>`**,
en bij AutoScout24 in `__NEXT_DATA__`. Op de oude weg kon de AI die dus principieel niet vinden,
hoe goed ze ook keek. `SchoonAdvertentie` bewaart die blokken, kort ze in tot 6000 tekens en geeft
ze **apart** mee, zodat ze niet wegvallen wanneer de pagina afgekapt wordt - ze staan vaak
helemaal achteraan. Nagemeten op zes echte advertentiepagina's:

| Site | Oude opschoning | Nieuwe | Wat de selectors opleveren |
|---|---|---|---|
| 2dehands | 0 blokken | 3 | 7 foto's, "12 sep. '26", 129 tekens |
| Marktplaats | 0 | 3 | 8 foto's, "20 aug. '26", 493 tekens |
| AutoScout24 | 0 | 4 (met `__NEXT_DATA__`) | 29 foto's, verkoper, datum, 1414 tekens |
| Delcampe | 0 | 1 | 3 foto's, verkoper, 901 tekens |
| Vinted | 0 | 1 | 2 foto's, verkoper, 36 tekens |
| Tweakers V&A | 0 | 1 | 1 foto, 213 tekens |

Het antwoord wordt **nageteld zoals op de zoekpagina**: `MeasureDetailAsync` voert de vier
selectors uit met dezelfde motor die het detailvenster later gebruikt, en telt enkel foto's mee
die op een adres lijken - een `::match` dat naast de foto's grijpt, levert anders brokstukken
tekst op die er in een telling goed uitzien. Klopt er iets niet, dan gaat die telling terug en mag
de AI één keer verbeteren (`DetailRounds`). De verkoper krijgt daarbij een uitzondering: staat hij
al op de zoekpagina (`SellerSelector`), dan blijft `DetailSellerSelector` leeg, want dan is de
naam er meteen zonder die pagina op te halen.

**Wat de meting meteen vond, en dat is het punt van meten:** bij Tweakers kwam niet de advertentie
binnen maar **"DPG Media Privacy Gate"** - 517 tekens, 0 foto's. De regel voor een toestemmingsmuur
op een ander domein stond in `GenericSource` en `DetailFetcher`, maar niet in `SiteAnalyzer`. Die
staat er nu ook, in `TryDirectAsync`, dus ze geldt voor de zoekpagina én de advertentiepagina:
7.945 tekens werden 248.213.

Het kost een extra pagina en een extra vraag, samen zo'n kwartje bovenop de 50 cent van een
analyse. Drie leden zijn `internal` in plaats van `private` (`Clean`, `SchoonAdvertentie`,
`MeasureDetailAsync`, `TryDirectAsync`) zodat een wegwerpprojectje dit op echte pagina's kan
nameten; het controleproject mag geen netwerk gebruiken, en juist de echte pagina is hier het punt.

**6. Wat op de zoekpagina te zien is, wordt ook gevraagd** (29 september 2026). Twee velden
stonden bij "dat doet de analyse niet" terwijl ze gewoon te lezen zijn: **`isAuction`** (staat er
"Huidig bod", een aantal biedingen, een aftelklok op elke kaart?) en **`timeLeftSelector`**. Het
eerste bepaalt of een site mee mag tellen in de prijsindicatie - een bod dat nog loopt zegt niets
over wat iets waard is - en het tweede zet "Nog 3 dagen" achter de plaats. In het venster staan
ze nu als een veld en een vinkje.

Verder zijn er lessen bij in de opdracht gekomen die uit het inregelen van de dertien sites
komen en die er nog niet in stonden: `$=` en `^=` voor sites die het nummer van het zoekertje in
elk `data-testid` zetten (Vinted), `data-original` en andere lazy attributen die de motor **niet**
zelf probeert (die valt enkel terug tussen `src` en `data-src`), de vraagprijs nemen en niet het
totaal met kopersbescherming, en de waarschuwing dat een site in een andere taal kan antwoorden
dan de gebruiker later ziet - leun je in een `::match` op een woord, zeg dat dan in notes.

**7. De paginering wordt nagemeten in plaats van geloofd** (29 september 2026). Alles wat de AI
over `pageTemplate` zei, was tot dan een gok die niemand nakeek. Nu haalt de app **pagina 2** op
en telt hoeveel zoekertjes daarop ook al op pagina 1 stonden - precies wat er bij Delcampe met de
hand gebeurde ("0 overlap tussen pagina 1 en 2"). Een site die het paginanummer negeert, geeft
gewoon pagina 1 terug en meldt geen enkele fout; enkel die telling ziet dat. Klopt het niet, dan
gaat de paginering eruit.

Daar hoort een tweede ding bij: de AI mag nu de **zoek-URL zelf bijsturen** (`searchUrlTemplate`).
Dat was nodig omdat paginering soms in het pad zit, en `SearchUrlBuilder` `{page}` daar gewoon
aanvaardt - alleen wist de AI dat niet. Het bewijs stond al in het bestand dat ze in september zelf
maakte: *"Paginering gebruikt paden zoals /s-seite:2/cd/k0, dus geen pageTemplate mogelijk; de
motor kan enkel pagina 1 lezen."* Dat was niet waar. Twee eisen bewaken het: `{query}` moet erin
blijven en het moet dezelfde host zijn; daarna wordt eerst pagina 1 nagekeken en pas dan pagina 2.
Ook `{offset}` met `pageSize` kan nu, voor een API die in zoekertjes telt in plaats van in pagina's.

**En `robots.txt` wordt gelezen.** Geen verbod dat de app afdwingt - dat is een keuze van wie de
site toevoegt - maar het hoort op het scherm. Bij Tweakers V&A is de paginering met opzet
dichtgelaten omdat hun `robots.txt` dat zoekpad verbiedt, en dat stond nergens behalve in het hoofd
van wie het uitzocht.

**8. De grote foto wordt echt opgehaald** (29 september 2026), en dat kwam uit een fout van
mezelf. In punt 6 zette ik de les "staat het formaat in de query, laat die dan weg voor het
origineel" in de opdracht - waar bij 2dehands en Marktplaats 726 beeldpunten 1600 werden. De
analyse paste die les netjes toe op Kleinanzeigen, en daar is die `rule`-parameter **verplicht**.
Nagemeten op één foto van die site:

| Adres | Resultaat |
|---|---|
| zonder query (wat de AI voorstelde) | **HTTP 400** |
| `?rule=$_2.AUTO` | 157x200 |
| `?rule=$_57.AUTO` | **1164x1481** |
| `?rule=$_59.AUTO` | 755x960 |

De les is daarom voorwaardelijk gemaakt ("soms", met de raad een variant te nemen die élders in
de pagina staat), maar belangrijker is het vangnet: de analyse haalt nu één grote foto op en kijkt
of er een afbeelding terugkomt. Zo niet, dan gaat `LargeImageSelector` eruit en valt de app terug
op de miniatuur - beter dan een kapotte foto in het detailvenster. Eén verzoek per analyse.

**Wat de hele ketting oplevert, gemeten op Kleinanzeigen** (29 september 2026, de site die in
september enkel met de AI gemaakt was, dus de eerlijke vergelijking): **56 seconden**, één ronde,
en alles ingevuld. 27 zoekertjes, alle 27 met prijs, link, foto en plaats. De zoek-URL werd
`https://www.kleinanzeigen.de/s-seite:{page}/{query}/k0`; pagina 1 gaf daarmee 27 zoekertjes en
pagina 2 er 27 met 3 overlap (gesponsorde kaarten die op elke pagina terugkomen), dus aanvaard.
robots.txt verbiedt dit pad niet. De grote foto werd opgehaald en wérkte: 80 839 bytes JPEG. Op de
advertentiepagina: de verkoper, "14.09.2026" en 957 tekens beschrijving. Het bestand van september
had geen enkel Detail-veld en bleef op één pagina steken. Kosten: ongeveer 111 000 tokens tegenover
76 000 voor enkel de zoekpagina.

**De bijgestuurde fotoles werkte meteen.** In een eerdere run koos de AI `::match(^[^?]+)` - het
adres dat 400 geeft. Met de voorwaardelijke formulering koos ze
`img@src::replace($_2.AUTO,$_59.AUTO)`, dus de parameter blijft staan en enkel de variant gaat
omhoog, en ze schreef er zelf bij: *"de query-parameter weglaten is hier NIET geprobeerd en wordt
afgeraden"*. Ze zag in diezelfde ronde nog iets dat met de hand niet opgemerkt was: het
`ld+json`-blok van een advertentie bevat óók foto's van **andere advertenties van dezelfde
verkoper**, dus ze nam de thumbnaillijst van de lightbox.

Twee dingen om te weten bij het lezen van zo'n uitkomst. Twee runs op dezelfde site geven **niet
hetzelfde bestand** - de ene koos `article[data-adid]` als kaart, de andere
`#srchrslt-adtable > li[data-clickable='card']` - en dat is geen fout zolang de telling klopt; de
telling is het oordeel, niet de selector. En de fotocontrole zegt enkel dát er foto's uitkomen,
niet dat het er genoeg zijn: één foto op een advertentiepagina kan kloppen (een advertentie met één
foto) of betekenen dat de galerij pas met JavaScript gevuld wordt. Dat onderscheid maakt de app nog
niet.

De AI vult nu ook de **korte naam**, de **grote foto** en de **volgende pagina** in, en
het venster toont die als bewerkbare velden - net als de vier velden van de advertentiepagina.

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
- De laatst geanalyseerde pagina staat in `%APPDATA%\Vindioo\laatste-analyse.html` (of
  `.json`). Dat bestand kwam vroeger op het bureaublad terecht.
- De Services-map heeft geen globale `using System.IO` in dit WPF-project: schrijf die
  zelf bovenaan, of `Path` en `File` bestaan niet.
- **Zonder API-sleutel** toont het venster bovenaan meteen een veld om de sleutel te
  plakken. Die wordt **beschermd door Windows** bewaard (DPAPI, in `instellingen.json`), net
  als het mailwachtwoord en het Telegram-token - zie `docs/zoeken.md`.

  Tot 1 oktober 2026 ging hij naar de **omgevingsvariabelen** van het Windows-account. Dat
  léék veiliger dan een bestand, want hij stond niet in de code, maar het is het omgekeerde:
  elk programma dat onder jouw account draait leest hem zo, hij staat zichtbaar in het
  systeemscherm van Windows, en hij reist mee naar élk proces dat de app start - ook naar de
  Chrome die Playwright opent. Gevonden in een codeanalyse van 30 september 2026.

  `ANTHROPIC_API_KEY` blijft wél werken als terugval, want wie hem daar zelf zet verwacht niet
  dat de app hem negeert - een wegwerpprojectje bijvoorbeeld. Stond hij er bij het opstarten en
  nergens anders, dan **verhuist** hij één keer naar het instellingenbestand, met een regel in
  het logboek erbij; de variabele zelf blijft staan, want die weghalen is een wijziging aan het
  Windows-account van de gebruiker. Wie dat wil, doet het zelf:

  ```bash
  [Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", $null, "User")
  ``` Vroeger kwam de melding pas na
  het klikken op Analyseren, met de raad een omgevingsvariabele te zetten. Weigert de API de
  sleutel (401), dan verschijnt dat veld opnieuw; daarvoor kon je een verkeerde sleutel enkel
  in de omgevingsvariabelen van Windows vervangen. Het veld zegt ook waar je een sleutel maakt,
  en dat de volledige zoekpagina naar Claude gaat.
- **Aanmelden bij een site** kan op de kaart van die site: tandwiel > Sites beheren > tab van de
  site > Aanmelden (`SettingsWindow.LoginButton_Click`), enkel bij sites die de browser van de app
  gebruiken. Dat stond vroeger enkel in "Site toevoegen", en dan maakte je een lege nieuwe
  site aan om je bij een bestaande aan te melden.

**9. De filters, en waarom die als laatste kwamen** (29 september 2026). Negen van de dertien
sitebestanden hebben een `Filters`-mapping en acht hebben eigen filters, en de analyse liet
allebei leeg - het laatste grote gat. Het is ook het moeilijkste, want **een filter kan je niet
zien**. Een parameter die er goed uitziet, kan door de site aanvaard en meteen genegeerd worden;
dat is precies de valkuil van 2dehands in `SITES.md`.

Daarom: de AI **stelt voor**, de app **meet na**. De vraag gaat als gewoon bericht verder in
hetzelfde gesprek, zodat de pagina niet nog eens mee hoeft. Wat er daarna gebeurt met elk
voorstel:

- De app haalt pagina 1 nog eens op met dat ene filter erbij en vergelijkt de sleutels met de
  ongefilterde pagina.
- **Bij een prijsgrens telt ze de prijzen na.** Ze rekent zelf de mediaan van pagina 1 uit - dat
  moet ongeveer de helft wegsnijden - en kijkt daarna of de teruggekomen prijzen binnen die grens
  bleven. Komen er duurdere terug, dan is het filter *aanvaard maar genegeerd* en gaat het eruit.
- Alleen bekende sleutels tellen als vast filter: `priceMin`, `priceMax`, `priceRangeEuro`,
  `priceRangeCents`, `postcode`, `location`, `radius`, `radiusMeters`. Een andere naam doet niets,
  want `SearchUrlBuilder` vult hem nooit in; die hoort bij de eigen filters.
- Hoogstens acht filters worden uitgeprobeerd - elk kost een paginabezoek.

**De ruis was groter dan gedacht, en dat kwam uit de meting.** De eerste versie stuurde één
controle mee: een parameter die niet bestaat. Op Vinted veranderde dát al **17%** van de eerste
pagina - er komen nu eenmaal zoekertjes bij terwijl je meet - en daardoor haalde een verzonnen
filter de drempel en bleef het staan. Nu gaan er **twee** controles mee, telt de grootste, en ligt
de drempel op `max(20%, 2 x ruis + 10%)`. Nagemeten op Vinted, met de echte filters uit
`vinted.json` en opzettelijke rommel ernaast:

| Voorstel | Veranderde | Oordeel |
|---|---|---|
| `priceMax` = `&price_to={value}` | 48% | gehouden |
| `priceMin` = `&price_from={value}` | 49% | gehouden |
| `radius` = `&straalinkm={value}` (bestaat niet) | 9% | eruit |
| eigen filter `&onzinfilter={value}` (bestaat niet) | 2% | eruit |

De site verschoof zelf 12%. Wat eruit kwam, is letterlijk wat er met de hand in `vinted.json`
staat. Verschuift een site meer dan 40% vanzelf, dan zegt de app dat filters daar niet na te meten
zijn en laat ze alles leeg - liever niets dan een filter dat stil niets doet.

**Liever een filter te weinig dan een filter dat er staat en niets doet.** Dat is de afweging
achter die drempel: een ontbrekend filter zie je meteen, een filter dat stil genegeerd wordt niet.
Het venster toont daarom per voorstel wat het deed en waarom het eruit ging, zodat je het met de
hand kan terugzetten.

**Live nagemeten op Vinted** (30 september 2026), een volledige analyse van begin tot einde in
**2 minuten**. Van de vijf voorstellen bleven er drie over:

| Voorstel | Veranderde | Oordeel |
|---|---|---|
| `priceMin` = `&price_from={value}` | 51% | gehouden |
| `priceMax` = `&price_to={value}` | 52% | gehouden |
| eigen filter `&status_ids={value}` (Staat, 5 keuzes) | **94%** | gehouden |
| `&order={value}` | 10% | eruit |
| `&catalog={value}` | 10% | eruit |

De eerste twee zijn precies wat er met de hand in `vinted.json` staat. **Het derde stond daar
niet**: de AI gaf er zelf bij dat die id's "op ervaring gebaseerd" waren en niet in de pagina
stonden, en de meting hield het. Apart nagekeken op de staat die Vinted per zoekertje toont -
dat is bij die site `DescriptionSelector` - en alle vijf de waarden kloppen: `status_ids=6` gaf
96 van de 96 "Neuf avec étiquette", `=2` 96 van de 96 "Très bon état", `=4` 96 van de 96
"Satisfaisant". Een filter dat handwerk gemist had.

Dat `order` eruit ging is ook juist: een sortering is geen filter. En bij `catalog` twijfelde de
AI zelf ("of catalog[]= als queryparameter werkt, moet de test uitwijzen") - de test zei nee.

**De filtervraag was eerst een tweede vraag, en dat kostte geld.** Ze ging als bericht verder in
hetzelfde gesprek, met de bedoeling dat de pagina uit de cache kwam. Dat deed ze **niet**: gemeten
stond er `naar cache 75122, uit cache 0`, terwijl een gewone verbeterronde er wél 71 220 uit de
cache las. Het verschil is het **schema** - dat hoort bij het gecachete begin, dus een andere vorm
van antwoord betekent een nieuwe cache. Eén analyse kostte zo 186 000 tokens.

Daarom staan `filters` en `customFilters` sinds 30 september 2026 **in het schema van de eerste
vraag**. Er is geen tweede vraag meer nodig: de app meet de filters toch zelf na, dus ze hebben die
verbeterronde niet. Opnieuw gemeten op Vinted: **113 000 tokens**, en de uitkomst bleef dezelfde.

**Bij een prijsgrens telt de prijs, niet het verschil.** Dat moest bijgesteld worden, en de meting
wees het aan. In één run haalde `priceMax` 41% tegen een drempel van 41% en vloog eruit; een dag
eerder haalde diezelfde parameter 52% en bleef hij staan. Dezelfde site, dezelfde parameter, ander
toeval - en een prijsfilter dat stil ontbreekt is precies wat je niet wil. Daar is een veel sterker
bewijs voor: zonder filter ligt per definitie de helft van de zoekertjes boven de mediaan. Blijft
daar na het filteren vrijwel niets van over, dan werkt hij, hoe druk de site ook is; blijven er
duurdere staan, dan is hij aanvaard en genegeerd. Sindsdien beslist die telling bij `priceMin` en
`priceMax`, en de set-vergelijking enkel bij de rest. In de run erna: **3 van de 3 gehouden**, met
als reden "de prijzen bleven binnen de grens van 5" - terwijl de oude regel `priceMax` (43% bij 18%
ruis, drempel 45%) opnieuw zou hebben laten vallen.

En één notatiefout kostte een ronde: de AI schreef `[data-testid$='--image']@src, img@src`. De motor
knipt bij het **laatste** apenstaartje (`LastIndexOf('@')`), dus daar bleef ongeldige CSS over. Dat
staat nu in de opdracht: bij een komma-lijst hoort het `@attribuut` er één keer bij, achteraan.

**10. De pagina die geanalyseerd wordt, is niet te vertrouwen** (1 oktober 2026). Dat klinkt
vanzelfsprekend, maar het heeft een gevolg dat makkelijk over het hoofd te zien is: het
*antwoord* van de AI is gebaseerd op die pagina. Verborgen tekst erin kan het model vragen iets
anders neer te zetten dan wat er te zien is.

Het aantrekkelijkste veld daarvoor is **`baseUrl`**, want dat vult élke relatieve link en élke
relatieve foto aan (`MakeAbsolute`). Stond daar een vreemde host, dan haalde de app voortaan
daar vandaan - en bij een brugsite doet jouw eigen Chrome dat, mét jouw cookies. Voor
`searchUrlTemplate` werd dat al nagekeken sinds punt 7; voor `baseUrl` niet.

`VeiligeBasis` eist nu **dezelfde host én hetzelfde schema** als de zoek-URL. Dat tweede is er
niet voor niets: anders zou `http://dezelfde.site` aanvaard worden op een zoekpagina die https
is, en haalt de app voortaan alles onversleuteld op zonder dat iemand het ziet. Wat niet door de
controle komt, valt terug op de host van de zoek-URL zelf, met een regel in het logboek.

Het kost niets: een relatief pad hoort per definitie bij de site waar het staat. Een eigen
testsite op `127.0.0.1` werkt gewoon - het gaat erom dat het antwoord niet van de zoekpagina
mag afwijken, niet om waar die staat. Nagemeten in `ImportChecks`.

**Wat hier nog openstaat**, en dat is bewust niet meegenomen: een selector mag met `::replace`
ook het adres van een **foto** herschrijven (`img@src::replace(250x188,1024x768)`), en in
principe kan daar een andere host in geschoven worden. De schade is kleiner - die foto's worden
opgehaald zonder jouw cookies, dus het is hoogstens een verzoek naar een vreemde server - maar
het is dezelfde soort injectie. De fotocontrole (punt 8) merkt het niet: een vreemde server kan
gewoon een geldige foto terugsturen.

Wat de analyse **nog steeds niet doet**, en waar je dus zelf aan moet: `Headers`,
`AllowsEmptyQuery`, `PriceReference`, `SellerSelector`, `AuctionSellers`,
`DetailEndDateSelector`, `DetailPriceSelector`, `EndTimeApi` en een eigen `UrlStyle`. `Headers`
en `EndTimeApi` zijn niet te raden: die zijn er gekomen door te proberen, niet door te kijken.
`DetailPriceSelector` is er pas sinds 2 oktober 2026 en hoort bij favorieten, niet bij het
detailvenster; de prompt vraagt nog altijd om vier `Detail`-velden.

Sinds 29 september 2026 vult ze wél in: de vier `Detail`-velden (punt 5), `IsAuction` en
`TimeLeftSelector` (punt 6), de paginering in het pad of met een offset (punt 7), en de filters
(punt 9).

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

# Hoe een site binnenkomt: rechtstreeks, de browser of de brug

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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

Een Chrome-extensie ("Vindioo Brug", uitgepakt geladen uit de map `extension\` van dit project)
vraagt elke 250 ms aan `http://127.0.0.1:8731/job` of er werk is, opent de URL
in een tabblad op de achtergrond en stuurt de HTML terug naar `/result`. De
koppeling gebeurt met een code uit `brug-code.txt`, zichtbaar via tandwiel >
*Koppelcode* in de app.

De eerste keer zijn er dus **twee** stappen in de popup van de extensie: de koppelcode plakken,
en de sites aanvinken die ze mag openen. Dat tweede sinds 1 oktober 2026 - zie "Toegang per site"
onderaan.

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
  (verbonden, verkeerde code, geen code, Vindioo draait niet);
- na "Code opslaan" vraagt de popup meteen `/ping`, dus je ziet bij het plakken of de code klopt;
- werkt de brug niet, dan **slaat de zoekopdracht de brugsites meteen over** met die reden,
  in plaats van per site 90 seconden te wachten;
- een opdracht waarop niemand meer wacht, slaat `/job` over, zodat Chrome geen tabbladen
  meer opent voor zoekopdrachten die allang voorbij zijn.

**Een verkeerde code telt enkel als ze van de extensie komt.** Elke webpagina in elke browser
op deze pc kan `127.0.0.1:8731` aanspreken. De koppelcode houdt haar buiten, maar tot september
2026 kon ze met een verzonnen code wel `WrongCodeRecently` aanzetten, en dan sloeg de app alle
brugsites over. Nu stuurt de extensie de kopregel `X-Vindioo-Brug` mee, en telt een foute code
enkel met die kopregel. Een webpagina kan zo'n eigen kopregel niet sturen zonder eerst
toestemming te vragen (een CORS-voorvraag), en die toestemming krijgt enkel nog een
`chrome-extension://`-herkomst: de brug antwoordde vroeger met `Access-Control-Allow-Origin: *`
en `Access-Control-Allow-Headers: *`. Nagemeten met verzoeken zoals een webpagina en de extensie
ze sturen. **Na deze wijziging moet de extensie herladen worden**; een oude extensie werkt
verder, maar een verkeerde code heet dan weer "de extensie meldt zich niet".

**De brug neemt niet alles aan** (22 september 2026, een tip uit een beoordeling door ChatGPT).
Tot dan las ze elke kop en elke body tot het einde, en reserveerde ze meteen de maat die een
verzoek aankondigde: `Content-Length: 1500000000` legde 1,5 GB vast nog voor er één byte binnen
was, en een verbinding die zweeg, bleef open tot Vindioo stopte. Nu, in `HandleClientAsync`:

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

## Een site achter Cloudflare

Cloudflare kan de app weigeren op de vingerafdruk van zijn TLS-handdruk: .NET en een
headless Playwright krijgen "Just a moment...", een echte browser niet. Dan is de brug
de weg, en voor een API de tweede soort brugopdracht (`rawText`, zie "Twee soorten
opdrachten"). Discogs is daar het voorbeeld van; het hele verhaal, met wat we
probeerden en waarom het niet lukte, staat in `SITES.md` van `vindioo-sites`.

## Een toestemmingsmuur op een ander domein

Sommige sites sturen een eerste bezoek naar een **tussenpagina op een ander domein**: een
toestemmingsmuur. Bij Tweakers is dat `myprivacy.dpgmedia.nl`. Die muur zet een sessiecookie en
laat het **volgende** verzoek gewoon door - 296 kB met de echte pagina erin.

Daarom, sinds 27 september 2026: belandt een verzoek op een ándere host dan gevraagd, dan
probeert de app het **één keer** opnieuw. De `HttpClient` houdt zijn cookies bij (`SocketsHttpHandler`
doet dat vanzelf), dus dat tweede verzoek slaagt. Het staat op de twee plaatsen die zelf een
pagina ophalen: `GenericSource` (de zoekpagina) en `DetailFetcher` (de pagina van een zoekertje).

Drie dingen om te weten:

- **Er wordt niets aanvaard.** Er gaat geen toestemmingskeuze mee, enkel een sessie. Een cookie
  die enkel het noodzakelijke aanvaardt (`dpg-consent-string=functional`) hielp trouwens niet:
  gemeten, de muur bleef staan. Wat wél werkte was gewoon een tweede verzoek.
- **Zonder koekjespot lukt het nooit.** Dat was precies het verschil tussen `curl` (altijd de
  muur) en een browser (meteen de pagina), en het kostte een halve middag om te zien.
- **Het kost niets bij een site zonder muur.** De regel kijkt enkel naar de host waarop je
  uitkwam; komt die overeen, dan is er één verzoek zoals altijd.

Nagemeten in `PaginaChecks` met een proefsite die het eerste verzoek doorstuurt naar
`localhost` (zelfde proefsite, ander domein) en daar een pagina zónder resultaten geeft. Met de
tegenproef: op de vorige code viel de zoekopdracht om met "gaf een bijna lege pagina terug (64
tekens) in plaats van resultaten" - het vangnet tegen stil falen pakte het dus al, maar er kwam
nooit een resultaat binnen.


## Waar de brug heen mag

**De extensie weigert sinds 1 oktober 2026 zelf een opdracht naar een onveilig adres**
(`waaromNiet` in `extension/background.js`, versiestempel 7, manifest 1.8).

Waarom dat nodig was, kwam uit een codeanalyse van 30 september 2026, en het is het punt met de
hoogste ernst dat daaruit kwam. De extensie is het stuk dat met **jouw** cookies werkt: ze opent
de zoek-URL in jouw Chrome, en bij een API-opdracht doet ze daar een `fetch` met
`credentials: "include"`. Dat adres komt uit een **sitebestand**, en zo'n bestand is bedoeld om te
delen - je krijgt het van iemand anders. Een verzonnen zoek-URL kon jouw aangemelde browser dus
verzoeken laten doen naar eender welke site, of naar een adres op je eigen netwerk; de
beheerpagina van een router vraagt daar vaak niet eens een aanmelding voor.

Wat er nu geweigerd wordt: een ander schema dan `https`, een naam zonder punt (`router`), de
lokale achtervoegsels (`.local`, `.localhost`, `.internal`, `.home`, `.lan`), de privé-reeksen
van IPv4 (10/8, 172.16/12, 192.168/16, 169.254/16, 127/8, 0/8) en de lokale adressen van IPv6.
De app zegt dan gewoon dat die site mislukte, met de reden erbij.

**Twee sloten, want één vergeet je.** Dezelfde controle staat aan de kant van de app in
`Services/SiteUrlCheck.cs`, en die draait bij het **importeren** van een sitebestand - zie
`docs/sites.md`. Het verschil is dat een extensie in Chrome blijft staan terwijl de app
verandert: wie een oude extensie heeft, heeft het slot van de app nog wel.

**Na deze wijziging moet de extensie herladen worden** (`chrome://extensions` → 🔄), anders draait
Chrome de oude code verder. Zo zie je welke versie er loopt: in de console van de service worker
staat `[brug] versie 7 geladen`.

Nagemeten op dertien adressen, met de functie los uitgevoerd: de twee gewone zoek-URL's van
2dehands en Marktplaats mogen, en `http://`, `192.168.1.1`, `127.0.0.1`, `10.0.0.5`, `172.20.1.1`,
`169.254.1.1`, `router`, `nas.local`, `file:///`, `[::1]` en `onzin` worden alle elf geweigerd.

## De koppelcode gaat niet meer over de lijn

**Sinds 1 oktober 2026 tekenen de app en de extensie elkaars berichten** in plaats van de
koppelcode mee te sturen (`BridgeServer.Teken`, `teken()` in `background.js`, extensie 1.9 /
stempel 8).

Het lek was dit: de extensie stuurde `?token=<koppelcode>` naar wie poort 8731 ook maar
vasthield. Een ander programma dat die poort **eerst** bezet, kende daarmee de code - en kon van
dan af jouw aangemelde browser pagina's laten ophalen, met jouw cookies, en het antwoord
meelezen. Gevonden in een codeanalyse van 30 september 2026, als een van de drie punten met
ernst "middel".

Hoe het nu gaat. Per verzoek maakt de extensie een **nonce**: een wegwerpgetal, geen geheim, dus
dat mag gewoon in het adres. Daarnaast gaan er twee kopregels mee:

| kopregel | waarover | waarvoor |
|---|---|---|
| `X-Vindioo-Sig` | `nonce \n body` | bewijst dat dit bericht van iemand komt die de code kent, en dat de body onderweg niet veranderd is |
| `X-Vindioo-Voor` | enkel `nonce` | hetzelfde bewijs, maar **al na te kijken met enkel de kopregels in de hand** |

**En de app tekent haar antwoord óók.** Dat is de helft die de extensie beschermt: zij voert uit
wat uit `/job` komt, dus zij moet weten dat ze met de echte Vindioo praat. Klopt de handtekening
niet, dan gaat er geen tabblad open.

**Waarom twee handtekeningen, en niet één.** De brug kon de koppelcode vroeger nakijken *voor* ze
een body las - die stond immers in het adres - en daar hing een rem aan: "wie de code niet kent,
krijgt nooit een body gelezen, hoe groot die ook zegt te zijn". Een handtekening óver de body kan
dat per definitie niet. Zonder die tweede kopregel zou die rem er dus stilletjes uit zijn
gegaan; dat kwam boven doordat een bestaande controle (1,5 GB aangekondigd → 413) ineens een 200
gaf.

**Een extensie van vóór 1 oktober 2026 werkt niet meer**, en de app zegt dat ook zo:
`BridgeStatus.OldExtension` ("de Vindioo Brug in Chrome is een oudere versie. Herlaad ze:
chrome://extensions...") in plaats van over de koppelcode te klagen, want daar is niets mis mee.
Die versies stuurden de code nog in het adres (`?token=`), en dat kan niet meer.

**De hernoeming naar Vindioo is wél overbrugd.** De kopregels heetten tot 3 oktober 2026
`X-Zentrix-Brug`, `-Sig` en `-Voor`. De app blijft die aanvaarden in een verzoek
(`BridgeServer.Kopregel`), en tekent haar antwoord onder **allebei** de namen met dezelfde
waarde. Dat laatste is nodig omdat de extensie apart herladen wordt: wie de zip bijwerkt maar
zijn broncode niet, draait een nieuwe app met een oude extensie.

> Tot 4 oktober 2026 werkte die overgang maar half, en dat was erger dan ze niet te hebben. Het
> verzoek werd aanvaard, maar het antwoord werd enkel onder `X-Vindioo-Sig` getekend. De oude
> extensie vond haar eigen kopregel niet, besloot "dit is Vindioo niet" en nam **geen enkele**
> opdracht aan - terwijl de app een geslaagd verzoek zag, `BridgeStatus.Ready` meldde en
> 90 seconden per brugsite wachtte op een antwoord dat nooit kwam. De popup in Chrome zei
> intussen "verkeerde koppelcode". Twee meldingen die allebei langs de oorzaak keken.
> `BridgeStatus.OldExtension` sloeg niet aan, want dat pad kijkt naar `?token=`.

`Teken()` bindt de handtekening aan de koppelcode en de inhoud, **niet** aan de naam van de
kopregel. Twee namen met dezelfde waarde verzwakt dus niets: er is geen naam die een zwakkere
controle aanroept, en wie allebei de namen meestuurt wint niets. Er staat een controle op dat
een verzoek met enkel de oude kopregels een antwoord krijgt dat allebei de handtekeningen
draagt, met dezelfde waarde.

Omgekeerd geldt nog altijd: een nieuwe extensie met een oude app krijgt geen geldige
handtekening terug en weigert dan elke opdracht.

Nagemeten, allebei de kanten:

- **Dat C# en JavaScript hetzelfde tekenen**, met waarden die met Node uitgerekend zijn - dus
  tegen een onafhankelijke implementatie en niet tegen zichzelf (`BrugChecks`, en die groep
  draait óók met Vindioo open, want ze heeft de poort niet nodig).
- **Dat de app weigert** zonder handtekening, met die van een andere code, en bij een webpagina
  met een verzonnen handtekening; dat haar antwoord getekend is; en dat een oud verzoek
  "verouderde extensie" krijgt (`BrugChecks`, met de poort vrij).
- **Dat de extensie weigert** wat niet van de app komt: `tools/meet-brug-handtekening.mjs` draait
  de échte `vraagApp()` uit `background.js` tegen een nep-app op een vrije poort. Die zegt ook of
  de koppelcode nog ergens in een adres of kopregel opduikt - dat doet ze niet.

```bash
node tools/meet-brug-handtekening.mjs
```

## Wie mag er met de brug praten

Twee dingen erbij op 1 oktober 2026, naast het tekenen hierboven.

**De Host-kopregel wordt nagekeken** (`GastheerOk`). De brug luistert enkel op 127.0.0.1, maar
dat zegt niet dat elk verzoek daarvandaan komt zoals je denkt: een webpagina kan haar eigen naam
naar 127.0.0.1 laten wijzen - *DNS-rebinding* - en dan praat jouw browser met ons. Daar is het
aan te zien, want de browser stuurt die naam mee als `Host` in plaats van het adres. Enkel
`127.0.0.1`, `localhost` en `[::1]` komen er nog door; de rest krijgt een **400**, ook met een
geldige handtekening.

**De CORS-kopregels komen er enkel bij een verzoek dat zich bewezen heeft.** Tot dan kreeg élke
`chrome-extension://`-herkomst ze. Een andere extensie in jouw Chrome kan wel tegen de poort
praten - dat kan elk programma op deze pc - maar zonder `Access-Control-Allow-Origin` houdt de
browser het antwoord bij haar weg. De voorvraag (`OPTIONS`) blijft wel beantwoord worden: die kan
geen eigen kopregels dragen en kan zich dus niet bewijzen, en ze verklapt ook niets.

**Waarom er géén vast extensie-ID in de code staat.** De codeanalyse stelde dat voor, en het is
hier niet de juiste oplossing: een **uitgepakte** extensie krijgt haar ID van Chrome, per
installatie verschillend. Eén ID vastleggen zou bij iedereen behalve op deze pc breken, en
"onthouden welke zich het eerst meldde" geeft een brug die stilvalt zodra je de extensie uit een
andere map laadt - met een foutmelding die niemand kan plaatsen. De eigenschap die je ervan wil,
is dat enkel ónze extensie het antwoord kan lezen, en dat is precies wat de twee regels hierboven
doen: wie de koppelcode niet kent, krijgt geen CORS-kopregels en kan niets lezen.

Nagemeten in `BrugChecks`: een vreemde `Host` wordt geweigerd (ook met een geldige
handtekening), `localhost` mag, een extensie zonder de code krijgt geen
`Access-Control-Allow-Origin`, en met de code wel.

## Toegang per site

Tot 1 oktober 2026 stond er in `manifest.json` dit:

```json
"host_permissions": ["<all_urls>", "http://127.0.0.1/*"]
```

Dat is: toegang tot **elke** site in je browser, met jouw cookies, vanaf het moment dat je de
extensie installeert. Het was het laatste van de twintig punten uit de codeanalyse van 30
september 2026, met ernst "laag" - en dat is te laag ingeschat, want het ligt in de verlenging
van wat de analyse zelf elders wél "middel" noemde.

**Waarom het meer is dan een lelijke regel in een bestand.** Het adres van een opdracht komt uit
een sitebestand, en zo'n bestand krijg je van iemand anders. `waaromNiet()` in `background.js`
houdt al het halve internet buiten - geen `http`, geen adressen op je eigen netwerk - maar een
verzonnen bestand dat naar een gewone https-site wijst, kwam daar netjes door. Naar je webmail
bijvoorbeeld. En wat de extensie opent, leest ze uit en stuurt ze naar de app.

Nu staat er:

```json
"host_permissions": ["http://127.0.0.1/*"],
"optional_host_permissions": ["https://*/*"]
```

Die tweede regel is **geen** toestemming, enkel het recht om ze te vragen. Chrome noemt dat
zelf de manier voor hosts die je pas tijdens het draaien kent. Bij de start heeft de extensie dus
toegang tot niets behalve de app op deze pc, en jij geeft ze per site.

**Waar de knop moet staan, en waarom er geen keuze is.** `chrome.permissions.request()` aanvaardt
Chrome enkel uit een **gebruikersklik**. Een achtergrondscript heeft die nooit, dus daar is
toestemming vragen onmogelijk - het kan enkel weigeren en onthouden waarvoor. De knop staat
daarom in de popup, en het achtergrondscript geeft haar de lijst.

Die lijst komt uit twee bronnen, en samen dekken ze alles:

| bron | wat ze weet | waarvoor ze er is |
|---|---|---|
| `/hosts` bij de app | de hosts van de sites met `UseBridge` (`SiteUrlCheck.Hosts`) | je vinkt ze aan **voor** er iets misloopt, in één Chrome-venster |
| `chrome.storage.local`, sleutel `nodig` | elke host die onderweg geweigerd werd | een site die nog niet in Vindioo staat (een nieuwe, die je laat analyseren), en een site die doorverwijst naar een andere naam |

`/hosts` zit achter dezelfde handtekening als de rest: welke sites er op deze pc gezocht worden,
is op zichzelf al iets over de gebruiker.

**Twee plaatsen waar de extensie nee zegt**, en het verschil ertussen is het hele punt:

1. **Vóór `chrome.tabs.create`** (`mag(job.url)`). Een tabblad openen stuurt al een verzoek **mét
   jouw cookies**; of we de pagina daarna mogen uitlezen, is dan te laat. Daarom staat deze
   controle voor het openen en niet erna.
2. **In `waitForDom`**, voor de doorverwijzing (`waaromGeenToegang`). Je gaf toestemming voor
   `voorbeeld.be` en de site stuurt je naar `www.voorbeeld.be`: dan geeft Chrome ons wel een
   tabblad, maar weigert het uitlezen. Zonder dit onderscheid liep elke `executeScript` op een
   uitzondering en liepen de 45 seconden van `waitForDom` gewoon vol - met "Cannot access
   contents of the page" als melding, wat niemand verder helpt.

In beide gevallen krijgt de app dezelfde zin te horen: *"de brug mag nog niet aan www.site.be -
klik op het Vindioo-pictogram in Chrome en geef toegang"*. Die staat dan in Vindioo bij die site.

**Wat je er zelf van merkt.** Na het herladen van de extensie staat er niets aangevinkt: open de
popup, en onderaan staan de sites met een vinkje of een streepje. Eén knop vraagt ze in één
Chrome-venster tegelijk. Die popup gaat bij dat venster dicht - de toestemming wordt wél gegeven;
open haar opnieuw en de vinkjes staan er.

Nagemeten:

- **`tools/meet-extensie-toegang.mjs`** draait de échte `handleJob()` uit `background.js` tegen
  een nagebootste Chrome: zonder toestemming gaat er **geen tabblad** open, komt er geen pagina
  mee, en zegt de melding welke site het is en wat je eraan doet; met toestemming werkt het
  gewoon; toestemming voor één site is er geen voor de volgende; en een doorverwijzing wordt
  binnen de milliseconden gemeld.
- **De tegenproef** (de controle vóór het tabblad tijdelijk uit): 3 van de 19 vallen om, en de
  eerste is "er gaat geen tabblad open" - dat is de enige die telt. De melding kwam er in die
  proef nog steeds, want het tweede slot vangt het, maar het tabblad was al open.
- **De tweede tegenproef** (de controle op de doorverwijzing uit) gaf precies waarvoor ze er is:
  **45 169 ms** en "Cannot access contents of the page.".
- **`BrugChecks`** op wat de app doet: `/hosts` geeft de brugsites door, wie de koppelcode niet
  kent krijgt die lijst niet, en een lijst die onderweg omvalt (het instellingenvenster voegt net
  een site toe) geeft een leeg antwoord in plaats van een stukke verbinding. Plus een groep die
  het manifest zelf naleest, want `"<all_urls>"` terugzetten is één woord typen.

```bash
node tools/meet-extensie-toegang.mjs
```

Allebei de node-metingen draaien sinds vandaag mee in de CI. Ze stonden in `tools/` omdat je ze
met de hand kan draaien, en dat is precies waarom ze erbij moesten: wat ze meten werkt met jouw
cookies, en de C#-controles raken geen JavaScript.

## De andere kant op: rechtsklikken op een zoekertje

Erbij op 2 oktober 2026 (extensie 2.1). Rechtsklik in Chrome op een zoekertje - op de link in een
lijst, of ergens op de advertentiepagina zelf - en kies **Zet in favorieten van Vindioo**.

**Dit is de enige weg die deze kant op gaat.** Overal elders geeft de app werk aan de extensie en
haalt die het op; hier stuurt de extensie iets dat de app niet gevraagd heeft, en dan nog een
webadres dat de app **zelf gaat ophalen**. Vandaar drie sloten, en ze doen alle drie iets anders:

| waar | wat | waarom juist daar |
|---|---|---|
| `background.js` | `waaromNiet(adres)` voor er iets vertrekt | een `javascript:`-link of een adres op je eigen netwerk hoort de poort niet eens te bereiken |
| `BridgeServer` | dezelfde handtekening als elk ander pad | een webpagina die poort 8731 vindt, mag de app geen verzoeken laten doen |
| `FavoriteFromUrl` | https, geen privé-adres, en een **bekende** site | de app gaat dit adres ophalen, met haar eigen netwerk |

Dat laatste slot is niet enkel beveiliging. Zonder sitebestand is er geen naam voor de bron en
geen `IdPattern` om een id uit de link te halen - en dan zou dezelfde kavel **twee keer** in je
favorieten staan: één keer via het zoekresultaat en één keer via Chrome, met twee verschillende
sleutels. Nu krijgt hij allebei de keren hetzelfde id.

**Volgparameters tellen niet mee voor de identiteit** (`GenericSource.SchoonAdres`, 2 oktober
2026). Vijf van de dertien sites hier hebben een `IdPattern` en zijn dus veilig; bij de acht
andere **is de link zelf** de identiteit. Het adres dat uit Chrome komt is het adres zoals jij het
voor je hebt, en daar hangt vaak een `?fbclid=...` of een `#foto2` aan die in het zoekresultaat
niet staat - en dan krijg je toch twee kaarten. Die gaan er nu af, samen met het stuk achter `#`.

Drie dingen om te weten als je daaraan raakt:

- **De lijst staat bij naam en is kort.** `ref`, `source` en `id` staan er met opzet **niet** in:
  op sommige sites dragen die wél betekenis. Liever een dubbel zoekertje dan twee verschillende
  kavels die als één tellen.
- **Alleen de identiteit wordt opgeschoond, niet de link zelf.** Die blijft staan zoals hij is,
  want hij moet het nog doen als je hem aanklikt.
- **Met tekstbewerking en niet via `Uri`.** Dat laatste schrijft een adres soms anders terug (een
  standaardpoort erbij, andere hoofdletters in een escape), en dan zou *élke* identiteit
  veranderen in plaats van alleen die met een volgparameter. Wat dat betekent: alles wat je ooit
  zag, zou opnieuw als nieuw tellen.

Dat laatste is niet beredeneerd maar nagemeten, met de échte methode over de échte databank:
van **4510** bewaarde identiteiten die een volledig adres zijn (2763 in `seen`, 1747 in
`outcome`), zou er **geen enkele** veranderen.

**Waar de titel vandaan komt.** Een sitebestand beschrijft de *zoekpagina*. Er staat wel een
selector in voor de einddatum en de foto's van een advertentiepagina, maar niet voor haar titel -
die stond nooit ergens anders dan in het zoekresultaat. `FavoriteWatch.TitelUitPagina` probeert
daarom drie dingen, in deze volgorde:

1. de `name` uit het `ld+json`-blok, **enkel** van een object dat ook een prijs of `offers`
   draagt. Die voorwaarde is nodig en geen overdaad: op elke advertentiepagina van 2dehands staat
   er een `BreadcrumbList` met een `name` ("Audio en Hifi"), en die staat er **vóór** het product;
2. `og:title`;
3. de `<title>` van de pagina.

Valt er niets te lezen, dan wordt er **niets** bewaard: een lege kaart in je favorieten is erger
dan een melding die zegt dat het niet lukte.

**`Handle` is async geworden** in `BridgeServer`, want dit pad haalt een pagina op en dat duurt
seconden. Dat houdt de rest niet tegen: elke verbinding heeft haar eigen taak, dus de extensie
blijft intussen gewoon om werk vragen.

**De terugmelding komt van Chrome zelf** (`chrome.notifications`), want de popup staat niet open
wanneer je rechtsklikt. Daarvoor zijn er twee rechten bijgekomen in het manifest, `contextMenus`
en `notifications`, en een pictogram - dat laatste omdat een melding er een eist, en meteen ook
omdat er tot nu een puzzelstukje in de werkbalk stond.

Het contextmenu zelf heeft **geen** toestemming per site nodig: Chrome geeft het adres van de
link mee zonder dat we in de pagina moeten kijken. Haalt de app die pagina daarna via de brug op
(een site met `UseBridge`), dan speelt jouw toestemming voor die site wel weer mee.

### Een kavel van een veilinghuis dat Vindioo niet kent

Je staat op `bopa.be` en wil dat kavel bewaren, maar bopa.be staat niet bij je sites. Een
sitebestand per veilinghuis maken is geen antwoord: AlleVeilingen verzamelt er **twintig**, en die
lijst verandert.

Wat wél werkt, en wat de hele opzet draagt: **een kavelpagina van AlleVeilingen draagt een link
terug naar het veilinghuis** ("Bekijk dit kavel op Bopa"). Daarmee hoeft Vindioo niet te raden of
twee kavels hetzelfde zijn - ze zoekt het adres waarop jij klikte terug in de pagina van de
kandidaat. Staat het er niet in, dan is het een ander kavel. Punt.

Dat maakt de zoekterm **onbelangrijk**, en dat is het mooie eraan: hij hoeft enkel goed genoeg te
zijn om het kavel ergens in de lijst te krijgen. Een misser levert "niet teruggevonden" op, nooit
een verkeerde favoriet. Daarom mag `Zoekterm()` losser zijn dan `FavoriteWatch.TitelUitPagina`,
die de titel van een favoriet bepaalt en dus wél precies moet zijn.

De weg, gemeten op 2 oktober 2026 met lot 1 van BOPA:

```
bopa.be/auction/520/lot/57380
  -> <h1> "Lot 1: Elektrische fiets Villette"
  -> zoeken op AlleVeilingen -> /nl/Bopa/kavel/12528539/lot-1---elektrische-fiets-villette
  -> die pagina bevat "bopa.be/auction/520/lot/57380"   -> treffer
  -> einddatum 08/10/2026 19:30, zoals op de site van BOPA zelf
```

**970 ms**, drie verzoeken, geen browser en geen toestemming per site. De favoriet is daarna een
gewone AlleVeilingen-favoriet: *Nakijken*, de einddatum en de veilingwaarschuwing werken allemaal.

Vier dingen die de opzet bepalen, en elk ervan is gemeten:

- **De `<h1>` levert de zoekterm**, niet `<title>`. De titel van een BOPA-kavelpagina is "BOPA
  Veilingen | Uw veiling makelaar voor online veilingen" - ook **ná** het renderen, dus zelfs de
  extensie zou er niets aan hebben. De h1 draagt wel de kavelnaam, en die staat al in de gewone
  HTML: geen brug nodig.
- **De kandidaten komen uit de kale zoekpagina**, geplukt met het `IdPattern` van het sitebestand.
  Dat patroon bestaat juist om een kavellink te herkennen, dus er hoeft niets nieuws ingesteld te
  worden - en er is geen browser nodig, ook al staat AlleVeilingen op `NeedsBrowser`. Gemeten: 30
  kavellinks in de kale HTML, maar 3 van de 30 volledige kaarten. Een site **zonder** `IdPattern`
  doet dus niet mee.
- **De titel van de favoriet komt uit de h1 van de kavelpagina**, niet uit de gewone weg:
  AlleVeilingen hangt de naam van het veilinghuis achter haar `og:title` én haar `<title>`
  ("Lot 1 - elektrische fiets villette **| Bopa**"), de h1 niet.
- **Het huidige bod komt mee**, sinds 2 oktober 2026. Hier stond tot dan dat het bod "pas met
  JavaScript een prijs wordt" en dat er dus niets te halen viel. Dat was een verkeerde conclusie,
  en wel omdat ze over de **verkeerde pagina** ging. Op bopa.be klopt ze: daar staat het getal in
  een inline script en zet Alpine het pas in beeld. Maar de app leest bopa.be helemaal niet - ze
  leest de kavelpagina van **AlleVeilingen**, en daar staat het bod gewoon in de kale HTML, zowel
  in de opmaak ("Huidig bod / € 270,00") als in het `ld+json`-blok.

  Wat er wél aan de hand was, en allebei telt het apart: het bod zit daar in `additionalProperty`
  en niet in `offers.price`, en het scripttype staat er als `application/ld&#x2B;json` - waardoor
  de algemene lezer van de app, toen nog een regex over de ruwe tekst, het blok niet eens zag.
  Dat tweede is intussen rechtgezet (hij gaat nu langs de ontlede pagina), het eerste blijft, en
  daarvoor wijst `DetailPriceSelector` in het sitebestand het bod aan. Zie `docs/favorieten.md`
  voor de meting en de tegenproeven.

  Let op het verschil tussen de twee wegen, want daar liep ik zelf op vast: **deze weg haalt de
  pagina ruw op**, met een gewone `HttpClient`. *Nakijken* gaat via `DetailFetcher`, en die zet
  voor AlleVeilingen Playwright in - dan krijg je de geserialiseerde DOM, waarin zo'n
  karakterverwijzing al opgelost is. Dezelfde pagina ziet er langs de twee wegen dus anders uit:
  32 kB tegen 582 kB.

  Nagegaan dat het getal van de tussenpersoon ook klopt met dat van het veilinghuis zelf: voor
  lot 1 van BOPA zegt bopa.be in zijn eigen script `highest_bid: 270` en
  `closing_date: 2026-10-08 19:30:00`, en dat is precies wat er via AlleVeilingen bewaard wordt.

**En de kopregels.** bopa.be antwoordde met **429 Too Many Requests** op het eerste verzoek met
enkel onze User-Agent, en met 200 op een kale `Mozilla/5.0` én op een volledige browser-set. Wie
zegt dat hij Chrome is maar de kopregels van Chrome niet meestuurt, valt op. `HttpFactory` stuurt
nu `Accept` en `Sec-Fetch-*` mee - nagemeten dat ze er **samen** bij moeten zijn, elk apart bleef
429. Met de tegenproef nagegaan dat je zeven rechtstreekse sites er niets van merken: dezelfde
statuscodes en dezelfde omvang, met en zonder.

Nagemeten: `tools/meet-extensie-toegang.mjs` draait de échte luisteraar van het contextmenu tegen
een nagebootste Chrome (een gewone link, een pagina zonder link, `http://`, een privé-adres,
`javascript:`, geen koppelcode, en een vreemd programma op de poort). De tegenproef zonder
`waaromNiet` laat de drie weigeringen omvallen. Aan de kant van de app doen `RechtsklikChecks` en
een groep in `BrugChecks` de rest.


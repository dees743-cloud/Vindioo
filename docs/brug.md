# Hoe een site binnenkomt: rechtstreeks, de browser of de brug

Onderdeel van de documentatie van Zentrix; de korte versie staat in
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

## Een site achter Cloudflare

Cloudflare kan de app weigeren op de vingerafdruk van zijn TLS-handdruk: .NET en een
headless Playwright krijgen "Just a moment...", een echte browser niet. Dan is de brug
de weg, en voor een API de tweede soort brugopdracht (`rawText`, zie "Twee soorten
opdrachten"). Discogs is daar het voorbeeld van; het hele verhaal, met wat we
probeerden en waarom het niet lukte, staat in `SITES.md` van `zentrix-sites`.

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
| `X-Zentrix-Sig` | `nonce \n body` | bewijst dat dit bericht van iemand komt die de code kent, en dat de body onderweg niet veranderd is |
| `X-Zentrix-Voor` | enkel `nonce` | hetzelfde bewijs, maar **al na te kijken met enkel de kopregels in de hand** |

**En de app tekent haar antwoord óók.** Dat is de helft die de extensie beschermt: zij voert uit
wat uit `/job` komt, dus zij moet weten dat ze met de echte Zentrix praat. Klopt de handtekening
niet, dan gaat er geen tabblad open.

**Waarom twee handtekeningen, en niet één.** De brug kon de koppelcode vroeger nakijken *voor* ze
een body las - die stond immers in het adres - en daar hing een rem aan: "wie de code niet kent,
krijgt nooit een body gelezen, hoe groot die ook zegt te zijn". Een handtekening óver de body kan
dat per definitie niet. Zonder die tweede kopregel zou die rem er dus stilletjes uit zijn
gegaan; dat kwam boven doordat een bestaande controle (1,5 GB aangekondigd → 413) ineens een 200
gaf.

**Een oude extensie werkt niet meer**, en de app zegt dat ook zo: `BridgeStatus.OldExtension`
("de Zentrix Brug in Chrome is een oudere versie. Herlaad ze: chrome://extensions...") in plaats
van over de koppelcode te klagen, want daar is niets mis mee. Omgekeerd geldt hetzelfde: een
nieuwe extensie met een oude app krijgt geen geldige handtekening terug en weigert dan elke
opdracht. **App en extensie moeten dus samen mee.**

Nagemeten, allebei de kanten:

- **Dat C# en JavaScript hetzelfde tekenen**, met waarden die met Node uitgerekend zijn - dus
  tegen een onafhankelijke implementatie en niet tegen zichzelf (`BrugChecks`, en die groep
  draait óók met Zentrix open, want ze heeft de poort niet nodig).
- **Dat de app weigert** zonder handtekening, met die van een andere code, en bij een webpagina
  met een verzonnen handtekening; dat haar antwoord getekend is; en dat een oud verzoek
  "verouderde extensie" krijgt (`BrugChecks`, met de poort vrij).
- **Dat de extensie weigert** wat niet van de app komt: `tools/meet-brug-handtekening.mjs` draait
  de échte `vraagApp()` uit `background.js` tegen een nep-app op een vrije poort. Die zegt ook of
  de koppelcode nog ergens in een adres of kopregel opduikt - dat doet ze niet.

```bash
node tools/meet-brug-handtekening.mjs
```


# Zoeken: de planner, meldingen, het systeemvak en paginering

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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

Op diezelfde tik hangt sinds 2 oktober 2026 ook `AuctionWatch`: de waarschuwing dat een bewaarde
veiling bijna afloopt (zie `docs/favorieten.md`). Die staat **vóór** de rem `if (_busy) return`, en
dat is met opzet: ze kost geen enkel verzoek, dus ze hoeft niet te wachten tot een trage
zoekopdracht klaar is - anders zou een kavel dat intussen sluit pas achteraf gemeld worden, of
niet meer.

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

**En de andere kant: niet alles wat geen resultaat geeft, is een mislukking.** AutoScout24 heeft
**geen vrije tekstzoekfunctie** - het zoekwoord ís het merk, en het staat in het pad van de
zoek-URL. Gemeten op 3 oktober 2026: `volkswagen`, `bmw/x5`, `land-rover` en een lege zoekterm
geven 200; `cd`, `cd speler` en `commodore` geven alle drie een **404**.

Dat telde als mislukking, en daar kwam dit van: een bewaarde zoekopdracht naar "cd speler" met
AutoScout24 aangevinkt mislukte bij **elke** beurt, stuurde na twee beurten een melding dat die
site stuk was, en bleef daarna voor altijd rood staan. Terwijl er niets stuk is - die site gaat
gewoon niet over cd-spelers. Een waarschuwing die nooit meer weggaat, leert je waarschuwingen
negeren, en dan werken de drie vangnetten hierboven ook niet meer.

Sinds 3 oktober 2026 gooit de motor daarvoor een **eigen soort** uitzondering
(`UnsupportedQueryException`) in plaats van een gewone fout. Die komt in
`outcome.QueryNotSupported` terecht, en dan geldt:

| | |
|---|---|
| op de tab van die site | **wel** - je leest waarom er niets kwam, met de uitweg erbij ("laat de zoekbalk leeg en gebruik de filters") |
| in de regel onderaan | **wel**, maar als eigen zin: "AutoScout24 kent dit zoekwoord niet", niet "1 site mislukte" |
| als mislukte beurt (`RecordRun`) | **niet** - geen streak, geen melding, geen rode zoekopdracht |
| in `outcome.Errors` | **niet** |

Met de echte site nagemeten over twee beurten na elkaar: 1816 resultaten van 2dehands, geen
enkele fout, geen melding, en de zoekopdracht blijft groen. De tegenproef - de oude soort
uitzondering terugzetten - laat zeven controles omvallen, waaronder die ene die telt: *"ook na
een tweede beurt geen melding"*. Een site die **écht** mislukt, geeft nog altijd na twee beurten
een melding; daar staat een aparte controle op, want anders had deze uitzondering het vangnet
opengescheurd.

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

**En er gaat nooit een wachtwoord over een onversleutelde verbinding** (1 oktober 2026). Stond
"SSL/TLS gebruiken" uit en was er een gebruikersnaam ingevuld, dan ging het wachtwoord gewoon mee
over de lijn - als `AUTH PLAIN`, en base64 is geen versleuteling maar een andere schrijfwijze. Op
een netwerk dat je niet zelf beheert is dat genoeg om je mailaccount kwijt te spelen. Gevonden in
een codeanalyse van 30 september 2026.

De controle kijkt naar de **verbinding** (`client.IsSecure`) en niet naar het vinkje: zo vangt ze
ook het geval waarin STARTTLS niet doorging en de verbinding gewoon open bleef. Is er een
gebruikersnaam en is de lijn niet versleuteld, dan wordt er niet verstuurd, en zegt de melding wat
je eraan doet. Een eigen relay in huis zónder aanmelding blijft gewoon werken - daar valt ook
niets te lekken.

Nagemeten in `StilFalenChecks` met een nagebootste mailserver die net genoeg SMTP spreekt om tot
het aanmelden te komen en elke binnengekomen regel onthoudt: de server zag enkel `EHLO` en `QUIT`.
Met de tegenproef (die ene regel uit) stond er `AUTH PLAIN AGphbi5wZWV0ZXJz...` tussen - het
wachtwoord, leesbaar na één base64-stap. **Let op bij het lezen van die controle:** zoeken naar de
letterlijke tekst zei bij die tegenproef doodleuk OK, want base64 ziet er anders uit. Ze ontcijfert
nu eerst. Een controle die te makkelijk slaagt is erger dan geen controle: ze geeft rust die er
niet is.

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

**Het mailwachtwoord, het Telegram-token en de API-sleutel staan beschermd door Windows** in `instellingen.json`
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
- **Een oudere Vindioo** kent `dpapi:` niet, leest de beschermde vorm als het wachtwoord zelf (de
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
teller van die zoekopdracht in Vindioo. Een e-mail zette er tot 22 september 2026 alle nieuwe in,
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

Waarom: na het opstarten van de pc bleef het venster soms spierwit, tot de eigenaar Vindioo
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

**De eerste echte pc-start erna ging goed** (23 september 2026): pc aan om 16:39, Vindioo om 16:40
in het systeemvak zonder venster, en bij het openen om 16:44:36 stond het beeld er 251 ms later.
Eén meetpunt - het witte venster kwam ook vroeger niet elke keer - maar precies het geval dat
misging. Blijft het bij volgende pc-starts goed, dan is het hiermee weg.

**Er draait maar één Vindioo tegelijk.** `App.OnStartup` neemt een benoemd slot (`Mutex`);
een tweede start vindt dat bezet, geeft het draaiende exemplaar een seintje
(`EventWaitHandle`) en stopt meteen, en dat exemplaar haalt zijn venster naar voren
(`MainWindow.BrengNaarVoren`). Vroeger liep een tweede exemplaar stil vast op de bezette
poort van de brug, en leek de snelkoppeling niets te doen. Gevolg voor wie test: een lege
gegevensmap proberen met `VINDIOO_DATA` kan enkel terwijl de gewone Vindioo dicht is.

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
`dotnet Vindioo.dll` - zo doet een testprogramma het - dan is het proces dotnet.exe, en schreef
`Autostart.RefreshPath` dat in het register: `"dotnet.exe" --systeemvak`, waarmee Windows niets
start. Op 16 september 2026 gebeurde dat echt, door een testprojectje dat `new App()` deed:
**`App.OnStartup` loopt ook dan**, zodra de dispatcher berichten verwerkt. `Autostart.ExePad`
neemt nu de `Vindioo.exe` naast de dll, en anders niets.

**Wie in een testprojectje `App` aanmaakt, moet `App.OnStartup` overslaan.** `new App()` zet
de opstart klaar, en die loopt zodra de dispatcher de eerste keer berichten verwerkt. Draait
Vindioo intussen gewoon, dan vindt die opstart het slot "Vindioo draait al", haalt ze het
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

## Enkel in de titel

Zoek je **matras 140x200**, dan geven de sites ook alles terug waar die woorden érgens staan: in
de beschrijving, bij de verzendkosten, of in een opsomming van andere maten die de verkoper ook
heeft. Wat jij zocht is een matras van die maat, en dat staat in de titel.

De schakelaar **Ab** boven de resultaten houdt enkel over wat alle woorden van je zoekterm in
zijn **titel** draagt. Hij werkt meteen: er wordt niet opnieuw gezocht, want het zeeft wat er al
binnen is.

**Hoeveel dat scheelt**, gemeten op 3 oktober 2026 met precies die zoekterm:

| | resultaten | met alle woorden in de titel |
|---|---|---|
| 2dehands | 300 | 238 |
| Marktplaats | 300 | 245 |

En in een echte beurt over allebei de sites: **3627 binnen, 2293 over** - een derde eruit.

**Er viel niets onterecht af**, en dat was de vraag die ertoe deed. De maat staat in die titels
als `140x200` (244x) of `140X200` (8x), en dat verschil is enkel een hoofdletter; `InTitel`
vergelijkt zonder hoofdlettergevoeligheid. Zoekertjes die de maat ánders schrijven en toch
zouden wegvallen: geen enkele.

**De regel is met opzet simpel**: elk woord moet ergens in de titel staan, in willekeurige
volgorde, en **niet** als heel woord. Dat laatste is geen slordigheid maar precies wat je wil -
`140x200` zit in "matras 140x200cm", en een controle op hele woorden zou juist dat zoekertje
weggooien.

**Wat hier buiten valt, bestaat niet voor de zoekopdracht**: het telt niet mee in de teller
"nieuw", het wordt niet bewaard en het geldt niet als gezien. Dezelfde keuze als bij de
prijsgrens, en om dezelfde reden - anders zegt de teller iets anders dan de lijst eronder, en
geldt als bekeken wat je nooit te zien kreeg. Zie `docs/sites.md` bij de prijsgrens.

**Het hoort bij de zoekopdracht**, niet bij het scherm. Staat er een bewaarde zoekopdracht open
als je de schakelaar omzet, dan gaat de keuze daar ook in - net als de filters - en gebruikt een
geplande beurt 's nachts dezelfde zeef. Zo krijg je geen melding over een zoekertje dat je woord
enkel in zijn beschrijving had staan. In het venster van een zoekopdracht staat hij ook als
vinkje, onder dat voor foto's.

**Wat het niet doet.** Dit zeeft wat er binnenkwam; het haalt niets extra op. Geeft een site
tweeduizend resultaten en zit jouw titeltreffer op plaats 2500, dan vind je hem hiermee ook
niet. Sommige sites kunnen zelf al op titel zoeken - Marktplaats heeft
`searchInTitleAndDescription` in zijn zoek-URL - en dat zou beter zijn, want dan komt er geen
ruis binnen om weg te gooien. Maar dat verschilt per site en hoort dus in het sitebestand; het
staat open.

De regel zelf staat op één plaats: `SavedSearch.InTitel`. Het hoofdscherm stelt dezelfde vraag
zonder bewaarde zoekopdracht - wie gewoon iets intypt verwacht hetzelfde - en twee lezers van
dezelfde regel groeien uit elkaar; zie `PriceParser` voor hoe dat afloopt.

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

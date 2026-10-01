# Wat je te zien krijgt: volgorde, plaats, veilingtijd en timers

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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
het niet: je zoekt om dingen te zien, niet om ze te verbergen. Wat eerder weggeklikt was, staat
gewoon weer in de lijst; de tabel `hidden` blijft in een bestaande databank staan, maar wordt
niet meer gelezen.

**En het vergrootglas is er ook uit** (27 september 2026). Op de plaats van dat oogje stond een
vergrootglas: de muis erop toonde de grote foto ernaast in een popup. Dat was er nog van voor
het detailvenster bestond. Nu opent een **dubbelklik** dat venster met álle foto's van de
advertentie, en een klik op de grote foto legt ze schermvullend over het venster - dus was het
zweven een tweede weg naar hetzelfde, en een knopje dat op elke kaart plaats innam naast de
ster. Zo koos de eigenaar het.

Meegegaan: `ListZoom` en `GridZoom` in de twee sjablonen, de stijl `OverlayIcon`, de hele
`Popup` in `PhotoThumbnail` met `ShowPreview`, `Preview_Opened` en `Preview_Closed`, en de
`CenterOffsetConverter` die enkel bestond om die popup op het midden van de miniatuur te
leggen. De ster staat nu rechtstreeks rechtsboven op de foto in plaats van in een rijtje van
twee. Nagemeten met de twee sjablonen buiten beeld, met de hand gevuld (het raster bouwt buiten
beeld geen kaarten op): 0 vergrootglazen, 0 popups, de miniatuur en de ster staan er nog, en de
ster staat op (215, 21) in de lijstkaart en (215, 19) in de rasterkaart - rechtsboven dus.

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

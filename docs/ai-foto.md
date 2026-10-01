# AI-controle op een foto, lokaal op de grafische kaart

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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

Naast *Kopiëren* staat **Prijsindicatie**, die aangaat zodra er gelezen is. Die geeft de gelezen
namen mee aan het prijsvenster, als voorstellen om aan te klikken; zie "Prijsindicatie" hierboven
voor waarom de app er zelf geen zoekterm uit kiest.

**Het kijken is te onderbreken** (26 september 2026). *Opnieuw kijken* wordt *Stoppen* zodra er
gekeken wordt, in amber - dezelfde vorm als het vergrootglas dat een stopknop wordt op het
hoofdscherm, en om dezelfde reden: één knop op één plaats, want daar staat je muis al. Bij zes
foto's duurt een grondige lezing meer dan een minuut, en tot dan kon je enkel het venster
sluiten.

Wat al bekeken is, blijft staan en is te kopiëren; de knop is meteen weer een startknop, dus je
kan er zo opnieuw aan beginnen (bijvoorbeeld zonder het vinkje "grondig"). Het stoppen gaat door
tot in Ollama: de `CancellationToken` gaat mee in het verzoek, dus een lezing die bezig is wordt
echt afgebroken en niet stilletjes uitgezeten.

`_gestopt` staat naast de token, want die staat óók op "geannuleerd" wanneer het venster dichtgaat
of wanneer er opnieuw gekeken wordt - en dan hoort er geen "Gestopt" in de statusregel te komen.

Nagemeten met het venster buiten beeld, de echte Ollama en een zoekertje van zes foto's: foto 1
klaar na 19 s, dan op Stoppen midden in foto 2, en **0,1 s later gestopt** met "Gestopt na één
foto; die staat hieronder." De knop stond weer op *Opnieuw kijken*, het blok van foto 1 stond er
nog en *Kopiëren* werkte.

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
dit zoekertje* haalt ze op van de pagina van het zoekertje zelf.

**Daarvan komt één verhaal, niet één per foto** (26 september 2026, gevraagd door de eigenaar:
"dit zijn allemaal foto's van hetzelfde apparaat"). Bovenaan staat *Wat de AI ziet - 4 foto's
samen*, en die tekst wordt **na elke foto opnieuw geschreven** met alles wat er tot dan gelezen
is; daaronder blijft per foto zijn miniatuur en zijn gelezen namen staan. Zo groeit het mee
terwijl je kijkt - bij vijf foto's duurt het geheel meer dan een minuut, en dan wil je niet naar
een leeg venster kijken.

Dat kon omdat het lezen en het vertellen al apart stonden: `PhotoAnalyzer.LeesAsync` geeft enkel
de namen, `VertelAsync` maakt er een alinea van. Bij vier foto's is dat vier keer lezen en
daarna telkens één keer vertellen over álle namen samen, in plaats van vier keer allebei. Wat
dat oplevert, gemeten op de vier foto's van één DVD-speler:

- **Het zet leesfouten recht.** De losse alinea van foto 3 maakte er "modelnummer 1650" van; met
  de namen van alle vier de foto's erbij stond er DVD1050, zoals op het toestel staat. Hetzelfde
  effect als bij de stukken van één foto, maar dan over de foto's heen.
- **Het brengt samen wat verspreid staat.** Het typenummer en "230V~ 50Hz" staan enkel op foto 4,
  DOLBY DIGITAL en dts enkel op foto 2. Eén alinea heeft ze allebei; vier alinea's elk een stuk.
- **Het kost niets extra.** Er wordt evenveel gelezen en even vaak verteld. Bracht een foto geen
  enkele nieuwe naam, dan kan het verhaal niet veranderen en wordt die vraag overgeslagen.

Nagemeten met het venster buiten beeld, de echte Ollama en het zoekertje uit het screenshot:
**44 s voor vier foto's**, vier versies van het verhaal (na elke foto een), en de laatste noemt
het merk, het modelnummer, de labels op de voorkant én het typeplaatje achteraan.

**Waarom er geen vinkje "alle foto's tonen hetzelfde voorwerp" is**, terwijl dat de eerste
ingeving was: dan moet je beslissen vóór je de foto's gezien hebt - het venster toont bij het
openen enkel de foto van de zoekpagina - en de AI zou dan niet meer vanzelf mogen starten. Eén
verhaal werkt bovendien ook bij een partij losse spullen: dan is het één overzicht in plaats van
vier halve, en per foto staat nog altijd wat dáár gelezen is. De vraag aan het model beweert
daarom ook niet dat het hetzelfde voorwerp is.

**Het model kán het overigens wel zien**, nagemeten op 26 september 2026: de vier foto's van deze
DVD-speler gaven drie keer "hetzelfde voorwerp" en een zoekertje "Partijen elektronica, laptops,
telefoons (defect)" twee keer "verschillende voorwerpen", met kloppende redenen. Alle foto's in
één vraag kan ook, maar dan moet het contextvenster omhoog (vier foto's van 1200 px zijn 4360
tokens tegen een venster van 4096) en hangt de tijd aan de fotomaat: 26 s op 1200 px tegen 4,8 s
op 900 px. Zeven vergelijkingen op twee zoekertjes is te weinig om erop te bouwen, maar het ligt
er als het ooit nodig is.

**De vraag aan het model is bros, en dat is gemeten.** Een eerdere formulering ("...van een
tweedehands-zoekertje met 4 foto's" plus de kopregel "GELEZEN VAN ALLE FOTO'S SAMEN") gaf bij
temperatuur 0 stelselmatig een **lege** alinea - drie keer op drie. Met zes varianten op dezelfde
foto en dezelfde namen bleek: elk van die twee stukken apart gaf een gewone alinea, enkel de
combinatie liep leeg. Er staat nu ook niet meer bij hoevéél foto's het zijn, want dan begon het
antwoord met "Je kijkt naar een zoekertje met 4 foto's" - dat is de werking van de app en niet
wat er te zien is. Wijzig die tekst dus niet zonder na te meten of er tekst uitkomt; een lege
beschrijving belandt sindsdien ook in het logboek, en het venster laat dan staan wat er al stond.

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
- **Geeft de pagina foto's, dan kijkt de AI enkel naar díe** (26 september 2026,
  `DetailFetcher.ListingDetails.PaginaFotos`). Tot dan kreeg ze de samengevoegde lijst, met de
  foto van de zoekpagina vooraan - en bij Facebook is dat **dezelfde foto op 260 px naast dezelfde
  op 960 px**, twee verschillende adressen. Gevolg: twee keer wachten op hetzelfde, en in die
  kleine las het model niets meer, waarop het er iets bij verzon. Bij een stapel videospellen gaf
  foto 1 "ongeveer twintig cd's" en foto 2 de 43 juiste titels. De eigenaar zag dat meteen: "de
  gegevens van foto 1 kloppen niet, die van foto 2 wel."

  Bij 2dehands verandert er niets: daar is het adres van de zoekpagina letterlijk de eerste foto
  van de pagina, dus die stond er sowieso maar één keer in.

  Nagemeten met het venster buiten beeld, de echte Ollama en twee echte Catawiki-foto's achter een
  proefpagina die Facebook nabootst (de zoekpagina geeft het kleine kaartformaat, de pagina het
  grote): **2 blokken in 24 s**, niet 3, en de koppen tellen tot 2.
- **Ook zonder het vinkje wordt er naar een leesbare foto gekeken** (26 september 2026,
  `DetailFetcher.GroteVersieAsync`). *AI-controle op deze foto* nam tot dan `Listing.LargeImage`,
  en bij Facebook is dat een miniatuur van 260 px waar niets van te lezen valt. Nu wordt die foto
  eerst **nagemeten** en pas dan gelezen: is ze een miniatuur, dan komt de grotere van de
  advertentiepagina in haar plaats.

  **De grens is 500 px op de lange zijde, en dat getal komt uit een meting.** Wat de zoekpagina's
  geven: Facebook **260**, 2dehands **800 tot 2048**, Catawiki 1800, eBay 1600. De eerste versie
  nam 900 px - de grens waaronder een foto niet meer in stukken geknipt wordt - en dat was fout:
  bij 2dehands *is* die 800 px het origineel (het adres van de zoekpagina is er letterlijk dat van
  de eerste foto op de pagina), dus daar viel niets te halen en betaalde **8 van de eerste 14**
  zoekertjes op "cd speler" 0,7 seconde voor niets. Met 500 px zit de grens boven elke miniatuur
  die we zagen en onder elk origineel.

  Drie dingen die daarbij horen:

  - **Het venster zegt het**, tijdens ("Deze foto is maar 260 × 260 beeldpunten - te klein om een
    label van te lezen...") en achteraf ("Bekeken is de foto van de advertentiepagina (960 × 720
    beeldpunten); die op de kaart is maar 260 × 260"), en het toont die foto erbij. Anders kijkt
    de AI naar iets anders dan waarop je klikte zonder dat iemand het weet.
  - **De foto gaat niet twee keer over de lijn.** Ze moet toch opgehaald worden om ze na te meten,
    dus ze komt mee terug en het venster gebruikt ze.
  - **Geeft de pagina niets groters, dan blijft de foto van de kaart staan.** Een andere foto
    tonen dan waarop geklikt is, is erger dan een foto die niet goed leesbaar is.

  Nagemeten op 26 september 2026 met echte zoekertjes: op 2dehands haalden **14 van de 14** geen
  pagina op (traagste 96 ms, 8 van hen met een foto van 800 px), en een echt Facebook-zoekertje
  ging van **260 × 260 naar 960 × 720 in 5,7 s** (107 kB). In het venster buiten beeld, met de
  echte Ollama: één blok, de statusregel die de omschakeling noemt, en de bekeken foto erbij. De
  logica eromheen staat in `FotoChecks`, met een proefsite die echte foto's serveert - het formaat
  is niet aan een adres af te lezen, dus er valt niets na te bootsen.
- **De titel van het venster volgt het vinkje.** Hij stond vast op "AI-controle op deze foto", ook
  wanneer er naar alle foto's gekeken werd.

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

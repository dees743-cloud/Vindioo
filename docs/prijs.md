# Prijsindicatie en de Pricewatch van Tweakers

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

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

**Wat de AI-controle eraan toevoegt: voorstellen, geen zoekterm** (26 september 2026,
`PriceIndicator.AlsZoektermen`). Heb je een zoekertje door de AI-controle gehaald, dan staat er
in dat venster een knop *Prijsindicatie*; het prijsvenster krijgt dan de namen mee die van de
foto's gelezen zijn, als **chipjes om aan te klikken**. Klikken zet zo'n naam achter de
zoekterm - erbij en niet in de plaats van, want meestal klopt het merk uit de titel en ontbreekt
enkel het typenummer. Staat de naam er al in, dan zegt de statusregel dat en verandert er niets.
Wat op een typenummer lijkt staat vooraan, hoogstens vijftien, dubbels en losse tekens eruit.

**Waarom de app er zelf geen kiest, en dat is gemeten.** De eerste versie deed dat wél: ze vroeg
het model welk merk en typenummer er op het voorwerp stonden en zette dat als zoekterm klaar.
Op zes echte zoekertjes waarvan de titel geen modelnummer gaf, leverde dat **één** marktwaarde
op - en die ging over een **Xbox 360 die op de achtergrond van een stereoset stond** (± € 7,98
uit 127 vraagprijzen). Twee keer las de foto het juiste typenummer ("C77ES" van een Sony,
"KX-W407D" van een Kenwood), en toen gaf de prijsindicatie 0 vergelijkingen op 16 en 29 treffers:
de sites schrijven dat anders (CDP-C77ES) of hebben er niets van te koop.

Twee dingen die daar los van elkaar misgingen, en die allebei blijven bestaan:

- **Een foto toont meer dan het voorwerp.** Welke gelezen naam het voorwerp ís, ziet een mens in
  één oogopslag en een model niet.
- **Een juist typenummer is nog geen vergelijking.** Zeldzame toestellen staan gewoon niet te
  koop, en dan blijft het bij "te weinig gegevens" - terecht.

Daarom leest de app en kies jij. Het scheelt ook een vraag aan het model: het venster gaat
meteen open in plaats van na vier seconden.

**Bij een partij ligt het anders, en daar kan het wél automatisch** (26 september 2026,
`LotPriceWindow`, de knop *Prijs per titel* in de AI-controle). Bij een doos spellen of een
stapel platen is de vraag niet wat de doos waard is maar **of er iets waardevols bij zit** - en
dan ís elke gelezen naam een titel die op zichzelf te koop staat. Er valt dus niets te kiezen:
alle titels worden apart opgezocht, met dezelfde prijsindicatie als elders, en ze komen op
volgorde van duur naar goedkoop te staan. Klikken op een regel opent de gewone prijsindicatie
voor díe titel, met haar vergelijkingen.

Gemeten op 26 september 2026, met de echte Ollama en de echte sites:

| Partij | Foto's | Namen gelezen | Bruikbaar | Gaven een marktwaarde |
|---|---|---|---|---|
| "Set van 160 PSP spellen" | 3 | 108 in 66 s | 98 | **10 van de eerste 15** |
| "verzameling pop cd's Prince U2 ..." | 6 | 109 in 73 s | 100 | **9 van de eerste 12** |

En de bedragen kloppen met wat zulke spellen doen: FIFA 12 € 3,99, Wipeout Pure € 7,50,
The Simpsons Game € 20, Yu-Gi-Oh! GX Tag Force € 25. Dat zijn precies de twee waar je naar op
zoek was.

**Er staat met opzet geen totaal bij**, en dat is geen voorzichtigheid maar een meting. Bij die
cd-verzameling las de AI het getal **"25000"** van een hoesje, en dat gaf een marktwaarde van
**€ 550** uit vijf dure treffers - meer dan alle echte titels van die verzameling samen. Eén
verkeerd gelezen naam maakt een totaal dus waardeloos, terwijl een lijst op volgorde de vraag
gewoon beantwoordt. Sindsdien valt een naam **zonder één letter erin** weg
(`PriceIndicator.AlsZoektermen`), maar dat vangt niet alles: "Prince" alleen geeft de prijs van
willekeurig welke Prince-cd.

**Per keer vijfentwintig.** Eén prijsindicatie kost ongeveer een seconde en vier verzoeken aan
de sites; alle 98 namen in één klik zou bijna vierhonderd verzoeken in twee minuten zijn. Dus
vijfentwintig, met een knop voor de volgende vijfentwintig, en de knop is intussen een stopknop
- dezelfde vorm als het vergrootglas en de AI-controle.

Nagemeten met het venster buiten beeld op die set PSP-spellen, met dertien namen waar met opzet
rommel bij zat (een getal, een los teken en een dubbele titel): **13 opgezocht in 14 s**, de drie
stukken rommel eruit, tien met een marktwaarde van duur naar goedkoop, de drie zonder onderaan,
en een klik op "Yu-Gi-Oh! GX TAG FORCE" opende de prijsindicatie met díe titel als zoekterm en
niet met die van de partij.

### Nieuw bij Tweakers: de andere kant van de prijs

De prijsindicatie hierboven rekent met **vraagprijzen van tweedehandszoekertjes**. Daarnaast
staat sinds 27 september 2026 in hetzelfde venster een blok **Nieuw bij Tweakers**
(`Services/Pricewatch.cs`): wat kost dit ding nieuw, en - als het niet meer te koop is - wat
kostte het het laatst. Dat is een **bovengrens** naast een marktwaarde: wie een Marantz CD6007
tweedehands voor € 250 ziet staan, weet met "nieuw vanaf € 395 bij 8 winkels" meteen waar dat
bedrag ligt. Gevraagd door de eigenaar, die de Pricewatch zelf al gebruikte.

Wat er uit een productpagina komt, gemeten op drie echte pagina's:

| Product | Wat het venster toont |
|---|---|
| Marantz CD6007 Zwart | Nieuw vanaf € 395 bij 8 winkels |
| Kensington-hoes (weg) | Niet meer te koop; laatst bekend € 41,51 op 20 juni 2026 |
| PlayStation 5 Slim 825GB | Nieuw vanaf € 599 bij 8 winkels · V&A: 12 advertenties, vanaf € 420 |

Die laatste regel is hun **eigen tweedehandsmarkt**, en die staat op de productpagina zelf.

**Drie dingen bepalen de hele opzet, en alle drie komen ze uit een meting.**

**1. Hun zoekpagina blijft met rust.** De `robots.txt` van Tweakers verbiedt élke zoekweg:
`/pricewatch/zoeken`, `/aanbod/zoeken`, `/zoeken` en `/search`. Wat ze juist wél publiceren is
een **sitemap met al hun producten**: 13 bestanden, **303 122 producten**. Het opzoeken gebeurt
daarom op de pc zelf, in een kopie van die sitemap (`pricewatch-index.txt` in de gegevensmap,
11 MB, hoogstens één keer per maand opgehaald in **2 seconden**), en er vertrekt pas een verzoek
naar Tweakers wanneer je een product aanklikt. Een opzoeking kost daarna ongeveer een tiende
seconde.

**2. De app kiest het product niet - jij klikt.** Dezelfde les als bij de namen die de
AI-controle van een foto leest, en opnieuw gemeten, op verse zoekertjes van de echte
zoektermen van de eigenaar:

| zoekterm | in de Pricewatch te vinden |
|---|---|
| iphone 12 pro | 14 van 40 |
| nintendo wii | 7 van 40 |
| marantz | 7 van 40 |
| cd speler | 1 van 40 |
| laptop | 1 van 40 |
| commodore 64 | **0 van 40** |

En erger dan die percentages: een deel van die treffers was **verkeerd**. "Marantz CD5003" kwam
uit op de CD-70, "HP EliteBook 840 G7" op een losse Intel-processor en "Nintendo Wii Mini
spelcomputer" op een golfspelletje. Daarom toont het venster **voorstellen met hun volledige
productnaam**, als klikbare chipjes, en haalt het pas een prijs op wanneer je er een aanklikt.
Twee regels houden de grootste onzin tegen, allebei in `Pricewatch.Kies`:

- **Staat er een modelnummer in de term, dan moet dat in de productnaam staan.** Zonder die eis
  werd de CD5003 de CD-70 - een ander toestel, met een andere prijs, en niets dat het verraadt.
- **Woorden tellen één keer.** De Wolverine-editie van de PS5 heeft "edition" twee keer in haar
  naam en stond daardoor bóven het toestel waar je naar kijkt.

Vindt hij niets, dan zegt het venster dat ook ("Tweakers heeft geen product dat op ... lijkt").
Eerlijk zwijgen is beter dan een verkeerd product: Tweakers heeft geen Commodore 64.

**Waarop hij zoekt:** de volledige titel van het zoekertje én de zoekterm, met de treffers van
de titel eerst. De zoekterm is met opzet kort ("Sony PlayStation Slim"), en dan komt de PS3
boven de PS5 te staan. Maar heb **jij** de zoekterm aangepast, dan telt enkel die: wie
"Commodore 64" intypt, wil geen PlayStations zien.

**3. De privacymuur kost één extra verzoek.** Een gewoon verzoek belandt op
`myprivacy.dpgmedia.nl`. Die muur zet een sessiecookie en laat het **tweede** verzoek gewoon
door - 296 kB met de prijs erin. Er wordt daarbij niets aanvaard: er gaat geen toestemmings-
keuze mee, enkel een sessie. Vandaar een `HttpClient` **met een koekjespot**
(`CookieContainer`), en dat was precies het verschil tussen `curl` (altijd de muur) en een
browser (meteen de pagina). Het scheelt de brug, dus dit werkt ook met Chrome dicht. De sitemap
staat niet achter die muur.

**De prijs komt uit het `ld+json`-blok** (schema.org), niet uit de opmaak: een `AggregateOffer`
met `lowPrice`, `highPrice` en `offerCount`. Is er geen winkel meer, dan staat er géén
offers-blok en zegt de pagina het in een zin, in `.noPriceMessage`. Dezelfde afweging als bij de
foto's van 2dehands: een webstandaard boven een klassenaam.

**Het werk blijft van de schermdraad af**, en dat is geen voorzorg maar een meting: zonder
`Task.Run` en `ConfigureAwait(false)` duurde het opzoeken **38 seconden** vanuit het venster,
tegenover 2,0 in een consoleprogramma. Elke voortzetting van een `await` kwam terug op de draad
die het scherm tekende. Gevonden met het venster buiten beeld - niet met een controle.

Nagemeten met het prijsvenster buiten beeld op een PlayStation 5 Slim: bij het openen staat er
niets van Tweakers (het kost een verzoek, dus jij vraagt het), na 2,5 s staan er zes voorstellen
met het kale toestel bovenaan, een klik geeft "Nieuw vanaf € 599 bij 8 winkels" met hun V&A
erbij, en "Commodore 64 met floppy drive" geeft niets met de reden erbij. De logica eromheen
staat in `PricewatchChecks`, met stukken van de echte pagina's en de echte productnamen.

**Wat het niet is:** een tweedehandsprijs, en geen dekking voor oud spul. Nog open: hun
prijsgeschiedenis (`/ajax/price_chart/<id>/be/`, de gegevens achter de grafiek
"Prijsontwikkeling") en hun Vraag & Aanbod als gewone zoekbron - dat laatste zou wél door de
verboden zoekpagina moeten.

**Waarom dat gemeten moest worden:** de prijsindicatie leidt haar zoekterm af uit de **titel**,
en daar staat meestal geen modelnummer in. Geteld op 2dehands, zestig zoekertjes per term: bij
"cd speler" hadden er **47 van de 60** geen modelnummer in de titel, bij "versterker" 52, bij
"platenspeler" 47 en bij "spelcomputer" 59. Dat gat is echt; het is enkel niet automatisch te
dichten.

**Wat het niet is:** een verkoopprijs. Het zijn vraagprijzen van vandaag, en een vraagprijs is wat
een verkoper hoopt. Verkochte prijzen (eBay heeft een filter "verkochte artikelen") zouden sterker
zijn, maar eBay weigert een gewoon verzoek en loopt in Vindioo via de browser; dat is een volgende
stap. De woordenlijsten (toebehoren, defect, geen achtervoegsel) staan in `PriceIndicator` en zijn
gemaakt op echte titels; een nieuwe soort rommel vraagt daar een woord bij. Nagemeten in
`PrijsChecks` met de titels van die dag, en met een lokale proefsite van begin tot einde.

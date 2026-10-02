# Het detailvenster van één zoekertje, en de foto's erbij

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

### Dubbelklikken: alles van één zoekertje

**Een dubbelklik opent niet meer de webpagina, maar een eigen venster** (25 september 2026,
`ListingDetailWindow`, gevraagd door de eigenaar). Daarin staan de foto's van de advertentie, wie
het verkoopt en hoelang het er al staat - precies wat je wil weten om te beslissen of je verder
kijkt. De browser openen, de cookiemelding wegklikken en de pagina laten laden was daarvoor een
omweg van een tien seconden per zoekertje. *Openen op de site* staat als knop in dat venster, dus
die weg blijft; ernaast staan *AI-controle* en *Prijsindicatie*, die doorsturen naar
`PhotoInsightWindow` en `PriceIndicationWindow`.

Die twee stonden eerst enkel achter een rechtsklik op een foto in de resultatenlijst
(26 september 2026 kwam *Prijsindicatie* erbij, gevraagd door de eigenaar). Ze horen ook hier:
dit venster is waar je een zoekertje bekijkt, en "wat is het waard" is dan de eerstvolgende
vraag. Nagemeten met het venster buiten beeld: vier knoppen naast elkaar nemen 335 van de 820
beeldpunten, dus de statusregel ernaast houdt ruim plaats over.

**De foto's: miniaturen boven, één grote eronder.** Een klik op een miniatuur wisselt de grote
foto, en een randje in het accent toont welke dat is. Dat ging eerst bij het zweven met de muis,
maar die passeert die rij ook op weg naar iets anders, en dan wisselde de foto ongevraagd - de
eigenaar vroeg er op 25 september 2026 een klik van te maken.

**De miniaturen staan op één rij die opzij schuift** (26 september 2026, `ThumbScroll`). Een
advertentie met vijftien foto's zou anders drie rijen hoog worden en de grote foto wegduwen. En
het stond er eerst in een `WrapPanel` in een kolom op `Auto`: die krijgt oneindige breedte en
breekt dus nooit af, dus de rij liep gewoon de vensterrand uit met de laatste foto half
afgesneden - precies de valkuil die een paar regels lager bij de UI-conventies staat, en toch
ingelopen. Het muiswiel schuift die rij opzij (`ThumbScroll_MouseWheel`): een wiel doet in een
rij die horizontaal schuift uit zichzelf niets, want het verzoek gaat omhoog op zoek naar iets
dat verticaal schuift.

**Een miniatuur toont de hele foto, niet een strook eruit.** Het vakje is vierkant (88x88) en de
foto gaat er met `Stretch="Uniform"` in, dus er blijven balken over bij een staande of een liggende
foto. Dat is met opzet anders dan op de kaarten in de resultatenlijst, waar `UniformToFill`
bijsnijdt zodat alle kaarten er gelijk uitzien: daar kijk je naar een rij zoekertjes, hier kies je
wélke foto je groot wil. In een liggend vakje met bijsnijden zag je van een staande foto enkel een
strook uit het midden - bij een Nintendo op een tapijt was dat het tapijt. Vierkant, want dan
blijft een staande én een liggende foto even groot in beeld.

Nagemeten met het venster buiten beeld op een echt zoekertje van 11 foto's: rij 780 breed,
inhoud 1056, schuifbalk zichtbaar, de laatste miniatuur bereikbaar, het wiel schuift, en alle 11
zijn getekend. Met een zoekertje van 2 foto's staat er geen balk.

**De grote foto krijgt de vrije ruimte** en groeit dus mee met het venster; een klik erop legt
hem schermvullend over het venster, met eronder hoeveel beeldpunten hij werkelijk heeft. Esc of
nog een klik sluit dat weer - Esc sluit eerst die laag en pas daarna het venster, anders valt bij
één druk alles weg. Geen apart venster: dan is er een tweede plaats die hetzelfde moet doen.

### Is de grote foto wel de grote foto?

Die vraag stelde de eigenaar op 25 september 2026, en het antwoord was **nee**. Gemeten over tien
zoekertjes van 2dehands, met de echte beeldpunten en niet met wat de URL belooft:

| Wat de app nam | Wat er te halen was |
|---|---|
| `pictures.extraExtraLargeUrl`: 726 px op de lange zijde, altijd | `pictures.url`: het origineel, tot **1600 px** |

Zeven van de tien waren groter dan 800 px; de grootste was 1600x1200 (551 kB) waar de app 726x545
(104 kB) toonde. Het is dus geen randgeval maar de regel. `LargeImageSelector` staat nu op
`pictures.url::match(^[^?]+)` - het stuk tot aan het vraagteken, want zónder de `?rule=`-parameter
geeft de CDN het origineel. Dat helpt ook de grote foto bij het zweven in de resultatenlijst, niet
enkel dit venster.

Datzelfde `::match(^[^?]+)` doet nog iets: het maakt de schrijfwijze gelijk aan die in het
`ld+json`-blok van de pagina, zodat de foto die we al hadden **niet twee keer** in de rij staat.
Met de query erbij waren het twee verschillende teksten voor dezelfde foto.

Bij `DetailImagesSelector` stopt het patroon nu ook vóór het vraagteken. Dat gaf meteen meer
foto's: Marktplaats ging van **1 naar 8** (elk 1200x1600), 2dehands van 3 naar 3 maar in 1024x768
in plaats van 800x800.

**Bij Facebook valt er op de zoekpagina niets te winnen, en dat is bewezen** (26 september 2026).
Wat de app daar krijgt is `stp=dst-jpg_p261x260_tt6`: een miniatuur van **261x261, 16 kB**. Gemeten
met verse adressen uit de databank:

| Wat er geprobeerd is | Antwoord van fbcdn |
|---|---|
| het adres zoals bewaard | 261x261, 16 kB |
| `p480x480`, `p720x720`, `p960x960`, `p2048x2048` | **403** |
| de uitsnede (`c0.152.261.261a_`) eruit | **403** |
| de `stp`-parameter helemaal weg | **403** |

**Facebook ondertekent de fotomaat mee.** De `oh=`-handtekening in het adres dekt die parameter,
dus elke wijziging maakt hem ongeldig. Bij 2dehands kon het formaat wél opgedreven worden omdat
hun CDN niet ondertekent; hier kan dat principieel niet. Die 261 px *is* wat de zoekpagina geeft.

Een eerdere poging kwam niet verder dan "onbekend", omdat élk bewaard adres toen verlopen was en
fbcdn dan ook op het origineel 403 geeft. Dat verlopen is op zich iets om te weten: **een favoriet
van Facebook verliest na een week of twee zijn foto** - de `oe`-parameter is een vervaldatum.

De weg naar grotere foto's, en naar meer dan één, is dus de advertentiepagina - zie hieronder.

**Wat er meteen staat en wat opgehaald wordt.** De titel, de prijs, de plaats en de site komen uit
het zoekertje zelf, en de foto van de zoekpagina staat er meteen groot - het venster is dus nooit
leeg. De andere foto's, de verkoper en "online sinds" staan op de pagina van het zoekertje, en die
wordt opgehaald zodra het venster opengaat.

**Dat ophalen is zichtbaar** (26 september 2026). Achter de miniaturen staat een vakje van dezelfde
maat met een draaiend wieltje erin, en de regel eronder zegt "De andere foto's van deze advertentie
ophalen...". Zonder dat zag je één miniatuur en één grote foto, en niets dat zei dat er nog iets
kwam - bij Facebook vier seconden lang, en dan verscheen er ineens een rij bij. Het wieltje gaat ook
weg wanneer het misging: een wieltje dat blijft draaien belooft iets dat niet meer komt.

**En zodra de pagina foto's geeft, verdwijnt die van de zoekpagina** (`ZetFotos`). Bij Facebook
stond ze er anders twee keer: eerst als 260x260 en meteen erna dezelfde foto als 960x720 - en de
grote foto eronder toonde dan de slechtste van de twee. Geeft de pagina niets, dan blijft ze staan;
dan is ze het enige wat we hebben.

Dat verschil is **niet** af te leiden uit de lijst die `DetailsAsync` teruggeeft, want daar staat
ze vooraan tússen de andere. Bij 2dehands ís het adres van de zoekpagina letterlijk de eerste foto
van de pagina, en die mag dus juist niet weg. Vandaar `ListingDetails.PaginaFotos`: enkel wat op de
pagina stond. `Fotos` blijft de samengevoegde lijst, want de AI-controle wil beginnen met de foto
die er zeker is.

Stond er een foto groot die er nog is, dan blijft die staan; anders de eerste van de pagina. Zo
springt het beeld niet weg onder iemand die net op een miniatuur geklikt had.

**In één verzoek, niet drie** (`DetailFetcher.DetailsAsync`). Het zijn drie gegevens van dezelfde
pagina; die drie keer ophalen zou bij een brugsite twaalf seconden kosten. `FotosAsync` (de
AI-controle) loopt sindsdien over dezelfde weg, met enkel de foto's eruit. Wat opgehaald is, blijft
onthouden op het adres van de pagina.

**Ingevuld voor alle dertien de sites** (26 september 2026; Vinted, Tweakers V&A en Delcampe
op 27 september). Welke selector en wat er gemeten is,
staat in `SITES.md` van `zentrix-sites`. Catawiki geeft 5 foto's van 1800 px waar de zoekpagina er
één gaf, eBay 5 van 1600 px, allebei in ongeveer 4,5 s via de brug; van allebei komt ook de
verkoper mee, en van Catawiki de volledige beschrijving.

De laatste vier kwamen er op dezelfde dag bij, elk nagemeten op vier echte zoekertjes: Kleinanzeigen
(3 tot 14 foto's, 960 naar **1600 px**), AutoScout24 (7 tot 50, 1024 naar **2048**), leboncoin (2 tot
10, 613 naar **1200**) en Discogs (1 tot 4, 300 naar **600**). Drie dingen die daar bovenkwamen en
die bij een volgende site weer kunnen spelen:

- **Eén site kan twee paginasjablonen door elkaar draaien.** Kleinanzeigen is halverwege een
  verhuizing: van zes advertenties stond er één op het oude sjabloon en vijf op het nieuwe. Een
  selector die enkel op het nieuwe werkt, faalt dan bij één op zes - zonder fout, gewoon leeg.
  Vandaar selectors die op allebei passen, met een komma-lijst waar dat nodig is.
- **De grootste variant staat niet waar je ze verwacht.** In de galerij van Kleinanzeigen staat
  `rule=$_59.AUTO` (960 px), terwijl `$_57.AUTO` dezelfde foto op **1600** geeft. Meet de varianten
  die in de pagina staan na, ook die in `data-`attributen; het is niet aan het getal te zien welke
  de grootste is.
- **Het origineel is niet altijd de beste keuze.** Bij AutoScout24 geeft het kale adres 5694x3202
  en **1,65 MB**, en een advertentie heeft daar tot vijftig foto's - in een fotostrook die ze
  allemaal laadt, is dat onbruikbaar. Daar is het 2048 px (253 kB) geworden.

**React-sites geven hun gegevens in één blok.** AutoScout24 en leboncoin hebben allebei
`script[id='__NEXT_DATA__']` met de foto's, de verkoper en de datum erin - dezelfde afweging als
het `ld+json`-blok van 2dehands, en een stuk stabieler dan klassenamen die bij elke uitrol
veranderen. De beschrijving komt er juist níet uit: in JSON staat ze vol `\u00e9` en `\"`, en in
de pagina staat ze gewoon.

Waar het staat, zegt het sitebestand - vier velden, alle vier ook in *Sites beheren*:

| Veld | Waarvoor |
|---|---|
| `DetailImagesSelector` | alle foto's van de advertentie (bestond al voor de AI-controle) |
| `DetailSellerSelector` | de verkoper, wanneer die niet al op de zoekpagina staat |
| `DetailPostedSelector` | sinds wanneer het online staat |
| `DetailDescriptionSelector` | de volledige beschrijving |

**De beschrijving is het ophalen waard**, want de zoek-API van 2dehands en Marktplaats kapt ze af
op **200 tekens** - en juist wat erna komt, zegt wat er mankeert of wat er precies bij zit. Gemeten:
1427 tekens op de pagina tegenover 200 op de zoekpagina. De 200 die we al hebben staan er meteen;
de volledige komt eroverheen zodra de pagina binnen is.

Twee dingen die de beschrijving vragen, en die je bij geen enkel ander veld tegenkomt:

- **Een selector plakt alle witruimte plat tot één spatie.** Terecht voor een titel of een prijs,
  maar het maakt van een beschrijving één brij. Daarom gaat er voor dít veld een kopie van de
  pagina in waarin `<br>` en het einde van een alinea gemarkeerd staan met `\u001F` - een
  stuurteken dat niet in `\s` zit en dus de opschoning overleeft, en dat in geen enkele advertentie
  voorkomt. `Opschonen` maakt er daarna echte regeleindes van, met hoogstens één lege regel
  achter elkaar en een grens van 4000 tekens.
- **Een letterlijke `\n` wordt alsnog een regeleinde.** 2dehands zet in het stuk dat uit zijn eigen
  databank komt de twee tékens `\` en `n` waar een regeleinde hoorde; onbewerkt staat dat zo op het
  scherm ("(LOSSE CD SPELER)\nOpslaglocatie: Onbekend"). Enkel in een beschrijving, want daar is
  een backslash-n vrijwel zeker een mislukt regeleinde.

De tekst staat in een `TextBox` zonder rand en niet in een `TextBlock`: er staat vaak een
typenummer of een maat in die je ergens anders wil plakken, en uit een TextBlock valt niets te
selecteren.

**Het onderste blok schuift** wanneer de tekst er niet in past, en die grens volgt de
vensterhoogte (`VolgVensterhoogte`: iets minder dan de helft). Zo groeit een lange beschrijving
mee wanneer je het venster groter maakt, krijgt een advertentie van drie regels geen half leeg
vak, en verdringt een handelaar met zijn algemene voorwaarden de foto niet. De grens staat op
het blok en niet op zijn rij - waarom dat uitmaakt, staat bij de UI-conventies; kort: een rij op
`Auto` meet met oneindige hoogte, en dan knipt ze de tekst af zonder ooit een schuifbalk te
tonen. Precies dat ging mis.

Twee dingen die daarbij horen:

- **"Online sinds" is tekst en geen datum**, om dezelfde reden als de tijd tot het einde van een
  veiling: elke site schrijft het anders op ("Eergisteren", "24 sep. '26", "Vandaag"), en wat de
  site zelf toont klopt altijd met wat een bezoeker daar ziet. Er wordt dus niets uitgerekend.
- **De verkoper hoeft meestal niet opgehaald te worden.** 2dehands en Marktplaats zetten hem al op
  hun zoekpagina (`SellerSelector`, dat er toch al staat voor "veilinghuizen overslaan"), dus daar
  blijft `DetailSellerSelector` leeg en staat de naam er meteen.

**Heeft een site geen van de drie velden, dan zegt het venster dat** ("Het sitebestand van Discogs
zegt nog niet waar de foto's en de verkoper staan") en wordt er niets opgehaald. Beter dan een leeg
vak waarvan niemand weet of het aan het laden is.

**De prijs komt uit `PriceTextConverter`**, dezelfde als op de kaart. Rekende dit venster zelf, dan
stond hetzelfde zoekertje hier op "€ 40" en in de lijst op "€ 39,95". Ook een **nul** gaat door die
converter, want de kaart doet dat ook: anders stond een zoekertje zonder prijs hier op "0" (de
eigen tekst van de site) en in de lijst op "€ 0". De eigen tekst is enkel de terugval wanneer er
helemaal geen prijs is.

Nagemeten met het venster buiten beeld op drie echte Facebook-zoekertjes: tijdens het ophalen
draait het wieltje en staat de foto van de zoekpagina er al (groot, dus het venster is niet leeg),
en na 3,7 tot 4,4 s zijn het er 2 tot 4 van de pagina, is de miniatuur van de zoekpagina weg, staat
de eerste van de pagina groot en zit er geen dubbel bij. Met de tegenproef op 2dehands, waar die
foto juist moet blijven: daar staan het er nog altijd 3 met die van de zoekpagina vooraan.

Nagemeten met het venster buiten beeld en een vers zoekertje uit de zoek-API van 2dehands (een
advertentie van gisteren kan al weg zijn): **3 foto's in 369 ms**, verkoper "Japoto", "24 sep. '26",
699 tekens beschrijving over 31 regels, zweven doet niets, een klik op miniatuur 2 wisselt de grote
foto en verzet het randje, een klik op de grote foto opent ze schermvullend met "800 × 800
beeldpunten" eronder, Esc sluit die laag zonder het venster te sluiten, en een site zonder die
velden toont de ene foto die we wel hebben met de reden erbij. De logica eromheen staat in
`FotoChecks`, met een proefsite die telt hoeveel verzoeken er komen - dat één verzoek is het hele
punt - en een vaste pagina voor de regelindeling van de beschrijving.

**Wat het nog niet doet:** "3 dagen online" uitrekenen uit "24 sep. '26" (dat vraagt een datumlezer
per site). 2dehands zet er trouwens ook "7x bekeken" en "0x bewaard" bij; dat is er met dezelfde
selector uit te halen.

### De derde weg: de aangemelde browser

**Sinds 26 september 2026 haalt `DetailsAsync` een pagina ook via de browser op**, naast de brug en
het gewone verzoek. Dat was nodig voor Facebook: zijn advertentiepagina is enkel met een aangemeld
profiel te openen, en dat profiel heeft `BrowserFetcher` al - het is hetzelfde waarmee Zentrix daar
zoekt. Het kost meer dan de andere twee wegen (seconden in plaats van tienden), en dat is te
verantwoorden omdat het pas gebeurt wanneer je zélf dubbelklikt.

Wat de opbrengst is, gemeten op vier echte Facebook-zoekertjes: **3 van de 4 gaven 4 tot 8 foto's
van 960x720** (tot 107 kB) waar de zoekpagina er één van 260x260 (13 kB) gaf, in 3,8 tot 5,4
seconden. De vierde gaf niets extra en deed er 20 s over: Facebook bouwt zijn pagina niet altijd
af, zeker niet bij vier keer laden na elkaar. Het venster toont dan gewoon de ene foto die er wel
is.

Drie dingen die daarbij hoorden:

- **Er moet gewacht worden tot de foto er staat.** Zonder dat lees je een pagina die nog niet af
  is, en bij Facebook is dat geen randgeval: op een van de opgehaalde pagina's stond **geen enkele
  `img`**, terwijl er al 9 MB omhulsel binnen was. Het CSS-stuk van `DetailImagesSelector` gaat nu
  mee als wachtselector (`CssDeel`: alles vóór `@attribuut` en vóór `::replace`/`::match`).
- **De maat van een Facebook-foto is geen bruikbaar kenmerk.** Op drie pagina's stonden er drie
  verschillende: `p960x960`, `s960x960` en `p720x720`. Het `alt`-label wel: de foto's van díe
  advertentie heten "Productfoto van ...", en de kaarten van de vergelijkbare items eronder
  "&lt;titel&gt; in &lt;plaats&gt;, VLG". Vandaar `img[alt^='Productfoto van']@src`. Dat leunt op
  de Nederlandse taal van Facebook, maar de app zet zelf `Locale = "nl-BE"` in `BrowserFetcher`,
  dus dat ligt vast.
- **Twee aanroepers konden allebei een Chrome starten** op dezelfde profielmap, en een profiel kan
  maar door één Chrome tegelijk geopend worden. Dat stond al als open punt bij "Wat er nog te halen
  valt"; met dit venster erbij werd het waarschijnlijk, want je kan dubbelklikken terwijl er
  gezocht wordt. Er staat nu een slot rond het *starten* (`BrowserFetcher._startSlot`); tabbladen
  in een draaiende Chrome mogen gewoon naast elkaar.

**De einddatum van AlleVeilingen loopt hier niet langs.** Die gaat via `FillAsync`, met een gewoon
verzoek, en dat blijft zo - dat draait automatisch voor elk zoekertje in beeld, en daar hoort geen
browser bij te komen.

## Bladeren door de vergrote foto's

Erbij op 2 oktober 2026. Klik je een foto groot, dan kostte de volgende er **drie**: deze kleiner
maken, een miniatuur kiezen, en die weer groot klikken. Nu staan er bovenaan twee pijlen met een
teller ertussen ("2 van 4"), en de pijltjestoetsen doen hetzelfde.

Drie keuzes die het gedrag bepalen:

- **De pijl die niet kan, staat er niet.** Bij de eerste foto geen pijl naar links, bij de laatste
  geen naar rechts - zo zie je meteen waar je bent, zonder een grijze knop die niets doet. Op
  `Hidden` en niet op `Collapsed`, want anders schuift de overblijvende pijl naar het midden en
  springt de teller heen en weer.
- **Bladeren verzet ook de foto eronder** (`ToonZoom` roept `ZetGroot` aan). Sluit je de
  vergroting, dan sta je op de foto die je als laatste bekeek - niet terug op die van daarvoor.
- **De tekst met het aantal beeldpunten wordt twee keer gezet.** Een `BitmapImage` van een
  webadres haalt zichzelf op de achtergrond op, dus bij het bladeren is het formaat nog nul; zodra
  de foto binnen is (`DownloadCompleted`) komt de juiste tekst er alsnog. Zonder dat stond er bij
  elke volgende foto enkel "Klik of Esc om te sluiten".

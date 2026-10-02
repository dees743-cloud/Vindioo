# Favorieten opvolgen

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

### Favorieten opvolgen: staat dit er nog, en wat kost het nu

Een favoriet is een **kopie**: hij blijft in de app staan ook als de site hem weghaalt, en de
prijs erop is die van de dag dat je hem bewaarde. Precies daar zit de vraag, en de eigenaar
stelde ze op 17 september 2026: staat dit er nog, en is het intussen duurder geworden? Bij een
kavel van Catawiki op 2dehands ging het bod tussen het bewaren en het terugkijken van € 5 naar
€ 24. Gebouwd op 30 september 2026 (`Services/FavoriteWatch.cs`).

Boven de lijst met favorieten staat een knop **Nakijken**. Die haalt per favoriet de
advertentiepagina op - **één verzoek per favoriet**, langs dezelfde drie wegen als het
detailvenster - en zet het antwoord op de kaart:

| op de kaart | wanneer |
|---|---|
| **Weg van de site** | de site antwoordt met 404 of 410 |
| **Veiling afgelopen op 14 september** | de einddatum op de pagina ligt in het verleden |
| **Nu € 24 - was € 5** | de prijs van vandaag verschilt van de bewaarde |
| Staat er nog, € 69 | de prijs is gelijk gebleven |
| de reden | niet na te gaan (site verdwenen, brug dicht, niets uit de pagina te lezen) |

Achter het aantal komt een samenvatting: "3 bewaard · 1 weg, 2 afgelopen". Weg en afgelopen
kleuren amber op de kaart; een prijs die veranderde is nieuws en geen waarschuwing.

**Het gebeurt niet vanzelf bij het openen van het tabblad.** Het kost een verzoek per favoriet,
dus jij vraagt het - dezelfde afweging als bij de grote foto en bij Tweakers. De knop is intussen
een **stopknop**, zoals het vergrootglas en de AI-controle, en om dezelfde reden: daar staat je
muis al. Ze gaan één voor één, want een brugsite heeft één wachtrij en de browsersites delen één
Chrome.

**Weg en afgelopen hebben geen veld in de sitebestanden nodig; de prijs bij een veiling wél.** Zo
is dat gemeten in plaats van aangenomen - op de drie echte favorieten en op verse advertenties:

| vraag | wat de meting gaf |
|---|---|
| hoe zie je dat iets weg is? | 2dehands antwoordt met **HTTP 410** en vier tekens. Ondubbelzinnig. **403 telt niet mee**: dat is "de site weigert de app", en dan weet je niets over het zoekertje zelf |
| en bij een veiling? | AlleVeilingen geeft gewoon een pagina (200), maar de einddatum staat er nog - "Einde op 14/09/2026 19:30" - en die wijst het sitebestand al aan met `DetailEndDateSelector`. Een gesloten kavel toont **geen bedrag** meer, dus daar is "afgelopen" het hele antwoord |
| en de prijs van vandaag? | 2dehands en Marktplaats zetten hem in het `ld+json`-blok (`Product.offers.price`), en dat klopte op vier verse advertenties met wat de zoekpagina zei: 0, 69, 1590 en 600. Een webstandaard, dus het werkt op elke site die hem gebruikt |
| en het bod op een kavel? | Daar werkt die webstandaard niet, en op 2 oktober 2026 is nagemeten waarom - zie hieronder. Vandaar `DetailPriceSelector` in het sitebestand |

### Het bod op een kavelpagina: `DetailPriceSelector`

Tot 2 oktober 2026 kwam een kavel zonder prijs binnen, en stond er dat het bod "pas met JavaScript
een getal wordt". **Die conclusie ging over de verkeerde pagina.** Bij het veilinghuis zelf klopt
ze - op bopa.be staat het getal in een inline script en zet Alpine het pas in beeld - maar de app
leest dat veilinghuis niet. Ze leest de kavelpagina van AlleVeilingen, en daar staat het bod
gewoon in de kale HTML. Twee keer zelfs, maar op geen van beide plaatsen kijkt de algemene lezer:

```
in de opmaak      <div class="row"><div>Huidig bod</div><div>€ 270,00</div></div>
in het datablok   {"@type":"PropertyValue","name":"Huidig bod","value":270.00,"unitText":"EUR"}
```

Twee dingen stonden die algemene lezer in de weg, en ze tellen allebei apart:

- **het bod zit niet in `offers.price` maar in `additionalProperty`**, onder een naam die de site
  zelf verzint ("Huidig bod"). Daar kan geen standaardlezer op af: dat is vrije tekst. **Deze
  reden blijft staan**, en daarom bestaat het veld;
- **het blok was voor hem onzichtbaar.** `FavoriteWatch` zocht het met een regex over de ruwe
  tekst, en de pagina schrijft haar type als `application/ld&#x2B;json`. Dat is sinds 2 oktober
  2026 rechtgezet - zie "Geen regexen meer op de pagina" hieronder - maar het veranderde hier
  niets aan: ook mét het blok in handen staat er geen prijs waar de standaard hem verwacht.

Daarom wijst het sitebestand het aan, net als de einddatum en de verkoper:

```
"DetailPriceSelector": "script[type='application/ld+json']::match(\"name\":\"Huidig bod\",\"value\":([\\d.]+))"
```

De selector gaat **voor** op het `ld+json`-blok, maar duwt het niet weg: levert hij niets op, dan
telt de gewone weg weer. Zo kan een opmaakwijziging bij één site de prijs bij de andere niet mee
omver halen.

**`PriceInCents` geldt hier niet**, en dat is met opzet: die vlag hoort bij de zoek-API van een
site (2dehands geeft daar centen), terwijl een advertentiepagina toont wat een bezoeker ziet.
Zou hij meetellen, dan werd € 270 ineens € 2,70.

Nagemeten op vijf echte kavels van vier veilinghuizen (Bopa, Troostwijk, Vavato, VH-Auctions):
1,00 / 11,00 / 35,00 / 55,00 / 65,00 euro, elk gelijk aan wat de pagina toont. Met drie
tegenproeven, want anders bewijst dat niets: **zonder** het veld geven diezelfde vijf pagina's
alle vijf `NULL`, en met een verkeerd label (`Hoogste bod`) of een verkeerd scripttype komt er
ook niets uit.

En de hele weg in één keer, met de echte code en het echte sitebestand: een rechtsklik op
`bopa.be/auction/520/lot/57380` levert in **1,1 seconde** de favoriet "Lot 1 - elektrische fiets
villette" van AlleVeilingen op, met **€ 270** en einde 08/10/2026 19:30. Dat getal is apart
nagekeken bij het veilinghuis zelf: bopa.be zet in zijn eigen script `highest_bid: 270` en
`closing_date: 2026-10-08 19:30:00`. De tussenpersoon vertelt dus niet iets anders dan de bron.

### Geen regexen meer op de pagina

`FavoriteWatch` las de pagina met drie reguliere expressies: één voor het `ld+json`-blok, één
voor `og:title` en één voor `<title>`. Sinds 2 oktober 2026 gaat alle drie langs de **ontlede**
pagina (AngleSharp), want een regex kent de regels van HTML niet:

| de pagina schrijft | de oude regex | de ontlede pagina |
|---|---|---|
| `type="application/ld&#x2B;json"` | niets | gewoon een `ld+json`-blok |
| `type=application/ld+json` (geen aanhalingstekens) | niets | idem |
| `type=" application/ld+json "` | niets | idem |
| `<meta content="..." property="og:title">` | niets - verkeerde volgorde | de titel |

**Waar dat echt beet**, en dat is een onderscheid dat ik eerst miste: niet overal. Het hangt af
van hóe de pagina binnenkomt.

- **Nakijken** gaat via `DetailFetcher`, en die zet voor een `NeedsBrowser`-site Playwright in.
  Wat daaruit komt is de **geserialiseerde DOM**: daar staat `application/ld+json` gewoon, en zag
  zelfs de oude regex het blok. Gemeten op een kavelpagina: ruw 32 kB met `ld&#x2B;json` 3x, via
  Playwright 582 kB met `application/ld+json` 3x.
- **Rechtsklikken** haalt de pagina met een gewone `HttpClient`, zonder browser. Daar stond de
  karakterverwijzing er nog, en vond de oude lezer **nul** blokken waar er drie staan.

**Nagemeten dat er niets kapotgaat**: 24 echte advertentiepagina's van de acht sites die
rechtstreeks antwoorden (2dehands, AlleVeilingen, AutoScout24, Delcampe, kleinanzeigen.de,
Marktplaats, Tweakers V&A, Vinted), oud naast nieuw. De gevonden blokken zijn **teken voor teken
gelijk**, en de prijs en de titel die eruit komen ook - 24 van de 24, nul verschillen. Ontleden
kost 1 tot 10 ms op pagina's van 100 tot 500 kB; de zwaarste (Vinted, 1,9 MB) 10 tot 23 ms.

Dat "teken voor teken" is geen bijzaak maar de spannende vraag. JSON zit vol tekens die in HTML
iets betekenen, en als de ontleding `&amp;` binnen een blok tot `&` zou maken, veranderden
titels stilletjes. Dat gebeurt niet: de inhoud van een `<script>` is *raw text* volgens de
HTML-norm, dus daar worden geen karakterverwijzingen in opgelost. Er staat een controle op.

**Met de tegenproef**: zet je de oude uitdrukkingen terug, dan vallen precies vier van de nieuwe
controles om - de drie over het scripttype en die over de volgorde van `og:title`. Zonder dat zou
niet vaststaan dat die controles iets meten.

Eerlijk over wat het vandaag oplevert: **op de dertien sites van nu verandert er niets**. Het bod
bij AlleVeilingen komt van `DetailPriceSelector`, niet hiervan. Wat weg is, is een klasse
fouten - het soort dat pas opvalt wanneer er een site bijkomt die haar HTML net iets anders
opschrijft, en dat dan stil misgaat in plaats van met een foutmelding.

**Elk antwoord "staat er nog" heeft bewijs nodig**: een prijs, of foto's van de
advertentiepagina. Komt de pagina binnen zonder een van beide, dan is het "niet na te gaan" en
niet "staat er nog" - anders meldt de app dat iets te koop staat terwijl niemand dat weet. Een
nul telt daarbij niet als prijs, net als elders: bij een "gezocht"-advertentie van 2dehands staat
er letterlijk `price 0`.

Nagemeten op 30 september 2026 op de drie echte favorieten, eerst met de dienst alleen en daarna
met het **hoofdscherm buiten beeld en de knop echt aangeklikt**: 4,8 s voor drie favorieten, de
knop werd "Stoppen" en weer "Nakijken", de teller zei "3 bewaard · 1 weg, 2 afgelopen", en een
foto van dat venster toont de drie regels ook echt getekend - de Denon op "Weg van de site" (410,
in 232 ms) en de twee kavels op "Veiling afgelopen op 23 september" en "op 14 september". Met de
tegenproef op een verse advertentie: "Nu € 69". De logica eromheen staat in `FavorietChecks`, met
een proefsite die per pad een andere status geeft (410, 404) - daarvoor kreeg `Proefsite` een
`Status`-tabel.

Twee valkuilen die daarbij bovenkwamen:

- **De kleur moet als `Setter` in de stijl staan, niet als attribuut op het element.** Een lokale
  waarde wint van een `DataTrigger`, en dan blijft "Weg van de site" gewoon grijs.
- **Een harnas zonder `app.Run()` heeft geen `SynchronizationContext`**, en dan komt de
  voortzetting na een `await` op een achtergronddraad terug - waarop elke schermwijziging omvalt
  met "the calling thread cannot access this object". In de echte app gebeurt dat niet; zet die
  context in het harnas, anders meet je het harnas.

Wat het **nog niet doet**: het onthoudt niets tussen twee starts (bij het opnieuw inlezen van de
favorieten gaan de regels weg), het kijkt niet vanzelf na op een schema, en het gaat één voor één -
met tientallen favorieten op een brugsite wordt dat traag. En bij een site met `NeedsBrowser` gaat
ook de advertentiepagina via Chrome, terwijl een kavelpagina van AlleVeilingen met een gewoon
verzoek binnenkomt: dat scheelt daar zo'n 4 seconden bij de eerste.

## Te zien op de kaart, en opruimen

Erbij op 2 oktober 2026. Boven de lijst stond wel "4 bewaard · 1 weg, 2 afgelopen", maar aan geen
enkele kaart was te zien **welke** dat waren. Met vier favorieten valt dat nog uit te zoeken, met
dertig niet.

Daarom staan er nu twee merktekens over de foto, in `Controls/PhotoThumbnail.xaml`:

| wat | hoe | wanneer |
|---|---|---|
| **weg van de site** | de foto dooft en er gaat een rood kruis over | `FavoriteState.Weg` |
| **veiling afgelopen** | een stempel **AFGELOPEN**, schuin over de foto | `FavoriteState.Afgelopen` |

Drie keuzes die daarachter zitten:

- **In de gedeelde miniatuur, niet in een eigen sjabloon.** `PhotoThumbnail` wordt door de
  lijstweergave én het raster gebruikt, dus zo zien ze er in allebei hetzelfde uit. Bij een gewoon
  zoekertje staan de vlaggen op `false` en is er niets van te zien - dezelfde afweging als bij
  `WatchText`, dat ook in het gedeelde sjabloon staat.
- **De merktekens staan binnen de `Border` met de `Clip`**, dus binnen de afronding. Erbuiten stak
  het kruis uit de ronde hoeken.
- **Het dempen hoort bij het kruis.** Een kruis alleen op een felle foto leest als versiering; met
  de foto gedoofd leest het als "hier valt niets meer te halen". En het bandje onder *AFGELOPEN*
  is nodig omdat wit op een lichte foto niet leest - nagemeten op vier proeffoto's, van bijna wit
  tot bijna zwart.

**Twee vlaggen en geen enum.** `Listing.WatchIsGone` en `WatchIsEnded`, met `WatchIsWarning`
ervan **afgeleid** in plaats van apart gezet. Met drie vlaggen naast elkaar kan er één
achterblijven, en dan zegt de tekst iets anders dan de foto. Let op bij het lezen: een afgeleide
eigenschap moet haar wijziging zelf melden (`OnPropertyChanged(nameof(WatchIsWarning))` in allebei
de setters), anders blijft de kaart in de oude kleur staan. `FavorietChecks` legt dat vast, met een
tegenproef die zonder die melding faalt.

**Opruimen** staat naast *Nakijken* en gooit weg wat weg of afgelopen is. Hij gaat af op diezelfde
vlaggen, dus hij doet pas iets ná een ronde Nakijken - en daarom staat hij uit zolang er niets te
ruimen valt. Een knop die niets doet en niet zegt waarom, laat je twijfelen of je wel goed klikte.
Met een bevestiging die zegt hoeveel en wat ("2 zoekertjes die weg zijn en 1 afgelopen veiling"),
zoals bij het verwijderen van een zoekopdracht of een site: een favoriet is iets wat je zelf
bewaarde en er is geen weg terug.

`ZetOpruimknop()` bepaalt die knop op **één** plaats en wordt ook door `UpdateEmptyHints()`
aangeroepen. Dat is nodig: wissel je van tabblad, dan haalt `LoadFavorites` de lijst opnieuw uit de
databank en zijn de merktekens weg - stond het ergens anders, dan bleef de knop aanstaan voor iets
wat er niet meer was.

**En *Nakijken* staat nu links bij de titel**, in de `Primary`-stijl van de hoofdknoppen elders in
de app. Rechts in de hoek viel hij niet op. Let op als je aan `ZetNakijkknop` raakt: die zet de
stijl na afloop terug, en dat stond nog op `Secondary` - waardoor de knop na één keer gebruiken
weer onopvallend werd.

## Een bewaarde veiling loopt bijna af

Erbij op 2 oktober 2026, in `Services/AuctionWatch.cs`. Tot dan keek de app enkel naar je
favorieten wanneer jij op *Nakijken* duwde. Een kavel waarop je wou bieden liep dus af terwijl je
iets anders deed, en achteraf stond er enkel "Veiling afgelopen op 28 september" op de kaart -
precies het moment dat je had willen weten, maar dan te laat.

Aanzetten in **Meldingen en achtergrond**, met de momenten erbij (1 dag, 4 uur, 1 uur, 15 minuten).
Het gaat via dezelfde kanalen als de rest: systeemvak, Telegram, e-mail.

**Er gaat hier geen enkel verzoek de deur uit**, en dat is de belangrijkste keuze. De waarschuwing
hangt aan de sluitingstijd die de app al kent. Zou dit zelf gaan ophalen, dan deed het dat elke
halve minuut voor elke favoriet, op een moment dat jij niet kijkt - en dat is precies wat een site
als robotverkeer ziet.

Die sluitingstijd komt van twee plaatsen, en allebei waren ze er al:

- **uit het zoekresultaat**, op het moment dat je het sterretje aanklikt. Bij een kavel staat de
  einddatum daar meestal al in (dat is wat de afteltimer op de kaart gebruikt);
- **uit een ronde *Nakijken***. `FavoriteWatch` las het einde van de advertentiepagina al, maar
  gooide het weg zodra bleek dat het in de **toekomst** lag - terwijl dat juist het nuttige geval
  is. Nu gaat het mee in `FavoriteStatus.Einde`, ook bij `TeKoop`, en het kost niets extra: de
  pagina is toch al gelezen.

**Een favoriet bewaarde zijn einddatum niet.** Die stond enkel in het geheugen, dus na een
herstart wist de app van geen enkele favoriet nog wanneer hij afliep. Er staan nu twee kolommen
bij in `favorites`: `endsAt` en `alertedLead`. Een waarschuwing die een herstart niet overleeft,
is geen waarschuwing.

**De regel die telt: de kleinste drempel waar we binnen zitten en die nog niet gemeld is.** Dat
"kleinste" is het hele punt. De planner tikt elke halve minuut, dus een regel die één keer te
breed staat, stuurt je tweehonderd berichten op een avond. Stond de app een nacht uit en kom je
terug met nog twintig minuten te gaan, dan zit je tegelijk binnen "1 dag", "4 uur" én "1 uur". De
grootste nemen zou drie meldingen na elkaar geven - één per tik - want elke fijnere blijft dan
openstaan. De kleinste nemen en die onthouden dekt alles wat grover is in één keer.

Dat is nagemeten, met een tegenproef die precies die cascade laat zien: met `max` in plaats van
`min` valt "en de grovere gaan niet alsnog achteraf af" om, en meldt de proefronde 1440 in plaats
van 60.

Drie dingen die daar nog aan hangen:

- **Eerst opschrijven, dan sturen.** Andersom zou een melding die halverwege vastloopt bij de
  volgende tik opnieuw vertrekken, en dan elke halve minuut.
- **Gerekend op de echte tijdlijn** (`Listing.Resterend`), niet met een kale aftrekking. Over de
  overgang naar de wintertijd scheelt dat een uur - dezelfde fout die de afteltimer had, zie
  `docs/resultaten.md`.
- **Anti-sniping.** Veilingsites verlengen bij een bod vlak voor sluitingstijd. Schuift het einde
  op, dan wist `SetFavoriteEnd` wat er al gemeld was, zodat je voor de nieuwe sluitingstijd
  opnieuw gewaarschuwd wordt. Hetzelfde einde nog eens wegschrijven doet dat **niet**, anders
  stuurde elke ronde *Nakijken* je de waarschuwingen opnieuw.

**Wat dit niet doet.** Het controleert niet zelf of de veiling nog loopt, dus een kavel dat
ingetrokken werd of waarvan de sluitingstijd veranderde zonder dat je *Nakijken* draaide, kan een
waarschuwing geven die niet meer klopt. En een favoriet waarvan de app nooit een einddatum zag
(een site die haar niet in het zoekresultaat zet, en je drukte nooit op *Nakijken*) krijgt geen
waarschuwing. De weg daarnaartoe ligt open: één ronde `FavoriteWatch.CheckAsync` per favoriet
waarvan het einde nadert, maar dan gaat er wél verkeer de deur uit en hoort daar een eigen keuze
bij.

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

**Er is geen nieuw veld in de sitebestanden voor nodig**, en dat is gemeten in plaats van
aangenomen - op de drie echte favorieten en op verse advertenties:

| vraag | wat de meting gaf |
|---|---|
| hoe zie je dat iets weg is? | 2dehands antwoordt met **HTTP 410** en vier tekens. Ondubbelzinnig. **403 telt niet mee**: dat is "de site weigert de app", en dan weet je niets over het zoekertje zelf |
| en bij een veiling? | AlleVeilingen geeft gewoon een pagina (200), maar de einddatum staat er nog - "Einde op 14/09/2026 19:30" - en die wijst het sitebestand al aan met `DetailEndDateSelector`. Een gesloten kavel toont **geen bedrag** meer, dus daar is "afgelopen" het hele antwoord |
| en de prijs van vandaag? | 2dehands en Marktplaats zetten hem in het `ld+json`-blok (`Product.offers.price`), en dat klopte op vier verse advertenties met wat de zoekpagina zei: 0, 69, 1590 en 600. Een webstandaard, dus het werkt op elke site die hem gebruikt - AlleVeilingen heeft er geen |

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

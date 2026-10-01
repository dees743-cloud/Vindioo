# Het hoofdscherm: indeling, tabs en de balk onderaan

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## De indeling van het hoofdscherm

Het scherm is opgebouwd zoals een app op een telefoon, met vier lagen:

1. **Blauwe kop** met de zoekbalk links en daarnaast **één** tandwiel. Daarachter
   zit alles: bovenaan *Instellingen van deze zoekopdracht* (welke sites meezoeken,
   met welke filters, en wanneer hij vanzelf draait), daaronder *Sites beheren*,
   *Site toevoegen*, *Sites importeren uit map*, *Koppelcode*, *Meldingen en
   achtergrond*, *Logboek openen* en *Weergave*. Elk begrip heeft één
   naam en elk pictogram staat er één keer: "Cards" (Engels), "Sitemap" (in webtaal een
   XML-bestand) en "Brug-code" (de extensie zegt "koppelcode") zetten de gebruiker op het
   verkeerde been, en hetzelfde oogje stond bij twee menu-items.

   *Sites beheren* opent meteen het venster met een tab per site (`ManageSitesMenu_Click`), op de
   site waarvan de tab openstaat. Tot 18 september 2026 hing daar een submenu met elke site apart,
   dat meegroeide met het aantal sites - een extra stap, want in het venster staan ze al als tabs.

   Er stonden hier een tijd **twee** tandwielen: een naast de zoekbalk voor de
   zoekterm en een rechtsboven voor de app. Ze zagen er identiek uit en stonden in
   dezelfde balk, dus niemand kon zien welke welke was. Nu is er één, en het
   onderscheid zit in het menu zelf — waar het te lezen valt in plaats van te raden.
   De titelbalk van Windows (`ui:TitleBar`) houdt enkel nog de knoppen van Windows.

2. **Zoekbalk**, wit op het blauw, met er vlak naast het tandwiel uit punt 1.
   De instellingen van de zoekterm staan bovenaan in dat menu: ze horen bij het
   woord dat ernaast staat, niet bij de app in het algemeen, en dus staan ze
   bovenaan en niet ergens tussen de rest.
   Bestaat er al een zoekopdracht met precies die zoekterm, dan bewerk je die;
   anders wordt er een nieuwe gemaakt met wat er op dat moment op het scherm
   staat, aangevinkte sites en filters inbegrepen.

   **De geopende bewaarde zoekopdracht hoort bij haar zoekterm** (`_activeSearch`,
   `IsActieveZoekterm`). Wie na het openen van "marantz" een ander woord zoekt, zoekt los
   daarvan. Die controle stond tot september 2026 enkel bij het vergrootglas: met Enter
   kwamen de resultaten van "fiets" in de zoekopdracht "marantz" terecht, als "al gezien",
   met een nieuw tijdstip dat de planner verzette en met de fouten van "fiets" over die van
   "marantz" heen. Nu staat ze vooraan in `RunSearchAsync`, zodat elke weg naar zoeken ze
   doorloopt, en opent het tandwiel de geopende zoekopdracht enkel als de zoekterm nog klopt.
   Hoofdletters en spaties aan de rand tellen niet.

3. **Eén tab per site die meezoekt**, met vooraan het tabblad **Alles** en
   achteraan het chipje **Sites**.
   Dat laatste heeft geen site en dus geen vinkje: het toont alles wat er binnen
   is, van elke bron samen. `SiteTab.Alles()` maakt het aan; `Def` en `Source`
   zijn daar leeg, en `IsAll` zegt het. De zoekinstellingen staan er uit — die
   horen bij één site — maar de volgorde en de weergave blijven bruikbaar.
   Op "Alles" geldt per zoekertje nog steeds de prijsgrens van zíjn eigen site.

   **De strip toont enkel de sites die meezoeken.** Vroeger stond er een tab per
   site mét een vinkje erin, en dan groeide de rij mee met het aantal sites
   terwijl je er doorgaans twee of drie gebruikt: acht sites vulden de volle
   breedte en braken al af naar een tweede rij, terwijl bij het opstarten alles
   uitgevinkt staat en die tabs dus geen van alle iets konden tonen. Nu groeit de
   strip mee met wat je gebruikt, en dat blijft altijd een handvol — hoeveel
   sites er ook bijkomen. Voor drie sites scheelde dat ongeveer 1950 naar 715
   beeldpunten.

   Het aan- en uitvinken zit daardoor in het **chipje achteraan** (`SiteChooser`),
   dat toont hoeveel sites meezoeken ("3 van 8 sites") en een lijst met vinkjes
   opent. Zoekt er niets mee, dan staat er "Sites kiezen" in de accentkleur: dan
   is dat het enige wat er nog te doen valt, en zonder die nadruk kijk je bij het
   opstarten naar één tab "Alles" zonder aanwijzing waar de rest gebleven is.

   Twee dingen die daarbij horen:

   - Het chipje zit **in dezelfde `WrapPanel`** als de tabs, via een
     `CompositeCollection` met een `CollectionContainer` rond de gefilterde
     weergave van de tabs. Zo loopt het gewoon mee achter de laatste tab, ook als
     de rij afbreekt. Het chipje als een extra `SiteTab` in de lijst stoppen lijkt
     eenvoudiger, maar dan krijgt elke plek die over "alle tabs behalve Alles"
     gaat er een uitzondering bij, en dat zijn er een stuk of tien.
   - Welke tab in de strip staat, volgt uit een filter op de weergave
     (`_tabsView`), en die weergave wordt ververst vanuit `PropertyChanged` op de
     `SiteTab` zelf — niet vanuit het vinkje. Anders moet élke plek die
     `IsEnabled` omzet eraan denken, en een bewaarde zoekopdracht die zijn sites
     oplegt (`PasToe`) doet dat ook. Vink je de site uit waarvan de tab openstaat,
     dan valt hij terug op "Alles" in plaats van op een lege pagina.

   Een site mag in zijn bestand een **kortere naam voor de tab** zetten
   (`ShortName`): "Facebook" in plaats van "Facebook Marketplace". In de strip
   staat alles naast elkaar en is breedte het schaarse goed; overal elders — de
   kaarten, de instellingen, de meldingen, de lijst achter het chipje — blijft de
   volledige naam staan, want daar is die de identiteit. Leeg laten betekent:
   gewoon de volledige naam. Het veld is bewerkbaar in het instellingenscherm.

   **Een site die mislukte, krijgt een waarschuwingsteken op zijn tab**, met de melding
   als tooltip (`SiteTab.ErrorText`). De statusregel onderaan noemt enkel nog hoeveel sites
   mislukten. Daar stonden vroeger alle fouten achter elkaar, en omdat die regel in een
   horizontale `StackPanel` stond, liep hij voorbij de vensterrand en viel het einde weg.
   Nu staat hij in een `Grid`, wordt hij afgekapt met een beletselteken en toont zijn
   tooltip de volledige tekst. Een lege lijst zegt ook wat er aan de hand is (`LeegTekst`):
   nog niets gezocht, niets gevonden, alles buiten je filters, of de site mislukte.

   De naam aanklikken opent de tab van die site. Je ziet dan **alleen de
   resultaten van die site** — dat gebeurt met een filter op de
   `ICollectionView` van de resultatenlijst, niet met een tweede lijst, zodat
   er één plaats blijft waar resultaten binnenkomen.
   Daaronder staat een rij met vooraan de **pager** — waar je in de resultaten zit —
   en daarnaast de **zoekinstellingen van díe site**: locatie, prijs, aantal per
   pagina, de sitegebonden filters, de volgorde, de weergaveknop en een knop naar
   zijn kaart.

   De pager stond eerst naast het logo in de kopbalk. Dat is een heel scherm
   verwijderd van waar je aan het kijken bent; vlak links van het locatiespeldje zit
   hij bij de resultaten waar hij over gaat. Elke site heeft dus
   zijn eigen postcode, straal en prijsgrenzen (`SiteTab.Filters`); er is geen
   gedeelde filterbalk meer.
   De tabstrip en de pagina worden getekend als **één doorlopende figuur**: een
   `Path` met de naam `TabFrame`, waarvan `UpdateTabFrame` in de code de vorm
   berekent uit de gemeten plaats van de open tab. Dat is de kern van de zaak:
   waar twee losse vormen tegen elkaar moeten aansluiten krijg je altijd een
   sprongetje of een puntje van een lijnuiteinde, hoe nauwkeurig je de
   coördinaten ook kiest. Binnen één figuur bestaat die naad niet.

   De vorm kent drie gevallen: geen open tab op de onderste rij (gewoon een
   afgerond vlak), de open tab helemaal links (de linkerlijn loopt door tot boven
   de tab en rondt daar af, zonder uitloop) en de open tab ergens in het midden
   (links en rechts een uitloop die naar buiten in de lijn buigt). `UpdateTabFrame`
   hangt aan `LayoutUpdated` en slaat over wanneer de maten niet veranderd zijn —
   zonder die controle zet elke toekenning van `Data` een nieuwe lay-outronde in
   gang en draai je in een lus.

   De open tab tekent zelf geen rand meer; de gesloten tabs hebben wel hun eigen
   randje en zweven tien pixels boven de lijn.

   Wat NIET werkt, en wat we achtereenvolgens geprobeerd hebben:

   - Een `Border` met `CornerRadius` voor de tab en een aparte `Border` voor de
     pagina. Een `Border` kan enkel naar **binnen** afronden; de tab wordt daar
     onderaan smaller van, terwijl de lijn juist naar buiten de paginarand in moet
     buigen.
   - Losse `Path`-vormpjes voor die uitloop, over de paginarand geschoven. Werkt
     op het oog, maar laat bij elke boog een puntje achter (het uiteinde van de
     lijn) en een sprongetje waar de boog de rechte lijn raakt, omdat beide op een
     andere halve pixel vallen.
   - De eerste tab laten inspringen zodat zijn uitloop op een recht stuk lijn
     landde. Lost de naad op maar zet de tab scheef; met één figuur is het niet
     meer nodig.

   Rond de tabs staat een `WrapPanel` en géén `ScrollViewer`: die laatste knipt af
   wat buiten zijn kader valt. Bij een smal venster breken de tabs af naar een
   tweede rij; staat de open tab dan niet op de onderste rij, dan tekent
   `UpdateTabFrame` een gewoon afgerond vlak zonder inkeping.
   Alle sites staan bij het opstarten **uitgevinkt**: elke extra site kost tijd,
   zeker die via de brug. Het veld "Standaard aangevinkt" op een kaart wordt
   daardoor niet gebruikt. De strip bestaat bij het opstarten dus uit "Alles" en
   het chipje.
4. **Balk onderaan** met vier tabbladen: *Zoeken*, *Favorieten*, *Recent* en
   *Zoekopdrachten*. Die wisselen enkel de zichtbaarheid van vier panelen.
   Op *Zoekopdrachten* staat per regel het schema en wanneer hij laatst liep, met
   een knop om hem nu te laten draaien, een vuilbakje en een knop naar zijn instellingen.
   Het vuilbakje vraagt eerst of het mag, net als de Delete-toets op een geselecteerde regel.
   Tot 19 september 2026 stond daarvoor een knop *Verwijderen* bovenaan, die pas werkte na
   het selecteren van een regel. *Nieuwe* en *Huidige vastzetten* staan vlak naast de titel,
   boven de kaarten; ze stonden eerst helemaal rechts, een schermbreedte van de lijst.
   Dubbelklikken toont de resultaten van de laatste beurt, zonder opnieuw te zoeken -
   bij sites via de brug scheelt dat al gauw een halve minuut wachten. De **teller**
   ervoor zegt hoeveel je daarvan nog niet bekeek; erop klikken toont enkel die (zie
   "3d. Nieuw is wat je nog niet bekeek").

   **Een geplande beurt neemt het scherm enkel over als het vrij is**
   (`Scheduler_Started`): je drukte zelf op haar afspeelknop, het scherm toont die
   zoekopdracht al, of er is nog niets gezocht en niets ingevuld. Vroeger nam ze het
   scherm altijd over zodra het venster in beeld stond, en dan waren je zoekterm, je
   vinkjes en je filters weg zonder dat je iets gevraagd had. De resultaten gaan niet
   verloren: de statusregel zegt wat de planner doet, en dubbelklikken toont ze.

   **Annuleren in het venster van een zoekopdracht annuleert** (`SearchSettingsWindow`).
   Wat je wijzigt, staat enkel op het scherm; pas *Bewaren* zet het in de zoekopdracht
   (`Neem(doel)`), en eerst na de controle op het zoekwoord. *Nu uitvoeren* draait op een
   kopie met hetzelfde `Id`. Tot september 2026 schreven *Nu uitvoeren* en een geweigerde
   *Bewaren* al in het echte object, hetzelfde object dat de planner gebruikt en bij zijn
   volgende beurt wegschrijft.

   Beide nagemeten met een testprojectje dat de vensters echt opbouwt (in een lege
   gegevensmap, het hoofdscherm buiten beeld getoond), en met de tegenproef: op de oude
   code faalden de geweigerde *Bewaren*, *Nu uitvoeren* en Enter.

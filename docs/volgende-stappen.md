# Volgende stappen

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## Volgende stappen

Wat er per site nog ontbreekt, staat bij "Nog open" in `SITES.md` van `zentrix-sites`.
Hieronder enkel wat aan de app zelf te doen valt.

1. **Facebook verliest soms een kwart van zijn kaarten tussen het scrollen en het lezen.**
   Het scrollen zelf is nagemeten en werkt (23 september 2026: 126 zoekertjes na 7 keer scrollen,
   98 resultaten in 18,0 s, zonder controlevraag). Wat er wegviel, is op 24 september uitgeplozen
   met de nieuwe logboekregel, en het bleek niet te liggen aan dubbels of aan kaarten zonder
   titel - daarvan zijn er maar 1 tot 3. Het scrollen telde 102 zoekertjes, en de motor kreeg er
   de ene beurt 102 te lezen en de andere maar 78; zie de linkmotor voor de cijfers. **De extra
   telling ligt er sinds 24 september**: het logboek zegt nu ook hoeveel zoekertjes er nog in de
   pagina staan vlak voor ze opgehaald wordt. Wat er nu nodig is, is een echte Facebook-beurt
   waarin het weer misgaat - dan wijst dat getal aan of de kaarten uit de pagina verdwenen of pas
   bij het uitlezen. De tabel in de linkmotor zegt wat welke uitkomst betekent.
2. Grote foto's op aanvraag. Bij sommige sites geeft de zoekpagina enkel kleine,
   bijgesneden foto's en staat de grote pas op de pagina van het zoekertje. Die
   ophalen kost een volledige browsersessie (5–10 s), dus niet tijdens het
   zoeken maar pas wanneer je erom vraagt.
3. **AI-controle op een foto**, lokaal op de grafische kaart. Gevraagd op 24 september 2026:
   de AI moet zien wat de eigenaar zelf niet ziet - een doos vol dvd's waarvan de titels te klein
   zijn, of wat voor toestel er staat en welk typenummer erop staat. Vier stappen:

   - ~~De motor.~~ Gedaan op 24 september 2026: `PhotoAnalyzer` knipt, leest, ontdubbelt en
     vertelt. Zie "AI-controle op een foto" bij Wat je te zien krijgt.
   - ~~Rechtsklik op één foto.~~ Gedaan op 24 september 2026: *AI-controle op deze foto*, naast
     *Prijsindicatie*, met `PhotoInsightWindow`.
   - ~~Alle foto's van dat zoekertje.~~ Gedaan op 24 september 2026: `DetailImagesSelector` in het
     sitebestand, opgehaald door `DetailFetcher.FotosAsync`. Ingevuld op 25 september voor
     2dehands, Marktplaats en AlleVeilingen, op 26 september voor Facebook, Catawiki en eBay, en
     diezelfde dag voor Kleinanzeigen, AutoScout24, leboncoin en Discogs, en op 27 september
     voor Vinted - dus voor alle elf.
   - **Meerdere zoekertjes tegelijk**, met vooraf een schermpje dat zegt hoeveel foto's en
     hoelang. Vraagt dat de lijst en het raster meervoudige selectie aankunnen; nog na te kijken.

   **Google Lens via de brug** is apart besproken en bewust achteraan gezet: het is níet offline -
   je stuurt dan een foto naar Google - er is geen officiële weg naartoe, en een Google-pagina
   besturen breekt bij de eerste opmaakwijziging. Wat het wél goed kan is precies wat het lokale
   model niet kan: een typenummer opzoeken en gelijkaardige items vinden. Dus: de vier stappen
   hierboven eerst en helemaal lokaal, en Lens daarna als een aparte knop waarvan je weet dat er
   iets de deur uit gaat.
4. De brug verder laten scrollen zodat sites met lazy loading meer
   resultaten geven
5. Het automatisch zoeken kan verder:
   - Een gemiste beurt wordt enkel dezelfde dag ingehaald (zie "3. De planner"). Stond de
     app drie dagen dicht, dan draait "dagelijks" één keer, niet drie - en dat is
     vermoedelijk ook wat je wil.
   - Per zoekopdracht toont de teller wat je nog niet bekeek. Een lijstje "dit kwam er
     sinds gisteren bij" over alle zoekopdrachten samen bestaat nog niet.
5b. **Favorieten opvolgen** (gevraagd op 17 september 2026, in drie stappen; alleen de tweede
   staat nog open):
   - ~~of een favoriet **nog te koop** is, en tegen welke prijs nu~~ (klaar op 30 september 2026,
     zie "Favorieten opvolgen" bij Wat je te zien krijgt). Nog open daarin: het onthouden tussen
     twee starts, en vanzelf nakijken op een schema;
   - **iets gelijkaardigs** vinden: dezelfde zoektocht als de prijsindicatie levert die al op;
   - **de prijsindicatie** (klaar, zie "Prijsindicatie"). Nog open daarin: verkochte prijzen van
     eBay als sterkere bron, en meer sites met het vinkje (Kleinanzeigen gaf een gewoon verzoek op
     17 september een 403, en moet eerst nagemeten worden).
6. Losse eindjes: `country` en `photosOnly` staan nog als ongebruikte kolommen
   in de SQLite-tabel van bewaarde zoekopdrachten (`photosOnly` zit nu in het JSON-blokje `config`).
   De ballon in het systeemvak toonde "zoekhulp" als afzender, de naam van het
   proces. Sinds de exe `Zentrix.exe` heet is dat vermoedelijk opgelost - nog na te
   kijken; anders vraagt het een AppUserModelID.
7. Uit de adviesronde van 15 september 2026, nog niet gedaan:
   - **Tekststijlen opruimen.** `TitleText`, `HeadingText`, `BodyText` en `CaptionText`
     staan in `App.xaml` maar worden bijna niet gebruikt; de XAML telt veertien
     lettergroottes, en `SettingsWindow` regelt tekstniveaus met `Opacity`. Veel, maar
     mechanisch werk.
   - **Playwright-vervolgpagina's naast elkaar**, zie "Wat er nog te halen valt".
   - Het veld `Enabled` ("Standaard aangevinkt") in het sitebestand doet niets en staat niet
     meer in *Sites beheren*. ("Max. resultaten" in het venster van een zoekopdracht botste
     daar ook mee; dat veld is weg sinds 22 september 2026, zie "Resultaten over pagina's".)
   - De koppelcode-controle, de rijstroken en de kortere wachttijden zijn met een build en
     een testprojectje nagekeken, maar nog niet gemeten tijdens een echte zoekopdracht.
8. Uit de adviesronde van 16 september 2026 (zes adviseurs, zie `.claude\agents`). Gedaan:
   de planner die bleef hangen of nooit startte, het afsluiten van andermans Chrome,
   Enter die in de vorige bewaarde zoekopdracht schreef, de planner die het scherm
   overnam, Annuleren dat niet annuleerde, dubbelklikken dat elke link opende, een
   webpagina die de brugsites kon laten overslaan, stil falen (nul na veel, controlepagina's,
   verdwenen sites, meldingen die niet vertrokken, Engelse foutmeldingen), een hernoemde site,
   de brug in golven, pagina 1 meteen, gecomprimeerde antwoorden, IdPattern, de linkmotor,
   Discogs met een aanhalingsteken, de weergave (contrast, afknippen, smal venster,
   pictogrammen, knopstijlen) en het controleproject. Nog te doen:
   - **Enkel live na te gaan**: of Kleinanzeigen pagineert met `s-seite:{page}` in het pad, of
     Catawiki een datum heeft in `time@datetime`, en of de brug met extensie 1.7 werkt.
     (**eBay via de brug werkt**, nagemeten op 26 september 2026: "cd speler" gaf 15 resultaten,
     en een kavelpagina kwam binnen in 4,6 s. De 0 resultaten van september waren dus niet
     blijvend.)
   - **Tekststijlen** (punt 7) en het opruimen van ongebruikte sleutels in `App.xaml`.
   - **Snelheid**: zie "Wat er nog te halen valt".
   - **Toetsenbord**: het sitechipje en de tabs zijn niet met Tab te bereiken.
   - **Zonder extensie** wacht elke zoekopdracht met een brugsite 30 seconden; de melding zegt
     nu wel hoe je de extensie installeert.
   - ~~**GitHub**~~ (beslist op 17 september 2026, **gedaan op 27 september**): `Zentrix` en
     `zentrix-sites` staan als **privé**-repositories op `dees743-cloud`, met het afgeschermde
     noreply-adres als afzender. Nagekeken op de remote zelf, niet enkel lokaal: er staat geen
     `.claude/`, `.codex/`, `AGENTS.md` of `chatgpt_tips.md` op, en geen `bin`, `obj`, `.vs` of
     `.user`. Pushen gaat sindsdien na elke commit, zonder het apart te vragen.

     **Openbaar maken is een aparte beslissing**, en die is in twee stappen genomen. Op
     27 september 2026 luidde het antwoord nee, met drie dingen die eerst moesten gebeuren; op
     **29 september is `Zentrix` openbaar gezet** nadat die drie er waren:

     - een **README** als voordeur - wat het is, hoe je het bouwt, en meteen eerlijk dat de app
       zonder sites komt en waarom;
     - een **licentie** (MIT, zoals de eigenaar koos). Zonder licentie is een openbare
       repository "alle rechten voorbehouden": te lezen, maar niemand mag er iets mee;
     - **nagekeken wat er werkelijk openbaar wordt**, en dat is meer dan de huidige bestanden:
       bij een openbare repository is de **hele geschiedenis** mee te lezen. Nagemeten over alle
       50 commits: geen enkele API-sleutel, geen Telegram-token, geen e-mailadres, geen postcode
       en geen Facebook-regionummer. Het enige persoonlijke was de Windows-gebruikersnaam in vijf
       paden; die zijn algemeen gemaakt. Wat in de geschiedenis blijft staan, is de naam van de
       auteur bij elke commit - dat is bij elke openbare repository zo, en het e-mailadres is het
       afgeschermde noreply-adres.

     **`zentrix-sites` blijft privé, en dat is principieel.** Die bestanden en `SITES.md`
     beschrijven per site hoe je zijn robotbeveiliging omzeilt, met de namen erbij, en bij
     Tweakers staat er zwart op wit dat we een pad gebruiken dat hun `robots.txt` verbiedt. Dat
     hoort niet als handleiding op straat. Daar komt bij dat `sites/facebook.json` het nummer van
     de Marketplace-regio houdt, en dat is bij benadering een woonplaats.

     Dat de app zelf wél openbaar kan, komt door een keuze van ver daarvoor: **Zentrix kent geen
     enkele site bij naam.** Elke site is een bestand. Zonder die splitsing was deze beslissing
     niet te nemen geweest zonder de sites mee te geven.
9. ~~**De twee zoeklussen samenbrengen**~~ (uit de beoordeling door ChatGPT van 20 september 2026).
   **Gedaan op 23 september 2026**, in vier stappen met bij elke stap een proef: tussentijdse
   leveringen per site, zoeken zonder bewaarde zoekopdracht, voortgang en fouten per site, en dan
   de verhuizing zelf. `SearchRunner` is nu de enige die zoekt en het scherm toont enkel; zie
   "Het scherm zoekt niet meer zelf" bij Automatisch zoeken.

   Waarom het moest: `MainWindow.RunSearchAsync` en `SearchRunner.RunAsync` voerden allebei een
   volledige zoekopdracht uit, en elke regel moest dus twee keer geschreven worden. Dat kostte in
   september 2026 al twee keer werk ("nieuw tot je kijkt", de nieuwe rem) en gaf echte verschillen:
   de prijsgrens en de gewijzigde filters, allebei rechtgezet op 22 september (zie "Filters werken
   meteen op wat er al staat").

   **De inventaris** (22 september 2026, door Codex, daarna punt per punt nagekeken in de code)
   somde de verschillen op. Bedoeld zoals ze waren, en dus geen werk: los zoeken zonder bewaarde
   zoekopdracht, een onuitvoerbare beurt die toch een tijdstip krijgt, tussentijdse resultaten
   enkel op het scherm, het wachten op het bewaren, meldingen enkel van de planner, en *Recent*
   enkel bij zelf zoeken. Rechtgezet bij de verhuizing: een verdwenen site die het scherm stil
   liet vallen, "al gezien" bij een site die halverwege faalt, `LastViewed` dat op het einde
   opnieuw gelezen hoort te worden, en de volgorde van het bewaren. Annuleren kwam er op
   24 september bij, en daarmee is er van die lijst niets meer open; zie "Een zoekopdracht
   stoppen" bij Automatisch zoeken.

   Ook nuttig uit die ronde: **de afspeelknop van een zoekopdracht start de planner**
   (`_scheduler.RunAsync`), en niet `RunSearchAsync`. Dat laatste loopt bij het vergrootglas, bij
   Enter, bij het sluiten van een filterpopup, en bij het openen van een zoekopdracht zonder
   bewaarde resultaten.

## Codeanalyse van 30 september 2026

Een collega liet de openbare code (commit `729f603`, versie 0.10.0) nalezen door een AI. Het
oordeel was mild over de code zelf; de grootste winst zat in **CLAUDE.md**, dat 281 kB groot was
en bij elke sessie volledig wordt ingeladen. Dat is opgelost (zie onderaan `CLAUDE.md`).

Wat er verder uit kwam, en wat ermee gebeurd is:

| | Punt | Stand |
|---|---|---|
| **Hoog** | de extensie haalt elke URL op die de app doorgeeft, met jouw cookies | **klaar** - twee sloten, zie `docs/brug.md` en `docs/sites.md` |
| Middel-laag | de `Id` uit een sitebestand ging ongefilterd in een bestandspad | **klaar**, zie `docs/sites.md` |
| Bug 1 | het zoekslot werd pas na het bewaren vrijgegeven, zonder eigen `finally` | **klaar** - anders wacht élke zoekopdracht daarna voor altijd |
| Bug 2 | een harde spatie in een prijs gaf `null`; de linkmotor maakte van "12.50" 1250 | **klaar** - één `PriceParser` voor beide motoren |
| Bug 3 | stoppen tijdens een brugsite telde als een fout van die site | **klaar** - een annulering is geen time-out meer |
| Bug 4 | twee Chrome's konden tegelijk op hetzelfde profiel starten | **klaar** - het startslot was een veld per instantie en is nu statisch; de site-analyse leent nu uit `BrowserPool` |
| Bug 5 | een Playwright-proces bleef achter bij een mislukte start | **klaar** - `try`/`finally` bij het starten én bij het afsluiten |
| Bug 6 | de brug zag de Chrome van Playwright voor die van de gebruiker aan | **klaar** - processen met ons eigen profiel tellen niet mee |
| Bug 7 | de afteltimer zat een uur fout rond de overgang naar de wintertijd | **klaar** - gerekend op de echte tijdlijn, zie hieronder |
| Bug 8 | de stopknop wachtte een trage pagina tot 45 s af | **klaar** - de token gaat mee met `WaitAsync` |
| Middel | een ander programma kan poort 8731 eerst bezetten en zich als de app voordoen | **open** - de opdrachten laten ondertekenen met de koppelcode (HMAC) |
| Middel | de koppelcode staat in de URL; elke `chrome-extension://`-herkomst wordt aanvaard | **open** - code in een kopregel, het extensie-ID vastpinnen, de Host-kopregel nakijken |
| Middel | een pagina die geanalyseerd wordt kan via verborgen tekst `baseUrl` elders laten wijzen | **open** - dezelfde hostcontrole als op `searchUrlTemplate` |
| Middel | de Anthropic-sleutel staat leesbaar in de omgevingsvariabelen van Windows | **open** - met DPAPI bewaren, zoals het mailwachtwoord |
| Laag | e-mail kan zich aanmelden zonder TLS | **open** |
| Laag | de extensie vraagt toegang tot alle sites | **open** - `optional_host_permissions` per site |
| Onderhoud | geen CI: de controles draaien enkel als iemand eraan denkt | **open** - een workflow op `windows-latest` |
| Onderhoud | `MainWindow.xaml.cs` (2804 regels) en `SiteAnalyzer.cs` (2261) opsplitsen | **open** |
| Onderhoud | dezelfde User-Agent staat vijf keer in de code | **open** - één `Services/Http.cs` |
| Onderhoud | `public async void StartOpAchtergrond()` is geen event-handler | **open** - `async Task` |

### Twee dingen die bij het nameten bovenkwamen

**Niet elke controle die je schrijft, bewijst de fout.** Bij bug 3 staat er nu ook een
`ct.ThrowIfCancellationRequested()` voor het wegschrijven, maar met de tegenproef (die regel
tijdelijk uit) slaagden de controles gewoon: er staat al eerder zo'n regel in de lus, dus dat
tweede vangnet gaat in de proef nooit af. Het blijft staan als vangnet, maar het is **niet**
aangetoond. De helft die wél stuk was, is het wel: met de oude `BridgeServer` gaf stoppen een
`TimeoutException`, en dus "gaf geen antwoord binnen de tijd" op de tab van die site.

**Een regel die van buiten komt, raakt ook je eigen proeven.** De nieuwe URL-controle bij het
importeren weigerde meteen een bestaande controle die sitebestanden importeerde die naar de
lokale proefsite (`http://127.0.0.1`) wezen. Terecht - haar proefgegevens wijzen nu naar een
gewone https-site. Let erop bij het schrijven van nieuwe controles: **importeren** is streng,
`Add` (een site die je zelf toevoegt) niet.

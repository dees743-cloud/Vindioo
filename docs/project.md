# Het project: naam, versie, gegevensmap, publiceren en GitHub

Onderdeel van de documentatie van Vindioo; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## De naam, de gegevensmap en GitHub

**Deze app heet sinds 3 oktober 2026 Vindioo**, met als ondertitel *Alles gevonden. Op één
plek.* Daarvoor heette ze **Zentrix**, en in de code daarvoor **zoekhulp**. De aanleiding voor
de laatste wijziging was niet technisch: de naam Zentrix bleek in meerdere vormen al te bestaan.

Die geschiedenis staat hier omdat ze nog in de app zit. Drie namen betekent twee verhuizingen,
en overal waar een oude naam blijft staan, staat hij er met opzet:

| Waar | Wat er staat | Waarom het blijft |
|---|---|---|
| `AppPaths.LegacyNames` | `Zentrix`, `Zoekhulp` | anders vindt de app de gegevensmap van een bestaande installatie niet |
| `AppPaths.OudeDataVariabelen` | `ZENTRIX_DATA` | een proefopstelling die daar nog op leunt, zou anders op de échte gegevens draaien |
| `Autostart.OudeNamen` | `Zentrix`, `Zoekhulp` | anders blijft Windows de oude exe mee opstarten |
| `App.OudeSloten` | `Local\Zentrix-een-exemplaar` | anders draaien een oude en een nieuwe versie tegelijk, en loopt de tweede vast op poort 8731 |
| `BridgeServer.Oude*Header` | `X-Zentrix-Brug` en twee andere | de extensie wordt apart herladen; zonder dit weigert de nieuwe app een extensie die nog niet bij is |
| `AppSettings.Extra` | `Zentrix-instellingen` | **sleutelmateriaal**, geen tekst: wijzig je dit, dan is de bewaarde API-sleutel onleesbaar |

Verder heet alles Vindioo: de namespace, het project `Vindioo.csproj`, de exe `Vindioo.exe`,
de gegevensmap `%APPDATA%\Vindioo` met `vindioo.db` en `vindioo-log.txt`, en de extensie
"Vindioo Brug". Alleen de projectmap `source\repos\zoekhulp` draagt nog de oudste naam van
de drie.

**De gegevensmap verhuist vanzelf.** `AppPaths` is de enige plaats die weet waar de
gegevens staan; vroeger schreven zeven klassen `%APPDATA%\Zoekhulp` elk zelf uit. Bij de
eerste start onder een nieuwe naam wordt de oude map hernoemd naar de nieuwe, en daarin de
databank en het logboek. Dat is een **keten**: `Vindioo` ← `Zentrix` ← `Zoekhulp`, nieuwste
eerst, zodat wie een hernoeming oversloeg zijn gegevens evengoed terugvindt. Een
naamswijziging op dezelfde schijf, geen kopie:
ook de 540 MB van het browserprofiel gaan mee zonder wachten. Lukt het niet - een bestand
in gebruik, bijvoorbeeld door een achtergebleven Chrome van Playwright - dan blijft de app
de oude map gebruiken en probeert ze het bij de volgende start opnieuw. Wat er gebeurde,
staat in het logboek.

Twee valkuilen die daarbij horen:

- `AppPaths` mag het logboek **niet** gebruiken. Het logboek vraagt zijn eigen pad aan
  `AppPaths`; een verhuis die zelf wil loggen, wacht dan op zichzelf. Daarom zet ze wat
  er gebeurde in `MigrationNote`, en `App` schrijft dat weg.
- Opstarten met Windows bewaart het **volledige pad** van de exe. Na het hernoemen wees
  dat naar `zoekhulp.exe`, die niet meer bestaat, en dan start Windows stilletjes niets.
  `Autostart.RefreshPath` zet het bij elke start gelijk.

## De versie

Het nummer staat op **één plaats**: `<Version>` in `Vindioo.csproj`. `Services/Versie.cs` leest
het daar uit de assembly, zodat het scherm nooit iets anders zegt dan het bestand. Sinds
27 september 2026 staat er een nul vooraan: de app doet wat ze moet doen, maar er staan nog
stukken open (zie "Volgende stappen"), en dat is wat die nul betekent.

> **Alles tot en met 0.17.3 is uitgebracht onder de naam Zentrix.** In het logboek hieronder
> staat overal de naam van nu, zodat het leesbaar blijft - maar de zips, de tags en de
> releases op GitHub van vóór 0.18.0 heten nog `Zentrix-…`. Dat is geen slordigheid: een
> uitgebracht bestand hernoem je niet achteraf.

**0.9.1** sinds 29 september 2026. Wat er veranderde zit helemaal in *Site toevoegen*: de
AI-analyse bekijkt nu ook de pagina van een zoekertje, stuurt de zoek-URL bij voor paginering in
het pad, en meet de paginering, de grote foto en de filters na in plaats van ze te geloven (zie
"Hoe de AI-analyse werkt", punten 5 tot 9). Geen nieuw scherm en geen nieuwe knop, dus geen 0.10:
dezelfde app, met een stuk dat zijn werk beter doet. Wat er voor 1.0 nog moet, staat onveranderd
bij "Volgende stappen".

**0.9.2** sinds 30 september 2026, en daar zit een les in over versienummers. 0.9.1 kreeg een tag,
en daarna kwamen er nog twee commits met echte verbeteringen aan diezelfde analyse: de filters
naar het eerste schema (186 000 tokens terug naar 113 000) en de prijsgrens die op de prijzen
beoordeeld wordt in plaats van op het verschil in de lijst. Toen er een release met de exe moest
komen, botste dat: **wat je uitbrengt moet zijn wat het nummer zegt.** De tag verplaatsen kan wel,
maar dan klopt een nummer dat al gepusht is niet meer met wat het ooit aanwees. Een nieuw nummer is
goedkoper. Dus: wie een versie tagt, brengt die ook uit, of nummert opnieuw.

**0.10.0** sinds 30 september 2026, en het tweede cijfer gaat mee omhoog omdat er iets **bij**komt
dat je ziet: de knop *Nakijken* op het tabblad Favorieten, die per favoriet zegt of het zoekertje
er nog staat en wat het nu kost (zie "Favorieten opvolgen" bij Wat je te zien krijgt). Dat is
precies het onderscheid dat 0.9.1 níet haalde - daar deed een bestaand stuk zijn werk beter, zonder
nieuw scherm en zonder nieuwe knop. Hier is het er wel een, dus geen 0.9.3. Uitgebracht op
dezelfde dag als `v0.10.0`, met de zip erbij (117 MB, 601 bestanden), en anoniem nagegaan dat die
te downloaden is: HTTP 206 en de eerste twee bytes zijn `PK`.

**0.10.1** sinds 1 oktober 2026. Een derde cijfer, want er komt niets bij dat je ziet: het zijn
rechtzettingen uit een codeanalyse die een collega liet maken van de openbare code (zie "Codeanalyse
van 30 september 2026" in `docs/volgende-stappen.md`). Wat je er wél van merkt:

- een prijs met een **harde spatie** ("1 499 €", zoals Franse en Spaanse sites ze schrijven) werd
  `null`, en zo'n zoekertje glipte door je prijsgrens heen en viel uit de prijsindicatie;
- de **afteltimer** van een veiling zat een uur fout rond de overgang naar de wintertijd - en dat
  is zondag 25 oktober;
- de **stopknop** kon een trage browserpagina tot 45 seconden niet onderbreken;
- **stoppen tijdens een brugsite** telde als een fout van die site, met een waarschuwingsteken op
  haar tab voor iets wat je zelf deed;
- de **brug** zag de Chrome van Playwright soms voor die van jou aan, wachtte dertig seconden op
  een extensie die er niet was, en sloeg dan alle brugsites over;
- en een gedeeld **sitebestand** wordt nu nagekeken voor het binnenkomt.

Het nummer is opgehoogd **voor** het publiceren, en met opzet: wat er in `C:\Vindioo` staat moet
zeggen wat het is. Bleef het op 0.10.0, dan stond er in het logboek "Vindioo 0.10.0 gestart" bij
iets anders dan de release met dat nummer, en dat is precies de les van 0.9.2 hierboven. Er hoort
dus nog een release `v0.10.1` bij, of het nummer gaat later opnieuw omhoog.

**0.11.0** sinds 1 oktober 2026, het laatste punt van die codeanalyse. Weer het tweede cijfer, en
deze keer niet omdat er in Vindioo iets bijkomt: de **extensie** vraagt geen toegang tot alle
sites meer, maar per site - en daarvoor zit er onderaan haar popup een lijst met de sites en een
knop om ze aan te vinken. Dat is een nieuw stuk dat je ziet én iets wat je één keer zelf moet
doen, dus geen 0.10.6.

**Let op bij deze versie:** `extension/manifest.json` gaat van 1.9 naar **2.0**, en de extensie
moet dus herladen worden (`chrome://extensions`, Herladen). Daarna staat er niets aangevinkt en
zoekt geen enkele brugsite nog, tot je in de popup op de knop klikt. Dat is niet te vermijden:
Chrome geeft toestemming per extensie, en wie van `<all_urls>` naar toestemming-per-site gaat,
begint per definitie bij nul. Zie `docs/brug.md` bij "Toegang per site".

**0.12.0** sinds 2 oktober 2026. Het tweede cijfer, want er komt iets bij dat je ziet: op het
tabblad Favorieten staat *Nakijken* nu links bij de titel in de hoofdknop-stijl, er staat een knop
**Opruimen** naast, en de kaarten dragen voortaan een **rood kruis** (weg van de site) of een
stempel **AFGELOPEN** (veiling voorbij). Dat laatste was het echte gemis: de teller zei wel "1 weg,
2 afgelopen", maar niet wélke. Zie `docs/favorieten.md`. De extensie verandert niet mee en blijft
op 2.0 - herladen hoeft dus niet.

**0.13.0** sinds 2 oktober 2026, dezelfde dag. Opnieuw het tweede cijfer: er komt een melding bij
die er niet was. Vindioo waarschuwt nu dat een **bewaarde veiling bijna afloopt**, via systeemvak,
Telegram of e-mail, op momenten die je zelf kiest (1 dag, 4 uur, 1 uur, 15 minuten). Aanzetten in
*Meldingen en achtergrond*; standaard staat het uit. Zie `docs/favorieten.md`.

Het nummer gaat omhoog en blijft niet op 0.12.0 staan, hoewel daar nog geen release bij hoort:
`C:\Vindioo` draaide al een exe die zich 0.12.0 noemde, met andere inhoud. Twee builds met
hetzelfde nummer in hetzelfde logboek is precies de les van 0.9.2 hierboven.

De databank krijgt er twee kolommen bij (`favorites.endsAt` en `alertedLead`); dat gaat vanzelf
bij de eerste start. Van je bestaande favorieten kent de app de sluitingstijd nog niet - die komt
er bij de eerstvolgende ronde *Nakijken* in, of zodra je ze opnieuw bewaart.

**0.14.0** sinds 2 oktober 2026. Rechtsklik in Chrome op een zoekertje - op de link in een lijst,
of op de advertentiepagina zelf - en kies **Zet in favorieten van Vindioo**. Dat is de eerste weg
die van de browser naar de app loopt in plaats van omgekeerd; zie `docs/brug.md` bij "De andere
kant op: rechtsklikken op een zoekertje".

**De extensie moet herladen worden** (`chrome://extensions`, Herladen): ze gaat van 2.0 naar
**2.1** en vraagt twee rechten bij, `contextMenus` en `notifications`. Dat tweede is voor de
terugmelding - de popup staat niet open wanneer je rechtsklikt, dus zonder melding zou je nooit
weten of het gelukt is. Je toestemmingen per site blijven staan; daar verandert niets aan. De
extensie heeft meteen ook een eigen pictogram gekregen in plaats van het puzzelstukje.

**0.14.1** sinds 2 oktober 2026. Het **derde** cijfer, want er komt niets bij dat je ziet: een
bestaand stuk doet zijn werk beter. Volgparameters (`?fbclid=`, `?utm_source=`) en het stuk achter
`#` tellen niet meer mee voor de identiteit van een zoekertje. Dat merk je enkel bij het
rechtsklikken in Chrome op een site zonder `IdPattern`: daar kreeg dezelfde kavel anders twee
kaarten, één van het zoekresultaat en één van Chrome. Zie `docs/brug.md`.

Nagemeten met de echte methode over de echte databank: van 4510 bewaarde identiteiten die een
volledig adres zijn, verandert er geen enkele. Dat was de vraag die ertoe deed - was het antwoord
anders geweest, dan had alles wat je ooit zag opnieuw als nieuw geteld.

**0.15.0** sinds 2 oktober 2026. Het tweede cijfer: rechtsklikken werkt nu ook op een **veilinghuis
dat Vindioo niet kent**. Je staat op bopa.be, kiest *Zet in favorieten van Vindioo*, en de app
zoekt dat kavel terug op je veilingsites - zeker, niet gokkend: de kavelpagina van AlleVeilingen
draagt een link terug naar het veilinghuis, en die moet het adres zijn waarop jij klikte. Geen
treffer betekent dat er niets bewaard wordt en dat gezegd wordt. Zie `docs/brug.md` bij "Een kavel
van een veilinghuis dat Vindioo niet kent".

Daar hing één wijziging aan die **elk** verzoek van de app raakt: `HttpFactory` stuurt nu de
kopregels mee die bij zijn User-Agent horen (`Accept` en `Sec-Fetch-*`). bopa.be gaf anders 429 op
het eerste verzoek. Met een tegenproef nagegaan dat de zeven rechtstreekse sites er niets van
merken. De extensie verandert niet mee en blijft op 2.1.

**0.16.x** van 2 oktober 2026, in stappen. **0.16.0**: bladerpijlen op de vergrote foto en een
pop-upvenster dat Vindioo niet meer achter een ander programma laat verdwijnen. **0.16.1**: die
pijlen reageerden niet op een klik - ze stonden in de strook die bij de titelbalk hoort, en die
ligt voor de muis boven de donkere laag. **0.16.2**: de grote foto houdt een maat die met de
breedte van het venster meegroeit in plaats van met de lengte van de beschrijving, en het
pictogram van de app is weer zichtbaar. **0.16.3**: dat pictogram in de gebruikelijke indeling,
met de maten die Windows bij schaling gebruikt.

Bij dat pictogram hoort een waarschuwing voor de volgende keer: na het vervangen bleef de taakbalk
een wit blad tonen, en dat lag niet aan de app maar aan de **iconcache** van Windows. Wissen met
`ie4uinit.exe -show`. Zie `docs/weergave.md` voor de drie metingen die dat uitwijzen.

**0.17.0** sinds 2 oktober 2026. Een favoriet die je via een **veilinghuis** toevoegt, krijgt nu
het **huidige bod** mee. Tot dan kwam zo'n kavel prijsloos binnen, met als uitleg dat het bod
"pas met JavaScript een getal wordt" - en die conclusie ging over de verkeerde pagina. Bij bopa.be
klopt ze, maar de app leest bopa.be niet: ze leest de kavelpagina van AlleVeilingen, en daar staat
het bod gewoon in de kale HTML. Het nieuwe veld `DetailPriceSelector` in het siteformaat wijst aan
waar; in *Sites beheren* heet het "Prijs (op de pagina zelf)". Zie `docs/favorieten.md` voor de
meting en de drie tegenproeven.

Wie AlleVeilingen al had staan, **importeert dat sitebestand opnieuw** of plakt de selector in dat
vakje - anders verandert er niets. Bestaande favorieten houden hun lege prijs; *Nakijken* vult ze
voortaan wel in.

**0.17.1** sinds 3 oktober 2026. `FavoriteWatch` las een advertentiepagina met drie reguliere
expressies - voor het `ld+json`-blok, voor `og:title` en voor `<title>`. Die gaan nu alle drie
langs de **ontlede** pagina. Aanleiding was de vorige stap: een regex vindt `ld+json` niet meer
zodra een site haar scripttype als `application/ld&#x2B;json` schrijft, en dat deed AlleVeilingen.
Op de dertien sites van vandaag verandert er **niets** - nagemeten op 24 echte advertentiepagina's
van de acht sites die rechtstreeks antwoorden, met dezelfde blokken teken voor teken en dezelfde
prijs en titel. Wat weg is, is een klasse fouten die pas opvalt wanneer er een site bijkomt. Zie
`docs/favorieten.md` bij "Geen regexen meer op de pagina", met de tegenproef waarin vier controles
omvallen zodra de oude uitdrukkingen terugkomen.

**0.17.2** sinds 3 oktober 2026. Een advertentie bij kleinanzeigen die *Segelyacht Compromis
777 "Fiete"* heet, kwam als favoriet binnen met `&#034;` in plaats van de aanhalingstekens. De
site codeert tekst die zelf al gecodeerd was, en de ontleder haalt daar maar één slag af; nu gaat
er één slag bovenop, op de titel en nergens anders. Gemeten: **1 van de 38** advertentietitels
verandert en dat is precies de kapotte, **0 van de 178** zoekresultaten hadden dit probleem. Zie
`docs/favorieten.md` bij "Een titel die dubbel gecodeerd is" - ook voor de misstap onderweg, want
mijn eerste oplossing zat op de verkeerde plaats.

**0.17.3** sinds 3 oktober 2026. Een zoekopdracht die niet over auto's gaat, liet
**AutoScout24 kapot lijken**. Die site heeft geen vrije tekstzoekfunctie - het zoekwoord is het
merk - en elk ander woord geeft daar een 404. Dat telde als mislukking, dus een bewaarde
zoekopdracht naar "cd speler" met die site erbij mislukte élke beurt, stuurde na twee beurten een
melding dat de site stuk was, en bleef daarna voor altijd rood staan. Nu is het een eigen soort
antwoord: je leest op de tab van die site waarom er niets kwam, maar het telt nergens als fout.
Zie `docs/zoeken.md` bij "niet alles wat geen resultaat geeft, is een mislukking".

**0.16.0** sinds 2 oktober 2026. Twee dingen aan de vensters. Een **vergrote foto** heeft nu
bladerpijlen bovenaan met een teller ertussen ("2 van 4"), en de pijltjestoetsen doen hetzelfde;
de volgende foto kostte er drie klikken. Bij de eerste foto staat er geen pijl naar links en bij
de laatste geen naar rechts. En een **pop-upvenster sluiten** haalt Vindioo weer naar voren in
plaats van het achter een Verkenner of Chrome te laten verdwijnen. Zie `docs/zoekertje.md` en
`docs/weergave.md`.

Het is op twee plaatsen zichtbaar, en allebei om dezelfde reden - **er draaien twee exe's op
deze pc**, een uit Visual Studio en een gepubliceerde (bij mij `C:\Vindioo`), met
dezelfde gegevensmap:

- **Onderaan het tandwielmenu**, als een grijs regeltje ("Vindioo 0.9.0"). Geen menu-item: er
  valt niets te klikken.
- **In het logboek bij elke start**, met de map erbij: `Vindioo 0.9.0 gestart vanuit
  C:\Vindioo`. Zonder die regel staat er in een logboek van twee weken niet bij
  welke versie een fout maakte.

`Version` levert ook `FileVersion` en `ProductVersion` op het bestand zelf, en die laatste
krijgt van de bouwomgeving de commit-hash erachter (`0.9.0+5998a84...`). `Versie.Nummer` knipt
dat af. Nagemeten met het hoofdscherm buiten beeld: het menu toont "Vindioo 0.9.0".

Let op bij het controleproject: dat compileert de broncode zelf, dus `Versie` leest daar de
assembly van *dat* project (1.0.0). Het nummer van de app is er dus niet na te meten - enkel
dat er een leesbaar nummer uit komt.

**Vindioo staat openbaar op GitHub** sinds 29 september 2026
(`dees743-cloud/Vindioo`, MIT-licentie); **`vindioo-sites` blijft privé**. Wat daarvoor nodig was
en waarom de sites niet meegaan, staat bij "Volgende stappen" punt 8. Twee bestanden zijn er toen
bijgekomen en horen bij een openbare repository: `README.md` (de voordeur: wat het is, hoe je het
bouwt, en dat de app zonder sites komt) en `LICENSE`.

**In de README staat een schermafbeelding** (`docs/schermafbeelding.png`, 29 september 2026), en
die is met opzet niet van de eigen Vindioo gemaakt. Ze toont een **lege gegevensmap**
(`VINDIOO_DATA` naar een verse map) met vier rechtstreekse sites, zodat er geen bewaarde
zoekopdrachten, favorieten, postcode of straal in beeld staan. Facebook blijft er bewust uit: die
zoekt in je eigen regio, en dan staan de steden rond je thuis op een foto die openbaar gaat. Zo is
ze opnieuw te maken:

- De sitebestanden naar `<map>\sites` kopiëren, de app één keer starten zodat `vindioo.db`
  bestaat, en dan met een scriptje een bewaarde zoekopdracht in de tabel `searches` zetten met
  `RunOnStartup` aan. De planner draait ze dan bij het opstarten
  (`SearchScheduler.RunStartupSearchesAsync`) en het scherm staat vanzelf klaar. Dat is de
  eenvoudigste weg, want de vinkjes voor de sites zitten in het chipje achteraan de tabstrip en
  dat is niet met het toetsenbord te bereiken.
- Het venster op maat zetten met `SetWindowPos` (1700x1450 beeldpunten is op 150% gelijk aan
  1133x967 eenheden van WPF) en fotograferen met `tools/vensterfoto.py`.

**Wat daarbij opviel: het raster bleef leeg - en dat lag aan de opstelling, niet aan de app**
(uitgezocht op 30 september 2026). Bij het maken van die schermafbeelding stonden met de
rasterweergave (`ResultView` 1) de tabs, de pager en de teller er wel - 4341 resultaten over 44
pagina's - maar werd er geen enkele kaart getekend. Nagemeten met een wegwerpprojectje dat het
**echte** hoofdscherm buiten beeld opbouwt in rasterweergave en daarna in het paneel kijkt: 16
kaarten opgebouwd, kaartmaat 278 x 309, 4 kolommen, bereik 1112 x 7733, geen fout in het logboek -
en een foto van dat venster toont die kaarten ook echt getekend, door leveringen, opruimen en
scrollen heen. Bij gewoon zoeken verschijnen de resultaten dus, zoals de eigenaar ook meldde.

Leeg krijg je het enkel met een kunstgreep, en die twee zijn het noteren waard omdat ze zeggen
waar het paneel bros is:

- **Een kaartmaat die ooit ontaardt, blijft.** `_kaart` wordt één keer gemeten en nooit meer.
  Met de maat via reflectie op 1 x 1 gezet: 100 kaarten opgebouwd, 1332 kolommen, bereik 1332 x 1 -
  alles staat er, in vakjes van één beeldpunt, en het scherm is leeg.
- **De herkansing is in een oogwenk op.** Wissel je van weergave terwijl de lijst al gevuld is, dan
  wordt het nieuwe paneel gemeten voor zijn generator er is, en vraagt het een herkansing. Die tien
  pogingen zijn binnen **5 ms** verbruikt - ze ketenen op `DispatcherPriority.Loaded`, dus ze lopen
  zo snel als de dispatcher kan - terwijl de generator pas later komt. Daarna wacht het paneel op
  iets dat een nieuwe meting uitlokt. Bij echt zoeken is dat de eerstvolgende levering, en dan staat
  het er meteen; in een opstelling waar niets meer binnenkomt, blijft het leeg.

**Wat niet op GitHub hoort** staat in `.gitignore`: `bin`, `obj`, `.vs`, de
`.user`-bestanden en `.claude` (de adviseurs, met persoonlijke paden). Sinds 22 september 2026
ook wat andere hulpmiddelen achterlieten: `.codex` (een kopie van die adviseurs), `AGENTS.md` (een
kopie van dit bestand) en `chatgpt_tips.md`. De sites staan sowieso niet in het project maar in de gegevensmap.

**De app komt zonder sites.** Die staan in een aparte repository, `vindioo-sites`: een
algemene zoekmotor publiek delen is iets anders dan kant-en-klare bestanden die op
bepaalde sites gericht zijn, en een deel daarvan omzeilt bewust de beveiliging tegen
robots. Wie de app voor het eerst start, ziet daarom geen lege lijst met "typ hierboven
wat je zoekt", maar **"Nog geen sites"** met twee knoppen: *Sites importeren uit map* en
*Site toevoegen* (`NoSitesPanel`, getoond vanuit `UpdateEmptyHints`). Importeren staat ook in
het tandwielmenu.

`SiteStore.ImportFolder` leest alle sitebestanden uit een map, elk met dezelfde
ontdubbeling als een losse import. Heeft de gekozen map een submap `sites`, dan leest hij
die: zo mag je de map van de repository zelf kiezen, of je eigen gegevensmap, waar naast
de sites ook `instellingen.json` staat. Nagemeten in een lege gegevensmap: 0 naar 10
sites, met elke sleutel, motor en eigen filter intact, en een tweede import gaf geen
dubbels.

**Een andere gegevensmap** kan met de omgevingsvariabele `VINDIOO_DATA`. Zo is een lege
eerste start na te bootsen zonder aan je eigen sites en zoekopdrachten te komen:

```bash
set VINDIOO_DATA=C:\ergens\leeg
```

Er wordt dan niets verhuisd; `AppPaths` gebruikt die map gewoon.

**De brug-extensie staat in `extension\`.** Chrome laadt een uitgepakte extensie vanaf een
vaste map; sinds 15 september 2026 is dat deze. Wie nog een oude kopie heeft (bv.
`C:\zoekhulp-brug`): `chrome://extensions`, de oude extensie verwijderen, "Uitgepakte
extensie laden", deze map kiezen en de koppelcode opnieuw plakken - een andere map is voor
Chrome een andere extensie, met een lege opslag.

## Starten zonder Visual Studio

Een versie die je gewoon dubbelklikt, zet je met het commando hieronder in een map naar keuze
(hier `C:\Vindioo`, sinds 18 september 2026). Ze is **zelfstandig**: .NET zit erin, dus ze start ook op een pc zonder
Visual Studio of .NET. Opnieuw maken na een wijziging, met Vindioo dicht (anders zijn de
bestanden in gebruik):

```bash
dotnet publish Vindioo.csproj -c Release -r win-x64 --self-contained true -o C:\Vindioo
```

Wat daarbij hoort:

- **Snelkoppelingen** "Vindioo" staan op het bureaublad en in het startmenu. Let op als je
  bureaublad door OneDrive beheerd wordt: dan staat het niet waar je het verwacht. Die in het
  startmenu staat onder `%APPDATA%`, dus vanuit de Claude-app aangemaakt via
  `Win32_Process.Create` - anders belandt hij in de omgeleide kopie en verschijnt hij nooit.
  Verhuist de map, dan wijzen ze nergens meer naar.
- **De hele map hoort bij elkaar**, niet enkel de exe: ruim zeshonderd bestanden, samen zo'n
  290 MB. Het grootste deel is .NET zelf en het stuk van Playwright dat Chrome aanstuurt
  (`.playwright\node`). Daarom geen "enkel bestand": Playwright zoekt die map naast de exe.
  Browsers hoeven er niet bij, want Playwright gebruikt de gewone Chrome (`Channel = "chrome"`).
- **Dezelfde gegevens als vanuit Visual Studio**: `%APPDATA%\Vindioo`, met dezelfde sites,
  favorieten en zoekopdrachten. En er draait er maar één tegelijk: sluit de ene voor je de
  andere start.
- **Opstarten met Windows volgt de exe die het laatst draaide.** `Autostart.RefreshPath` zet het
  pad bij elke start gelijk. Start je vanuit Visual Studio, dan wijst het naar
  `bin\Debug\...\Vindioo.exe`; start je daarna de gepubliceerde, dan naar die. Wie wil dat
  Windows de gepubliceerde start, start die dus één keer na het werken in Visual Studio.
- **Proefstarten vanuit de Claude-app** gebeurt met `VINDIOO_DATA` naar een lege map (zie "Fouten
  opsporen" over de omgeleide gegevensmap). Het register hoeft daarbij niet bewaakt te worden:
  ook dat is vanuit de Claude-app omgeleid, dus de proef kan het echte "opstarten met Windows"
  niet wijzigen. Zo nagemeten: proces draaiend na 7 s, "hoofdscherm opgebouwd" in het logboek,
  databank aangemaakt, brug op 8731.

## Wat er al werkt

- Zoeken over meerdere aangevinkte sites tegelijk, met voortgang en tijd per
  bron. De app komt zonder sites: welke er zijn en wat elk kan, staat in `SITES.md`
  van `vindioo-sites`. Tijdens het zoeken wordt het vergrootglas een **stopknop**: wat al
  binnen was blijft staan, en de zoekopdracht telt die halve beurt niet mee
- Sites importeren uit een map (ook meteen bij een lege eerste start), toevoegen
  met de AI-analyse, en verwijderen in het instellingen-scherm (de knop
  "Verwijderen" op de kaart van die site)
- Vastgezette zoekopdrachten op hun eigen tabblad, met een teller en een NIEUW-markering
  voor wat je nog niet bekeek - dat stapelt op over de beurten heen tot je de zoekopdracht
  opent. Een klik op de teller toont enkel de nieuwe (schakelaar "Enkel nieuwe"), dubbelklikken
  alles. Overleeft een herstart; "Huidige vastzetten" voegt er een toe
- **Favorieten**: het sterretje op elke foto zet een zoekertje apart. Wat je
  bewaart, wordt als kopie opgeslagen, dus het blijft zichtbaar ook als de site
  het zoekertje intussen weghaalt. De knop **Nakijken** zegt per favoriet of het er nog
  staat ("Weg van de site", "Veiling afgelopen op 14 september") en wat het nu kost
  ("Nu € 24 - was € 5")
- **Recent**: elke zoekterm komt in een lijst met wanneer je hem gebruikte;
  dubbelklikken herhaalt de zoekopdracht
- **Automatisch zoeken**: elke zoekopdracht heeft zijn eigen schema (om de zoveel
  minuten of dagelijks op een uur), eventueel binnen een tijdvenster, eventueel
  ook bij het opstarten. De app blijft daarvoor in het systeemvak draaien. Is er
  iets nieuws, dan komt er een melding — ballon, Telegram of e-mail
- **Filters per site binnen één zoekopdracht**: zoeken op "commodore" mag op
  de ene site een postcode en straal hebben en op de andere een keuze uit
  provincies. Het instellingenscherm toont per site enkel wat díe site kent
- **Filters die maar op één site bestaan**, beschreven in het sitebestand zelf:
  een keuze uit een lijst (ook meerdere tegelijk), een getal of aan/uit, in de
  filterpopup of bij de locatie. Een filter bijzetten is een regel JSON, geen code
- **Zoeken zonder zoekterm** op sites die enkel op filters werken
  (`AllowsEmptyQuery`), zoals een autosite waar het zoekwoord het merk is: met een
  lege balk zoek je dan niet merkgebonden en bepalen de filters wat je krijgt
- Filters per site: minimum- en maximumprijs, postcode, straal en maximum
  aantal. Wat een site niet kan, wordt gedimd of verborgen — afgeleid
  uit zijn `Filters`-mapping, dus zonder lijstje "welke site kan wat"
- **Resultaten over pagina's**, met de pager links van het locatiespeldje en nog
  eens onder de resultaten (hoogstens zeven nummers, schuivend). Het aantal per pagina (50/100/150/200) staat achter het `#`;
  de app haalt intussen op wat de sites geven: tot 2000 bij een site die rechtstreeks antwoordt,
  300 bij Facebook, 500 bij de rest
- Een tabblad **Alles** vooraan, met de resultaten van alle sites samen. De
  tabstrip toont verder enkel de sites die meezoeken; kiezen welke dat zijn
  gebeurt in het chipje achteraan de rij. Een site mag met `ShortName` een
  kortere naam voor zijn tab opgeven
- **Sorteren** op prijs (beide richtingen), op nieuwste of op de veiling die het eerst
  afloopt (over Catawiki, eBay en AlleVeilingen heen); de keuze wordt onthouden tussen twee starts
- **Prijsindicatie**: rechtsklik op een foto - of de knop in het detailvenster - geeft de
  marktwaarde van dat model uit de vraagprijzen op de sites met het vinkje, met varianten apart,
  zonder veilingen, sets en toebehoren, en verbreed naar de reeks als er te weinig zijn. Kwam je
  er via de AI-controle, dan staan de namen die van de foto's gelezen zijn erbij als klikbare
  voorstellen voor de zoekterm
- **Nieuw bij Tweakers** in hetzelfde prijsvenster: wat het ding nieuw kost, of wat het laatst
  kostte toen het nog te koop was, plus hun eigen tweedehandsaanbod. De app stelt producten voor
  en jij klikt het juiste aan; hun zoekpagina wordt niet aangeraakt (die verbiedt robots.txt),
  het opzoeken gebeurt in een kopie van hun sitemap
- **Prijs per titel** voor een partij: elke naam die de AI van de foto's las, apart opgezocht en
  op volgorde van duur naar goedkoop. Zo zie je of er in een doos spellen of een stapel platen
  iets waardevols zit; klikken op een regel geeft de vergelijkingen van die ene titel
- **AI-controle op een foto**: rechtsklik op een foto laat een model op je eigen grafische kaart
  lezen wat er op de voorwerpen staat, en erover vertellen in gewone taal - voor een doos vol
  dvd's waarvan de titels te klein zijn, of het typenummer op een label. Lokaal, dus er gaat geen
  foto de deur uit. Ook voor alle foto's van een zoekertje tegelijk, zodra het sitebestand zegt
  waar die staan (`DetailImagesSelector`); daarvan komt **één** verhaal over alles samen, dat na
  elke foto bijgewerkt wordt, met per foto wat daar gelezen is. Is de foto van de zoekpagina een
  miniatuur - Facebook geeft er van 260 px - dan wordt de grotere van de advertentiepagina
  gelezen
- Naast de prijs de **stad** (en anders het land), en bij een veiling erachter **hoelang er nog
  geboden kan worden**. Past die regel niet, dan vervaagt het einde en schuift ze zodra je er met
  de muis op gaat staan. Staat die tijd niet op de zoekpagina van de site maar wel op de pagina van
  het kavel, dan haalt de app ze daar op - enkel voor de kavels die je op dat moment ziet
- Rechtsonder op een veilingkaart een **timer** die echt aftelt waar het tijdstip exact is
  (AlleVeilingen, Catawiki via zijn API, eBay op het einde), in het laatste uur in amber
- Miniaturen in de resultatenlijst. **Dubbelklikken opent een venster met alles van dat zoekertje**:
  de foto's van de advertentie (miniaturen boven, één grote eronder; klikken wisselt, en een klik op
  de grote foto legt ze schermvullend over het venster), de verkoper, hoelang het online staat en de
  volledige beschrijving, met knoppen naar de site en naar de AI-controle
- *Sites beheren* met een tab per site: alle velden bewerkbaar, per site testen,
  aanmelden bij sites die dat vragen, en exporteren/importeren van losse sitebestanden
- Server-side zoekfilters via de `Filters`-mapping
- Resultatenlijst: prijs staat links naast de foto, met op de foto zelf enkel het NIEUW-label
  en de favorietenster. De grote foto vraag je op met een **dubbelklik**, die het detailvenster
  opent; `LargeImageSelector` in het sitebestand (een puntpad naar een groter formaat, of
  `::replace` op de URL van de miniatuur) bepaalt welke dat is, met terugval op de miniatuur
- Miniaturen in een vast vak (260×220, bijgesneden), met afgeronde hoeken en schaduw
- Twee weergaven voor de resultaten: **Lijst** (brede kaart: foto, titel,
  prijs — om te lezen) en **Raster** (foto's naast en onder elkaar zoals
  Facebook Marketplace — om te overzien). Wisselen kan met de weergaveknop bij
  de zoekinstellingen of via het tandwiel bij *Weergave*. Alleen het sjabloon en
  het paneel wisselen, dus de resultaten blijven staan. De keuze geldt voor de
  hele app en wordt onthouden tussen twee starts
- Alle sites zijn bestanden. Er is geen ingebouwde bron meer: een site met een
  afwijkende opbouw krijgt een andere motor via het `Engine`-veld
- Resultaten verschijnen per bron zodra die klaar is, en bij de brug zelfs al
  tijdens het laden van de pagina (tussentijdse leveringen)
- Paginering via `PageTemplate`, `{page}` of `{offset}` in de zoek-URL: de app haalt extra
  pagina's op tot de rem van die soort site (20 of 10 pagina's). Geeft een site bij
  pagina 2 hetzelfde terug, dan stopt hij vanzelf. Eén browser wordt hergebruikt
  over alle pagina's. Een API die meteen genoeg teruggeeft, heeft het niet nodig
- Prijsfilter op de site zelf wanneer het sitebestand er een heeft; anders filtert
  de app achteraf
- **Fouten bij de site zelf**: een waarschuwingsteken op de tab, en bij een bewaarde
  zoekopdracht "· 1 site mislukt" in de lijst. Mislukt een geplande zoekopdracht twee keer
  op rij op dezelfde site, dan komt er een melding
- **De brug zegt waarom ze niet werkt** (verkeerde koppelcode, extensie uit, geen Chrome),
  zowel in de app als in de popup van de extensie, en slaat de brugsites dan meteen over
- Maar **één Vindioo tegelijk**; een tweede start haalt het open venster naar voren

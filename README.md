# Zentrix

Doorzoek meerdere tweedehands- en veilingsites tegelijk, vanuit één Windows-app. Wat
anders een voormiddag klikken kost - dezelfde zoekterm op vijf sites, elk met zijn eigen
filters - wordt één zoekopdracht.

## Wat het doet

- **Eén zoekterm, meerdere sites tegelijk.** De resultaten verschijnen per site zodra
  die klaar is, en samen op het tabblad *Alles*.
- **Filters per site**: prijs, postcode en straal, en de filters die alleen die ene site
  kent (brandstof op een autosite, een veilinghuis op een veilingsite).
- **Automatisch zoeken.** Een zoekopdracht kan om de zoveel minuten of dagelijks draaien
  terwijl de app in het systeemvak staat, met een melding bij iets nieuws: een ballon,
  Telegram of e-mail.
- **Prijsindicatie**: rechtsklik op een foto, en de app zoekt wat hetzelfde model elders
  kost - zonder veilingen, sets en toebehoren, en met de vergelijkingen erbij.
- **Favorieten en recente zoektermen**, bewaard tussen twee starts.
- **Sites zijn bestanden, geen code.** Een nieuwe site toevoegen vraagt geen
  programmeerwerk: plak een zoek-URL, en de app laat Claude uitzoeken hoe de pagina in
  elkaar zit en telt het resultaat na.

## Wat je nodig hebt

- Windows 10 of 11
- De [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Google Chrome**, voor sites die een browser nodig hebben
- Optioneel: een **Claude API-sleutel** voor *Site toevoegen* met de AI-analyse
- Optioneel: Python 3, voor de hulpscripts in `tools\`

## Bouwen en starten

```bash
dotnet run --project Zentrix.csproj
```

Of open `Zentrix.slnx` in Visual Studio en druk op F5.

## De eerste start: sites toevoegen

**Zentrix komt zonder sites.** De app is een algemene zoekmotor die uitvoert wat een
sitebestand beschrijft; welke sites je doorzoekt, kies je zelf. Bij de eerste start zie
je daarom *Nog geen sites*, met twee knoppen:

- **Sites importeren uit map** - heb je een map met sitebestanden (één JSON-bestand per site),
  importeer die dan in één keer. Dit staat ook in het tandwielmenu.
- **Site toevoegen** - zoek op de site zelf naar een gewoon woord (bv. "fiets") en plak
  de URL van die zoekpagina. De app haalt de pagina op, laat Claude bepalen waar titel,
  prijs, plaats, link en foto staan, en toont meteen hoeveel zoekertjes dat oplevert. Je
  kan alles bijsturen en testen voor je bewaart.

Voor de AI-analyse heb je een API-sleutel nodig. Zonder sleutel toont *Site toevoegen*
bovenaan een veld om hem te plakken; Zentrix bewaart hem dan zelf als omgevingsvariabele
van je Windows-account. Je kan hem ook vooraf zelf zetten:

```bash
setx ANTHROPIC_API_KEY "jouw-sleutel"
```

Een analyse van een volle pagina kost ongeveer 50 cent. De sleutel staat nooit in de
code of in een bestand van de app.

Sites bewerken, testen, exporteren en importeren gebeurt via het tandwiel → *Sites
beheren*, met een kaart per site. Vraagt een site om aan te melden, dan doe je dat daar
met de knop *Aanmelden*.

## Sites die robots weren: de brug

Sommige sites laten alleen een echte browser door. Daarvoor zit in `extension\` een
Chrome-extensie, **Zentrix Brug**, die zoekpagina's opent in je eigen Chrome en de
inhoud doorgeeft aan de app op dezelfde computer.

1. Open `chrome://extensions` en zet rechtsboven *Ontwikkelaarsmodus* aan.
2. Kies *Uitgepakte extensie laden* en selecteer de map `extension`.
3. Klik in Zentrix op het tandwiel → *Koppelcode*. De code staat dan op je klembord.
4. Klik in Chrome op het pictogram van de extensie, plak de code en kies *Code opslaan*.
   De extensie zegt meteen of de code klopt.

Na een nieuwe versie van Zentrix: herlaad de extensie in `chrome://extensions` (het pijltje
bij Zentrix Brug), anders draait Chrome de oude.

De extensie praat alleen met `127.0.0.1`. Staat Chrome dicht wanneer een zoekopdracht
de brug nodig heeft, dan start de app hem zelf, geminimaliseerd. Werkt de brug niet (een
andere koppelcode, de extensie staat uit), dan zegt Zentrix waarom en slaat het die sites
over in plaats van erop te wachten.

## Meldingen

Tandwiel → *Meldingen en achtergrond*. Elk kanaal heeft een testknop.

- **Ballon in het systeemvak** - werkt meteen.
- **Telegram** - maak een bot via @BotFather, plak het token, stuur je bot één bericht
  en klik op *Chat-id ophalen*. Zoekertjes komen met foto binnen.
- **E-mail** - via je eigen mailserver. De gebruikersnaam is meestal je volledige
  e-mailadres; bij Gmail heb je een app-wachtwoord nodig.

Meldingen gaan rechtstreeks van je pc naar de dienst die je kiest. Er komt niets centraal
samen.

## Waar je gegevens staan

Alles staat in `%APPDATA%\Zentrix`:

| | |
|---|---|
| `sites\` | één JSON-bestand per site |
| `zentrix.db` | zoekopdrachten, favorieten, recente zoektermen, wat je al zag |
| `instellingen.json` | instellingen van de app en de meldingen |
| `browser-profiel\` | het Chrome-profiel dat de app gebruikt, met je logins |
| `zentrix-log.txt` | het logboek - de eerste plek om te kijken als iets niet werkt |

Een andere gegevensmap kiezen kan met de omgevingsvariabele `ZENTRIX_DATA`, bijvoorbeeld
om een lege eerste start uit te proberen zonder aan je eigen gegevens te komen.

## Verantwoord gebruik

Zentrix leest openbare zoekpagina's, zoals je browser dat doet. Veel sites beperken
geautomatiseerd uitlezen in hun gebruiksvoorwaarden. Kijk die na voor je een site
toevoegt, zoek met mate, en gebruik de app voor je eigen zoekwerk.

## Voor ontwikkelaars

`CLAUDE.md` is de uitgebreide werkdocumentatie: de opbouw van de code, hoe een
sitebestand in elkaar zit, de motoren en URL-stijlen, en de valkuilen die we
onderweg tegenkwamen - telkens met wat er gemeten werd. De hulpscripts in `tools\`
controleren sitebestanden buiten de app om en maken schermafbeeldingen van de app voor
het nakijken van de interface.

De logica van de app heeft controles die zonder netwerk en zonder je eigen gegevens draaien,
en gewoon naast Visual Studio kunnen:

```bash
dotnet run --project tests\Zentrix.Checks -- --snel
```

Gebouwd met C# en WPF op .NET 10, met [WPF-UI](https://github.com/lepoco/wpfui),
[AngleSharp](https://anglesharp.github.io/),
[Playwright](https://playwright.dev/dotnet/), SQLite en
[MailKit](https://github.com/jstedfast/MailKit).

## Licentie

MIT - zie [LICENSE](LICENSE). Je mag de code gebruiken, aanpassen en verspreiden, zolang
de copyrightvermelding erbij blijft. De licentie geldt voor de app; sitebestanden die je
zelf maakt of importeert, vallen daar niet onder.

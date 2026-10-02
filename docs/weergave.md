# Designsysteem en UI-conventies

Onderdeel van de documentatie van Zentrix; de korte versie staat in
[CLAUDE.md](../CLAUDE.md).

## Designsysteem

De app heet overal **Zentrix**, ook in de code en in de gegevensmap; zie "De naam,
de gegevensmap en GitHub" voor hoe dat hernoemen ging.

Alle kleur, vorm en typografie staat in `App.xaml` en nergens anders. Vensters
en sjablonen verwijzen enkel naar die namen, nooit naar een losse kleurcode:

- **Accent** `AccentBrush` indigo #4F46E5, met `AccentGradientBrush` naar violet
  #7C3AED. Dat verloop is voor primaire knoppen en voor wat actief staat, niet
  voor grote vlakken.
- **Achtergrond** `AppBackgroundBrush`: verloop #3B3470 → #2E2859 → #1D193A, met daarboven
  de achtergrondfoto (zie Afbeeldingen).
- **Oppervlakken** `SurfaceBrush` (wit op 6%), `SurfaceHoverBrush` (10%),
  `SurfaceBorderBrush` (10%), hoeken `SurfaceRadius` (12). `PanelBrush` is de
  ondoorzichtige variant, voor balken en popups die leesbaar moeten blijven
  boven de achtergrondfoto.
- **Kaarten** zijn iets anders dan oppervlakken, en dat onderscheid is bewust.
  Een *kaart* is een stuk inhoud — een zoekertje, een bewaarde zoekopdracht, een
  regel in Recent — en is licht: `CardBrush` (#F5F3FF op 94%, zodat de
  achtergrond er net doorheen schemert) met `CardTextBrush` (#1E1B2E) en
  `CardTextSubtleBrush` (60%) erop, en de prijs in het accent. Een *oppervlak*
  is een donker paneel of knopvlak dat op de achtergrond meedeint.
  Alles buiten de kaarten — de zoekbalk, de chips, de statusregel — blijft wit
  op de achtergrond.
- **Tekst** `TextPrimaryBrush` #F1EFF7 en `TextSubtleBrush` (60%), met de stijlen
  `TitleText` (20 semibold), `HeadingText` (14 semibold), `BodyText` (13) en
  `CaptionText` (12, gedempt) in Segoe UI Variable.
- **Accent als tekst op donker** `AccentOnDarkBrush` (#A5B4FC). Het gewone indigo haalt als
  tekst of pictogram op de donkere achtergrond maar 2:1 - de actieve tab onderaan was
  daardoor slechter leesbaar dan de inactieve. Dit lichte indigo haalt ongeveer 6:1.
  `AccentBrush` blijft voor vlakken en voor tekst op de lichte kaarten (ongeveer 5,7:1).
- **Waarschuwing** `WarningBrush` (amber #FBBF24) op de donkere achtergrond en
  `WarningOnCardBrush` (#B45309) op de kaarten.
- **Knopjes boven een foto** `OverlayBrush` (#80000000) en `OverlayHoverBrush` (#A6000000). Op
  een witte productfoto haalde een wit pictogram op het vroegere #59000000 maar 2,4:1.
- **De achtergrondfoto is in het midden en rechts lichtpaars.** Tekst die daar rechtstreeks op
  staat, haalde 2 à 3:1, ook in `AccentOnDarkBrush`. De teksten bij een lege lijst en "Nog geen
  sites" staan daarom op een vlak in `PanelBrush`; tellers en knoppen in de koppen zijn
  `TextPrimaryBrush`. Lijnen en pictogrammen in het accent op donker (het draaiende wieltje, de
  rand van het lege sitechipje, de open tab in Sites beheren) zijn `AccentOnDarkBrush`.

Twee valkuilen die de app bij het opstarten lieten crashen of leeg lieten:

- Zet **geen `<StaticResource x:Key="A" ResourceKey="B" />`** als regel in een
  resourcewoordenboek om een naam door te verwijzen. Dat breekt het woordenboek:
  andere sleutels worden dan niet meer gevonden ("Cannot find resource named …").
  Definieer liever een echt penseel met `Color="{StaticResource ...Color}"`.
- Geef **`ui:FluentWindow` geen impliciete stijl** (`<Style TargetType="{x:Type
  ui:FluentWindow}">`). WPF-UI levert zijn venstersjabloon als thema-stijl; een
  eigen stijl neemt die plaats in en dan tekent het venster niets meer — je
  krijgt een leeg wit kader. Zet lettertype, tekstkleur, achtergrond en
  `WindowBackdropType` per venster.

Nog twee dingen over de achtergrond:

- Het verloop staat als **eigen `Border` achter de inhoud** (`Grid.RowSpan` over
  alle rijen), niet als `Background` van het venster. WPF-UI zet die laatste bij
  het laden zelf terug op de effen themakleur (#202020); je verloop verdwijnt dan
  zonder foutmelding. De sleutel `ApplicationBackgroundBrush` overschrijven helpt
  evenmin. Dat geldt voor **elk** venster, niet alleen het hoofdscherm: elk nieuw
  `ui:FluentWindow` heeft die Border nodig, anders staat het er vlak grijs bij
  tussen de rest.
- Het verloop loopt van links licht naar rechts donker met de middelste stop op
  0,65, zodat het grootste deel van het scherm aan de lichte kant blijft.

Mica staat uit: die vlekt door het verloop heen. `ApplicationAccentColorManager`
krijgt in `App.xaml.cs` het indigo mee, zodat de vinkjes en de selectie in
lijsten van WPF-UI dezelfde kleur hebben als de rest.

### Afbeeldingen

`Assets/` bevat `logo.png`, `background.png` en `zentrix.ico`, alle drie als
`Resource` in het csproj — ze zitten dus in de exe. `zentrix.ico` staat als
`ApplicationIcon` en verschijnt in de titelbalk, de taakbalk en op het bestand.
De iconen worden gemaakt met `tools/afbeeldingen.py` uit de aangeleverde
bestanden; draai dat script opnieuw wanneer er een nieuw logo komt.

Wat dat script doet en waarom:

- De aangeleverde afbeeldingen dragen rechtsboven een badge "Made with AI". Bij
  het logo wordt die weggesneden, bij de achtergrond opgevuld met de kleuren
  eromheen (daar is het verloop glad genoeg om dat onzichtbaar te doen).
- De ondergrond van het logo wordt weggerekend naar doorzichtigheid, zodat het
  op onze eigen achtergrond staat. **Let op de richting**, want die hangt af van
  de ondergrond:
  - licht kunstwerk op **zwart** (het huidige neonlogo): `pixel = a*K`, dus de
    dekking komt uit het **lichtste** kanaal. De gloed wordt daardoor vanzelf een
    half-doorzichtige waas — precies wat je wil.
  - donker kunstwerk op **wit** (het vorige logo): `pixel = a*K + (1-a)*255`, dus
    de dekking komt uit het **donkerste** kanaal.

  Wie die twee verwisselt, krijgt een logo dat bijna helemaal doorzichtig wordt.
- Uitsnijden gebeurt op een drempel in de alfa, niet op "alles wat niet helemaal
  doorzichtig is". De gloed rond dit logo reikt tot ver in het beeld en zou het
  uitsnijden anders zinloos maken.
- Het icoon gebruikt enkel het merkteken links (het vergrootglas met de ring),
  op 0,78 van de hoogte uitgesneden: de naam erbij zou op 16 px onleesbaar zijn.

De achtergrondfoto wordt in code gezet (`ApplyBackgroundPhoto`), niet in de XAML,
zodat een ontbrekend bestand de app niet laat vallen: dan blijft het verloop uit
`App.xaml` staan. Belangrijk daarbij: `BitmapCacheOption.OnLoad`, anders leest
WPF de afbeelding pas bij het tekenen in en valt de fout buiten de `catch`.
Boven de foto ligt `ScrimBrush` (35% van de donkerste achtergrondkleur) zodat
witte tekst leesbaar blijft.

## UI-conventies

- Vermijd horizontale `StackPanel`s voor werkbalken die kunnen overlopen; een
  `StackPanel` klipt wat buiten beeld valt. Gebruik een `WrapPanel` of
  `*`-kolommen zodat alles zichtbaar blijft als het venster wordt verkleind.
- Let op: een `WrapPanel` breekt alleen af als hij een begrensde breedte heeft.
  In een horizontale `StackPanel` krijgt hij oneindige breedte en breekt hij
  nooit af, en in een `Grid`-kolom op `Auto` net zomin - dan loopt hij de
  vensterrand uit en wordt het laatste kind half afgesneden (zo ging het bij de
  miniaturen in `ListingDetailWindow`). Voor een vast aantal per rij (los van de
  vensterbreedte) is een `UniformGrid` met een vast `Columns` de juiste keuze;
  moet alles op één rij blijven, dan een `ScrollViewer` met een horizontale
  `StackPanel` erin.
- WPF-UI stijlt de koppen van een `TabControl` niet vanzelf tot herkenbare
  tabs. Geef `TabItem` een eigen `ControlTemplate` (afgeronde bovenhoeken,
  rand, accentkleur bij `IsSelected`) — zie de stijl in `SettingsWindow.xaml`.
- Voor een preview die met de muis mee komt en gaat: gebruik een `Popup` met
  `IsOpen` gekoppeld aan `IsMouseOver` van het doel, niet een `ToolTip`. Een
  tooltip blijft openstaan zodra de muis zijn eigen popup raakt. Zet de inhoud
  op `IsHitTestVisible="False"`. (De app heeft er sinds 27 september 2026 geen
  meer - de vergroting op de miniaturen is weg - maar de les blijft.)
- Een `Image` tekent op zijn "natuurlijke" grootte, en die hangt af van de
  DPI-metadata in het bestand — een grote foto kan daardoor klein uitvallen.
  Wikkel hem in een `Viewbox` met `MaxWidth`/`MaxHeight` om echt op maat te
  schalen. Let ook op: het standaardsjabloon van een WPF-UI-tooltip legt een
  maximumbreedte op.
- WPF klipt niet op `CornerRadius`; `ClipToBounds` knipt rechthoekig. Voor een
  foto met afgeronde hoeken: een `Clip` met een `RectangleGeometry` (zie
  `RoundedClipConverter`, die meeschaalt met de werkelijke maat). Zet een
  schaduw op een búitenste laag, anders knipt de clip hem weg.
- Wil je een **gewone** `WrapPanel` als `ItemsPanel` van een lijst, zet dan
  `ScrollViewer.CanContentScroll` op `False` én
  `ScrollViewer.HorizontalScrollBarVisibility` op `Disabled`. Bij het standaard
  "per regel scrollen" krijgt het paneel geen echte breedte mee en breekt het
  nooit af — dezelfde valkuil als een `WrapPanel` in een `StackPanel`.

  Voor ons `VirtualizingWrapPanel` geldt precies het **omgekeerde**:
  `CanContentScroll` moet juist AAN, want dat paneel regelt zijn schuiven zelf via
  `IScrollInfo`. Staat het uit, dan krijgt het oneindige hoogte en bouwt het alsnog
  alle kaarten op. De resultatenlijst zet het daarom op `True` voor allebei de
  weergaven.
- Deelt een stuk beeld zich over twee sjablonen, maak er dan een `UserControl`
  van in `Controls/` in plaats van het te kopiëren. De miniatuur met zijn
  grote foto zit vol subtiliteiten (popup, clip, centreren); die wil je
  maar op één plaats onderhouden.
- Een `RadioButton` met `IsChecked="True"` in de XAML vuurt zijn `Checked` af
  **tijdens** het inlezen van die XAML, dus voor de velden in de code-behind
  bestaan. Vandaar de vlag `_ready` in `MainWindow`: zonder die vlag crasht de
  app bij het opstarten op een lege verwijzing.
- XML-commentaar mag geen twee streepjes achter elkaar bevatten. Een
  scheidingsregel als `<!-- ---------- kop ---------- -->` laat de XAML-compiler
  struikelen op `MC3000`; gebruik `=` als je zo'n balk wil.
- Wil je een pictogram vol laten lopen zodra iets actief is, geef de
  `ui:SymbolIcon` dan een stijl met een `DataTrigger` op
  `{Binding IsChecked, RelativeSource={RelativeSource AncestorType=RadioButton}}`
  en zet `Filled`. Vanuit een `ControlTemplate.Triggers` lukt dat niet: de
  inhoud van de knop staat in een andere namescope.
- Kleuren staan als penselen in `App.xaml` (`HeaderBrush`, `AccentBrush`,
  `CardBrush`, `TextSubtleBrush` …). Wil je de app een andere tint geven, dan
  is dat de enige plaats die je hoeft aan te passen.
- Wil je een dropdown onder een knop, hang er dan een `ContextMenu` aan en open
  die in de klik met `PlacementTarget` + `Placement="Bottom"`.
- **`ui:TitleBar` eist zijn hele rechthoek op als sleepgebied.** Ligt er een
  invoerveld onder — zoals de zoekbalk, sinds de kop tot één rij is
  teruggebracht — dan toont de muis daar een pijl, neemt het veld geen invoer
  aan, en maximaliseert een dubbelklik het venster in plaats van tekst te
  selecteren. Precies tot waar de titelbalk reikt, dus "de onderste helft van
  het vak werkt wel".
  `WindowChrome.IsHitTestVisibleInChrome` helpt daar **niet** tegen: WPF-UI doet
  zijn eigen hit-test en kent die vlag niet (de naam komt niet eens voor in
  `Wpf.Ui.dll`). De oplossing is de titelbalk **rechts uitlijnen**, zodat hij
  enkel de strook bij zijn eigen knoppen beslaat, en het slepen van het venster
  zelf te regelen met een `MouseLeftButtonDown` op de kopbalk die `DragMove()`
  aanroept (dubbelklik daar maximaliseert of herstelt). Zie
  `Header_MouseLeftButtonDown`.
- Twijfel je of een plek invoer aanneemt, meet het dan in plaats van te kijken:
  `tools/cursortest.py` loopt een reeks punten af en zegt welke muisaanwijzer
  Windows er toont. Een I-balk betekent gewone inhoud, een pijl betekent
  sleepgebied. Een `Menu` met
  één `MenuItem` erin doet hetzelfde, maar tekent een balk mee.
- **Een uitgeschakelde knop toont geen tooltip**, tenzij `ToolTipService.ShowOnDisabled` aan
  staat. De filterknoppen zetten in hun tooltip waarom ze gedimd zijn; zonder die regel
  kwam die uitleg nooit in beeld.
- **Een `Popup` die dicht is, voert zijn bindings toch uit.** Hij hoort bij de logische
  boom en erft de gegevens. Een `Image` met een webadres als `Source` begint dan meteen te
  downloaden, ook als niemand de popup opent. Zet zo'n bron pas in `Opened` en maak ze leeg
  in `Closed`. Dat gold voor de vergroting op de miniaturen, die er sinds 27 september 2026
  niet meer is: bij Catawiki was de grote foto 365 kB tegenover 66 kB voor de miniatuur, dus
  een pagina van vijftig kaarten haalde zo'n 18 MB op waar niemand over zweefde.
- **Een `OpacityMask` rekent met de omhullende van het element én zijn kinderen.** Steekt een
  kind buiten het vak - een tekst die breder is dan haar kader - dan valt een verloop van 0 tot 1
  grotendeels in het weggeknipte stuk. Gebruik dan `MappingMode="Absolute"` met een eindpunt in
  beeldpunten. En let op bij het nakijken: een `VisualBrush` neemt de `OpacityMask`, de `Clip` en de
  `Transform` van zijn wórtelelement niet mee, wel die van de kinderen.
- **Een `ScrollViewer` in een rij op `Auto` schuift nooit.** Zo'n rij meet haar kind met
  oneindige hoogte; de ScrollViewer besluit dan dat er niets te schuiven valt en zet zijn
  schuifbalk niet aan (`VerticalScrollBarVisibility="Auto"` kijkt daar één keer naar). Een
  `MaxHeight` op de *rij* helpt niet: die klemt de rij pas na het meten, en dan wordt de tekst
  gewoon afgeknipt - zichtbaar afgekapt, zonder balk. Zet de `MaxHeight` op het **element**, dan
  wordt de ScrollViewer wél begrensd gemeten. Zo staat het in `ListingDetailWindow`
  (`InfoBlock`, `VolgVensterhoogte`), waar die grens de vensterhoogte volgt. Nagemeten: met de
  grens op de rij 699 inhoud in een viewport van 699 (niets te schuiven), met de grens op het
  blok 699 in 371 en 329 te schuiven.
- **Een `ObservableCollection` wissen geeft een Reset**, en daarop gooit een lijst of raster
  al zijn containers weg en springt het naar boven. Pas aan wat veranderde (zie
  `ToonPagina`) in plaats van `Clear()` en alles opnieuw toevoegen.
- **Esc en Enter in een dialoogvenster** komen van `IsCancel` en `IsDefault` op de knoppen.
  Zet `IsDefault` niet in een venster waar je lange codes of adressen plakt (zoals
  Meldingen): daar sluit een Enter het venster te makkelijk.
- Een `TextBox` in een zelfgetekend kader (de zoekbalk) krijgt een eigen, kaal sjabloon met
  enkel `PART_ContentHost`. Het standaardsjabloon van WPF-UI tekent anders zijn eigen
  onderlijn, ook met `BorderThickness="0"`.
- **Een eigen stijl voor een knop of tekstvak zonder `BasedOn`** valt terug op het sjabloon
  van Windows: vierkant, lichtblauw bij zweven, en een zwarte cursor in een donker veld. Zo
  stonden `PrimaireKnop`, `StilleKnop` en `Veld` in de vensters Meldingen en Zoekopdracht; nu
  zijn het `ui:Button` (Primary, Secondary, Transparent) en `ui:TextBox`, zoals in Sites
  beheren. Een klein getalveld is een gewone `TextBox` zonder `Style`: dan geldt de stijl van
  WPF-UI, en in een `ui:TextBox` past het wisknopje niet in 46 beeldpunten.
- **Een vlak rond een tekst die de code toont of verbergt**: laat het vlak de zichtbaarheid van
  de tekst volgen (`Visibility="{Binding Visibility, ElementName=EmptyHint}"`), dan hoeft de
  code niets te weten van dat vlak.
- **Een statusregel of melding naast knoppen hoort in een `Grid`** met een `*`-kolom, met
  `TextTrimming` en een tooltip met de volledige tekst, of met `TextWrapping`. In een rechts
  uitgelijnde `StackPanel` verdween het begin van een lange melding links buiten beeld.
- **Het logo verdwijnt onder 1040 beeldpunten breed.** Het staat gecentreerd in dezelfde rij als
  de zoekbalk, en schoof anders over het vergrootglas en het tandwiel.

## Een venster boven een ander venster

Elk venster dat van een ander opengaat, gaat via `Vensters.Boven(eigenaar)` - één plaats in
plaats van dertien keer `{ Owner = this }`. Die helper doet twee dingen: de eigenaar zetten, en de
eigenaar naar voren halen wanneer dit venster sluit.

**Dat tweede hoort niet nodig te zijn.** Windows activeert bij het sluiten van een venster normaal
zijn eigenaar. In de praktijk gebeurde op 2 oktober 2026 iets anders: stond er een Verkenner of
een Chrome tussen Zentrix en het pop-upvenster, dan sprong Zentrix bij het sluiten helemaal naar
achter - je moest het uit de taakbalk terughalen om verder te werken.

**Niet nagemeten in een harnas, en dat hoort erbij.** Windows laat een proces dat op de achtergrond
gestart is de vensters niet herschikken: in de proefopzet kwam noch Kladblok (op Windows 11 een
Store-app, waarvan je het venster niet eens te pakken krijgt) noch de Verkenner naar voren, dus de
situatie viel er niet na te bootsen. Wat er wél gemeten is, met het echte detailvenster en
`GetForegroundWindow`: zónder een ander programma ertussen komt het hoofdvenster na het sluiten
gewoon terug - met en zonder deze helper. De klacht gaat dus over het geval met iets ertussen, en
daar dwingt deze regel het antwoord af in plaats van erop te vertrouwen.

Het kan geen aandacht stelen van een ander programma: wie dit venster sluit, had het net nog
vooraan staan. En een eigenaar die intussen weg of geminimaliseerd is, blijft met rust - anders zou
het sluiten van een venster een geminimaliseerde Zentrix uit het systeemvak trekken.

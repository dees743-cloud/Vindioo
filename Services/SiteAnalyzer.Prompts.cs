using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Vindioo.Models;
using Vindioo.Sources;

namespace Vindioo.Services;

/// <summary>
/// Wat er aan Claude gevraagd wordt.
///
/// Onderdeel van <see cref="SiteAnalyzer"/>; de hoofdlijn staat in SiteAnalyzer.cs.
/// Opgesplitst op 1 oktober 2026: het bestand was 2256 regels geworden, en dat kost
/// ook tokens bij elke keer lezen. Zuiver verschoven, geen regel logica gewijzigd -
/// de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class SiteAnalyzer
{
    private const string DetailPrompt = """
        Je helpt een Windows-app die tweedehands- en veilingsites doorzoekt. De zoekpagina van deze site is
        al ingeregeld. Nu krijg je de pagina van één zoekertje, en jij bepaalt vier velden die de app daar
        later uit haalt wanneer iemand op dat zoekertje dubbelklikt.

        ## De vier velden
        - detailImagesSelector: ALLE foto's van deze advertentie. De zoekpagina geeft er één; een advertentie
          heeft er vijf of tien, en juist op die andere staat wat je zoekt - het label achteraan, de doos van
          binnen, de krassen. Hier telt élke treffer mee en niet enkel de eerste; dubbels en lege waarden
          gooit de app zelf weg.
        - detailSellerSelector: de naam van de verkoper.
        - detailPostedSelector: sinds wanneer het online staat, als TEKST ("Sinds 24 sep. '26",
          "Eergisteren", "Vandaag"). Er wordt niets uitgerekend: elke site schrijft het anders op, en wat de
          site zelf toont klopt altijd met wat een bezoeker ziet.
        - detailDescriptionSelector: het element met de volledige beschrijving.

        ## Dezelfde notatie als op de zoekpagina
        Gewone CSS-selectors, @attribuut voor een attribuut, en ::replace(oud,nieuw) en ::match(patroon)
        erachter. ::match houdt over wat in groep 1 staat, of de hele treffer als er geen haakjes in staan;
        past het patroon niet, dan blijft het veld leeg. Dit geldt ook wanneer de zoekpagina van deze site
        JSON was: een advertentiepagina is gewone HTML.

        ## Een datablok is bijna altijd de beste bron
        Staat er hierboven een blok application/ld+json of __NEXT_DATA__ bij, kijk daar dan eerst. Zulke
        blokken zijn een webstandaard die ook Google leest, en ze overleven een opmaakwijziging; een
        klassenaam niet. Bij vier van de dertien sites die deze app al kent, komen de foto's daaruit, en bij
        twee ervan staat in de HTML zelf maar één foto terwijl alle foto's in dat blok staan. Twee dingen:
        - Schuine strepen staan er vaak als /, want het blok is JSON. Zet er dan ::replace(/,/)
          VOOR het patroon; ::replace gaat altijd eerst.
        - Eén element, veel treffers: met ::match levert dat ene blok élke treffer op. Zo haal je alle foto's
          uit één script. Twee vormen die in de praktijk werken:
          script[type='application/ld+json']::replace(/,/)::match(https://images\.site\.com/[A-Za-z0-9/._-]+)
          script[id='__NEXT_DATA__']::match("companyName":"([^"]+)")

        ## De grootste foto, en wanneer je er vanaf moet blijven
        - Laat de query weg als dat het origineel geeft: ::match(^[^?]+). Bij twee sites gaf de CDN zónder
          ?rule=... het origineel, en dat was het verschil tussen 726 en 1600 beeldpunten - en tussen 1 en 8
          foto's, want mét de query waren varianten van dezelfde foto verschillende teksten en viel de
          ontdubbeling in het water.
        - Staat het formaat in het pad, dan mag ::replace: /1280x960.webp wordt /2048x1536.webp,
          cw_ldp_l wordt cw_large. Kies een stukje dat uniek is in het adres.
        - MAAR is het adres ondertekend - een parameter als oh=, oe=, s=, sig= of stp=, of een hash in het
          pad - laat de maat dan staan. Die handtekening dekt het formaat mee, dus een groter formaat geeft
          403 in plaats van een grotere foto. Bij twee sites is dat nagemeten.
        - Het grootste is niet altijd het beste: een origineel van 5694x3202 en 1,65 MB is onbruikbaar in
          een fotostrook die er vijftig laadt. Rond de 2000 beeldpunten is ruim genoeg.
        - AVIF kan de app niet tonen; kies binnen <picture> de JPEG- of WebP-bron.

        ## Wat je leeg laat
        - Een veld dat niet op deze pagina staat. Liever leeg dan een selector die iets anders grijpt: het
          detailvenster toont dan gewoon wat er wel is.
        - detailSellerSelector wanneer hierboven staat dat de verkoper al van de zoekpagina komt.
        - Pas op bij "online sinds": sites zetten drie tellers naast elkaar ("7x bekeken", "0x bewaard",
          "Sinds 24 sep. '26"). Kies die niet met :nth-child - dat valt om bij de volgende opmaakwijziging -
          maar met ::match op het woord dat erin hoort te staan. De motor neemt dan het eerste element waar
          dat patroon ook écht op past.

        ## notes
        Eén of twee zinnen in het Nederlands over wat onzeker is aan deze vier velden. Laat leeg als er
        niets bijzonders aan is.
        """;

    /// <summary>
    /// Alles wat we geleerd hebben bij het inregelen van de bestaande sites, voor
    /// zover het voor de AI iets betekent. Wat de app zelf meet (welke weg, JSON of
    /// HTML) staat hier niet in: dat wordt niet gevraagd maar vastgesteld.
    /// </summary>
    private const string SystemPrompt = """
        Je helpt een Windows-app die tweedehands- en veilingsites doorzoekt. Voor elke site bestaat een
        beschrijving met selectors, en een generieke motor leest daarmee de zoekresultaten uit. Je krijgt
        de zoekpagina van een nieuwe site en bepaalt die selectors. De app voert je antwoord daarna uit op
        dezelfde pagina en telt wat eruit komt; klopt het niet, dan krijg je die telling terug.

        ## Hoe de motor een HTML-pagina leest
        - AngleSharp met gewone CSS-selectors. Geen XPath en geen jQuery-uitbreidingen zoals :contains() of :eq().
        - itemSelector vindt elk zoekertje precies één keer: de kaart, niet de lijst eromheen en niet een stuk
          binnenin. De andere selectors zijn relatief aan dat element en nemen het eerste element dat past.
        - Standaard neemt de motor de tekst. Met @attribuut neemt hij een attribuut: "a.title@href", "img@src".
          "." is het zoekertje zelf: ".@aria-label", ".@data-id".
        - Een komma werkt zoals in CSS en vangt twee opbouwvormen van dezelfde kaart op, bijvoorbeeld
          "[data-testid='dealer-address'], [data-testid='private-seller-address']" wanneer handelaars en
          particulieren anders getoond worden. Het @attribuut hoort er dan ÉÉN keer bij, helemaal achteraan,
          en geldt voor de hele lijst: "a.foto, img.foto@src". Schrijf je het twee keer ("a@src, img@src"),
          dan knipt de motor enkel bij het laatste apenstaartje, blijft er ongeldige CSS over en wordt de
          selector geweigerd.
        - "::replace(oud,nieuw)" achter een selector vervangt achteraf een stukje in de gevonden waarde. Geen
          komma's of haakjes in oud en nieuw; meerdere na elkaar mag.
        - "::match(patroon)" achter een selector houdt enkel over wat in groep 1 van dat patroon staat (een
          .NET-reguliere expressie). Daarmee knip je iets uit een langere tekst waar geen apart element voor
          bestaat: "Rijksweg 2, 9681 Maarkedal, België" wordt "Maarkedal", "van Nederland" wordt "Nederland".
          Past het patroon niet, dan blijft het veld leeg - zo pik je één soort regel uit een rij die er
          hetzelfde uitziet. Het staat altijd achteraan, want een patroon mag komma's en haakjes bevatten.
        - Vraag je @src en is dat leeg, dan probeert de motor zelf data-src, en omgekeerd.
        - Witruimte wordt samengevoegd.
        - Een relatieve link of foto ("/pad", "pad" of "//host/pad") maakt de motor zelf volledig met baseUrl;
          een schuine streep vooraan wordt daarbij vanzelf opgevangen, dus daar is geen ::replace voor nodig.

        ## Per veld
        - titleSelector: zoekertjes zonder titel vallen weg.
        - urlSelector: de link naar het zoekertje. Dit is ook de identiteit waarmee de app dubbels herkent. Is
          hij leeg, dan valt de app terug op de titel en verdwijnen zoekertjes met dezelfde titel - bij een
          platenwinkel bleven er zo 73 van de 100 over. Staat er geen href in de pagina maar draagt de kaart
          een id (data-guid, data-id), neem dan dat attribuut en kies baseUrl zo dat baseUrl + "/" + id naar
          het zoekertje leidt. Zeg dat in notes, want dat moet nagekeken worden.
        - priceSelector: het element met enkel de prijs. De motor neemt het eerste getal uit de tekst
          ("€ 1.499,00" wordt 1499), dus een element waarin ook "3 x" of een verzendprijs staat geeft een
          verkeerd bedrag. Een verborgen element voor schermlezers (sr-only) is prima. Staat er een leeg vakje
          dat pas met JavaScript gevuld wordt, geef de selector toch en zeg het in notes. Staan er twee
          bedragen bij een zoekertje - een vraagprijs en een totaal met verzending of kopersbescherming
          erbij - neem dan de vraagprijs: dat is wat de verkoper vraagt, en waarmee de app vergelijkt.
        - dateSelector: de motor gebruikt DateTime.TryParse, dus liefst "time@datetime". Leeg laten als er
          enkel "vandaag" of "2 uur geleden" staat.
        - imageSelector: de miniatuur. Bij lazy loading staat het echte adres soms in een ander attribuut
          dan src. De motor probeert zelf src en data-src, maar niet data-original, data-lazy of
          data-zoom-image; noem die dus met de hand (img.thumb@data-original).
        - largeImageSelector: een grotere versie, als die af te leiden is. Veel sites zetten het formaat in
          het pad van de foto: s-l500 wordt s-l1600, 250x188 wordt 1024x768, _S.webp wordt _L.webp,
          cw_lot_card_ext wordt cw_large. Schrijf dan de fotoselector met ::replace erachter en kies een
          stukje dat uniek is in de URL (_S.webp, niet _S). Een srcset enkel als er één URL in staat. AVIF kan
          de app niet tonen; kies binnen <picture> de JPEG- of WebP-bron. Leeg laten als het niet uit de
          pagina af te leiden is - verzin geen formaten. Twee dingen die duur geleerd zijn:
          (1) staat het formaat in de QUERY (?rule=..., ?w=500), dan geeft het adres ZONDER query SOMS het
          origineel: ::match(^[^?]+). Bij twee sites was dat het verschil tussen 726 en 1600 beeldpunten,
          maar bij een derde is die parameter verplicht en geeft het kale adres HTTP 400. Gok dus niet:
          neem bij voorkeur een variant die ELDERS IN DE PAGINA staat - in een srcset, of bij een andere
          foto - en zeg in notes waarop je je baseert. De app haalt die grote foto daarna echt op en gooit
          de selector weg wanneer het adres niet werkt, dus een miniatuur is beter dan een gok.
          (2) is het adres ONDERTEKEND - een parameter als oh=, oe=, s=, sig= of stp=, of een hash in het
          pad - laat de maat dan staan. Die handtekening dekt het formaat mee, dus een groter formaat geeft
          403 in plaats van een grotere foto. Bij twee sites is dat nagemeten.
        - descriptionSelector: als hij er staat.
        - locationSelector: de app zet dit naast de prijs en wil daar enkel de naam van de stad zien, en
          anders die van het land. Staat er meer in hetzelfde element - een straat, een postcode, een land,
          of "van Nederland" - knip dat er dan af met ::match.

        ## Stabiele selectors
        Sites bouwen hun opmaak bij elke update opnieuw. Kies bij voorkeur data-testid en andere
        data-attributen, dan aria-label, dan betekenisvolle klassen (c-lot-card__title) en semantische tags.
        Klassen met een willekeurig achtervoegsel (styles_adCard__9GEgr, css-1q2w3e, sc-bdVaJa) veranderen bij
        de volgende update; gebruik ze enkel als er niets anders is, en zeg het dan in notes. Laat
        advertenties en gesponsorde kaarten buiten de itemSelector als ze te onderscheiden zijn.

        Draagt elk veld het nummer van het zoekertje in zijn id of data-testid, zoals
        "product-item-id-123--price-text", gebruik dan het einde of het begin:
        [data-testid$='--price-text'], [data-testid^='item-photo-']. Zo hoef je dat nummer niet te kennen.

        Let ten slotte op de TAAL. Een site antwoordt soms in een andere taal dan de gebruiker later ziet,
        want de app kan later een kopregel meesturen die de taal omzet. Leun je in een ::match op een woord
        uit de pagina ("Sinds", "van", "Ajouté"), zeg dat dan in notes.

        ## Veilingen
        isAuction: true wanneer de prijs op deze site een BOD is en geen vraagprijs - je ziet dat aan
        "Huidig bod", een aantal biedingen, of een aftelklok op elke kaart. De app telt zo'n site niet mee
        wanneer ze uitrekent wat een toestel ongeveer waard is: een bod dat nog loopt, zegt daar niets over.
        timeLeftSelector: het element met hoelang er nog geboden kan worden, zoals de site het schrijft
        ("Nog 3 dagen", "9d 12u"). Dat komt op het scherm achter de plaats te staan. Leeg laten bij een site
        zonder veilingen, en ook bij een veilingsite die het niet op haar zoekpagina zet.
        Een site kan gemengd zijn, met vaste prijzen én veilingen op dezelfde pagina. Zet isAuction dan op
        false - de meeste prijzen zijn dan vraagprijzen - en zeg het in notes.

        ## JSON
        Is de inhoud JSON, dan zijn het puntpaden: itemSelector "listings", priceSelector
        "priceInfo.priceCents". Er zijn geen indexen: kom je onderweg een lijst tegen, dan neemt de motor het
        eerste element, dus "pictures.largeUrl" werkt ook als pictures een lijst is. @ en komma's bestaan hier
        niet, ::replace wel. priceInCents is true wanneer het bedrag in centen staat.

        ## Vervolgpagina's
        Zoek in de links van de paginering wat er verandert tussen pagina 1 en pagina 2. Er zijn drie vormen
        en de app kan ze alle drie:

        - Een stukje dat ACHTER de zoek-URL komt: pageTemplate met {page} erin, zoals "&page={page}" of
          "&_pgn={page}".
        - Het nummer staat middenin het PAD, bijvoorbeeld /s-seite:2/cd/k0. Zet {page} dan daar, in
          searchUrlTemplate, en laat pageTemplate leeg. {query} moet erin blijven staan en het moet dezelfde
          site blijven. De app kijkt daarna zelf na of pagina 1 met dat adres nog zoekertjes geeft en zet je
          voorstel terug als dat niet zo is - een fout is hier dus geen ramp, maar zwijgen wel: zonder dit
          veld blijft zo'n site steken op de ene pagina die je ziet.
        - De site telt niet in pagina's maar in zoekertjes: "limit=100&offset=200" is de derde pagina. Zet
          dan {offset} in searchUrlTemplate of in pageTemplate, en pageSize op hoeveel er op één pagina
          passen. Zonder pageSize telt {offset} niet als paginering.

        firstPage is het nummer van de eerste pagina, meestal 1 en soms 0. searchUrlTemplate laat je leeg
        wanneer het adres goed is zoals het is.

        ## Namen
        name: de naam van de site zoals ze zichzelf noemt. shortName: enkel als name langer is dan een
        twaalftal tekens, een kortere vorm voor een tabblad; anders leeg. baseUrl: schema en host
        (https://www.site.be), tenzij je meer nodig hebt voor een link die uit een id gebouwd wordt.

        ## Filters
        Twee lijsten, en allebei mogen ze leeg blijven wanneer je geen filters ziet.

        "filters" zijn de vaste, die de app van binnenuit kent omdat ze op elke site hetzelfde betekenen.
        Enkel deze sleutels bestaan; noem je iets anders, dan doet het niets:
        - priceMin, priceMax: een bedrag in euro
        - priceRangeEuro, priceRangeCents: beide grenzen in één parameter, als "min:max"
        - postcode of location: een postcode of een plaatsnaam
        - radius: een straal in kilometer. radiusMeters: dezelfde straal in meter
        Het fragment is het stukje URL met {value} erin, bijvoorbeeld "&priceTo={value}". Let op de eenheid:
        staat er in de URL van de site een bedrag in centen, kies dan priceRangeCents, en bij een straal in
        meter radiusMeters. Dat is bij een echte site fout gegaan.

        "customFilters" is alles wat enkel op deze site bestaat: staat, categorie, brandstof, soort verkoper,
        alleen met verzending. kind is "Choice" (een keuze uit een lijst; multiple op true als er meerdere
        tegelijk mogen), "Number" (een vrij getal) of "Toggle" (aan of uit). Bij Choice geef je de keuzes
        zoals de site ze verwacht, met het label in de taal van de site. Hoogstens acht, en dan de nuttigste.

        Geef bij elk filter een testValue die op deze site echt geldig is: een postcode die bestaat, een
        bedrag in de juiste eenheid. Bij een Choice neemt de app de eerste keuze, en bij priceMin en
        priceMax rekent ze zelf een bedrag uit.

        Waar je ze vindt: het filterformulier in de pagina (een select, een input of een checkbox met een
        name), de links van de filters in de zijbalk, of een blok met de hele filterlijst (__NEXT_DATA__,
        een taxonomy). Neem de namen letterlijk over, en noem geen parameter die al in de zoek-URL staat:
        die komt er dan twee keer in, en sommige sites antwoorden daarop met een serverfout.

        De app probeert elk filter daarna ECHT uit op deze zoekopdracht en vergelijkt de resultaten met de
        ongefilterde pagina. Wat niets verandert gaat eruit, en er gaan twee parameters mee die niet bestaan
        als controle. Een gok die fout blijkt kost dus niets, maar een filter dat je niet noemt bestaat
        nooit. Een sortering is géén filter: die verandert de volgorde en niet wat er te zien valt.

        ## notes
        Een paar zinnen in het Nederlands voor wie de site later onderhoudt: wat onzeker is en wat opviel.
        Bijvoorbeeld dat de prijs pas met JavaScript verschijnt, dat maar een deel van de zoekertjes in de
        pagina staat omdat de lijst meeschuift met het scrollen, of dat er klassen met willekeurige
        achtervoegsels gebruikt zijn. Een JSON-API is stabieler en sneller dan HTML: staan er sporen van in de
        meegestuurde lijst, noem dan de meest waarschijnlijke.

        Laat een veld leeg ("") wanneer het gegeven niet op de pagina staat.
        """;
}

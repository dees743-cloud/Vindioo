namespace Zentrix.Models;

/// <summary>Hoe de bron zijn resultaten aanlevert.</summary>
public enum SiteKind
{
    /// <summary>Gewone HTML-pagina, uitgelezen met CSS-selectors.</summary>
    Html,
    /// <summary>JSON-API, uitgelezen met een pad zoals "listings" en "priceInfo.priceCents".</summary>
    Json
}

/// <summary>
/// Welke motor de pagina uitleest. De meeste sites werken met de generieke motor
/// op basis van de selectors hieronder. Sommige sites hebben eigen code nodig
/// omdat hun opbouw niet met selectors te beschrijven is.
/// </summary>
public enum SiteEngine
{
    /// <summary>De generieke motor: leest de pagina uit met de selectors uit dit bestand.</summary>
    Generic,
    /// <summary>
    /// De linkmotor, voor sites met versleutelde klassenamen die bij elke update
    /// veranderen. Die volgt de links naar zoekertjes (ItemSelector) en leidt prijs,
    /// titel en plaats af uit de tekstregels van zo'n link; de details staan in
    /// <see cref="SiteDefinition.LinkText"/>. De andere selectors worden niet gebruikt.
    ///
    /// Heette tot september 2026 "Facebook", met die details in de code. Het nummer
    /// (1) is gebleven, zodat "Engine": 1 in een sitebestand blijft werken.
    /// </summary>
    LinkText
}

/// <summary>
/// Hoe de zoek-URL van een site in elkaar zit. De meeste sites zetten hun
/// zoekterm en filters gewoon als parameters in de URL; enkele pakken alles
/// samen in één versleuteld blok.
/// </summary>
public enum SiteUrlStyle
{
    /// <summary>Gewone parameters: {query} in de URL, filters erachter geplakt.</summary>
    Standard,

    /// <summary>
    /// De hele zoekopdracht zit als JSON in één base64-parameter. Die JSON staat
    /// gewoon in de zoek-URL, na het isgelijkteken, met {query}, {page} en
    /// {filters} erin; de app vult hem in en versleutelt hem. AlleVeilingen werkt
    /// zo (`?fi=`): losse parameters aan de URL plakken wordt daar genegeerd.
    ///
    /// Heette tot september 2026 naar die site, met het JSON-blok in de code. Het
    /// nummer (1) is gebleven, zodat "UrlStyle": 1 in een sitebestand blijft werken.
    /// </summary>
    Base64Json
}

/// <summary>
/// De beschrijving van één site: puur data, geen code.
/// Wordt opgeslagen en kan door de gebruiker of door de AI aangemaakt worden.
/// </summary>
public class SiteDefinition
{
    /// <summary>
    /// Versie van het bestandsformaat. Laat een latere versie van de app een
    /// ouder, gedeeld sitebestand automatisch bijwerken.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Vaste, korte sleutel (bv. "alleveilingen"). Dient als bestandsnaam en als
    /// identiteit die niet meebreekt als je de weergavenaam wijzigt. Wordt
    /// automatisch afgeleid van de naam wanneer hij leeg is.
    /// </summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// Kortere naam voor de tabstrip, bv. "Facebook" voor "Facebook Marketplace".
    /// De tabs staan naast elkaar en de breedte is daar schaars; overal elders
    /// (de kaarten, de instellingen, de meldingen) blijft <see cref="Name"/> staan.
    /// Leeg laten betekent: gewoon de volledige naam.
    /// </summary>
    public string ShortName { get; set; } = "";

    /// <summary>
    /// Mag deze site doorzocht worden zonder zoekterm? Bij de meeste sites is een
    /// lege zoekopdracht zinloos — je krijgt dan de hele catalogus. Bij AutoScout24
    /// is het net de gewone manier van werken: daar is het zoekwoord het merk, en
    /// wie niet merkgebonden wil zoeken zet alleen filters (brandstof, prijs,
    /// bouwjaar) en laat het merk weg.
    /// </summary>
    public bool AllowsEmptyQuery { get; set; }

    /// <summary>
    /// Extra kopregels bij het verzoek. De meeste sites hebben er geen nodig,
    /// maar een API kan er een eisen: de GraphQL-API van Discogs weigert elk
    /// verzoek zonder `x-apollo-operation-name` met een CSRF-fout. Zet hier dus
    /// enkel wat de site vraagt, geen sleutels die geheim moeten blijven - een
    /// sitebestand is bedoeld om te delen.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Waarmee de filters aan elkaar geplakt worden wanneer ze samen in een blok
    /// horen (de plaatshouder {filters} in de zoek-URL). Bij Discogs is dat een
    /// komma, want daar is het blok JSON; bij Catawiki een versleutelde ampersand
    /// (%26), want daar zit een hele querystring ín één parameter.
    /// </summary>
    public string FilterJoin { get; set; } = ",";

    /// <summary>Welke motor deze site uitleest. Standaard de generieke.</summary>
    public SiteEngine Engine { get; set; } = SiteEngine.Generic;

    /// <summary>
    /// De instellingen van de linkmotor: welk id, welke link, welke regel de prijs is.
    /// Enkel nodig bij <see cref="SiteEngine.LinkText"/>; bij elke andere site leeg.
    /// </summary>
    public LinkTextOptions? LinkText { get; set; }

    /// <summary>Zoek-URL met {query} als plaatshouder, bv. https://site.be/zoek?q={query}</summary>
    public string SearchUrlTemplate { get; set; } = "";

    /// <summary>
    /// Hoe de zoek-URL opgebouwd wordt. Bij een eigen stijl wordt van de
    /// zoek-URL enkel het deel vóór het vraagteken gebruikt.
    /// </summary>
    public SiteUrlStyle UrlStyle { get; set; } = SiteUrlStyle.Standard;

    public SiteKind Kind { get; set; } = SiteKind.Html;

    /// <summary>Basis-URL om relatieve links aan te vullen, bv. https://site.be</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Selector (HTML) of pad (JSON) naar de lijst met resultaten.</summary>
    public string ItemSelector { get; set; } = "";

    // Selectors of paden per veld, relatief aan één resultaat.
    public string TitleSelector { get; set; } = "";
    public string DescriptionSelector { get; set; } = "";
    public string PriceSelector { get; set; } = "";
    public string LocationSelector { get; set; } = "";
    public string DateSelector { get; set; } = "";
    public string UrlSelector { get; set; } = "";
    public string ImageSelector { get; set; } = "";

    /// <summary>
    /// Selector (HTML) of pad (JSON) naar een grotere/betere foto dan de miniatuur,
    /// als de zoekpagina die al aanbiedt. Laat leeg wanneer er maar één foto is.
    /// </summary>
    public string LargeImageSelector { get; set; } = "";

    /// <summary>
    /// Selector (HTML) of pad (JSON) naar hoelang er nog geboden kan worden, zoals de
    /// site het zelf schrijft ("Nog 3 dagen", "Nog 9d 12u"). Dat komt naast de plaats te
    /// staan. Leeg laten bij een site zonder veilingen — en ook bij een veilingsite die
    /// het niet op haar zoekpagina zet, want dan zou het per zoekertje een paginabezoek
    /// kosten.
    ///
    /// Met opzet tekst en geen datum: de sites schrijven er geen tijdstip bij, en wat zij
    /// tonen klopt altijd met wat de bezoeker op hun eigen pagina ziet. Daarom wordt het
    /// ook niet bewaard bij een favoriet: "nog 3 dagen" van vorige week is een leugen.
    /// </summary>
    public string TimeLeftSelector { get; set; } = "";

    /// <summary>
    /// Selector naar de sluitingsdatum op de pagina van het zoekertje zélf, niet op de
    /// zoekpagina. Staat die er, dan haalt <c>DetailFetcher</c> die pagina op voor de
    /// zoekertjes die op dat moment op het scherm staan — niet voor alles wat binnenkwam,
    /// want het is één verzoek per zoekertje.
    ///
    /// AlleVeilingen heeft die datum enkel daar: <c>div[title='Einddatum']</c>, met
    /// "Einde op 29/09/2026 19:00" erin. Anders dan <see cref="TimeLeftSelector"/> is dit
    /// een echt tijdstip, dus de app rekent zelf uit hoelang het nog duurt en dat blijft
    /// kloppen.
    /// </summary>
    public string DetailEndDateSelector { get; set; } = "";

    /// <summary>
    /// Waar de foto's van één zoekertje staan, op de pagina van dat zoekertje zelf. De
    /// zoekpagina geeft er meestal één; een advertentie heeft er vijf of tien, en juist op die
    /// andere staat vaak wat je wil zien - het label achteraan, de doos van binnen.
    ///
    /// Elk element dat past telt mee, in de volgorde van de pagina, ontdubbeld. Dus meestal
    /// iets als <c>.gallery img@src</c>, gerust met <c>::replace</c> erachter om de grote
    /// variant te krijgen, net als bij <see cref="LargeImageSelector"/>.
    ///
    /// Gebruikt door de AI-controle op alle foto's van een zoekertje (<c>PhotoInsightWindow</c>)
    /// en door het detailvenster (<c>ListingDetailWindow</c>); leeg laten betekent dat daar
    /// enkel de foto van de zoekpagina te zien is.
    /// </summary>
    public string DetailImagesSelector { get; set; } = "";

    /// <summary>
    /// De naam van de verkoper op de pagina van het zoekertje zelf. Anders dan
    /// <see cref="SellerSelector"/>, die op de zoekpagina leest: sommige sites zetten de
    /// verkoper enkel op de advertentie. Staat hij wél al op de zoekpagina, laat dit dan leeg -
    /// dan is hij er meteen, zonder verzoek.
    /// </summary>
    public string DetailSellerSelector { get; set; } = "";

    /// <summary>
    /// Sinds wanneer het zoekertje online staat, op zijn eigen pagina. Bij 2dehands staat dat
    /// er als "Sinds 24 sep. '26".
    ///
    /// Dit is <b>tekst</b> en geen tijdstip, met opzet en om dezelfde reden als
    /// <see cref="TimeLeftSelector"/>: elke site schrijft het anders op ("Eergisteren",
    /// "24 sep. '26", "vandaag"), en wat de site zelf toont klopt altijd met wat een bezoeker
    /// daar ziet. Er wordt dus niets uitgerekend, en het wordt niet bewaard bij een favoriet.
    /// </summary>
    public string DetailPostedSelector { get; set; } = "";

    /// <summary>
    /// Een API die het exacte sluitingstijdstip voor veel zoekertjes tegelijk geeft. Zie
    /// <see cref="EndTimeApiOptions"/>. Leeg bij de meeste sites.
    /// </summary>
    public EndTimeApiOptions? EndTimeApi { get; set; }

    /// <summary>Wanneer de prijs in centen staat in plaats van euro (zoals bij 2dehands).</summary>
    public bool PriceInCents { get; set; }

    /// <summary>
    /// Telt deze site mee voor een prijsindicatie (rechtsklik op een foto, zie
    /// <c>PriceIndicator</c>)? Zet het aan bij sites met vraagprijzen van gewone verkopers.
    /// Een veilingsite hoort er niet bij: daar is de prijs een bod dat nog kan stijgen.
    /// </summary>
    public bool PriceReference { get; set; }

    /// <summary>
    /// Is de prijs op deze site een lopend bod in plaats van een vraagprijs? Dan zegt de
    /// prijsindicatie "huidig bod", en telt die prijs niet mee voor de marktwaarde.
    /// </summary>
    public bool IsAuction { get; set; }

    /// <summary>Selector (HTML) of pad (JSON) naar de naam van de verkoper. Mag leeg blijven.</summary>
    public string SellerSelector { get; set; } = "";

    /// <summary>
    /// Veilinghuizen die op deze site adverteren. Catawiki zet zijn kavels op 2dehands en
    /// Marktplaats, met het huidige bod als een gewone vaste prijs: bij "marantz" was een kwart
    /// tot een derde van de resultaten zo'n advertentie, terwijl je diezelfde kavels ook
    /// rechtstreeks op de veilingsite vindt. Hun zoekertjes worden daarom overgeslagen bij het
    /// uitlezen. Werkt enkel met <see cref="SellerSelector"/>; hoofdletters tellen niet.
    /// </summary>
    public List<string> AuctionSellers { get; set; } = new();

    /// <summary>
    /// Een reguliere expressie die het vaste id uit de link van een zoekertje haalt, met dat
    /// id als eerste groep, bv. <c>/kavel/(\d+)</c>. Leeg: de volledige link is de identiteit.
    ///
    /// Waarvoor: de link is wat een zoekertje uniek maakt ("al gezien",
    /// favoriet), maar sommige sites zetten er de zoekopdracht zelf in. Bij AlleVeilingen
    /// staat het paginanummer in de link, dus een kavel die van pagina 2 naar 3 schoof, was
    /// "nieuw" - 14 van de 140 kavels stonden er twee keer in. Catawiki zet de zoekterm erin.
    /// Enkel voor de gewone motor; de linkmotor heeft zijn eigen IdPattern.
    /// </summary>
    public string IdPattern { get; set; } = "";

    /// <summary>Pagina eerst met een echte browser laten renderen (JavaScript-sites).</summary>
    public bool NeedsBrowser { get; set; }

    /// <summary>Pagina ophalen via je eigen Chrome (de Zentrix Brug-extensie).</summary>
    public bool UseBridge { get; set; }
    /// <summary>Staat het vinkje standaard aan.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hoe deze site zijn eigen zoekfilters in de URL zet. De sleutel is een vaste
    /// filternaam die de app kent, de waarde is het stukje dat achter de zoek-URL
    /// komt met {value} als plaatshouder. Voorbeeld:
    /// <code>
    /// "priceMin": "&amp;price[min]={value}",
    /// "location": "&amp;locations={value}",
    /// "radius":   "&amp;radius={value}"
    /// </code>
    /// Alleen ingevulde filters worden toegevoegd; wat hier niet in staat, filtert
    /// de app achteraf zelf (het vangnet). Wordt in een latere stap door de bronnen
    /// gebruikt; nu al onderdeel van het bestandsformaat zodat gedeelde bestanden
    /// meteen goed zitten.
    /// </summary>
    public Dictionary<string, string> Filters { get; set; } = new();

    /// <summary>
    /// Hoe deze site zijn vervolgpagina's aanspreekt: een stukje URL met {page},
    /// bijvoorbeeld "&amp;page={page}". De eerste pagina wordt altijd zonder dit
    /// stukje opgehaald; pas vanaf de tweede wordt het toegevoegd. Laat leeg
    /// wanneer paginering niet nodig is — bijvoorbeeld bij een API die in één
    /// keer het gevraagde aantal teruggeeft.
    /// </summary>
    public string PageTemplate { get; set; } = "";

    /// <summary>
    /// Nummer van de eerste pagina. Meestal 1; sommige sites tellen vanaf 0.
    /// De tweede pagina wordt dan dit getal plus één.
    /// </summary>
    public int FirstPage { get; set; } = 1;

    /// <summary>
    /// Hoeveel zoekertjes er op één pagina staan, voor sites die hun pagina's niet nummeren
    /// maar zeggen vanaf het hoeveelste zoekertje ze beginnen: <c>{offset}</c> in de zoek-URL
    /// of in <see cref="PageTemplate"/> wordt (pagina - 1) maal dit getal. Bij 2dehands 100,
    /// net als de <c>limit=100</c> in zijn zoek-URL: dat is het meeste wat zijn API per keer
    /// geeft (limit=200 gaf een 400). Leeg (0) bij sites met {page}.
    /// </summary>
    public int PageSize { get; set; }

    // ---------- hoeveel de app van een site ophaalt ----------

    /// <summary>
    /// Antwoordt deze site rechtstreeks, zonder browser of brug? Zo'n site is snel - bij
    /// 2dehands 0,3 tot 0,8 seconde voor honderd zoekertjes - en mag dus meer leveren.
    /// Methodes en geen eigenschappen: die zouden mee in het sitebestand belanden.
    /// </summary>
    public bool AnswersDirectly() => Engine == SiteEngine.Generic && !NeedsBrowser && !UseBridge;

    /// <summary>
    /// Hoeveel zoekertjes de app hoogstens van deze site ophaalt, per zoekopdracht. Een rem
    /// die de app zelf kiest, geen grens van de site. Tot 22 september 2026 was het 500 voor
    /// elke site, en elke bewaarde zoekopdracht onthield er zelf een (100 of 500, uit het veld
    /// "Max. resultaten"). Toen bleek dat 2dehands voor "cd speler" 3961 zoekertjes heeft, en
    /// dat er ook vanaf het 2000e nog de helft echte cd-spelers zijn. Zo koos de eigenaar het:
    /// <list type="bullet">
    /// <item>2000 bij een site die rechtstreeks antwoordt;</item>
    /// <item>300 bij de linkmotor (Facebook): die heeft geen pagina's maar scrolt, ongeveer
    /// een seconde per twintig zoekertjes, als jouw aangemelde account;</item>
    /// <item>500 bij een site via de browser of de brug, zoals voordien: daar kost een pagina
    /// 2 tot 8 seconden, en veel pagina's na elkaar valt een robotbeveiliging op.</item>
    /// </list>
    /// </summary>
    public int ResultLimit() =>
        Engine == SiteEngine.LinkText ? 300 : AnswersDirectly() ? 2000 : 500;

    /// <summary>
    /// Veiligheidsrem op het aantal pagina's per zoekopdracht, voor een site die blijft
    /// antwoorden. 20 voor een site die rechtstreeks antwoordt (2dehands: 20 x 100 = 2000),
    /// anders 10. Een site met kleine pagina's haalt de 2000 dus niet: Kleinanzeigen geeft
    /// er een dertigtal per pagina. Tachtig verzoeken na elkaar aan dezelfde site is precies
    /// wat een robotbeveiliging opvalt.
    /// </summary>
    public int PageLimit() => AnswersDirectly() ? 20 : 10;

    /// <summary>
    /// Filters die alleen op deze site bestaan: brandstof, kilometerstand,
    /// carrosserie en dergelijke. Zie <see cref="CustomFilter"/>. Leeg bij de
    /// meeste sites; dan blijft de knop ervoor uit staan.
    /// </summary>
    public List<CustomFilter> CustomFilters { get; set; } = new();

    /// <summary>Vrije notitie, bv. hoe de definitie tot stand kwam.</summary>
    public string Notes { get; set; } = "";
}
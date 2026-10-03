// Vindioo Brug
//
// Vraagt elke seconde aan de Vindioo-app of er een pagina opgehaald moet
// worden. Zo ja: opent die op de achtergrond in een tabblad, wacht tot ze
// geladen is, leest de HTML uit en sluit het tabblad weer.
//
// Alles gebeurt op deze computer. Er wordt niets naar buiten gestuurd.

const APP_URL = "http://127.0.0.1:8731";

// Aan deze kopregel herkent de app dat een verzoek van de extensie komt en niet van een
// webpagina. Een webpagina kan hem niet meesturen: daarvoor moet ze eerst toestemming
// vragen, en die geeft de app enkel aan de extensie. Zonder deze kopregel kon een
// webpagina met een verzonnen code de app laten denken dat de koppelcode niet klopte.
const BRUG_KOP = { "X-Vindioo-Brug": "1" };

// ---------- bewijzen dat je de koppelcode kent, zonder hem te versturen ----------
//
// Tot 1 oktober 2026 ging de code als "?token=..." mee naar wie poort 8731 ook maar
// vasthield. Een ander programma dat die poort eerst bezet, kende hem daarmee - en kon
// deze extensie pagina's laten ophalen met JOUW cookies. De code hoort dus nergens heen.
//
// Nu: per verzoek een nonce (een wegwerpgetal, geen geheim), en de andere kant tekent met
// de code. Dat werkt twee kanten op:
//   - wij tekenen wat we sturen, zodat de app weet dat het van ons komt;
//   - de app tekent wat ze antwoordt, zodat wij weten dat we met de ECHTE app praten.
// Dat tweede is wat deze extensie beschermt: zij voert uit wat daaruit komt.
const SIG_KOP = "X-Vindioo-Sig";
const VOOR_KOP = "X-Vindioo-Voor";

async function teken(code, data) {
  const enc = new TextEncoder();

  const sleutel = await crypto.subtle.importKey(
    "raw", enc.encode(code), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);

  const ruw = await crypto.subtle.sign("HMAC", sleutel, enc.encode(data));

  return [...new Uint8Array(ruw)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function nonce() {
  return crypto.randomUUID().replace(/-/g, "");
}

/// Een verzoek aan de app, getekend. Geeft het antwoord terug, of null wanneer de
/// handtekening van de app niet klopt - dan praten we niet met Vindioo.
async function vraagApp(code, pad, body = null) {
  const n = nonce();
  const inhoud = body === null ? "" : JSON.stringify(body);

  // Twee handtekeningen, en dat is geen dubbelop: de tweede gaat enkel over de nonce, zodat de
  // app de code kan nakijken VOOR ze een body leest. Zonder dat zou een programma dat de poort
  // bezet houdt haar een reusachtige body kunnen laten inlezen.
  const kop = {
    ...BRUG_KOP,
    [SIG_KOP]: await teken(code, n + "\n" + inhoud),
    [VOOR_KOP]: await teken(code, n)
  };
  if (body !== null) kop["Content-Type"] = "application/json";

  const antwoord = await fetch(`${APP_URL}${pad}?n=${n}`, {
    method: body === null ? "GET" : "POST",
    headers: kop,
    body: body === null ? undefined : inhoud
  });

  const tekst = await antwoord.text();

  // En nu de andere kant: heeft de app dit getekend? Zo niet, dan zit er iets anders op
  // die poort, en voeren we er zeker niets van uit.
  if (antwoord.headers.get(SIG_KOP) !== await teken(code, n + "\n" + tekst)) {
    return { nietDeApp: true };
  }

  try {
    return JSON.parse(tekst);
  } catch {
    return { nietDeApp: true };
  }
}
const POLL_MS = 250;           // hoe vaak we om werk vragen
const LOAD_TIMEOUT_MS = 45000; // hoe lang we op een pagina wachten
const SNAPSHOT_MS = 750;       // hoe vaak we tussentijds een momentopname sturen
const MAX_PARTIALS = 15;       // rem op het aantal tussentijdse leveringen
const SETTLE_MS = 2500;        // wachten na het scrollen wanneer we niets beters weten
const STABLE_POLL_MS = 100;    // hoe vaak we tellen of er na het scrollen nog zoekertjes bijkomen
const STABLE_MAX_MS = 1500;    // ... en hoe lang hoogstens
const ITEM_TIMEOUT_MS = 8000; // hoe lang we standaard op het eerste zoekertje wachten
const MAX_TEGELIJK = 3;       // hoeveel opdrachten er tegelijk mogen lopen
const MIN_HTML = 5000;         // kleiner dan dit is nog een lege of ladende pagina
const DOM_POLL_MS = 200;       // hoe vaak we kijken of de DOM al bruikbaar is

let running = false;
let lastContact = 0;    // wanneer de app ons het laatst aanvaardde
let lastWrongCode = 0;  // wanneer de app onze koppelcode het laatst weigerde
let bezig = 0;      // hoeveel opdrachten er nu lopen

// Versiestempel: zie je deze regel niet in de console van de service worker,
// dan draait Chrome nog de oude versie en moet de extensie herladen worden.
// ---------- waar mogen we heen? ----------

// Wat wij openen, opent met JOUW cookies - bij een API-opdracht zelfs met
// credentials: "include". Het adres komt uit een sitebestand, en zo'n bestand krijg je van
// iemand anders. Een verzonnen zoek-URL zou jouw aangemelde browser dus verzoeken kunnen
// laten doen naar eender welke site, of naar een adres op je eigen netwerk - de beheerpagina
// van je router vraagt daar vaak niet eens een aanmelding voor.
//
// De app kijkt het ook na bij het importeren (Services/SiteUrlCheck.cs). Hier nog eens, want
// een extensie blijft in Chrome staan terwijl de app verandert, en twee sloten vergeet je
// niet allebei tegelijk.
//
// Geeft null terug als het mag, en anders de reden.
function waaromNiet(adres) {
  let u;

  try {
    u = new URL(adres);
  } catch {
    return "geen geldig webadres";
  }

  if (u.protocol !== "https:") return `${u.protocol.replace(":", "")} in plaats van https`;

  const host = u.hostname.toLowerCase().replace(/^\[|\]$/g, "");

  // Een naam zonder punt ("router") staat per definitie op je eigen netwerk.
  if (!host.includes(".")) return `'${host}' is een naam op je eigen netwerk`;
  if (/\.(local|localhost|internal|home|lan)$/.test(host)) return `'${host}' is een lokale naam`;

  const v4 = host.match(/^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/);

  if (v4) {
    const a = +v4[1];
    const b = +v4[2];

    if (a === 10 || a === 127 || a === 0 ||
        (a === 172 && b >= 16 && b <= 31) ||
        (a === 192 && b === 168) ||
        (a === 169 && b === 254)) {
      return `'${host}' is een adres op je eigen netwerk`;
    }
  }

  if (host === "::1" || /^f[cd][0-9a-f]{2}:/.test(host) || /^fe80:/.test(host)) {
    return `'${host}' is een adres op je eigen netwerk`;
  }

  return null;
}

// ---------- en mogen we er wel bij? ----------
//
// Tot 1 oktober 2026 vroeg de extensie bij het installeren toegang tot <all_urls>: álle sites,
// met jouw cookies. Dat is veel meer dan ze nodig heeft, en het was ook niet waar te maken dat
// het niet anders kon - dus staat er nu "https://*/*" bij optional_host_permissions. Dat is géén
// toestemming, enkel het recht om ze te vragen. Bij de start heeft de extensie dus toegang tot
// niets, en jij geeft ze per site, uit het pictogram in Chrome.
//
// Waarom dat de moeite is: het adres van een opdracht komt uit een sitebestand, en zo'n bestand
// krijg je van iemand anders. waaromNiet() hierboven houdt al het halve internet buiten (geen
// http, geen adressen op je eigen netwerk), maar een verzonnen bestand dat naar een gewone
// https-site wijst, kwam daar netjes door - naar je webmail bijvoorbeeld, en die pagina lezen we
// uit en sturen we naar de app. Met toestemming per host kan dat niet meer: wat jij niet hebt
// aangevinkt, kan de extensie niet openen en zeker niet uitlezen.
//
// LET OP bij het lezen: chrome.permissions.request() mag enkel uit een gebruikersklik komen. Dit
// achtergrondscript heeft die niet en kan dus nooit zelf toestemming vragen - het kan enkel
// weigeren en onthouden waarvoor. De knop staat daarom in de popup.

/// Het patroon waarmee Chrome over één site praat. Een pad doet er niet toe: Chrome negeert dat
/// bij een hostrecht, dus "https://www.site.be/*" is precies "de hele site".
function patroonVoor(adres) {
  return new URL(adres).origin + "/*";
}

/// Mogen we aan dit adres? Bij twijfel nee.
async function mag(adres) {
  try {
    return await chrome.permissions.contains({ origins: [patroonVoor(adres)] });
  } catch (e) {
    return false;
  }
}

/// Onthoudt een host waarvoor we toestemming misten, zodat de popup hem kan aanbieden.
///
/// Dit vult aan wat de app zelf doorgeeft (/hosts kent de sites die via de brug zoeken). Het
/// vangt de twee gevallen die daar niet in staan: een nieuwe site die je in Vindioo laat
/// analyseren, en een site die doorverwijst naar een andere naam (2dehands.be naar
/// www.2dehands.be).
async function onthoudNodig(host) {
  try {
    const bewaard = await chrome.storage.local.get("nodig");
    const lijst = Array.isArray(bewaard.nodig) ? bewaard.nodig : [];

    if (lijst.includes(host)) return;

    // Een rem, zodat een site die blijft doorverwijzen de lijst niet laat volgroeien.
    await chrome.storage.local.set({ nodig: [...lijst, host].slice(-25) });
  } catch (e) {
    // Zonder deze lijst werkt de rest gewoon; ze is enkel een gemak voor de popup.
  }
}

/// De zin die de app te zien krijgt wanneer een opdracht geweigerd wordt. Die komt in Vindioo
/// bij de site te staan, dus hij moet zeggen wat je eraan doet.
function geenToegangTekst(host) {
  return `de brug mag nog niet aan ${host} - klik op het Vindioo-pictogram in Chrome en geef toegang`;
}

/// Staat het tabblad op een pagina waar we niet in mogen kijken? Geeft de reden terug, of null.
///
/// Dit is er voor de doorverwijzing: we kregen toestemming voor het adres uit de opdracht, maar
/// de site stuurt ons naar een andere naam. Zonder deze controle mislukt elk executeScript en
/// liepen de 45 seconden van waitForDom gewoon vol - met een foutmelding die niets zegt.
async function waaromGeenToegang(tabId) {
  try {
    const tab = await chrome.tabs.get(tabId);

    // about:blank of een pagina die nog aan het laden is: niets aan de hand.
    if (!tab || !tab.url || !tab.url.startsWith("http")) return null;
    if (await mag(tab.url)) return null;

    const host = new URL(tab.url).hostname;
    await onthoudNodig(host);

    return geenToegangTekst(host);
  } catch (e) {
    return null;
  }
}

console.log("[brug] versie 11 geladen — nieuwe naam: Vindioo, en nieuwe kopregels X-Vindioo-*");

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function getToken() {
  const stored = await chrome.storage.local.get("token");
  return stored.token || "";
}

// ---------- hoofdlus ----------

async function loop() {
  if (running) return;
  running = true;

  while (true) {
    const token = await getToken();

    if (!token) {
      // Nog niet gekoppeld: rustig blijven wachten.
      await sleep(3000);
      continue;
    }

    // De rem: lopen er al drie opdrachten, dan geen nieuwe vragen. Vroeger stond deze
    // controle pas NA het aannemen van een opdracht. Die bepaalde dan enkel of we
    // meteen opnieuw vroegen; na de korte pauze nam de extensie er toch een bij. Bij
    // Catawiki liepen er zo negen tegelijk.
    if (bezig >= MAX_TEGELIJK) {
      await sleep(POLL_MS);
      continue;
    }

    try {
      const job = await vraagApp(token, "/job");

      if (job && job.nietDeApp) {
        // Er zit iets op poort 8731 dat de koppelcode niet kent. Dat telt niet als contact,
        // en we nemen er zeker geen opdracht van aan.
        lastWrongCode = Date.now();
      } else if (job && job.error) {
        // De app draait, maar weigert deze koppelcode. Dat telt NIET als contact: de
        // popup toonde dan "verbonden" terwijl de app de extensie weigerde.
        lastWrongCode = Date.now();
      } else {
        lastContact = Date.now();

        if (job && job.url) {
          // NIET afwachten: de app vraagt vervolgpagina's tegelijk op, en die hoeven
          // niet op elkaar te wachten. Elke opdracht is een eigen tabblad.
          bezig++;
          handleJob(job, token).finally(() => { bezig--; });

          continue;  // meteen kijken of er nog werk is; de rem hierboven houdt het op drie
        }
      }
    } catch (error) {
      // App draait niet of poort dicht: gewoon opnieuw proberen.
    }

    await sleep(POLL_MS);
  }
}

// ---------- één pagina ophalen ----------

// Leest de huidige HTML van een tabblad. Kan mislukken zolang het tabblad nog
// aan het navigeren is; de aanroeper vangt dat op.
async function readHtml(tabId) {
  const [result] = await chrome.scripting.executeScript({
    target: { tabId },
    func: () => document.documentElement.outerHTML
  });

  return (result && result.result) || "";
}

// Haalt een JSON-API op in plaats van een pagina.
//
// Waarom dit bestaat: Discogs zit achter Cloudflare, en die weigert elk verzoek
// van de app zelf (die kijkt naar de vingerafdruk van de TLS-handdruk; .NET komt
// er niet door, jouw Chrome wel). En naar de API navigeren als pagina kan niet,
// want dan mist de kopregel die de API eist — bij een navigatie kan je er geen
// meegeven.
//
// Dus: we openen een gewone pagina van die site (daar komt Chrome wel door, en
// dan staan de cookies goed) en doen van daaruit de fetch. De app krijgt de ruwe
// tekst terug in hetzelfde veld als anders de HTML.
async function handleRawJob(job, token) {
  let tabId = null;

  const t0 = Date.now();
  const since = () => ((Date.now() - t0) / 1000).toFixed(1) + "s";

  // Voor er iets opengaat: mag dit adres wel?
  const bezwaar = waaromNiet(job.url);

  if (bezwaar) {
    console.warn("[brug] geweigerd:", job.url, "-", bezwaar);
    await sendResult(token, { id: job.id, error: `de brug weigerde dit adres: ${bezwaar}` });
    return;
  }

  // En heb jij deze site aangevinkt? Zo niet, dan gaat er geen tabblad open. Deze controle
  // staat bewust VOOR chrome.tabs.create: een tabblad openen stuurt al een verzoek mét jouw
  // cookies, ook als we de pagina daarna niet mogen uitlezen.
  if (!(await mag(job.url))) {
    const host = new URL(job.url).hostname;

    await onthoudNodig(host);
    console.warn("[brug] nog geen toegang tot", host);
    await sendResult(token, { id: job.id, error: geenToegangTekst(host) });

    return;
  }

  console.log("[brug] API-opdracht opgepikt:", job.url);

  try {
    const oorsprong = new URL(job.url).origin;

    const tab = await chrome.tabs.create({ url: oorsprong, active: false });
    tabId = tab.id;

    await waitForDom(tabId);
    console.log(`[brug] ${since()} pagina van ${oorsprong} klaar`);

    const [result] = await chrome.scripting.executeScript({
      target: { tabId },
      world: "MAIN",
      args: [job.url, job.headers || {}],
      func: async (adres, kop) => {
        try {
          const antwoord = await fetch(adres, { headers: kop, credentials: "include" });
          return { ok: true, status: antwoord.status, tekst: await antwoord.text() };
        } catch (e) {
          return { ok: false, fout: String(e && e.message ? e.message : e) };
        }
      }
    });

    const uit = result && result.result;

    if (!uit || !uit.ok) {
      throw new Error(uit ? uit.fout : "geen antwoord van de pagina");
    }

    console.log(`[brug] ${since()} API gaf ${uit.status}, ${uit.tekst.length} tekens`);
    await sendResult(token, { id: job.id, html: uit.tekst });
  } catch (error) {
    console.log(`[brug] ${since()} FOUT bij API-opdracht:`, error);
    await sendResult(token, { id: job.id, error: String(error && error.message ? error.message : error) });
  } finally {
    if (tabId !== null) {
      try {
        await chrome.tabs.remove(tabId);
      } catch (e) {
        // Tabblad was al gesloten.
      }
    }
  }
}

async function handleJob(job, token) {
  // Een API-opdracht gaat een andere weg: geen tabblad uitlezen, maar een fetch
  // vanuit een pagina van die site.
  if (job.rawText) return handleRawJob(job, token);

  let tabId = null;
  let stop = false;

  // Tijdmeting, zodat we in de console zien waar de seconden blijven.
  const t0 = Date.now();
  const since = () => ((Date.now() - t0) / 1000).toFixed(1) + "s";

  // Voor er iets opengaat: mag dit adres wel?
  const bezwaar = waaromNiet(job.url);

  if (bezwaar) {
    console.warn("[brug] geweigerd:", job.url, "-", bezwaar);
    await sendResult(token, { id: job.id, error: `de brug weigerde dit adres: ${bezwaar}` });
    return;
  }

  // En heb jij deze site aangevinkt? Zo niet, dan gaat er geen tabblad open. Deze controle
  // staat bewust VOOR chrome.tabs.create: een tabblad openen stuurt al een verzoek mét jouw
  // cookies, ook als we de pagina daarna niet mogen uitlezen.
  if (!(await mag(job.url))) {
    const host = new URL(job.url).hostname;

    await onthoudNodig(host);
    console.warn("[brug] nog geen toegang tot", host);
    await sendResult(token, { id: job.id, error: geenToegangTekst(host) });

    return;
  }

  console.log("[brug] opdracht opgepikt:", job.url);

  try {
    const tab = await chrome.tabs.create({ url: job.url, active: false });
    tabId = tab.id;
    console.log(`[brug] ${since()} tabblad geopend`);

    // Terwijl de pagina laadt, sturen we tussentijdse versies door. Zo kan de app
    // de eerste zoekertjes al tonen in plaats van te wachten tot alles klaar is.
    // Enkel als de app ze ook leest (stream): bij een vervolgpagina of een geplande
    // zoekopdracht kopieerden we anders elke 0,75 s de hele pagina voor niets.
    let lastLength = 0;
    let partials = 0;

    const streaming = job.stream === false ? Promise.resolve() : (async () => {
      while (!stop && partials < MAX_PARTIALS) {
        await sleep(SNAPSHOT_MS);
        if (stop) break;

        try {
          const html = await readHtml(tabId);

          // Enkel sturen als er merkbaar iets is bijgekomen.
          if (html.length > MIN_HTML && html.length > lastLength * 1.02) {
            lastLength = html.length;
            partials++;
            console.log(`[brug] ${since()} tussentijds verstuurd (${html.length} tekens)`);
            await sendResult(token, { id: job.id, html, partial: true });
          }
        } catch (e) {
          // Tabblad nog aan het navigeren: volgende ronde opnieuw proberen.
        }
      }
    })();

    await waitForDom(tabId);
    console.log(`[brug] ${since()} DOM klaar`);

    // Wachten tot het eerste zoekertje er staat in plaats van een vaste pauze.
    // Die pauze was 2,5 seconde per pagina, en bij Catawiki zijn dat er vijf voor
    // honderd kavels. Staat het zoekertje er, dan valt er niets meer af te wachten.
    // Een vervolgpagina krijgt van de app een korte wachttijd mee: die is vaak gewoon
    // leeg, en dan liepen de acht seconden telkens vol.
    const staanErAl = await waitForItem(tabId, job.waitSelector, job.itemTimeoutMs || ITEM_TIMEOUT_MS);
    console.log(`[brug] ${since()} zoekertjes ${staanErAl ? "gezien" : "niet gezien"}`);

    // Even scrollen: veel sites laden hun resultaten pas dan.
    await chrome.scripting.executeScript({
      target: { tabId },
      func: () => {
        window.scrollTo(0, document.body.scrollHeight / 2);
      }
    });

    // Staan de zoekertjes er, dan wachten tot er geen meer bijkomen in plaats van een
    // vaste halve seconde. Staan ze er niet op een vervolgpagina, dan is die pagina
    // leeg en valt er niets af te wachten. Enkel zonder zoekertje én zonder korte
    // wachttijd van de app blijft de lange pauze.
    if (staanErAl) await waitUntilStable(tabId, job.waitSelector);
    else if (!job.itemTimeoutMs) await sleep(SETTLE_MS);

    // Streamen stoppen voor we de volledige versie sturen.
    stop = true;
    await streaming;

    const html = await readHtml(tabId);
    console.log(`[brug] ${since()} volledige versie verstuurd (${html.length} tekens)`);
    await sendResult(token, { id: job.id, html });
  } catch (error) {
    console.log(`[brug] ${since()} FOUT:`, error);
    stop = true;
    await sendResult(token, { id: job.id, error: String(error && error.message ? error.message : error) });
  } finally {
    if (tabId !== null) {
      try {
        await chrome.tabs.remove(tabId);
      } catch (e) {
        // Tabblad was al gesloten.
      }
    }
  }
}

// Wacht tot de DOM bruikbaar is, niet tot het load-event.
//
// Waarom: "status complete" van een tabblad komt overeen met het load-event, en
// dat wacht óók op advertenties, trackers en afbeeldingen. Op leboncoin duurde
// dat 23 seconden, terwijl de zoekresultaten er na 1 seconde al stonden. We
// kijken daarom naar document.readyState.
async function waitForDom(tabId) {
  const deadline = Date.now() + LOAD_TIMEOUT_MS;

  while (Date.now() < deadline) {
    try {
      const [result] = await chrome.scripting.executeScript({
        target: { tabId },
        func: () => document.readyState
      });

      const state = result && result.result;
      if (state === "interactive" || state === "complete") return;
    } catch (e) {
      // Twee heel verschillende redenen waarom dit mislukt: het tabblad navigeert nog, of we
      // mogen in deze pagina niet kijken. Dat laatste gebeurt bij een doorverwijzing naar een
      // andere naam, en dan heeft blijven proberen geen zin: zonder dit onderscheid liepen de
      // 45 seconden vol en kreeg je een melding waar niets in stond.
      const bezwaar = await waaromGeenToegang(tabId);
      if (bezwaar) throw new Error(bezwaar);
    }

    await sleep(DOM_POLL_MS);
  }
}

// Wacht tot de eerste treffer van `selector` op de pagina staat.
//
// Geeft true zodra hij er is. Zonder selector, of als hij er binnen de tijd niet
// komt, geeft hij false en valt de aanroeper terug op de lange pauze.
async function waitForItem(tabId, selector, timeoutMs) {
  if (!selector) return false;

  const deadline = Date.now() + (timeoutMs || ITEM_TIMEOUT_MS);

  while (Date.now() < deadline) {
    try {
      const [result] = await chrome.scripting.executeScript({
        target: { tabId },
        args: [selector],
        func: (sel) => {
          try {
            return document.querySelector(sel) !== null;
          } catch (e) {
            return false;   // onbruikbare selector: niet blijven proberen
          }
        }
      });

      if (result && result.result === true) return true;
    } catch (e) {
      // Tabblad nog aan het navigeren: zo meteen opnieuw.
    }

    await sleep(DOM_POLL_MS);
  }

  return false;
}

// Wacht tot er na het scrollen geen zoekertjes meer bijkomen.
//
// Telt elke 100 ms hoeveel er staan en stopt zodra dat aantal twee keer gelijk
// bleef, met anderhalve seconde als maximum. Een site die alles meteen in de HTML
// zet, is zo na 200 ms klaar in plaats van na een vaste halve seconde; een site die
// bijlaadt, krijgt de tijd die ze nodig heeft.
async function waitUntilStable(tabId, selector) {
  const deadline = Date.now() + STABLE_MAX_MS;
  let vorige = -1;
  let gelijk = 0;

  while (Date.now() < deadline) {
    await sleep(STABLE_POLL_MS);

    let aantal = -1;
    try {
      const [result] = await chrome.scripting.executeScript({
        target: { tabId },
        args: [selector],
        func: (sel) => {
          try {
            return document.querySelectorAll(sel).length;
          } catch (e) {
            return -1;
          }
        }
      });
      aantal = result ? result.result : -1;
    } catch (e) {
      // Tabblad even onbereikbaar: volgende ronde opnieuw.
    }

    if (aantal === vorige) {
      gelijk++;
      if (gelijk >= 2) return;
    } else {
      gelijk = 0;
      vorige = aantal;
    }
  }
}

async function sendResult(token, payload) {
  try {
    await vraagApp(token, "/result", payload);
  } catch (error) {
    // App gestopt: niets meer te doen.
  }
}

/// De sites die de brug nodig heeft, met per site of ze er al aan mag.
///
/// Twee bronnen, en samen dekken ze alles: de app weet welke sites via de brug zoeken (/hosts),
/// en wij onthouden wat er onderweg geweigerd werd. Dat tweede is er voor een site die nog niet
/// in Vindioo staat (een nieuwe, die je laat analyseren) en voor een doorverwijzing.
async function hostLijst() {
  const hosts = new Set();

  try {
    const token = await getToken();

    if (token) {
      const uit = await vraagApp(token, "/hosts");
      if (uit && Array.isArray(uit.hosts)) uit.hosts.forEach((h) => hosts.add(h));
    }
  } catch (e) {
    // Vindioo draait niet: dan blijft staan wat we zelf onthouden hebben.
  }

  try {
    const bewaard = await chrome.storage.local.get("nodig");
    if (Array.isArray(bewaard.nodig)) bewaard.nodig.forEach((h) => hosts.add(h));
  } catch (e) {
    // Niets onthouden: geen bezwaar.
  }

  const uit = [];

  for (const host of [...hosts].sort()) {
    uit.push({ host, ok: await mag(`https://${host}/`) });
  }

  return uit;
}

// ---------- van Chrome naar Vindioo: rechtsklikken op een zoekertje ----------
//
// De enige weg die deze kant op gaat. Overal elders geeft de app werk aan ons; hier sturen wij
// iets dat zij niet gevraagd heeft - vandaar dat FavoriteFromUrl aan de andere kant nakijkt wat
// er binnenkomt voor er een pagina opgehaald wordt.
//
// Het contextmenu zelf heeft GEEN toestemming per site nodig: Chrome geeft ons het adres van de
// link waarop je klikte, zonder dat we in de pagina moeten kijken. De app haalt die pagina op,
// en of dat via de brug gaat (en dus via jouw toestemming voor die site) hangt af van het
// sitebestand.

const MENU_ID = "vindioo-favoriet";

function maakMenu() {
  // removeAll eerst: bij elke herstart van het achtergrondscript zou create anders klagen dat
  // het menu-item al bestaat, en dan staat er niets meer.
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: MENU_ID,
      title: "Zet in favorieten van Vindioo",
      contexts: ["link", "page"]
    }, () => {
      // Met een terugmelding, want zonder was dit niet na te gaan: staat het item er niet, dan
      // zie je in Chrome enkel dát het er niet staat en nergens waaróm. Op 2 oktober 2026 ging
      // daar een halve zoektocht in zitten.
      if (chrome.runtime.lastError) {
        console.warn("[brug] het contextmenu kon niet aangemaakt worden:",
                     chrome.runtime.lastError.message);
      } else {
        console.log("[brug] contextmenu klaar: rechtsklik op een zoekertje");
      }
    });
  });
}

chrome.runtime.onInstalled.addListener(maakMenu);
chrome.runtime.onStartup.addListener(maakMenu);

// En ook gewoon bij het laden van dit script. Die twee gebeurtenissen hierboven dekken niet
// alles: of een herlaad in chrome://extensions er een geeft, hangt af van Chrome, en het
// achtergrondscript wordt tussendoor afgesloten en weer gewekt. Dit kan geen kwaad, want
// removeAll gaat eraan vooraf - zonder dat zou create klagen dat het item al bestaat.
maakMenu();

/// Een melding van Chrome zelf. De popup staat niet open wanneer je rechtsklikt, dus zonder dit
/// zou je nooit weten of het gelukt is - en stil mislukken is bij zoiets het ergste.
function meld(tekst) {
  try {
    chrome.notifications.create({
      type: "basic",
      iconUrl: "icon128.png",
      title: "Vindioo",
      message: tekst
    });
  } catch (e) {
    // Geen melding kunnen tonen mag niets stukmaken.
    console.log("[brug]", tekst);
  }
}

chrome.contextMenus.onClicked.addListener(async (info) => {
  if (info.menuItemId !== MENU_ID) return;

  // De link waarop je klikte, en anders de pagina waar je op staat - zo werkt het ook op de
  // advertentiepagina zelf en niet enkel in een lijst.
  const adres = info.linkUrl || info.pageUrl || "";

  const bezwaar = waaromNiet(adres);

  if (bezwaar) {
    meld(`Dit adres gaat niet: ${bezwaar}.`);
    return;
  }

  const token = await getToken();

  if (!token) {
    meld("Vul eerst de koppelcode in: klik op het Vindioo-pictogram in Chrome.");
    return;
  }

  try {
    const uit = await vraagApp(token, "/favorite", { url: adres });

    if (!uit || uit.nietDeApp) meld("Er zit iets anders op poort 8731; dit is Vindioo niet.");
    else if (uit.error) meld("Vindioo weigerde de koppelcode. Kopieer ze opnieuw uit de app.");
    else meld(uit.melding || "Klaar.");
  } catch (e) {
    meld("Vindioo draait niet op deze computer.");
  }
});

// ---------- opstarten en wakker houden ----------

// Chrome mag dit achtergrondscript afsluiten wanneer het stil ligt.
// De wekker zet het elke minuut weer aan.
chrome.alarms.create("wakker", { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener(() => loop());

chrome.runtime.onStartup.addListener(() => loop());
chrome.runtime.onInstalled.addListener(() => loop());

// Voor de popup: hoe het met de verbinding staat, en een code controleren.
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message === "status") {
    getToken().then((token) => sendResponse({
      connected: Date.now() - lastContact < 5000,
      wrongCode: Date.now() - lastWrongCode < 5000,
      hasToken: !!token
    }));
    return true;
  }

  // Meteen na "Code opslaan": vraagt de app of deze code klopt, zonder een opdracht
  // aan te nemen. Zo zie je het verschil tussen "klopt", "klopt niet" en "Vindioo
  // draait niet" op het moment dat je de code plakt, en niet pas bij het zoeken.
  if (message && message.type === "check") {
    vraagApp(message.token, "/ping")
      .then((uit) => sendResponse({ result: uit && uit.ok && !uit.nietDeApp ? "ok" : "wrong" }))
      .catch(() => sendResponse({ result: "noapp" }));
    return true;
  }

  // Welke sites heeft de brug nodig, en welke mag ze al? De popup kan dat niet zelf aan de app
  // vragen - daarvoor zou ze de koppelcode moeten tekenen - dus halen wij het op. Toestemming
  // VRAGEN doet zij wel zelf: dat mag enkel uit een klik, en die hebben wij hier niet.
  if (message === "hosts") {
    hostLijst().then(sendResponse).catch(() => sendResponse([]));
    return true;
  }

  return false;
});

loop();

// Weigert de extensie een site waar ze geen toestemming voor heeft?
//
// Sinds 1 oktober 2026 vraagt de extensie bij het installeren geen toegang tot <all_urls> meer,
// enkel het recht om per site te vragen. Dit is de proef dat die toestemming ook écht iets
// tegenhoudt - en vooral: dat er geen tabblad opengaat. Dat laatste is het punt. Een tabblad
// openen stuurt al een verzoek mét jouw cookies; of we de pagina daarna mogen uitlezen, is dan
// te laat.
//
// We draaien de ECHTE handleJob() en mag() uit background.js, tegen een nagebootste Chrome.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const hier = dirname(fileURLToPath(import.meta.url));
const bron = readFileSync(join(hier, "..", "extension", "background.js"), "utf8");

const knip = (naam, patroon) => {
  const m = bron.match(patroon);
  if (!m) throw new Error("niet gevonden in background.js: " + naam);
  return m[0];
};

const stukken = [
  knip("de constanten", /const POLL_MS[\s\S]*?const DOM_POLL_MS = \d+;/),
  knip("sleep", /function sleep\(ms\) \{[\s\S]*?\n\}/),
  knip("getToken", /async function getToken\(\) \{[\s\S]*?\n\}/),
  knip("waaromNiet en de toestemmingen",
       /function waaromNiet\(adres\)[\s\S]*?(?=\nconsole\.log\("\[brug\] versie)/),
  knip("de opdrachtlus",
       /async function readHtml\(tabId\)[\s\S]*?\nasync function sendResult\(token, payload\) \{[\s\S]*?\n\}/),
  knip("het contextmenu",
       /const MENU_ID = [\s\S]*?chrome\.contextMenus\.onClicked\.addListener\([\s\S]*?\n\}\);/)
];

// ---------- de nagebootste Chrome ----------

const HTML = "<html><body>" + "x".repeat(9000) + "</body></html>";

let toegestaan = new Set();   // de patronen die "de gebruiker" heeft aangevinkt
let geopend = [];             // elk adres waarvoor een tabblad openging
let tabUrl = null;            // waar het tabblad nu staat
let onthouden = [];           // de lijst die de popup te zien krijgt
let geleverd = [];            // wat er naar de app ging

let bewaardeCode = "koppelcode-van-de-gebruiker";   // wat chrome.storage teruggeeft
let meldingen = [];                                 // wat Chrome aan de gebruiker toonde
let klikHandler = null;                             // de luisteraar van het contextmenu

const mijnChrome = {
  runtime: { onInstalled: { addListener: () => {} }, onStartup: { addListener: () => {} } },
  contextMenus: {
    removeAll: (klaar) => klaar(),
    create: () => {},
    onClicked: { addListener: (fn) => { klikHandler = fn; } }
  },
  notifications: { create: (opties) => meldingen.push(opties.message) },
  permissions: {
    contains: async ({ origins }) => origins.every((o) => toegestaan.has(o))
  },
  storage: {
    local: {
      get: async (key) => key === "nodig" ? { nodig: [...onthouden] } : { token: bewaardeCode },
      set: async (obj) => { if (obj.nodig) onthouden = obj.nodig; }
    }
  },
  tabs: {
    create: async ({ url }) => {
      geopend.push(url);
      // tabUrl is al gezet wanneer we een doorverwijzing nabootsen.
      if (tabUrl === null) tabUrl = url;
      return { id: 7 };
    },
    get: async () => ({ url: tabUrl }),
    remove: async () => {}
  },
  scripting: {
    executeScript: async ({ func }) => {
      // Chrome weigert een script in een pagina waar de extensie geen hostrecht voor heeft, en
      // dat is precies het geval dat we willen nabootsen.
      if (!toegestaan.has(new URL(tabUrl).origin + "/*")) {
        throw new Error("Cannot access contents of the page.");
      }

      const code = String(func);

      if (code.includes("readyState")) return [{ result: "complete" }];
      if (code.includes("outerHTML")) return [{ result: HTML }];
      if (code.includes("querySelectorAll")) return [{ result: 3 }];
      if (code.includes("querySelector")) return [{ result: true }];

      return [{ result: undefined }];
    }
  }
};

// De app nabootsen: we onthouden enkel wat de extensie aflevert.
let gevraagd = [];              // elk pad dat er naar de app ging
let antwoord = {};              // wat de nep-app terugzegt

const mijnVraagApp = async (token, pad, body = null) => {
  gevraagd.push({ pad, body });
  if (pad === "/result") geleverd.push(body);
  return antwoord;
};

const stil = { log: () => {}, warn: () => {} };

const laad = new Function("chrome", "vraagApp", "console",
  stukken.join("\n\n") + "\nreturn { handleJob, mag };");

const { handleJob, mag } = laad(mijnChrome, mijnVraagApp, stil);

// Het contextmenu hangt zijn luisteraar op bij het laden; die hebben we hierboven opgevangen.
const rechtsklik = async (info) => {
  gevraagd = [];
  meldingen = [];
  await klikHandler(info);
};

// ---------- de proeven ----------

let fouten = 0;
const dat = (ok, wat) => { if (!ok) fouten++; console.log((ok ? "OK   " : "FOUT ") + wat); };

const schoon = () => { geopend = []; geleverd = []; onthouden = []; tabUrl = null; };
const laatste = () => geleverd[geleverd.length - 1] || {};

const opdracht = (url, extra = {}) =>
  ({ id: "1", url, stream: false, itemTimeoutMs: 1, ...extra });

// 1. Een site die niet is aangevinkt: er gaat geen tabblad open.
schoon();
toegestaan = new Set();
await handleJob(opdracht("https://webmail.voorbeeld.be/inbox"), "code");

dat(geopend.length === 0, `zonder toestemming gaat er geen tabblad open (${geopend.length})`);
dat((laatste().error || "").includes("webmail.voorbeeld.be"),
    `de app krijgt te horen welke site het is ("${laatste().error}")`);
dat((laatste().error || "").includes("Zentrix-pictogram"),
    "en wat je eraan doet");
dat(laatste().html === undefined, "er komt zeker geen pagina mee");
dat(onthouden.includes("webmail.voorbeeld.be"),
    `de host staat onthouden voor de popup (${JSON.stringify(onthouden)})`);

// 2. Dezelfde opdracht met toestemming: nu werkt het gewoon.
schoon();
toegestaan = new Set(["https://www.voorbeeld.be/*"]);
await handleJob(opdracht("https://www.voorbeeld.be/q/cd/"), "code");

dat(geopend.length === 1 && geopend[0] === "https://www.voorbeeld.be/q/cd/",
    `met toestemming gaat het tabblad wel open (${JSON.stringify(geopend)})`);
dat(laatste().html === HTML, "en de pagina komt bij de app");
dat(laatste().error === undefined, "zonder foutmelding");

// 3. Eén site aangevinkt geeft geen toegang tot een ándere site.
schoon();
toegestaan = new Set(["https://www.voorbeeld.be/*"]);
await handleJob(opdracht("https://www.iets-anders.be/q/cd/"), "code");

dat(geopend.length === 0, "toestemming voor één site is geen toestemming voor de volgende");

// 4. Een API-opdracht (rawText) gaat door dezelfde deur.
schoon();
toegestaan = new Set();
await handleJob(opdracht("https://api.voorbeeld.be/zoek?q=cd", { rawText: true }), "code");

dat(geopend.length === 0, "een API-opdracht ook niet");
dat((laatste().error || "").includes("api.voorbeeld.be"), "met de host in de melding");

// 5. Een site die doorverwijst naar een naam die niet is aangevinkt. Dit is het geval waarin
//    Chrome ons wél een tabblad geeft, maar het uitlezen weigert. Dan hoort de extensie dat
//    meteen te zeggen in plaats van 45 seconden te blijven proberen.
schoon();
toegestaan = new Set(["https://voorbeeld.be/*"]);
tabUrl = "https://www.heel-iets-anders.be/elders";   // daar komen we terecht

const t0 = Date.now();
await handleJob(opdracht("https://voorbeeld.be/q/cd/"), "code");
const duur = Date.now() - t0;

dat((laatste().error || "").includes("www.heel-iets-anders.be"),
    `een doorverwijzing naar een andere naam wordt gemeld ("${laatste().error}")`);
dat(laatste().html === undefined, "en de pagina komt niet bij de app");
dat(duur < 5000, `en dat binnen de seconden, niet na de 45 van waitForDom (${duur} ms)`);
dat(onthouden.includes("www.heel-iets-anders.be"),
    "de echte host staat onthouden, zodat de popup hem kan aanbieden");

// 6. mag() zelf: een pad doet niet mee (Chrome negeert dat bij een hostrecht), een andere
//    poort of een ander schema wel.
toegestaan = new Set(["https://www.voorbeeld.be/*"]);

dat(await mag("https://www.voorbeeld.be/wat/dan/ook?x=1"), "mag(): het pad doet niet mee");
dat(!(await mag("https://ander.voorbeeld.be/")), "mag(): een andere naam niet");
dat(!(await mag("http://www.voorbeeld.be/")), "mag(): http is niet https");
dat(!(await mag("niet-eens-een-adres")), "mag(): onleesbaar adres is nee");

// ---------- rechtsklikken op een zoekertje ----------
//
// De enige weg die van Chrome naar de app loopt. Wat de extensie doorgeeft, gaat de app ZELF
// ophalen - dus wat niet deugt, hoort hier al te stranden en niet pas aan de overkant.

antwoord = { ok: true, melding: "Bij je favorieten gezet: Lot 229" };

await rechtsklik({ menuItemId: "zentrix-favoriet", linkUrl: "https://www.voorbeeld.be/kavel/229" });

dat(gevraagd.length === 1 && gevraagd[0].pad === "/favorite" &&
    gevraagd[0].body.url === "https://www.voorbeeld.be/kavel/229",
    `een gewone link gaat naar de app (${JSON.stringify(gevraagd)})`);

dat(meldingen.length === 1 && meldingen[0].includes("Lot 229"),
    `en je krijgt te zien wat er gebeurde (${meldingen[0]})`);

// Geen link maar de pagina zelf: dan werkt het ook op de advertentiepagina.
await rechtsklik({ menuItemId: "zentrix-favoriet", pageUrl: "https://www.voorbeeld.be/kavel/300" });

dat(gevraagd.length === 1 && gevraagd[0].body.url === "https://www.voorbeeld.be/kavel/300",
    "zonder link wordt het adres van de pagina genomen");

// En nu wat er NIET mag vertrekken. Elk van deze drie zou de app een verzoek laten doen dat ze
// uit zichzelf nooit zou doen.
for (const [adres, waarom] of [
  ["http://www.voorbeeld.be/kavel/1", "http in plaats van https"],
  ["https://192.168.1.1/beheer", "een adres op je eigen netwerk"],
  ["javascript:alert(1)", "geen webadres"]
]) {
  await rechtsklik({ menuItemId: "zentrix-favoriet", linkUrl: adres });

  dat(gevraagd.length === 0 && meldingen.length === 1,
      `${waarom}: er gaat niets naar de app, wel een melding (${meldingen[0] ?? "geen"})`);
}

// Nog geen koppelcode: dan zegt de extensie wat je moet doen in plaats van stil te vallen.
bewaardeCode = "";
await rechtsklik({ menuItemId: "zentrix-favoriet", linkUrl: "https://www.voorbeeld.be/kavel/229" });

dat(gevraagd.length === 0 && (meldingen[0] ?? "").includes("koppelcode"),
    `zonder koppelcode: geen verzoek, wel uitleg (${meldingen[0] ?? "geen"})`);

bewaardeCode = "koppelcode-van-de-gebruiker";

// Iets anders op poort 8731: dan voeren we er niets van uit en zeggen we dat.
antwoord = { nietDeApp: true };
await rechtsklik({ menuItemId: "zentrix-favoriet", linkUrl: "https://www.voorbeeld.be/kavel/229" });

dat((meldingen[0] ?? "").includes("Zentrix niet"),
    `een vreemd programma op de poort wordt gemeld (${meldingen[0] ?? "geen"})`);

// Een ander menu-item (van een andere extensie) mag ons niet laten lopen.
antwoord = { ok: true, melding: "zou niet mogen" };
await rechtsklik({ menuItemId: "iets-anders", linkUrl: "https://www.voorbeeld.be/kavel/229" });

dat(gevraagd.length === 0 && meldingen.length === 0, "een ander menu-item doet niets");

console.log();
console.log(fouten === 0 ? "alles OK" : fouten + " FOUT");

process.exitCode = fouten === 0 ? 0 : 1;

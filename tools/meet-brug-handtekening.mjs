// Weigert de extensie een antwoord dat niet van de echte app komt?
//
// Dat is de helft die HAAR beschermt: zij voert uit wat uit /job komt, met jouw cookies. Een
// programma dat poort 8731 eerst bezet, kan vanaf nu wel antwoorden - maar het kan niet tekenen,
// want het kent de koppelcode niet.
//
// We draaien hier de ECHTE vraagApp() en teken() uit background.js, tegen een nep-app op een
// vrije poort.
import { createServer } from "node:http";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import crypto from "node:crypto";

// Ten opzichte van dit script, zodat het overal werkt en er geen pad van iemand in staat.
const hier = dirname(fileURLToPath(import.meta.url));
const bron = readFileSync(join(hier, "..", "extension", "background.js"), "utf8");

// De stukken die we nodig hebben, letterlijk uit het bestand knippen.
const knip = (naam, patroon) => {
  const m = bron.match(patroon);
  if (!m) throw new Error("niet gevonden in background.js: " + naam);
  return m[0];
};

const stukken = [
  knip("SIG_KOP", /const SIG_KOP = .*?;/s),
  knip("VOOR_KOP", /const VOOR_KOP = .*?;/s),
  knip("teken", /async function teken\(code, data\) \{[\s\S]*?\n\}/),
  knip("nonce", /function nonce\(\) \{[\s\S]*?\n\}/),
  knip("vraagApp", /async function vraagApp\(code, pad, body = null\) \{[\s\S]*?\n\}/)
];

const CODE = "koppelcode-van-de-gebruiker";
let poort;

// De nep-app. Met "eerlijk" tekent ze zoals Vindioo; anders doet ze maar wat - zoals een
// programma dat de poort bezet houdt.
let eerlijk = true;

const server = createServer((req, res) => {
  const n = new URL(req.url, "http://x").searchParams.get("n");
  const body = JSON.stringify({ id: "1", url: "https://www.voorbeeld.be/q/cd/" });

  const sig = eerlijk
    ? crypto.createHmac("sha256", CODE).update(n + "\n" + body).digest("hex")
    : "0".repeat(64);

  res.setHeader("Content-Type", "application/json");
  res.setHeader("X-Vindioo-Sig", sig);
  res.end(body);
});

await new Promise((klaar) => server.listen(0, "127.0.0.1", klaar));
poort = server.address().port;

// vraagApp laden met APP_URL naar onze nep-app.
const APP_URL = `http://127.0.0.1:${poort}`;
const BRUG_KOP = { "X-Vindioo-Brug": "1" };

const laad = new Function("APP_URL", "BRUG_KOP", "crypto", "fetch", "TextEncoder",
  stukken.join("\n\n") + "\nreturn { vraagApp, teken };");

const { vraagApp, teken } = laad(APP_URL, BRUG_KOP, globalThis.crypto, fetch, TextEncoder);

let fouten = 0;
const dat = (ok, wat) => { if (!ok) fouten++; console.log((ok ? "OK   " : "FOUT ") + wat); };

// 1. De echte app: de opdracht komt gewoon door.
eerlijk = true;
let uit = await vraagApp(CODE, "/job");
dat(uit.url === "https://www.voorbeeld.be/q/cd/" && !uit.nietDeApp,
    `de echte app: de opdracht komt door (${uit.url ?? uit.nietDeApp})`);

// 2. Iets anders op die poort: geen opdracht, dus er gaat geen tabblad open.
eerlijk = false;
uit = await vraagApp(CODE, "/job");
dat(uit.nietDeApp === true && uit.url === undefined,
    `een programma dat de poort bezet: geweigerd (${JSON.stringify(uit)})`);

// 3. En met de verkeerde koppelcode klopt de handtekening van de echte app ook niet.
eerlijk = true;
uit = await vraagApp("een-andere-code", "/job");
dat(uit.nietDeApp === true, `met een verkeerde koppelcode: geweigerd (${JSON.stringify(uit)})`);

// 4. De koppelcode staat nergens in wat er verstuurd wordt.
let gezien = "";
const kijker = createServer((req, res) => {
  gezien += req.url + "\n" + JSON.stringify(req.headers) + "\n";
  res.setHeader("X-Vindioo-Sig", "0".repeat(64));
  res.end("{}");
});
await new Promise((klaar) => kijker.listen(0, "127.0.0.1", klaar));

const laad2 = new Function("APP_URL", "BRUG_KOP", "crypto", "fetch", "TextEncoder",
  stukken.join("\n\n") + "\nreturn { vraagApp };");
const { vraagApp: vraagApp2 } = laad2(`http://127.0.0.1:${kijker.address().port}`,
  BRUG_KOP, globalThis.crypto, fetch, TextEncoder);

await vraagApp2(CODE, "/job");
await vraagApp2(CODE, "/result", { id: "1", html: "<html></html>" });

dat(!gezien.includes(CODE), "de koppelcode staat in geen enkel adres of kopregel");

console.log();
console.log(fouten === 0 ? "alles OK" : fouten + " FOUT");

// Netjes sluiten en pas dan de uitkomst zetten: process.exit() midden in het sluiten laat
// Node struikelen, en dan lijkt een geslaagde proef mislukt.
await new Promise((klaar) => server.close(klaar));
await new Promise((klaar) => kijker.close(klaar));

process.exitCode = fouten === 0 ? 0 : 1;

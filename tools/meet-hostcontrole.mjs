// Houdt de hostcontrole van de extensie dezelfde omwegen tegen als die van de app?
//
// Twee omwegen, allebei gemeten op 18 oktober 2026 en allebei toen werkend:
//
//   een punt op het eind   "localhost." en "127.0.0.1." zijn in DNS dezelfde adressen - de
//                          punt zegt alleen dat de naam absoluut is - maar met die punt past
//                          geen enkel achtervoegsel meer en matcht het IPv4-patroon niet.
//
//   IPv4 in een IPv6-jasje "::ffff:10.0.0.5" is gewoon 10.0.0.5, maar de IPv6-regels kennen
//                          enkel ::1, fc/fd en fe80 en zien daar niets in.
//
// De app heeft dezelfde twee gaten gehad (Services/SiteUrlCheck.cs); daar staan ze in
// ImportChecks. Deze proef is de tegenhanger voor de extensie, en draait de ECHTE waaromNiet()
// uit background.js - geen kopie, want een kopie loopt uit elkaar.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const hier = dirname(fileURLToPath(import.meta.url));
const bron = readFileSync(join(hier, "..", "extension", "background.js"), "utf8");

const m = bron.match(/function waaromNiet\(adres\) \{[\s\S]*?\n\}/);
if (!m) throw new Error("waaromNiet() niet gevonden in background.js");

const waaromNiet = new Function(`${m[0]}; return waaromNiet;`)();

// [adres, moet geweigerd worden?]
const proeven = [
  ["https://www.2dehands.be/q/cd/", false],
  ["https://www.2dehands.be./q/cd/", false],   // punt op het eind, maar een echte site

  ["https://127.0.0.1/x", true],
  ["https://localhost/x", true],
  ["https://10.0.0.5/x", true],
  ["https://192.168.1.1/x", true],
  ["https://router/x", true],
  ["https://nas.local/x", true],
  ["http://www.2dehands.be/x", true],          // geen https

  ["https://127.0.0.1./x", true],              // de punt
  ["https://localhost./x", true],
  ["https://10.0.0.5../x", true],

  ["https://[::1]/x", true],
  ["https://[::ffff:127.0.0.1]/x", true],      // IPv4 in een IPv6-jasje
  ["https://[::ffff:10.0.0.5]/x", true],
  ["https://[fe80::1]/x", true],
];

let fout = 0;

for (const [adres, moetWeg] of proeven) {
  const bezwaar = waaromNiet(adres);
  const geweigerd = bezwaar !== null;
  const ok = geweigerd === moetWeg;

  if (!ok) fout++;

  console.log(`  ${ok ? "OK  " : "FOUT"} ${adres.padEnd(34)} ` +
              `${geweigerd ? "geweigerd" : "toegelaten"}${bezwaar ? " - " + bezwaar : ""}`);
}

console.log(fout === 0
  ? `\nALLES OK (${proeven.length} adressen)`
  : `\n${fout} FOUT(EN) op ${proeven.length} adressen`);

process.exit(fout === 0 ? 0 : 1);

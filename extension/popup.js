const tokenInput = document.getElementById("token");
const saveButton = document.getElementById("save");
const savedNote = document.getElementById("saved");
const dot = document.getElementById("dot");
const statusText = document.getElementById("statusText");
const hostList = document.getElementById("hostList");
const grantButton = document.getElementById("grant");
const uitleg = document.getElementById("uitleg");

// Bewaarde code tonen.
chrome.storage.local.get("token", (stored) => {
  if (stored.token) tokenInput.value = stored.token;
});

// Opslaan en meteen controleren. Een verkeerde code viel vroeger pas op bij het
// zoeken, en dan zei de app enkel dat de extensie zich niet meldde.
saveButton.addEventListener("click", () => {
  const token = tokenInput.value.trim();

  chrome.storage.local.set({ token }, () => {
    chrome.runtime.sendMessage({ type: "check", token }, (response) => {
      const result = response && response.result;

      savedNote.textContent =
        result === "ok" ? "Opgeslagen. De code klopt."
        : result === "wrong" ? "Opgeslagen, maar deze code klopt niet. Kopieer ze opnieuw in Vindioo: tandwiel > Koppelcode."
        : "Opgeslagen. Vindioo draait nu niet, dus de code is nog niet gecontroleerd.";

      savedNote.className = result === "ok" ? "saved" : "saved warn";
      savedNote.style.display = "block";

      if (result === "ok") setTimeout(() => (savedNote.style.display = "none"), 4000);
      refresh();

      // Nu de code klopt, kan de app zeggen welke sites ze via de brug zoekt.
      toonHosts();
    });
  });
});

// ---------- toestemming per site ----------
//
// De extensie vraagt bij het installeren geen toegang tot alle sites meer, enkel het recht om
// per site te vragen (optional_host_permissions in manifest.json). Hier geef je ze.
//
// Deze knop MOET hier staan en niet in het achtergrondscript: Chrome aanvaardt
// chrome.permissions.request() enkel uit een gebruikersklik. Een achtergrondscript heeft die
// nooit, dus daar is toestemming vragen onmogelijk.

let ontbreekt = [];

function toonHosts() {
  chrome.runtime.sendMessage("hosts", (lijst) => {
    const sites = Array.isArray(lijst) ? lijst : [];

    ontbreekt = sites.filter((s) => !s.ok).map((s) => s.host);
    hostList.textContent = "";

    if (sites.length === 0) {
      const leeg = document.createElement("li");
      leeg.className = "dim";
      leeg.append(teken("·"), tekst("Nog geen sites: Vindioo zegt bij het zoeken welke ze nodig heeft."));
      hostList.append(leeg);
    }

    for (const site of sites) {
      const regel = document.createElement("li");
      regel.className = site.ok ? "yes" : "no";
      regel.append(teken(site.ok ? "✓" : "–"), tekst(site.host));
      hostList.append(regel);
    }

    grantButton.style.display = ontbreekt.length > 0 ? "block" : "none";
    grantButton.textContent =
      ontbreekt.length === 1 ? `Geef toegang tot ${ontbreekt[0]}`
      : `Geef toegang tot deze ${ontbreekt.length} sites`;

    uitleg.textContent = ontbreekt.length > 0
      ? "Zonder toegang opent de brug deze sites niet. Ze kan ook niets anders openen."
      : "";
  });
}

function teken(wat) {
  const span = document.createElement("span");
  span.className = "mark";
  span.textContent = wat;
  return span;
}

function tekst(wat) {
  const span = document.createElement("span");
  span.textContent = wat;
  return span;
}

grantButton.addEventListener("click", () => {
  if (ontbreekt.length === 0) return;

  // Chrome toont nu zijn eigen venster met de sites erin. Deze popup gaat daarbij dicht, dus de
  // terugmelding komt er vaak niet aan - de toestemming wordt wél gegeven. Open de popup
  // opnieuw en je ziet de vinkjes staan.
  chrome.permissions.request(
    { origins: ontbreekt.map((host) => `https://${host}/*`) },
    () => toonHosts()
  );
});

// Ergens anders toegestaan of ingetrokken (chrome://extensions): meteen bijwerken.
chrome.permissions.onAdded.addListener(toonHosts);
chrome.permissions.onRemoved.addListener(toonHosts);

// Status elke seconde bijwerken. Vier toestanden, want elk vraagt iets anders.
function refresh() {
  chrome.runtime.sendMessage("status", (response) => {
    const s = response || {};

    let tekst;
    let klasse = "";

    if (!s.hasToken) {
      tekst = "Nog geen koppelcode ingevuld.";
    } else if (s.connected) {
      tekst = "Verbonden met Vindioo";
      klasse = "on";
    } else if (s.wrongCode) {
      tekst = "Verkeerde koppelcode. Kopieer ze opnieuw in Vindioo en plak ze hier.";
      klasse = "warn";
    } else {
      tekst = "Vindioo draait niet op deze computer.";
    }

    dot.className = klasse ? `dot ${klasse}` : "dot";
    statusText.textContent = tekst;
  });
}

refresh();
setInterval(refresh, 1000);

toonHosts();

// Trager dan de status: hier zit een vraag aan de app in, en de lijst verandert zelden.
setInterval(toonHosts, 5000);

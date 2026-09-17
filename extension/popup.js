const tokenInput = document.getElementById("token");
const saveButton = document.getElementById("save");
const savedNote = document.getElementById("saved");
const dot = document.getElementById("dot");
const statusText = document.getElementById("statusText");

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
        : result === "wrong" ? "Opgeslagen, maar deze code klopt niet. Kopieer ze opnieuw in Zentrix: tandwiel > Koppelcode."
        : "Opgeslagen. Zentrix draait nu niet, dus de code is nog niet gecontroleerd.";

      savedNote.className = result === "ok" ? "saved" : "saved warn";
      savedNote.style.display = "block";

      if (result === "ok") setTimeout(() => (savedNote.style.display = "none"), 4000);
      refresh();
    });
  });
});

// Status elke seconde bijwerken. Vier toestanden, want elk vraagt iets anders.
function refresh() {
  chrome.runtime.sendMessage("status", (response) => {
    const s = response || {};

    let tekst;
    let klasse = "";

    if (!s.hasToken) {
      tekst = "Nog geen koppelcode ingevuld.";
    } else if (s.connected) {
      tekst = "Verbonden met Zentrix";
      klasse = "on";
    } else if (s.wrongCode) {
      tekst = "Verkeerde koppelcode. Kopieer ze opnieuw in Zentrix en plak ze hier.";
      klasse = "warn";
    } else {
      tekst = "Zentrix draait niet op deze computer.";
    }

    dot.className = klasse ? `dot ${klasse}` : "dot";
    statusText.textContent = tekst;
  });
}

refresh();
setInterval(refresh, 1000);

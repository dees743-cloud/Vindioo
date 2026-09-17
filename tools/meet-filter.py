# -*- coding: utf-8 -*-
"""Meet of een filterparameter op een site echt iets doet.

Bouwt dezelfde zoek-URL als de app (via het sitebestand), hangt er de parameters
aan die je opgeeft, en kijkt naar TWEE dingen:

  - het totale aantal treffers
  - hoeveel van de resultaten dezelfde blijven als zonder filter

Dat tweede is er niet voor de sier. Bij 2dehands verandert `totalResultCount`
NOOIT mee met een attribuutfilter: dat veld telt de zoekterm, niet de filter.
Wie alleen naar het totaal kijkt, besluit daar dat niets werkt — en dat is precies
wat er gebeurde. Omgekeerd gaf `attributeRanges[]=condition:32` daar wel een ander
totaal, maar dezelfde 4618 voor elke waarde: de API verslikte zich in een kapot
bereik. Een veranderd getal is dus geen bewijs.

De overlap liegt niet. Zakt die van 100 naar 12, dan filtert het echt; blijft ze
op 99, dan gebeurt er niets, wat het totaal ook zegt.

Er gaat ook altijd een onzin-parameter mee als CONTROLE. Verandert die het beeld
ook, dan meet je iets anders dan je denkt.

Met alles samen zie je of meerdere waarden als OF of als EN werken: tellen de
aantallen op, dan is het een OF; wordt het kleiner dan elk apart, dan is het een EN.

Gebruik:
    python tools/meet-filter.py 2dehands fiets "condition=used"
    python tools/meet-filter.py autoscout24 porsche "fuel=D" "gear=A"
    python tools/meet-filter.py autoscout24 porsche "fuel=B,D"

Werkt alleen voor sites die rechtstreeks bereikbaar zijn. Sites met
NeedsBrowser of UseBridge (eBay, Catawiki, leboncoin, Facebook) weigeren een
gewoon verzoek; die meet je door de filter op de site zelf aan te klikken en te
kijken wat er in de adresbalk verandert.
"""
import io
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request
import gzip

# De sitesmap van de app. Sinds die Zentrix heet staat ze in %APPDATA%\Zentrix; wie
# de app sinds het hernoemen nog niet opstartte, heeft enkel de oude map.
MAP = next((m for m in (os.path.expandvars(r"%APPDATA%\Zentrix\sites"),
                         os.path.expandvars(r"%APPDATA%\Zoekhulp\sites")) if os.path.isdir(m)),
           os.path.expandvars(r"%APPDATA%\Zentrix\sites"))

def is_json(d):
    """Is dit een JSON-bron? Kind mag 1 zijn of de naam "Json".

    Sinds SiteStore met een JsonStringEnumConverter schrijft, staat er "Json" in
    plaats van 1. Wie alleen op het nummer keek, hield elke JSON-site voor HTML -
    en meldde dan dat hij hem niet kon controleren.
    """
    k = d.get("Kind")
    return k == 1 or (isinstance(k, str) and k.lower() == "json")



UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36")


def lees_site(sleutel):
    pad = os.path.join(MAP, sleutel + ".json")
    if not os.path.exists(pad):
        namen = sorted(os.path.splitext(f)[0] for f in os.listdir(MAP) if f.endswith(".json"))
        sys.exit(f"onbekende site '{sleutel}'. Beschikbaar: {', '.join(namen)}")
    return json.load(io.open(pad, encoding="utf-8"))


def bouw_url(d, zoekwoord):
    """Zelfde opbouw als SearchUrlBuilder.BuildStandard."""
    sjabloon = d["SearchUrlTemplate"]

    # Staat {query} in het pad, dan blijft een schuine streep een padscheiding.
    plek, vraag = sjabloon.find("{query}"), sjabloon.find("?")
    in_pad = plek >= 0 and (vraag < 0 or plek < vraag)

    if in_pad:
        waarde = "/".join(urllib.parse.quote(deel, safe="") for deel in zoekwoord.split("/"))
    else:
        waarde = urllib.parse.quote(zoekwoord, safe="")

    url = sjabloon.replace("{query}", waarde)

    # Enkel de vaste fragmenten (sortering en dergelijke); filterwaarden laten we
    # met opzet weg, want die willen we hier juist zelf sturen.
    for _, fragment in (d.get("Filters") or {}).items():
        if fragment and "{value}" not in fragment:
            url += fragment

    return url


def haal(url):
    verzoek = urllib.request.Request(url, headers={
        "User-Agent": UA,
        "Accept-Language": "nl-BE,nl;q=0.9",
        "Accept": "text/html,application/json",
    })
    antwoord = urllib.request.urlopen(verzoek, timeout=40)
    ruw = antwoord.read()

    if antwoord.headers.get("Content-Encoding") == "gzip":
        ruw = gzip.decompress(ruw)

    return ruw.decode("utf-8", "replace")


# Velden waarin sites het TOTALE aantal treffers zetten. Dat is wat we willen
# weten: het aantal op de pagina staat vast (20, of de limiet die we vragen) en
# verandert dus niet als een filter werkt.
TOTAALVELDEN = ("totalResultCount", "numberOfResults", "totalCount", "total",
                "resultCount", "nbResults", "total_all")


def zoek_totaal(obj, diep=0):
    """Zoekt een van de bekende totaalvelden, hooguit een paar niveaus diep."""
    if diep > 4 or not isinstance(obj, dict):
        return None

    for veld in TOTAALVELDEN:
        waarde = obj.get(veld)
        if isinstance(waarde, int):
            return waarde

    for waarde in obj.values():
        if isinstance(waarde, dict):
            gevonden = zoek_totaal(waarde, diep + 1)
            if gevonden is not None:
                return gevonden

    return None


def vingerafdrukken(d, inhoud):
    """Een herkenbaar spoor per resultaat, om twee ladingen te vergelijken."""
    if is_json(d):
        try:
            data = json.loads(inhoud)
            for deel in d["ItemSelector"].split("."):
                data = data[deel]
            return [json.dumps(x, sort_keys=True)[:160] for x in data]
        except Exception:
            return []

    # HTML: het id of de guid van elk resultaat is het meest kenmerkend.
    sporen = re.findall(r'<article[^>]*\bid="([^"]+)"', inhoud)
    if sporen:
        return sporen

    return re.findall(r'href="(/[^"]{20,90})"', inhoud)


def meet(d, url):
    """Het totaal en de vingerafdrukken van deze lading."""
    try:
        inhoud = haal(url)
    except Exception as fout:
        return f"{type(fout).__name__}", []

    return tel_uit(d, inhoud), vingerafdrukken(d, inhoud)


def tel_uit(d, inhoud):
    """Het totale aantal treffers, of anders het aantal op deze pagina."""

    if is_json(d):                               # JSON
        try:
            data = json.loads(inhoud)

            totaal = zoek_totaal(data)
            if totaal is not None:
                return totaal

            for deel in d["ItemSelector"].split("."):
                data = data[deel]
            return len(data)
        except Exception:
            return "onleesbaar"

    # Bij een HTML-pagina van een Next.js-site staat het totaal in het JSON-blok.
    blok = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', inhoud, re.S)
    if blok:
        try:
            totaal = zoek_totaal(json.loads(blok.group(1)))
            if totaal is not None:
                return totaal
        except Exception:
            pass

    # HTML: het aantal keer dat de ItemSelector voorkomt. We halen er het meest
    # kenmerkende stuk uit, want een volledige CSS-selector kunnen we hier niet
    # uitvoeren.
    selector = d["ItemSelector"]

    attribuut = re.search(r"\[([\w-]+)\s*=\s*['\"]?([^'\"\]]+)", selector)
    if attribuut:
        merk = f'{attribuut.group(1)}="{attribuut.group(2)}"'
    else:
        klasse = re.search(r"\.([\w-]+)", selector)
        merk = klasse.group(1) if klasse else selector

    return len(re.findall(re.escape(merk), inhoud))


def hoofd():
    if len(sys.argv) < 3:
        sys.exit(__doc__)

    sleutel, zoekwoord, parameters = sys.argv[1], sys.argv[2], sys.argv[3:]

    d = lees_site(sleutel)

    if d.get("NeedsBrowser") or d.get("UseBridge"):
        print(f"LET OP: {d['Name']} heeft een browser of de brug nodig. Een gewoon "
              f"verzoek wordt geweigerd; meet deze site door de filter op de site "
              f"zelf aan te klikken en de adresbalk te lezen.\n")

    basis = bouw_url(d, zoekwoord)
    print(f"site      : {d['Name']}")
    print(f"zoek-URL  : {basis}\n")

    nul, nul_sporen = meet(d, basis)

    print(f"  {'':38}  {'totaal':>9}  {'zelfde resultaten':>18}")
    print(f"  {'zonder filter':38}  {nul!s:>9}  {len(nul_sporen):>9} van {len(nul_sporen)}")

    def oordeel(aantal, sporen):
        """Filtert dit echt?

        Twee aanwijzingen, en de overlap weegt het zwaarst. Een totaal kan
        gelijk blijven terwijl er wel gefilterd wordt (2dehands telt daar de
        zoekterm, niet de filter), en het kan veranderen zonder dat er iets
        gefilterd wordt (een kapot bereik dat de API laat struikelen)."""
        gedeeld = len(set(sporen) & set(nul_sporen))

        if nul_sporen and gedeeld <= len(nul_sporen) * 0.9:
            return f"   <== FILTERT ({gedeeld}/{len(nul_sporen)} zelfde)"

        if isinstance(aantal, int) and isinstance(nul, int) and abs(aantal - nul) > max(5, nul * 0.005):
            return "   <== ander totaal, maar dezelfde resultaten: niet vertrouwen"

        return "   (geen verschil)"

    for p in parameters:
        time.sleep(0.4)
        aantal, sporen = meet(d, basis + "&" + p)
        gedeeld = len(set(sporen) & set(nul_sporen))
        print(f"  {p:38}  {aantal!s:>9}  {gedeeld:>9} van {len(nul_sporen)}{oordeel(aantal, sporen)}")

    if len(parameters) > 1:
        time.sleep(0.4)
        samen = basis + "".join("&" + p for p in parameters)
        aantal, sporen = meet(d, samen)
        gedeeld = len(set(sporen) & set(nul_sporen))
        print(f"  {'alles samen':38}  {aantal!s:>9}  {gedeeld:>9} van {len(nul_sporen)}")

    time.sleep(0.4)
    aantal, sporen = meet(d, basis + "&ditbestaatniet=7")
    gedeeld = len(set(sporen) & set(nul_sporen))

    stabiel_totaal = (not isinstance(aantal, int) or not isinstance(nul, int)
                      or abs(aantal - nul) <= max(5, nul * 0.005))
    stabiele_volgorde = gedeeld > len(nul_sporen) * 0.9 if nul_sporen else True

    if stabiel_totaal and stabiele_volgorde:
        oordeel_controle = "   goed"
    elif stabiel_totaal:
        # De site geeft bij elk verzoek een andere volgorde of selectie terug.
        # 2dehands doet dat. De overlap zegt dan niets meer; het totaal wel.
        oordeel_controle = "   LET OP: de volgorde wisselt per verzoek — kijk hier naar het totaal, niet naar de overlap"
    else:
        oordeel_controle = "   LET OP: de controle verandert het totaal ook — de meting zegt niets"

    print(f"  {'CONTROLE (onbestaande parameter)':38}  {aantal!s:>9}  {gedeeld:>9} van {len(nul_sporen)}"
          + oordeel_controle)


if __name__ == "__main__":
    hoofd()

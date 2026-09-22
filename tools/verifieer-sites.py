# -*- coding: utf-8 -*-
"""
Bouwt per site dezelfde zoek-URL als SearchUrlBuilder en haalt hem op.
Zo controleren we de sitebestanden end-to-end, zonder de app te moeten bedienen.
Alleen sites die rechtstreeks bereikbaar zijn (geen browser, geen brug).
"""
import io, json, os, glob, base64, urllib.parse, urllib.request

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
      "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36")

# Dezelfde filterwaarden als een gebruiker zou invullen.
F = dict(PriceMin=100, PriceMax=200, Postcode="1000", RadiusKm=25)

def value_for(key):
    k = key.strip().lower()
    if k == "pricemin":  return str(F["PriceMin"])
    if k == "pricemax":  return str(F["PriceMax"])
    if k in ("postcode", "location"): return F["Postcode"]
    if k == "radius":    return str(F["RadiusKm"])
    if k == "radiusmeters": return str(F["RadiusKm"] * 1000)
    if k == "pricerangecents": return "%d:%d" % (F["PriceMin"]*100, F["PriceMax"]*100)
    if k == "pricerangeeuro":  return "%d-%d" % (F["PriceMin"], F["PriceMax"])
    return ""

def build(d, query, page=1):
    if d.get("UrlStyle") in (1, "Base64Json"):      # JSON in een base64-parameter
        # Zelfde als BuildBase64Json in de app: de JSON staat in de zoek-URL na "={".
        # Zonder gekozen filters: {filters} krijgt enkel wat een filter wil zien als er
        # niets gekozen is (EmptyFragment), met een komma ervoor zoals in de app.
        t = d["SearchUrlTemplate"]
        i = t.find("={")
        if i >= 0:
            leeg = [c["EmptyFragment"] for c in d.get("CustomFilters") or [] if c.get("EmptyFragment")]
            blok = (t[i + 1:].replace("{query}", json.dumps(query)[1:-1])
                             .replace("{page}", str(d.get("FirstPage", 1) + page - 1))
                             .replace("{filters}", ("," + ",".join(leeg)) if leeg else ""))
            enc = base64.b64encode(blok.encode("utf-8")).decode()
            return t[:i + 1] + urllib.parse.quote(enc, safe="")
    url = d["SearchUrlTemplate"].replace("{query}", urllib.parse.quote(query, safe=""))
    # Hoort de filter in een blok ({filters}), dan kan je hem er niet achteraan
    # plakken: dat maakt de URL stuk. We tonen die sites dus zonder filters.
    in_blok = "{filters}" in url
    for key, frag in (d.get("Filters") or {}).items():
        if in_blok and "{value}" in frag: continue
        if not frag: continue
        if "{value}" not in frag:
            url += frag                              # vast fragment (bv. sortering)
            continue
        v = value_for(key)
        if not v: continue
        url += frag.replace("{value}", urllib.parse.quote(v, safe=""))
    if page > 1 and d.get("PageTemplate"):
        url += d["PageTemplate"].replace("{page}", str(d.get("FirstPage", 1) + page - 1))
    # {page} in de zoek-URL zelf, en {offset}: vanaf het hoeveelste zoekertje een pagina
    # begint (2dehands, Marktplaats), zoals SearchUrlBuilder.Offset in de app.
    url = (url.replace("{page}", str(d.get("FirstPage", 1) + page - 1))
              .replace("{offset}", str((page - 1) * (d.get("PageSize") or 0))))
    # Sites die hun filters in een blok zetten ({filters}); hier zonder filters.
    return url.replace('{filters}', '')

def walk(obj, path):
    """Zelfde logica als TryWalk/Read in GenericSource."""
    cur = obj
    for part in [p for p in path.split(".") if p]:
        if isinstance(cur, list):
            if not cur: return None
            cur = cur[0]
        if not isinstance(cur, dict) or part not in cur: return None
        cur = cur[part]
    if isinstance(cur, list):
        return cur[0] if cur else None
    return cur

for f in sorted(glob.glob(os.path.join(MAP, "*.json"))):
    d = json.load(io.open(f, encoding="utf-8"))
    naam = d["Name"]
    if d.get("NeedsBrowser") or d.get("UseBridge"):
        print("%-22s OVERGESLAGEN (browser/brug nodig)" % naam)
        continue
    if not is_json(d):
        # Dit script leest enkel JSON uit; voor HTML-sites is er geen
        # CSS-selectormotor beschikbaar buiten de app om. De zoek-URL tonen we
        # wel, want die is de helft van wat er mis kan gaan.
        print("%-22s %s" % (naam, build(d, "fiets")[:118]))
        print("   OVERGESLAGEN (HTML-site; controleer met de Testen-knop in de instellingen)")
        print()
        continue
    url = build(d, "fiets")
    print("%-22s %s" % (naam, url[:118]))
    try:
        # Ook de eigen kopregels van de site meesturen: sommige API's weigeren
        # anders (Discogs wil x-apollo-operation-name).
        kop = {"User-Agent": UA, "Accept": "application/json"}
        kop.update(d.get("Headers") or {})
        req = urllib.request.Request(url, headers=kop)
        raw = urllib.request.urlopen(req, timeout=30).read().decode("utf-8", "replace")
        data = json.loads(raw)
        items = walk(data, d["ItemSelector"])
        arr = data
        for part in d["ItemSelector"].split("."):
            arr = arr[part]
        print("   resultaten: %d" % len(arr))
        for it in arr[:3]:
            titel = (walk(it, d["TitleSelector"]) or "")[:34]
            prijs = walk(it, d["PriceSelector"])
            plaats = walk(it, d["LocationSelector"]) or "-"
            foto = walk(it, d["ImageSelector"]) or ""
            groot = walk(it, d["LargeImageSelector"]) or "" if d.get("LargeImageSelector") else ""
            eur = (prijs/100.0) if (d.get("PriceInCents") and isinstance(prijs, (int, float))) else prijs
            print("     EUR %-9s %-34s %-14s foto:%s groot:%s" %
                  (eur, titel, plaats[:14], "ja" if foto else "NEE", "ja" if groot else "-"))
    except Exception as e:
        print("   FOUT: %s" % e)
    print()

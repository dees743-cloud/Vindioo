"""Alle beeldmateriaal van Vindioo uit één bron: Assets/logo-bron.png.

Die bron is de aangeleverde tekening van 1774x887 met twee blokken naast elkaar:

    x   54 -  392   het beeldmerk (vergrootglas met de V en de ring eromheen)
    x  406 - 1151   het woord "Vindioo"
    x 1216 - 1695   een kant-en-klare tegel met afgeronde hoeken

Daaruit komt:

    Assets/logo.png             de banner voor de kop van de app
    Assets/vindioo.ico          het pictogram van de exe
    extension/icon{16,48,128}.png  de pictogrammen van de brug-extensie

DE ENE AANPASSING DIE HIER GEBEURT, en waarom. In de bron staat "Vindi" in rgb(3, 7, 38) -
bijna zwart - en enkel de "oo" in kleur. Op de kopbalk van de app is dat zwart niet donker maar
ONZICHTBAAR: gemeten 1,18:1, waar 3:1 de ondergrens is om grote letters nog te lezen. Het hele
woord krijgt daarom het verloop van die twee laatste letters, met een donker randje eromheen.

ALLE DRIE DE KLEUREN KOMEN UIT DE BRON, niet uit iemands hoofd: BLAUW en PAARS zijn de uiteinden
van de "oo", kolom per kolom gemeten, en DONKER is de ring die in het beeldmerk rond de V staat.
Zo hoort het woord bij het beeldmerk in plaats van ernaast te staan.

WAT DAT KOST, en het is eerlijker dat te weten dan het niet te weten. Tegen de echte kopbalk -
rgb(40, 51, 113), gemeten uit een schermafbeelding en niet uit App.xaml, want daar ligt nog een
doorzichtige laag overheen - haalt het blauwe begin 3,37:1 en het paarse eind 1,75:1. Wit haalde
10,23:1. Het randje is wat het paarse eind leesbaar houdt: dat zet de letters los van de
achtergrond waar hun eigen kleur dat niet doet. Een logo is geen lopende tekst, dus 3:1 is hier
een richtlijn en geen eis.

Het beeldmerk wordt met rust gelaten - dat heeft zelf donkere delen (de lens), en die horen
donker te blijven.

Draaien: python tools/maak-beeldmateriaal.py
"""
import io
import os
import struct

from PIL import Image, ImageDraw, ImageFilter

HIER = os.path.dirname(os.path.abspath(__file__))
WORTEL = os.path.normpath(os.path.join(HIER, ".."))
BRON = os.path.join(WORTEL, "Assets", "logo-bron.png")

# De drie blokken in de bron, in kolommen. Zie de kop hierboven.
MERK = (54, 393)
WOORD = (406, 1152)
TEGEL = (1216, 1696)

# Het woord krijgt het verloop van de twee laatste letters, met een donker randje eromheen.
# Alle drie de kleuren komen UIT DE BRON zelf, niet uit iemands hoofd:
#   BLAUW en PAARS zijn de uiteinden van de "oo", per kolom gemeten;
#   DONKER is de ring die in het beeldmerk rond de V staat.
BLAUW = (14, 139, 248)
PAARS = (97, 36, 251)
DONKER = (2, 12, 59)

# Hoe dik dat randje is, gemeten op de bron. 4 punten is op de hoogte waarop de app het logo
# toont ongeveer een enkel beeldpunt - genoeg om de letters van de achtergrond te scheiden,
# weinig genoeg om ze niet zwaar te maken.
RAND = 4

MATEN = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def bron():
    return Image.open(BRON).convert("RGBA")


def uitsnede(beeld, kolommen):
    """Eén blok, strak rond wat er niet doorzichtig is."""
    deel = beeld.crop((kolommen[0], 0, kolommen[1], beeld.height))
    doos = deel.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox()
    return deel.crop(doos)


def woordmerk():
    """Het woord in het verloop van de "oo", met een donker randje eromheen.

    In de bron staat "Vindi" in bijna zwart en enkel de "oo" in kleur. Op de kop van de app is
    dat zwart onleesbaar (gemeten 1,18:1), en de eerste oplossing was het hele woord wit te
    maken. Dat leest wel, maar het staat los van het beeldmerk ernaast.

    Nu krijgt het HELE woord het verloop van die twee laatste letters, en dat scheelt leesbaar-
    heid: tegen de echte kopbalk (rgb(40, 51, 113), uit een schermafbeelding gemeten) haalt het
    blauwe begin 3,37:1 en het paarse eind 1,75:1. Daarom het randje: dat zet de letters los van
    de achtergrond waar hun eigen kleur dat niet doet. Een logo is geen lopende tekst, dus 3:1
    is hier een richtlijn en geen eis - maar het is beter dat getal te kennen dan het niet te
    kennen.
    """
    woord = uitsnede(bron(), WOORD)
    masker = woord.getchannel("A")

    # Het verloop over de volle breedte van het woord, en daarna het masker eroverheen: zo
    # krijgen ook de letters die eerst zwart waren hun plaats in het verloop.
    verloop = Image.new("RGBA", woord.size)
    tekenaar = ImageDraw.Draw(verloop)

    for x in range(woord.width):
        deel = x / max(1, woord.width - 1)
        kleur = tuple(round(BLAUW[i] + (PAARS[i] - BLAUW[i]) * deel) for i in range(3))
        tekenaar.line([(x, 0), (x, woord.height)], fill=kleur + (255,))

    verloop.putalpha(masker)

    # Het randje: hetzelfde masker, uitgezet met een maximumfilter, in het donker van de ring
    # rond de V. De ruimte eromheen moet mee groeien, anders valt het randje van het doek af.
    ruimte = RAND * 2
    groot = Image.new("L", (woord.width + ruimte * 2, woord.height + ruimte * 2), 0)
    groot.paste(masker, (ruimte, ruimte))
    dik = groot.filter(ImageFilter.MaxFilter(RAND * 2 + 1))

    doek = Image.new("RGBA", groot.size, (0, 0, 0, 0))
    doek.paste(Image.new("RGBA", groot.size, DONKER + (255,)), (0, 0), dik)
    doek.alpha_composite(verloop, (ruimte, ruimte))

    return doek


def banner():
    """Het beeldmerk met het woord ernaast."""
    merk = uitsnede(bron(), MERK)
    woord = woordmerk()

    # De tussenruimte komt uit de bron: daar staat 13 punten tussen het beeldmerk en het woord.
    # Zelf een getal kiezen lag voor de hand, maar dan verzin je de verhoudingen van een
    # tekening die iemand anders gemaakt heeft.
    tussen = WOORD[0] - MERK[1] + 13

    hoogte = max(merk.height, woord.height)
    doek = Image.new("RGBA", (merk.width + tussen + woord.width, hoogte), (0, 0, 0, 0))

    doek.alpha_composite(merk, (0, (hoogte - merk.height) // 2))
    doek.alpha_composite(woord, (merk.width + tussen, (hoogte - woord.height) // 2))

    print(f"  woord: verloop rgb{BLAUW} -> rgb{PAARS}, randje {RAND} in rgb{DONKER}")

    return doek


def tegel(zijde):
    """De aangeleverde tegel, op maat. Die heeft al afgeronde hoeken en een eigen achtergrond."""
    return uitsnede(bron(), TEGEL).resize((zijde, zijde), Image.LANCZOS)


def alsBmp(beeld):
    """Een frame zoals een .ico het verwacht: BITMAPINFOHEADER, BGRA van onder naar boven, en
    daarachter het (hier lege) 1-bits masker. De hoogte in de kop is twee keer de echte."""
    breedte, hoogte = beeld.size
    kop = struct.pack("<IiiHHIIiiII", 40, breedte, hoogte * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    punten = bytearray()
    for y in range(hoogte - 1, -1, -1):
        for x in range(breedte):
            r, g, b, a = beeld.getpixel((x, y))
            punten += bytes((b, g, r, a))

    perRij = ((breedte + 31) // 32) * 4
    return kop + bytes(punten) + bytes(perRij * hoogte)


def alsPng(beeld):
    buffer = io.BytesIO()
    beeld.save(buffer, format="PNG")
    return buffer.getvalue()


def schrijfIco(pad):
    """BMP tot 128, PNG voor 256 - de indeling die Windows zelf schrijft, met ook de maten
    20 en 40 die het bij 125% en 150% schaling gebruikt."""
    frames = [(maat, alsPng(tegel(maat)) if maat >= 256 else alsBmp(tegel(maat))) for maat in MATEN]

    inhoud = struct.pack("<HHH", 0, 1, len(frames))
    begin = 6 + 16 * len(frames)

    for maat, blok in frames:
        inhoud += struct.pack("<BBBBHHII", maat % 256, maat % 256, 0, 0, 1, 32, len(blok), begin)
        begin += len(blok)

    for _, blok in frames:
        inhoud += blok

    with open(pad, "wb") as f:
        f.write(inhoud)

    return len(inhoud), len(frames)


# ---------- schrijven ----------

print(f"bron: {os.path.relpath(BRON, WORTEL)}")

logo = banner()
logoPad = os.path.join(WORTEL, "Assets", "logo.png")
logo.save(logoPad)
print(f"  {os.path.relpath(logoPad, WORTEL)}: {logo.width}x{logo.height}")

icoPad = os.path.join(WORTEL, "Assets", "vindioo.ico")
bytes_, aantal = schrijfIco(icoPad)
print(f"  {os.path.relpath(icoPad, WORTEL)}: {bytes_} bytes, {aantal} formaten")

for maat in (16, 48, 128):
    pad = os.path.join(WORTEL, "extension", f"icon{maat}.png")
    tegel(maat).save(pad)
    print(f"  {os.path.relpath(pad, WORTEL)}: {maat}x{maat}")

# ---------- nameten ----------

print("\nnameten:")

for maat in (16, 48, 256):
    punten = list(tegel(maat).convert("RGBA").getdata())
    gemiddeld = sum(p[3] for p in punten) / len(punten)
    print(f"  tegel {maat:>3}: gemiddelde dekking {gemiddeld:5.1f}/255")

# En het contrast van het woord tegen de kop van de app, want daar begon het mee.
BOVEN, ONDER = (59, 52, 112), (29, 25, 58)


def licht(kleur):
    delen = []
    for waarde in kleur[:3]:
        v = waarde / 255
        delen.append(v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4)
    return 0.2126 * delen[0] + 0.7152 * delen[1] + 0.0722 * delen[2]


def verhouding(a, b):
    la, lb = licht(a), licht(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


# De echte kopbalk, gemeten uit een schermafbeelding: op het verloop uit App.xaml ligt nog een
# doorzichtige laag, dus rekenen met #3B3470 alleen zou een te mooi getal geven.
KOPBALK = (40, 51, 113)

for naam, kleur in (("begin (blauw)", BLAUW), ("eind (paars) ", PAARS), ("randje       ", DONKER)):
    print(f"  {naam} rgb{str(kleur):<16} tegen de kopbalk: {verhouding(kleur, KOPBALK):5.2f}:1")

print(f"  en het randje tegen de letters: {verhouding(DONKER, BLAUW):5.2f}:1 (blauw), "
      f"{verhouding(DONKER, PAARS):5.2f}:1 (paars)")

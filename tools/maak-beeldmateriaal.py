"""Alle beeldmateriaal van Vindioo uit één bron: Assets/logo-bron.png.

Die bron is de aangeleverde tekening van 1774x887 met twee blokken naast elkaar:

    x   54 -  392   het beeldmerk (vergrootglas met de V en de ring eromheen)
    x  406 - 1151   het woord "Vindioo"
    x 1216 - 1695   een kant-en-klare tegel met afgeronde hoeken

Daaruit komt:

    Assets/logo.png             de banner voor de kop van de app
    Assets/vindioo.ico          het pictogram van de exe
    extension/icon{16,48,128}.png  de pictogrammen van de brug-extensie

DE ENE AANPASSING DIE HIER GEBEURT, en waarom. Het woord is in de bron getekend in
rgb(3, 7, 38) - bijna zwart. De kop van de app heeft een verloop van #3B3470 naar #1D193A, en
daar is dat niet donker maar ONZICHTBAAR: gemeten contrast 1,18:1 tot 1,81:1, waar 3:1 de
ondergrens is om grote letters nog te kunnen lezen en wit 10,99:1 zou geven. Daarom worden de
donkere letters omgekleurd naar #F1EFF7, de tekstkleur van de app zelf (TextPrimaryColor in
App.xaml) - geen zelfverzonnen wit.

DE DREMPEL IS GEMETEN, niet gekozen. In het woord liggen 33 241 punten onder helderheid 60 (de
letters) en 17 447 punten boven 220 (de blauwe "oo"), met daartussen een gat waar nauwelijks
iets in zit: 60-79 telt 319 punten, 80-199 samen 146. Een grens op 150 raakt dus alle letters
en geen enkel accent. Het beeldmerk wordt met rust gelaten - dat heeft zelf donkere delen (de
lens), en die horen donker te blijven.

Draaien: python tools/maak-beeldmateriaal.py
"""
import io
import os
import struct

from PIL import Image

HIER = os.path.dirname(os.path.abspath(__file__))
WORTEL = os.path.normpath(os.path.join(HIER, ".."))
BRON = os.path.join(WORTEL, "Assets", "logo-bron.png")

# De drie blokken in de bron, in kolommen. Zie de kop hierboven.
MERK = (54, 393)
WOORD = (406, 1152)
TEGEL = (1216, 1696)

# De tekstkleur van de app (TextPrimaryColor in App.xaml).
LICHT = (241, 239, 247)

# Alles donkerder dan dit in het woord wordt omgekleurd.
DREMPEL = 150

MATEN = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def bron():
    return Image.open(BRON).convert("RGBA")


def uitsnede(beeld, kolommen):
    """Eén blok, strak rond wat er niet doorzichtig is."""
    deel = beeld.crop((kolommen[0], 0, kolommen[1], beeld.height))
    doos = deel.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox()
    return deel.crop(doos)


def banner():
    """Het beeldmerk met het woord ernaast, en het woord in de kleur van de app."""
    beeld = bron()

    # Het woord omkleuren vóór het uitsnijden, zodat de kolomgrenzen van de bron gelden.
    punten = beeld.load()

    omgekleurd = 0
    for x in range(WOORD[0], WOORD[1]):
        for y in range(beeld.height):
            r, g, b, a = punten[x, y]

            # Ook de halfdoorzichtige randjes: die zijn even donker, en zonder dit houd je
            # een donkere waas rond lichte letters over. De alfawaarde blijft staan, dus de
            # zachte rand blijft zacht.
            if a > 0 and max(r, g, b) < DREMPEL:
                punten[x, y] = LICHT + (a,)
                omgekleurd += 1

    print(f"  woord: {omgekleurd} punten omgekleurd naar rgb{LICHT}")

    # Beeldmerk en woord blijven staan zoals ze getekend zijn: één uitsnede over allebei, strak
    # rond wat niet doorzichtig is. Ze apart uitsnijden en met een zelfgekozen tussenruimte
    # weer samenzetten lag voor de hand, maar dan verzin je de verhoudingen van een tekening
    # die iemand anders gemaakt heeft - gemeten staat er 13 punten tussen, en dat is een keuze.
    return uitsnede(beeld, (MERK[0], WOORD[1]))


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


print(f"  woord rgb{LICHT} tegen #3B3470: {verhouding(LICHT, BOVEN):5.2f}:1")
print(f"  woord rgb{LICHT} tegen #1D193A: {verhouding(LICHT, ONDER):5.2f}:1")

"""Het beeldmerk op een eigen tegel, in de kleuren van de app, als Assets/vindioo.ico.

Twee dingen die op 2 oktober 2026 nagemeten zijn en samen de taakbalk weer een pictogram geven:

1. DE BRON. Het oude icoon was gemaakt uit logo.png, en dat is een BANNER van 1158x513 - het
   beeldmerk EN het woord "Vindioo" - waarvan maar 10% van de punten dekkend is; de rest is gloed.
   Dat hele ding in een vierkantje persen gaf een veeg: gemiddelde dekking 33 van 255. Hier wordt
   enkel het beeldmerk uitgesneden en op een tegel gezet: 245 van 255.

   Waarom een tegel en niet doorzichtig: het beeldmerk is neonkunst, lichtgevende lijnen op niets.
   Op een donkere taakbalk leest dat, op een lichte wast het uit tot een vaag kadertje - precies
   de klacht. Met een eigen achtergrond is het overal hetzelfde.

2. HET FORMAAT IN HET BESTAND. Het .ico wordt hier met de hand geschreven: BMP tot 128, PNG voor
   256, met ook de tussenmaten 20 en 40 die Windows bij 125% en 150% schaling gebruikt. Dat is de
   indeling die Windows zelf schrijft.

   LET OP, want ik dacht eerst dat dit de oorzaak was van het witte blad in de taakbalk: dat is
   het NIET. Pillow schrijft elk formaat als PNG, en met een tegenproef (het volledig-PNG bestand
   uit git, opnieuw ingebouwd) gaf SHGetFileInfo - dezelfde weg die de taakbalk neemt - een
   perfect pictogram terug, dekking 244,5. De shell leest PNG-frames dus gewoon.

   Het witte blad kwam van de ICONCACHE van Windows: het oude icoon was zo goed als doorzichtig,
   daar maakte de shell een algemeen bestandspictogram van, en dat bleef in de cache staan. Die
   wis je met "ie4uinit.exe -show".

   Deze indeling blijft staan omdat ze de gebruikelijke is en de schaalmaten erbij heeft, niet
   omdat ze iets repareert.

Draaien: python tools/maak-icoon.py
"""
import os
import struct

from PIL import Image, ImageDraw

HIER = os.path.dirname(os.path.abspath(__file__))
LOGO = os.path.join(HIER, "..", "Assets", "logo.png")
ICO = os.path.join(HIER, "..", "Assets", "vindioo.ico")

BOVEN = (59, 52, 112)     # #3B3470, BackgroundTopColor uit App.xaml
ONDER = (29, 25, 58)      # #1D193A, BackgroundBottomColor

MATEN = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def tegel(zijde):
    """Het beeldmerk, gecentreerd op een tegel met afgeronde hoeken."""
    logo = Image.open(LOGO).convert("RGBA")

    # Het beeldmerk staat links in de banner; de zwakke gloed telt niet mee voor de uitsnede,
    # anders bepaalt een waas waar de rand ligt.
    links = logo.crop((0, 0, 430, logo.height))
    merk = links.crop(links.getchannel("A").point(lambda a: 255 if a > 90 else 0).getbbox())

    doek = Image.new("RGBA", (zijde, zijde), (0, 0, 0, 0))

    verloop = Image.new("RGBA", (zijde, zijde))
    tekenaar = ImageDraw.Draw(verloop)

    for y in range(zijde):
        deel = y / max(1, zijde - 1)
        kleur = tuple(int(BOVEN[i] + (ONDER[i] - BOVEN[i]) * deel) for i in range(3))
        tekenaar.line([(0, y), (zijde, y)], fill=kleur + (255,))

    masker = Image.new("L", (zijde, zijde), 0)
    ImageDraw.Draw(masker).rounded_rectangle([0, 0, zijde - 1, zijde - 1],
                                             radius=max(2, int(zijde * 0.22)), fill=255)
    doek.paste(verloop, (0, 0), masker)

    rand = max(1, int(zijde * 0.12))
    ruimte = zijde - 2 * rand
    schaal = min(ruimte / merk.width, ruimte / merk.height)
    groot = merk.resize((max(1, int(merk.width * schaal)), max(1, int(merk.height * schaal))),
                        Image.LANCZOS)

    doek.alpha_composite(groot, ((zijde - groot.width) // 2, (zijde - groot.height) // 2))
    return doek


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

    # Het masker telt niet meer mee bij 32 bits, maar het moet er staan: per rij afgerond op
    # vier bytes.
    perRij = ((breedte + 31) // 32) * 4
    masker = bytes(perRij * hoogte)

    return kop + bytes(punten) + masker


def alsPng(beeld):
    import io
    buffer = io.BytesIO()
    beeld.save(buffer, format="PNG")
    return buffer.getvalue()


frames = []
for maat in MATEN:
    beeld = tegel(maat)
    # PNG enkel voor 256: dat is wat Windows zelf doet, en een BMP van 256x256 is 256 kB.
    frames.append((maat, alsPng(beeld) if maat >= 256 else alsBmp(beeld)))

inhoud = struct.pack("<HHH", 0, 1, len(frames))
begin = 6 + 16 * len(frames)

for maat, blok in frames:
    inhoud += struct.pack("<BBBBHHII", maat % 256, maat % 256, 0, 0, 1, 32, len(blok), begin)
    begin += len(blok)

for _, blok in frames:
    inhoud += blok

with open(ICO, "wb") as f:
    f.write(inhoud)

print(f"geschreven: {os.path.normpath(ICO)}  ({len(inhoud)} bytes, {len(frames)} formaten)")

# Nameten wat erin staat.
ico = Image.open(ICO)
for maat in sorted(ico.info["sizes"]):
    ico.size = maat
    punten = list(ico.convert("RGBA").getdata())
    gemiddeld = sum(p[3] for p in punten) / len(punten)
    print(f"  {maat[0]:>3}: gemiddelde dekking {gemiddeld:5.1f}/255")

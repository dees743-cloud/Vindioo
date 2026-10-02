"""Het beeldmerk op een eigen tegel, in de kleuren van de app.

Waarom een tegel en niet doorzichtig: het beeldmerk is neonkunst - lichtgevende lijnen op niets.
Op een donkere taakbalk leest dat, op een lichte wast het uit tot een vaag kadertje, en dat is
precies de klacht. Met een eigen achtergrond is het pictogram overal hetzelfde en overal
leesbaar.

De kleuren komen uit App.xaml (BackgroundTopColor #3B3470 naar BackgroundBottomColor #1D193A),
zodat het pictogram bij de app hoort.
"""
import os

from PIL import Image, ImageDraw

LOGO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "logo.png")
ICO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "zentrix.ico")
UIT = os.path.dirname(os.path.abspath(__file__))

BOVEN = (59, 52, 112)     # #3B3470
ONDER = (29, 25, 58)      # #1D193A

logo = Image.open(LOGO).convert("RGBA")

# Enkel het beeldmerk links; de zwakke gloed telt niet mee voor de uitsnede.
links = logo.crop((0, 0, 430, logo.height))
vak = links.getchannel("A").point(lambda a: 255 if a > 90 else 0).getbbox()
merk = links.crop(vak)

ZIJDE = 1024          # groot tekenen en pas op het einde verkleinen
RAND = int(ZIJDE * 0.12)

# De tegel, met een verloop van boven naar onder.
tegel = Image.new("RGBA", (ZIJDE, ZIJDE), (0, 0, 0, 0))
verloop = Image.new("RGBA", (ZIJDE, ZIJDE))
tekenaar = ImageDraw.Draw(verloop)

for y in range(ZIJDE):
    deel = y / (ZIJDE - 1)
    kleur = tuple(int(BOVEN[i] + (ONDER[i] - BOVEN[i]) * deel) for i in range(3))
    tekenaar.line([(0, y), (ZIJDE, y)], fill=kleur + (255,))

# Afgeronde hoeken, zoals Windows ze zelf tekent.
masker = Image.new("L", (ZIJDE, ZIJDE), 0)
ImageDraw.Draw(masker).rounded_rectangle([0, 0, ZIJDE - 1, ZIJDE - 1],
                                         radius=int(ZIJDE * 0.22), fill=255)
tegel.paste(verloop, (0, 0), masker)

# Het beeldmerk zo groot mogelijk binnen de rand.
ruimte = ZIJDE - 2 * RAND
schaal = min(ruimte / merk.width, ruimte / merk.height)
groot = merk.resize((int(merk.width * schaal), int(merk.height * schaal)), Image.LANCZOS)

tegel.alpha_composite(groot, ((ZIJDE - groot.width) // 2, (ZIJDE - groot.height) // 2))

MATEN = [16, 24, 32, 48, 64, 128, 256]
tegel.save(ICO, format="ICO", sizes=[(m, m) for m in MATEN])
print(f"geschreven: {ICO}")

ico = Image.open(ICO)
for maat in sorted(ico.info["sizes"]):
    ico.size = maat
    punten = list(ico.convert("RGBA").getdata())
    print(f"  {maat[0]:>3}: gemiddelde dekking {sum(p[3] for p in punten) / len(punten):5.1f}/255")

# Proefbeeld op donker en op licht.
proef = Image.new("RGB", (4 * 82 + 10, 2 * 82 + 10), (60, 60, 60))
for rij, achter in enumerate([(32, 32, 32, 255), (243, 243, 243, 255)]):
    for kol, maat in enumerate([16, 24, 32, 48]):
        ico.size = (maat, maat)
        doek = Image.new("RGBA", (maat, maat), achter)
        doek.alpha_composite(ico.convert("RGBA"))
        proef.paste(doek.convert("RGB").resize((72, 72), Image.NEAREST), (10 + kol * 82, 10 + rij * 82))

proef.save(f"{UIT}\\icoon-tegel.png")
print("proefbeeld: icoon-tegel.png")

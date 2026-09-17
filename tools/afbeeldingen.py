"""Maakt de aangeleverde afbeeldingen klaar voor gebruik in de app.

Draai dit opnieuw wanneer er een nieuw logo of een nieuwe achtergrond komt:

    python tools/afbeeldingen.py

Wat er moet gebeuren en waarom:

- De aangeleverde afbeeldingen dragen rechtsboven een badge "Made with AI". Bij
  het logo snijden we die weg, bij de achtergrond vullen we het plekje op met de
  kleuren eromheen (daar is het verloop glad genoeg om dat onzichtbaar te doen).
- Het logo staat op een zwarte ondergrond. Die rekenen we weg naar
  doorzichtigheid, zodat het op onze eigen achtergrond komt te staan.

Let op de richting van die omrekening, want ze hangt af van de ondergrond:

- Licht kunstwerk op ZWART (dit logo, neon met een gloed): pixel = a*K, dus de
  dekking komt uit het LICHTSTE kanaal. De gloed wordt daardoor vanzelf een
  half-doorzichtige waas, precies wat je wil.
- Donker kunstwerk op WIT (het vorige logo): pixel = a*K + (1-a)*255, dus de
  dekking komt uit het DONKERSTE kanaal.

Wie die twee verwisselt, krijgt een logo dat bijna helemaal doorzichtig wordt.
"""
import os
import sys
from PIL import Image, ImageFilter

# Map met de aangeleverde new_logo.png en background.png. Standaard de map
# Downloads van wie het script draait; een andere map mag als argument:
#     python tools/afbeeldingen.py D:\ontwerpen
BRON = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.expanduser("~"), "Downloads")
DOEL = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets")

os.makedirs(DOEL, exist_ok=True)

# Alles onder deze waarde geldt als achtergrond. De zwarte ondergrond is niet
# helemaal zwart (7,6,9) en heeft een flauwe blauwe gloed; zonder ondergrens
# blijft daar een vierkante waas van over.
DREMPEL = 26


def zwart_naar_doorzichtig(im):
    """Rekent licht-kunstwerk-op-zwart om naar kunstwerk met alfa."""
    im = im.convert("RGB")
    breedte, hoogte = im.size

    uit = Image.new("RGBA", (breedte, hoogte))
    bron, doel = im.load(), uit.load()

    for y in range(hoogte):
        for x in range(breedte):
            r, g, b = bron[x, y]
            hoogste = max(r, g, b)

            if hoogste <= DREMPEL:
                doel[x, y] = (0, 0, 0, 0)
                continue

            # De dekking oprekken zodat de drempel op nul uitkomt en het
            # helderste punt op volle dekking blijft.
            a = int(round((hoogste - DREMPEL) * 255.0 / (255 - DREMPEL)))

            # Kleur terugrekenen met de oorspronkelijke helderheid, anders
            # verschuift de tint van de zwakkere delen.
            kleur = tuple(min(255, int(round(c * 255.0 / hoogste))) for c in (r, g, b))
            doel[x, y] = kleur + (max(0, min(255, a)),)

    return uit


def snij_bij(im, marge=20, ondergrens=45):
    """Snijdt tot wat er echt getekend staat, met wat lucht eromheen.

    Kijkt niet naar "alles wat niet helemaal doorzichtig is": de gloed rond dit
    logo reikt tot ver in het beeld en zou het uitsnijden zinloos maken. Het
    kader komt van wat echt oplicht; de marge houdt de gloed eromheen.
    """
    alfa = im.split()[3].point(lambda w: 255 if w >= ondergrens else 0)

    vak = alfa.getbbox()
    if vak is None:
        return im

    links, boven, rechts, onder = vak
    return im.crop((max(0, links - marge), max(0, boven - marge),
                    min(im.width, rechts + marge), min(im.height, onder + marge)))


def vierkant(im, marge=0.08):
    """Legt de afbeelding in een vierkant vlak, gecentreerd."""
    zijde = int(max(im.width, im.height) * (1 + 2 * marge))

    vlak = Image.new("RGBA", (zijde, zijde), (0, 0, 0, 0))
    vlak.paste(im, ((zijde - im.width) // 2, (zijde - im.height) // 2), im)
    return vlak


def badge_wegwerken(im, vak):
    """Vult een rechthoek op met de kleuren van de rand eromheen."""
    links, boven, rechts, onder = vak
    breedte, hoogte = rechts - links, onder - boven

    strook = im.crop((links, onder, rechts, min(im.height, onder + hoogte)))
    im.paste(strook.resize((breedte, hoogte), Image.LANCZOS), (links, boven))

    ruim = (max(0, links - 30), max(0, boven - 30),
            min(im.width, rechts + 30), min(im.height, onder + 30))
    im.paste(im.crop(ruim).filter(ImageFilter.GaussianBlur(18)), ruim)
    return im


# ----------------------------------------------------------------- logo
logo = Image.open(os.path.join(BRON, "new_logo.png")).convert("RGB")

# De badge rechtsboven zwart maken; die valt dan weg met de ondergrond.
logo.paste(Image.new("RGB", (int(logo.width * 0.32), int(logo.height * 0.11)), (0, 0, 0)),
           (logo.width - int(logo.width * 0.32), 0))

kaal = snij_bij(zwart_naar_doorzichtig(logo))
kaal.save(os.path.join(DOEL, "logo.png"))
print("logo.png ->", kaal.size)

# --------------------------------------------------------------- icoon
# Voor het icoon enkel het merkteken links (het vergrootglas met de bolletjes);
# de naam erbij zou op 16 px onleesbaar zijn.
# 0,78 van de hoogte: net het vergrootglas met zijn ring, zonder de Z aan
# te snijden.
teken = snij_bij(kaal.crop((0, 0, int(kaal.height * 0.78), kaal.height)), marge=10)

maten = [16, 32, 48, 256]
vierkant(teken).resize((256, 256), Image.LANCZOS).save(
    os.path.join(DOEL, "zentrix.ico"), sizes=[(m, m) for m in maten])
print("zentrix.ico ->", ", ".join(f"{m}x{m}" for m in maten))

# ---------------------------------------------------------- achtergrond
acht = Image.open(os.path.join(BRON, "background.png")).convert("RGB")
acht = badge_wegwerken(acht, (int(acht.width * 0.815), 0,
                              acht.width, int(acht.height * 0.095)))
acht.save(os.path.join(DOEL, "background.png"))
print("background.png ->", acht.size)

"""Leest welke muisaanwijzer Windows toont op een reeks punten in het venster.

Zo kunnen we zonder mensenoog nagaan of het zoekvak over zijn volle hoogte
invoer aanneemt: een tekstcursor (I-balk) betekent gewone inhoud, een pijl
betekent dat de vensterrand de muis afvangt als sleepgebied.
"""
import ctypes
import sys
import time
from ctypes import wintypes

ctypes.windll.shcore.SetProcessDpiAwareness(2)

user32 = ctypes.windll.user32


class CURSORINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("flags", wintypes.DWORD),
                ("hCursor", wintypes.HANDLE), ("ptScreenPos", wintypes.POINT)]


# De standaardaanwijzers waarmee we vergelijken.
NAMEN = {}
for naam, code in [("pijl", 32512), ("I-balk", 32513), ("hand", 32649),
                   ("noord-zuid", 32645), ("west-oost", 32644)]:
    NAMEN[user32.LoadCursorW(None, code)] = naam


def huidige_aanwijzer():
    info = CURSORINFO()
    info.cbSize = ctypes.sizeof(CURSORINFO)
    user32.GetCursorInfo(ctypes.byref(info))
    return NAMEN.get(info.hCursor, "andere (%s)" % info.hCursor)


def vind_venster(titel):
    gevonden = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def terug(hwnd, _):
        lengte = user32.GetWindowTextLengthW(hwnd)
        if lengte:
            buf = ctypes.create_unicode_buffer(lengte + 1)
            user32.GetWindowTextW(hwnd, buf, lengte + 1)
            if titel.lower() in buf.value.lower() and user32.IsWindowVisible(hwnd):
                gevonden.append(hwnd)
        return True

    user32.EnumWindows(terug, 0)
    if not gevonden:
        return None

    def oppervlak(hwnd):
        r = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        return (r.right - r.left) * (r.bottom - r.top)

    return max(gevonden, key=oppervlak)


hwnd = vind_venster(sys.argv[1] if len(sys.argv) > 1 else "Zentrix")
if hwnd is None:
    print("venster niet gevonden")
    sys.exit(1)

rect = wintypes.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))
print("venster op", rect.left, rect.top, "-", rect.right, rect.bottom)

# Het zoekvak: 18 logische pixels van links, 340 breed, in de kop. Op 150%
# schaling is dat 27 tot 537 in echte beeldpunten. We lopen de hoogte af.
x = rect.left + 200

for y in range(6, 100, 6):
    user32.SetCursorPos(x, rect.top + y)
    time.sleep(0.12)
    print("y=%3d  ->  %s" % (y, huidige_aanwijzer()))

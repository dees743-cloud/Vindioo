"""Neemt een foto van een venster, ook als het achter een ander venster ligt.

Gebruikt PrintWindow: Windows laat het venster zichzelf tekenen in een eigen
bitmap. Zo hoeven we het niet naar voren te halen terwijl de gebruiker met
iets anders bezig is.
"""
import ctypes
import sys
from ctypes import wintypes
from PIL import Image

# DPI-bewust, anders geeft GetWindowRect verkleinde maten terwijl PrintWindow
# op echte beeldpunten tekent: dan krijg je maar een hoek van het venster.
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    ctypes.windll.user32.SetProcessDPIAware()

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32


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

    # Het grootste venster met deze titel. Zonder deze keuze pak je soms een
    # tooltip of een pop-upje van 237x39 dat toevallig dezelfde titel draagt, en
    # dan fotografeer je niets.
    def oppervlakte(hwnd):
        r = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        return (r.right - r.left) * (r.bottom - r.top)

    return max(gevonden, key=oppervlakte)


def foto(hwnd, pad):
    rect = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    breedte = rect.right - rect.left
    hoogte = rect.bottom - rect.top

    venster_dc = user32.GetWindowDC(hwnd)
    doel_dc = gdi32.CreateCompatibleDC(venster_dc)
    bitmap = gdi32.CreateCompatibleBitmap(venster_dc, breedte, hoogte)
    gdi32.SelectObject(doel_dc, bitmap)

    # 2 = PW_RENDERFULLCONTENT, nodig voor vensters die met de GPU tekenen.
    user32.PrintWindow(hwnd, doel_dc, 2)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG),
                    ("biHeight", wintypes.LONG), ("biPlanes", wintypes.WORD),
                    ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                    ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
                    ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD),
                    ("biClrImportant", wintypes.DWORD)]

    kop = BITMAPINFOHEADER()
    kop.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    kop.biWidth = breedte
    kop.biHeight = -hoogte          # negatief: van boven naar onder
    kop.biPlanes = 1
    kop.biBitCount = 32
    kop.biCompression = 0

    buffer = ctypes.create_string_buffer(breedte * hoogte * 4)
    gdi32.GetDIBits(doel_dc, bitmap, 0, hoogte, buffer, ctypes.byref(kop), 0)

    beeld = Image.frombuffer("RGB", (breedte, hoogte), buffer, "raw", "BGRX", 0, 1)
    beeld.save(pad)

    gdi32.DeleteObject(bitmap)
    gdi32.DeleteDC(doel_dc)
    user32.ReleaseDC(hwnd, venster_dc)
    return breedte, hoogte


if __name__ == "__main__":
    titel = sys.argv[1] if len(sys.argv) > 1 else "Vindioo"
    pad = sys.argv[2] if len(sys.argv) > 2 else "venster.png"

    hwnd = vind_venster(titel)
    if hwnd is None:
        print("venster niet gevonden:", titel)
    else:
        print("gefotografeerd:", foto(hwnd, pad), "->", pad)

"""Klikt op een plek BINNEN een venster, gemeten op het moment van klikken.

Het venster verschuift af en toe; met vaste schermcoordinaten klik je dan naast.
Gebruik: python klik.py <x-in-venster> <y-in-venster> [aantal-klikken] [venstertitel]
"""
import ctypes
import sys
import time
from ctypes import wintypes

ctypes.windll.shcore.SetProcessDpiAwareness(2)
user32 = ctypes.windll.user32

import vensterfoto as vf

titel = sys.argv[4] if len(sys.argv) > 4 else "Zentrix"
hwnd = vf.vind_venster(titel)
if hwnd is None:
    print("venster niet gevonden:", titel)
    sys.exit(1)

rect = wintypes.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))

x = rect.left + int(sys.argv[1])
y = rect.top + int(sys.argv[2])
keren = int(sys.argv[3]) if len(sys.argv) > 3 else 1

user32.SetCursorPos(x, y)
time.sleep(0.15)

for _ in range(keren):
    user32.mouse_event(0x0002, 0, 0, 0, 0)   # omlaag
    time.sleep(0.05)
    user32.mouse_event(0x0004, 0, 0, 0, 0)   # omhoog
    time.sleep(0.08)

print(f"geklikt op {titel}({sys.argv[1]},{sys.argv[2]}) = scherm({x},{y})")

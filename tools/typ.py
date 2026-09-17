"""Typt tekst in het actieve venster, ongeacht de toetsenbordindeling.

Cijfers gaan via het numerieke klavier (VK_NUMPAD0..9): op een AZERTY-bord
geeft de gewone 2-toets anders een 'e met accent'. Voor gewone tekst gebruiken
we het klembord met Ctrl+V, om diezelfde reden.

Gebruik: python typ.py [--selecteer-alles] "tekst"
"""
import ctypes
import subprocess
import sys
import time

ctypes.windll.shcore.SetProcessDpiAwareness(2)
u = ctypes.windll.user32

VK_NUMPAD = {str(n): 0x60 + n for n in range(10)}


def toets(vk):
    u.keybd_event(vk, 0, 0, 0)
    time.sleep(0.03)
    u.keybd_event(vk, 0, 2, 0)
    time.sleep(0.04)


def met_ctrl(vk):
    u.keybd_event(0x11, 0, 0, 0)
    toets(vk)
    u.keybd_event(0x11, 0, 2, 0)
    time.sleep(0.08)


args = sys.argv[1:]

if args and args[0] == "--selecteer-alles":
    args = args[1:]
    met_ctrl(0x41)          # Ctrl+A

tekst = args[0] if args else ""

if tekst and all(ch in VK_NUMPAD for ch in tekst):
    for ch in tekst:
        toets(VK_NUMPAD[ch])
elif tekst:
    # Via het klembord: dat is onafhankelijk van de indeling.
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    f"Set-Clipboard -Value {tekst!r}".replace("'", '"')], check=False)
    time.sleep(0.25)
    met_ctrl(0x56)          # Ctrl+V

print("getypt:", tekst)

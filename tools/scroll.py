"""Scrolt in een venster, gemeten op het moment zelf."""
import ctypes, sys, time
from ctypes import wintypes
ctypes.windll.shcore.SetProcessDpiAwareness(2)
user32 = ctypes.windll.user32
import vensterfoto as vf

hwnd = vf.vind_venster(sys.argv[1])
rect = wintypes.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))
x = rect.left + int(sys.argv[2]); y = rect.top + int(sys.argv[3])
klikken = int(sys.argv[4])
user32.SetCursorPos(x, y); time.sleep(0.15)
for _ in range(abs(klikken)):
    user32.mouse_event(0x0800, 0, 0, -120 if klikken > 0 else 120, 0)
    time.sleep(0.05)
print(f"gescrold {klikken} op ({x},{y})")

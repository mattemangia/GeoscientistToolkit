---
name: verify-windows
description: Windows counterpart of `verify` — build and drive the GAIA ImGui/OpenTK GUI on Windows to verify changes end-to-end (import flows, viewers). Use on Windows when a change needs runtime observation in the real app. There is no Xvfb on Windows, so this needs an interactive desktop session (see the hard requirement below).
---

# Verifying GAIA changes on Windows

This is the Windows port of the `verify` skill. The build step is identical; the
headless-display, software-GL, screenshot and input-injection layers are all
different because none of the X11 tooling (Xvfb, xdotool, xwd, openbox) exists here.

## Hard requirement: an interactive desktop session
Windows has **no virtual framebuffer** like Xvfb. GAIA (OpenTK) cannot create a window
without a logged-in, interactive desktop. That means one of:
- run on a normal logged-in machine, or
- a CI agent configured for **autologon** running as a desktop process (not a service), or
- an RDP session that stays connected (a minimized/disconnected RDP session tears down the
  desktop and GL context — keep it open, or use a console session).
A pure "service with no desktop" host cannot run this skill at all.

## Build
```powershell
# dotnet is usually already installed; otherwise:
#   winget install Microsoft.DotNet.SDK.10
dotnet build GAIA.csproj -c Debug
# output: bin\Debug\net10.0\GAIA.exe
```

## Software GL (only when there is no usable GPU driver)
On a real GPU with an interactive session, skip this — the hardware driver works.
For a GPU-less host, drop a Windows Mesa **llvmpipe** `opengl32.dll` next to the exe. The
application directory is searched before system32 for `opengl32.dll` (it is not a KnownDLL),
so a local copy overrides the system driver:
```powershell
# From a prebuilt Mesa-for-Windows (llvmpipe) x64 package:
Copy-Item mesa\x64\opengl32.dll bin\Debug\net10.0\
$env:GALLIUM_DRIVER = "llvmpipe"
$env:LP_NUM_THREADS = "8"
```
llvmpipe exposes GL 4.5, so GAIA's `#version 330 core` shaders compile without any
`MESA_GL_VERSION_OVERRIDE`.

## Launch
```powershell
$p = Start-Process bin\Debug\net10.0\GAIA.exe -PassThru `
     -RedirectStandardOutput $env:TEMP\gaia.log -RedirectStandardError $env:TEMP\gaia.err
Start-Sleep -Seconds 25          # startup takes ~20-30 s (much longer under llvmpipe)
```
Shader-compile failures and the like are most reliable in GAIA's **persistent** log, not
stdout: `%LOCALAPPDATA%\GAIA\gt-<yyyy-MM-dd>.log` (see `Settings.LogFilePath`). That is where
the Linux run surfaced the real error, so grep it:
```powershell
Select-String -Path $env:LOCALAPPDATA\GAIA\gt-*.log -Pattern 'error|exception|shader|LOD'
```

## Python automation deps
The same Python toolchain as the Linux skill, plus Windows-native input/screenshot/window libs:
```powershell
pip install pyautogui pygetwindow mss numpy tifffile
```

## DPI gotcha — do this first
pyautogui/mss coordinates are physical pixels only if the process is DPI-aware; otherwise
Windows virtualizes them and clicks land in the wrong place under any display scaling ≠ 100%.
At the top of every automation script:
```python
import ctypes; ctypes.windll.user32.SetProcessDPIAware()
```
Or set the display to 100% scaling for the session.

## Window management (pygetwindow)
No openbox needed — the OS manages the window. Find, focus, size and place it:
```python
import pygetwindow as gw
w = gw.getWindowsWithTitle("GAIA")[0]
w.activate(); w.resizeTo(1000, 760); w.moveTo(0, 0)
```
The native title bar adds a vertical offset to the client area, so **take click coordinates
from a screenshot** rather than computing them — same rule as the Linux skill.

## Screenshots (mss)
```python
import mss
with mss.mss() as sct:
    sct.shot(output=r"s.png")   # full virtual screen; PNG directly, no xwd/convert step
```

## Driving the UI (pyautogui)
```python
import pyautogui, time
pyautogui.PAUSE = 0.0            # we insert our own sleeps

def click(x, y):                # per-frame polling: press/release must be slow
    pyautogui.moveTo(x, y); time.sleep(0.35)
    pyautogui.mouseDown();      time.sleep(0.2)
    pyautogui.mouseUp();        time.sleep(0.1)

def scroll(notches):            # zoom the 3D viewport; one wheel notch = 120
    for _ in range(abs(notches)):
        pyautogui.scroll(120 if notches > 0 else -120); time.sleep(0.15)
```

## Input differences vs the Linux skill
- **Input is still polled per frame**, so a fast click is MISSED — keep the slow
  mousedown → sleep → mouseup pattern above.
- **Keyboard WORKS here.** Unlike XTEST on X11, pyautogui's SendInput reaches the focused
  OpenTK window, so you can *type* file paths directly (`pyautogui.write(path)`,
  `pyautogui.press('enter')`) instead of click-navigating ImGuiFileDialog. The window must be
  activated first (`w.activate()`).
- **Double-click** for folder entry works via `pyautogui.click(x, y, clicks=2, interval=0.07)`
  once the window is small (high FPS); at large framebuffers it can be missed, same as Linux.
- `imgui.ini` is written to the **cwd** (repo root) and persists window positions; delete it
  after runs — it is untracked. Windows may open a panel partly offscreen otherwise; drag it
  back by the title bar with mouseDown → several moveTo steps → mouseUp.

## CT import test data
Identical to the `verify` skill — the numpy/tifffile generator and the `.Volume.bin` /
`.gvt` binary-format parsing are pure Python and cross-platform. Only use Windows paths
(`r"C:\..."` / `os.path.join`). Header layouts, chunk/brick ordering and the bit-for-bit
`reference.npy` comparison are unchanged.

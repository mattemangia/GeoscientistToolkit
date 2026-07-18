---
name: verify
description: Build and drive the GAIA ImGui/OpenTK GUI headlessly under Xvfb to verify changes end-to-end (import flows, viewers). Use when a change needs runtime observation in the real app.
---

# Verifying GAIA changes headlessly

## Build
```bash
# dotnet is NOT preinstalled in the remote container:
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build GAIA.csproj -c Debug
```

## Launch under Xvfb (software GL works)
```bash
apt-get update -q && apt-get install -y xdotool x11-apps imagemagick openbox
Xvfb :7 -screen 0 1600x1000x24 &            # start display
DISPLAY=:7 openbox &                        # WM needed for window move/resize
DISPLAY=:7 LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe LP_NUM_THREADS=8 \
  DOTNET_ROOT="$HOME/.dotnet" bin/Debug/net10.0/GAIA > /tmp/gaia.log 2>&1 &
sleep 25                                    # startup takes ~20-30 s
# Shrink the window — smaller framebuffer = much higher FPS (needed for double-click):
WID=$(DISPLAY=:7 xdotool search --name "GAIA" | head -1)
DISPLAY=:7 xdotool windowsize $WID 1000 720 windowmove $WID 0 20
```

## Screenshots
`xdotool` screenshot tools: `DISPLAY=:7 xwd -root -silent > s.xwd && convert s.xwd s.png`
(PIL cannot read xwd; use ImageMagick.)

## Driving the UI — critical gotchas
- **Input is polled per frame**, not event-queued. A fast `xdotool click` (press+release
  in ~5 ms) is MISSED. Always click as:
  `xdotool mousemove X Y sleep 0.35 mousedown 1 sleep 0.2 mouseup 1`
- **Double-click** (needed to enter folders in ImGuiFileDialog): two fast clicks with
  ~80 ms hold / ~70 ms gap works once the window is small (high FPS). At 1600x1000 it
  never registers.
- **Keyboard input does NOT work** via xdotool/XTEST (OpenTK TextInput never fires).
  Do all navigation with clicks only: Up button + double-click on [D] entries; files
  are single-click + Select button.
- ImGui windows may open partially offscreen (positions persist in `imgui.ini`,
  written to the repo root cwd — delete it after runs, it is untracked). Drag by
  title bar with mousedown → several mousemove steps → mouseup.
- After a WM is running the app window sits at (0,20): add that offset to
  screen-coordinates taken from earlier screenshots.

## CT import test data
Generate a synthetic stack with numpy/tifffile (PNG folder + multipage TIFF) plus a
`reference.npy`; after import, parse the produced `<name>.Volume.bin` (40-byte header:
5×int32 w,h,d,chunkDim,bpp + double pixelSize + 3×int32 chunk counts, then 256³
chunks z-major) and `<name>.gvt` (header 5×int32 w,h,d,brickSize,numLods; per-LOD
3×int32 dims + int64 offset; 64³ bricks, x-fastest order) in Python and compare
bit-for-bit against the reference. The import dialog writes its outputs next to the
source folder/file.

LIQUID GLASS ON WINDOWS - liquidImgui showcase
==============================================

What this is
  liquidImgui (by charlie-x, MIT licence, a fork of liquidDX11): a native
  Windows app (Direct3D 11 + HLSL) that draws Apple-style Liquid Glass. It
  captures the live desktop behind its window and refracts it in real time:
  rim refraction, chromatic separation, frosted blur, specular edge light,
  spring-driven widgets and dock. Built unmodified from
  https://github.com/charlie-x/liquidImgui (commit 1f6f022).

How to run
  1. Unzip the whole folder anywhere (keep the "data" folder next to the exe).
  2. Double-click LiquidGlass.exe.
  3. Windows SmartScreen may say "Windows protected your PC" because this
     build is not code-signed: click "More info", then "Run anyway".

Try
  - Move the window over busy things (a photo, a web page, a video): the glass
    bends what is behind it.
  - The sidebar pages (General, Display, Battery, Storage, Customize) and the
    dock along the bottom.
  - Ctrl+K: the command palette.
  - Customize > Material: tune refraction, blur, rim, dispersion live.
  - Esc closes it. F11 toggles whether it shows up in screen captures.
    F9 records an MP4 (only if ffmpeg.exe is on your PATH).

Needs
  Windows 10 or 11, 64-bit, a DirectX 11 GPU. The live backdrop uses Desktop
  Duplication, which does not work inside some virtual machines or Remote
  Desktop sessions (the glass then shows a plain fallback).

On first run it writes glass.cfg next to the exe (its settings and presets).

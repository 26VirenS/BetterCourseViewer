LIQUID GLASS ON WINDOWS - AuroraGlass showcase
==============================================

What this is
  AuroraGlass (VeiQiuLab, Apache-2.0 licence): a Windows "Liquid Glass" SDK in
  C++20 / Direct3D 11 / HLSL, with Win32, WPF and WinUI 3 adapters. Built
  unmodified from https://github.com/VeiQiuLab/AuroraGlass (commit 9355428).
  This is a second, younger candidate (version 0.9.0); it draws glass over its
  own content inside its window, not over the desktop.

How to run
  1. Unzip the whole folder (keep the "shaders" folder next to the exes).
  2. Run P3_Interactive_Smoke.exe: glass you can drag over text, shapes and a
     grid, plus a glass button, toggle and slider.
     For its scripted 59-second tour: open a terminal in this folder and run
         P3_Interactive_Smoke.exe --recording-demo
  3. P4_Motion_Smoke.exe (if present): its motion and spring demo.
  SmartScreen may warn (not code-signed): "More info", then "Run anyway".
  Esc closes a demo.

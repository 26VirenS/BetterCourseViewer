# Liquid Glass on Windows

The goal: Apple's Liquid Glass — its buttons, controls and motion — on Windows. This folder starts with the closest
copies already out there, built for Windows (`.github/workflows/windows-glass.yml`), so they can be judged on a real
PC before deciding whether to adopt one or build our own.

## What exists (October 2026)

| Project | Stack | Refracts | Motion / controls | Licence | Verdict |
| --- | --- | --- | --- | --- | --- |
| [charlie-x/liquidImgui](https://github.com/charlie-x/liquidImgui) (fork of liquidDX11) | Win32, Direct3D 11, HLSL, Dear ImGui | The live desktop (DXGI Desktop Duplication): rim refraction, chromatic separation, Dual Kawase blur, specular rim light | Spring widgets, dock, sidebar, sliders, toggles, command palette, live material tuning | MIT | **Closest copy found.** Built here. |
| [VeiQiuLab/AuroraGlass](https://github.com/VeiQiuLab/AuroraGlass) | C++20, Direct3D 11, HLSL; Win32 / WPF / WinUI 3 adapters | Its own window's content | Button, toggle, slider, motion system | Apache-2.0 | Young (0.9.0) but SDK-shaped. Built here. |
| [JamesLinYJ/LiquidGlassWinUI3](https://github.com/JamesLinYJ/LiquidGlassWinUI3) | WinUI 3, Windows Composition | No (blur + tint + rim only; refraction is "reserved") | Surfaces only | MIT | Frosted glass, not Liquid Glass. |
| [dragosniamtu/WPF-Liquid-Glass-Effect](https://github.com/dragosniamtu/WPF-Liquid-Glass-Effect) | WPF pixel shader over a GDI desktop capture | The desktop | Window only | — | A learning demo. |
| [pratikone/liquid-glass-WinUI](https://github.com/pratikone/liquid-glass-WinUI) | WinUI 3, Win2D displacement | Its content | Draggable panels | — | A demo. |
| Web: [liquidGL](https://github.com/naughtyduk/liquidGL), [samasante/liquid-glass](https://github.com/samasante/liquid-glass), [rdev/liquid-glass-react](https://github.com/rdev/liquid-glass-react) | WebGL / SVG displacement | Page content | Varies | MIT | Usable on Windows through WebView2 / Electron (Chromium). |
| Flutter: [liquid_glass_renderer](https://pub.dev/packages/liquid_glass_renderer) | Impeller shaders | Its content | Blending shapes | MIT | Needs Impeller: no Windows. |

## The showcase

Each push to this folder (on `claude/windows-liquid-glass`) builds both native candidates on a Windows runner and
publishes them as a pre-release, `windows-glass-r<run>`, with a zip each: unzip, run the exe (each zip has a
`README-FIRST.txt`). Nothing here touches Simpl's own builds, releases or update feeds.

## Next, depending on the verdict

- **liquidImgui is good enough:** lift its glass renderer (`src/glass/`: backdrop capture, the HLSL glass shader,
  springs) out of Dear ImGui into a small library of our own, and build Simpl's Windows controls on it (button, toggle,
  slider, segmented control, tab bar, sheet, menu), each with Liquid Glass's motion: press squish, the lens that
  follows the pointer, shapes that merge and separate, springs everywhere.
- **Neither is:** our own renderer — Direct3D 11 (or Win2D) — from what these get right: an SDF per shape (smooth-min
  so neighbouring shapes merge), refraction from the SDF's normal with a convex bevel profile, per-channel dispersion,
  a frosted blur of what is behind, a specular rim lit from the pointer, and springs (damping 1.0, response 0.3–0.4 s)
  for every move.

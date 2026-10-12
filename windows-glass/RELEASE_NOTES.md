**Liquid Glass on Windows — native, on AuroraGlass.** A test build for evaluation; it is not Simpl, and Simpl's own updates never offer it.

**LiquidGlass-Showcase-win64.zip** — unzip (keep the `shaders` folder beside the app) and run **LiquidGlass.exe**. Nothing to install. Not code-signed: on "Windows protected your PC", choose **More info → Run anyway**.

Rebuilt from scratch for speed and the Mac's look: Direct3D 11 throughout, the glass drawn on the GPU by AuroraGlass's renderer (Apache-2.0) and frosted deeply — it reads a small copy of what is behind, so the frost costs almost nothing — with the Mac's warm tint and a hairline rim of light; the words and icons by DirectWrite. It runs at the display's refresh rate (the frame rate is in the window's title).

- the Mac's *Unread announcements* popover (drag it around);
- a toolbar with the Dashboard's Cards · List · Activity, and a tab bar — a lens slides between the choices;
- switches and sliders whose knobs turn into glass lenses while held (they refract the panel under them);
- glass buttons; a glass bubble to drag and fling;
- **Tune**: frost, refraction, dispersion, thickness, colour, tint and rim, live; Dark or Light; the moving backdrop on or off.

Esc closes it.

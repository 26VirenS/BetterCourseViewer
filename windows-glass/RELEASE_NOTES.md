**Liquid Glass on Windows — native, Simpl's own glass.** A test build for evaluation; it is not Simpl, and Simpl's own updates never offer it.

**LiquidGlass-Showcase-win64.zip** — unzip and run **LiquidGlass.exe**. Nothing to install. Not code-signed: on "Windows protected your PC", choose **More info → Run anyway**.

The glass is now Simpl's own shader, modelled on what macOS 26 draws (Core Animation's glass filter, as taken apart by [ShatteredGlass](https://github.com/AlexStrNik/ShatteredGlass)) and tuned against a screenshot of the Mac's popover:

- **the frost**: what is behind, copied at a quarter of its size (as macOS does) and blurred by a true Gaussian — one smooth blur, no blotches;
- **the edges**: a band at the rim bends what is behind outward, so what lies just past the edge is drawn into it; a thin rim of light, brightest along the top and bottom and coloured by the glass; a dark hairline just outside; a soft shadow;
- **the tone**: what is behind kept at about 62% and lifted toward the material's colour (the popover's: the Mac's 10% orange tint), its colours made richer;
- **corners**: cards are soft squircles (continuous corners: a superellipse of degree 3 over 1.5 × the radius); bars and every other capsule are round. Tune → Corners → Round puts round corners on the cards too, to compare;
- **the settings chosen**: Frost 13 pt, Refraction 30 pt, Edge 13 pt, Dispersion 0.03, Colour 1.80, Tint 0.38, Rim 0.53, dark.

Also: the Mac's *Unread announcements* popover, a toolbar and tab bar with a sliding lens, switches and sliders whose knobs turn to glass while held, glass buttons, a card of clear glass, a bubble to fling; backdrops **Simpl**, **Grid** (a sharp test card) and **Photo** (your wallpaper and Windows' photos, or drop a picture on the window); **Tune**: frost, refraction, edge, dispersion, colour, tint, rim. Esc closes it.

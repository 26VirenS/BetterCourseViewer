#!/usr/bin/env python3
"""The pictures a CI run took, looked at before anyone has to: each one must be there, and must show something.

A picture that is blank or all one colour (a window that never drew, a web view that stayed white, a capture of
nothing) or that is missing (the app did not get there) fails the run, named with what was measured, so a broken
screen is caught by the run rather than by someone scrolling through a hundred pictures. A picture meant to be plain
is named in the allowlist (scripts/dev/shot-checks.json), each with its reason and the floor it is held to instead.

    python3 scripts/dev/check-shots.py <dir> [--expect names.txt] [--pattern '*.png'] [--allow shot-checks.json]
                                             [--app mac|ios|windows] [--report out.txt]

--expect: a file whose lines start with each picture's name (mac-shots.sh's shot-times.txt, a list of names): every
one must have a picture (<name>.png or <name>-app.png). Each picture is measured at a width of 320 pixels:
  spread   the standard deviation of its grey (0 a flat colour; a screen of text and cards is 20 and more)
  dominant the share of it in its commonest colour (colours counted in steps of 16 per channel)
  colours  how many such colours cover at least 0.2% of it
and fails when spread < MIN_SPREAD, dominant > MAX_DOMINANT or colours < MIN_COLOURS. Needs Pillow.
"""
import argparse
import fnmatch
import json
import os
import sys

try:
    from PIL import Image, ImageOps, ImageStat
except ImportError:  # (the workflows install it; said plainly when one did not)
    sys.exit("check-shots.py needs Pillow: python3 -m pip install pillow")

MIN_SPREAD = 5.0
MAX_DOMINANT = 0.99
MIN_COLOURS = 3


def measure(path):
    """(spread, dominant, colours, width, height) of the picture at path."""
    with Image.open(path) as im:
        width, height = im.size
        im.draft("RGB", (320, 320))
        if im.mode in ("RGBA", "LA", "P"):
            # (a see-through window pictured on nothing is laid on white, as it would be seen)
            rgba = im.convert("RGBA")
            ground = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
            ground.alpha_composite(rgba)
            im2 = ground.convert("RGB")
        else:
            im2 = im.convert("RGB")
    small = im2.resize((320, max(1, round(320 * height / max(1, width)))), Image.BILINEAR)
    spread = ImageStat.Stat(small.convert("L")).stddev[0]
    # (each channel in steps of 16: 4,096 colours at most)
    counts = [c for c, _ in ImageOps.posterize(small, 4).getcolors(4096)]
    total = small.width * small.height
    dominant = max(counts) / total
    colours = sum(1 for c in counts if c / total >= 0.002)
    return spread, dominant, colours, width, height


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("dir")
    ap.add_argument("--expect", help="a file of the pictures' names, one a line (the first word of each)")
    ap.add_argument("--pattern", default="*.png")
    ap.add_argument("--allow", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "shot-checks.json"))
    ap.add_argument("--app", default="", help="the allowlist's section: mac, ios or windows")
    ap.add_argument("--report", help="where to write every picture's numbers")
    a = ap.parse_args()

    allow = {}
    if a.allow and os.path.exists(a.allow):
        with open(a.allow, encoding="utf-8") as f:
            data = json.load(f)
        allow = {**data.get("all", {}), **data.get(a.app, {})}

    def allowance(name):
        for pattern, rule in allow.items():
            if fnmatch.fnmatch(name, pattern):
                return pattern, rule
        return None, None

    files = sorted(f for f in os.listdir(a.dir) if fnmatch.fnmatch(f, a.pattern)) if os.path.isdir(a.dir) else []
    problems, rows = [], []
    for f in files:
        name = f[:-4]
        try:
            spread, dominant, colours, w, h = measure(os.path.join(a.dir, f))
        except Exception as e:  # (a file that is not a picture at all)
            problems.append(f"{f}: not a picture that opens ({e})")
            continue
        rows.append(f"{name:44} {w:5}x{h:<5} spread {spread:6.1f}  dominant {dominant:5.3f}  colours {colours:4}")
        pattern, rule = allowance(name)
        lo_spread = rule.get("min_spread", MIN_SPREAD) if rule else MIN_SPREAD
        hi_dominant = rule.get("max_dominant", MAX_DOMINANT) if rule else MAX_DOMINANT
        lo_colours = rule.get("min_colours", MIN_COLOURS) if rule else MIN_COLOURS
        why = []
        if w < 200 or h < 150:
            why.append(f"only {w}x{h}")
        if spread < lo_spread:
            why.append(f"spread {spread:.1f} < {lo_spread}")
        if dominant > hi_dominant:
            why.append(f"{dominant:.1%} one colour > {hi_dominant:.0%}")
        if colours < lo_colours:
            why.append(f"{colours} colours < {lo_colours}")
        if why:
            problems.append(f"{f}: blank or nearly one colour ({'; '.join(why)})" + (f" [allowlist {pattern}]" if pattern else ""))

    missing = []
    if a.expect:
        have = {f[:-4] for f in files}
        with open(a.expect, encoding="utf-8") as fh:
            # (a name, then how its copy of the app ended, when the job wrote that down)
            lines = [ln.split(None, 1) for ln in fh if ln.strip() and not ln.startswith("#")]
        names = [w[0] for w in lines]
        ended = {w[0]: w[1].strip() for w in lines if len(w) > 1}
        for n in names:
            if n not in have and f"{n}-app" not in have and not (allowance(n)[1] or {}).get("missing"):
                missing.append(n)
        for n in sorted(set(missing)):
            problems.append(f"{n}: no picture (the app did not get there, or quit first)" + (f" {ended[n]}" if n in ended else ""))

    if a.report:
        with open(a.report, "w", encoding="utf-8") as fh:
            fh.write("\n".join(rows) + "\n")
    print(f"{len(files)} pictures looked at in {a.dir}" + (f", {len(missing)} missing" if a.expect else "") + f": {len(problems)} problem(s)")
    for p in problems:
        # (GitHub's annotation when run there; a plain line otherwise)
        print(f"::error title=picture::{p}" if os.environ.get("GITHUB_ACTIONS") else f"  {p}")
    if not files and not a.expect:
        print("::error title=picture::no pictures at all" if os.environ.get("GITHUB_ACTIONS") else "  no pictures at all")
        return 1
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

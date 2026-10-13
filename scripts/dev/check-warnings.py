#!/usr/bin/env python3
"""A build's compiler warnings against the ones already known (a baseline committed beside this script), so a new
warning fails the build where it was made instead of joining the pile: the C# compiler's (dotnet build / publish),
Swift's and clang's (xcodebuild). Fixing warnings lowers the baseline: run with --write and commit what it writes.

    python3 scripts/dev/check-warnings.py <build.log> --baseline scripts/dev/baselines/<app>-warnings.txt [--write]

A warning is known by its file (from the repository's root) and its words, not its line, so code moved about does
not make it new; the same warning twice in a file is counted twice. Only warnings in the repository's own sources
count (not a tool's notes, nor generated files under obj/, build/ or DerivedData). An incremental build compiles only
what changed, so a known warning not seen this time may simply not have been compiled: it is said, never failed.
"""
import argparse
import collections
import os
import re
import sys

SWIFT = re.compile(r"^(?P<file>/[^:\n]+?):(?P<line>\d+):(?P<col>\d+): warning: (?P<msg>.+?)\s*$")
DOTNET = re.compile(r"^\s*(?P<file>[^\s(][^(]*?)\((?P<line>\d+),(?P<col>\d+)\): warning (?P<code>[A-Z]+\d+): (?P<msg>.+?)(?: \[[^\]]+\])?\s*$")
GENERATED = re.compile(r"(^|/)(obj|build|DerivedData|\.build)/|\.g\.(cs|i\.cs)$|_wpftmp")


def read(log, root):
    """The warnings in a build log: {(file, line, col, key)}, file relative to the repository's root."""
    seen = set()
    marker = "/" + os.path.basename(os.path.normpath(root)) + "/"
    with open(log, encoding="utf-8", errors="replace") as f:
        for raw in f:
            raw = raw.rstrip("\n")
            m = SWIFT.match(raw) or DOTNET.match(raw)
            if not m:
                continue
            path = m.group("file").replace("\\", "/")
            # (the runner's checkout, wherever it is: the path from the repository's own folders on)
            if path.startswith(root.rstrip("/") + "/"):
                path = path[len(root.rstrip("/")) + 1:]
            elif marker in path:
                path = path.split(marker)[-1]
            elif re.match(r"^[A-Za-z]:/", path) or path.startswith("/"):
                continue  # (outside the repository: a package's, the SDK's)
            if GENERATED.search(path):
                continue
            code = m.groupdict().get("code") or ""
            msg = re.sub(r"\s+", " ", m.group("msg"))
            key = f"{path}: {code + ' ' if code else ''}{msg}"
            seen.add((path, int(m.group("line")), int(m.group("col")), key))
    return seen


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("log")
    ap.add_argument("--baseline", required=True)
    ap.add_argument("--root", default=os.getcwd(), help="the repository's root (default: here)")
    ap.add_argument("--write", action="store_true", help="write this build's warnings as the baseline")
    a = ap.parse_args()

    found = read(a.log, a.root)
    now = collections.Counter(k for _, _, _, k in found)
    if a.write:
        os.makedirs(os.path.dirname(os.path.abspath(a.baseline)), exist_ok=True)
        with open(a.baseline, "w", encoding="utf-8") as f:
            f.write("# The compiler warnings already known (scripts/dev/check-warnings.py): one a line, by file and words.\n")
            f.write("# A build with a warning not here fails. Fixing one: after a full build, run the script with --write.\n")
            for k in sorted(now.elements()):
                f.write(k + "\n")
        print(f"{sum(now.values())} warnings written to {a.baseline}")
        return 0

    known = collections.Counter()
    if os.path.exists(a.baseline):
        with open(a.baseline, encoding="utf-8") as f:
            known.update(ln.rstrip("\n") for ln in f if ln.strip() and not ln.startswith("#"))
    new = now - known
    gone = known - now
    gh = bool(os.environ.get("GITHUB_ACTIONS"))
    print(f"{sum(now.values())} compiler warnings in this build's sources; {sum(known.values())} known "
          f"({a.baseline}): {sum(new.values())} new")
    for path, line, col, key in sorted(found):
        if new.get(key):
            new[key] -= 1
            text = key[len(path) + 2:]
            print(f"::error file={path},line={line},col={col},title=new compiler warning::{text}" if gh else f"  new: {path}:{line}:{col}: {text}")
    if gone:
        print(f"  {sum(gone.values())} known warning(s) not seen this time (fixed, or not compiled in an incremental "
              f"build): after a full build without them, lower the baseline with --write")
    return 1 if sum((now - known).values()) else 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""The logs a CI run's pictures left (each app's console and its own log), read for what went wrong while it was
pictured: an exception, a crash, "[Simpl] error", a promise nobody handled, a page's script that threw. Any such line
fails the run, printed with its file, unless the allowlist (scripts/dev/log-checks.json) names it as harmless, each
entry with its reason. A crash report among the files (crash-*.txt) fails it too.

    python3 scripts/dev/scan-logs.py --app mac|ios|windows <file or glob>... [--allow log-checks.json]

Every allowlisted line that was seen is counted, so an entry that no longer matches anything can be found and
taken out (printed as "unused").
"""
import argparse
import glob
import json
import os
import re
import sys

# what fails: (a name for it, a regular expression on one line)
BAD = [
    ("exception", re.compile(r"\b(?:\w+\.)*\w*Exception\b(?!s)|\bUnhandled exception\b|\bunhandled error\b", re.I)),
    ("simpl error", re.compile(r"\[Simpl(?: Courses)?\] (?:error|.*\bfailed\b|.*\banswer not understood\b)", re.I)),
    ("crash", re.compile(r"\bcrash(?:ed|es)?\b|\bFatal error\b|\bEXC_BAD_ACCESS\b|\bSIGABRT\b|\bSIGSEGV\b|\bterminating due to\b", re.I)),
    ("promise", re.compile(r"unhandled ?(?:promise )?rejection|Uncaught \(in promise\)", re.I)),
    ("script error", re.compile(r"\bUncaught\b|\b(?:Type|Reference|Syntax|Range)Error\b|\bpage: (?:uncaught|console\.error)\b")),
    ("picture failed", re.compile(r"\bpicture failed\b")),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--app", default="")
    ap.add_argument("--allow", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "log-checks.json"))
    a = ap.parse_args()

    allow = []
    if os.path.exists(a.allow):
        with open(a.allow, encoding="utf-8") as f:
            data = json.load(f)
        for section in ("all", a.app):
            for e in data.get(section, []):
                allow.append({"re": re.compile(e["pattern"]), "pattern": e["pattern"], "reason": e["reason"], "seen": 0, "section": section})

    files = []
    for p in a.paths:
        files += sorted(glob.glob(p)) if any(c in p for c in "*?[") else ([p] if os.path.exists(p) else [])
    problems = []
    lines_read = 0
    for path in files:
        name = os.path.basename(path)
        if name.startswith("crash-"):
            with open(path, encoding="utf-8", errors="replace") as f:
                head = " ".join(f.read(4000).split())[:300]
            problems.append(f"{name}: a crash report ({head})")
            continue
        with open(path, encoding="utf-8", errors="replace") as f:
            for n, line in enumerate(f, 1):
                lines_read += 1
                line = line.rstrip("\n")
                kinds = [k for k, rx in BAD if rx.search(line)]
                if not kinds:
                    continue
                hit = next((e for e in allow if e["re"].search(line)), None)
                if hit:
                    hit["seen"] += 1
                    continue
                problems.append(f"{name}:{n}: {', '.join(kinds)}: {line.strip()[:400]}")

    print(f"{len(files)} logs, {lines_read} lines read: {len(problems)} problem(s)")
    gh = bool(os.environ.get("GITHUB_ACTIONS"))
    for p in problems[:200]:
        print(f"::error title=log::{p}" if gh else f"  {p}")
    if len(problems) > 200:
        print(f"  … and {len(problems) - 200} more")
    for e in allow:
        if e["seen"]:
            print(f"  allowed {e['seen']}×: {e['pattern']} ({e['reason']})")
    unused = [e for e in allow if not e["seen"] and e["section"] == a.app]
    for e in unused:
        print(f"  unused allowlist entry ({a.app}): {e['pattern']}")
    if not files:
        print("::warning title=log::no logs to read" if gh else "  no logs to read")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

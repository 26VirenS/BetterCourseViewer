#!/usr/bin/env python3
"""Signs the update files of Simpl's releases, so an app installs only a file signed with Simpl's own key.

Each update file — Simpl for Mac's zip (mac-v* releases, Simpl-Mac-<version>.zip), the Safari app's zip (v* releases,
Simpl-Courses-Mac-<version>.zip) and Simpl for Windows' installer (windows-fluent-v* and windows-fluent-beta-v*,
Simpl-Setup.exe) — gets a signature beside it in the same release, <file>.sig: a little JSON naming the release's tag,
the file and its SHA-256, and an ECDSA P-256 signature (SHA-256, DER, base64) over

    simpl-update-v1\\n<tag>\\n<file>\\n<sha256>\\n

The apps (mac/Simpl/Support/UpdateSignature.swift, the Safari app's Updater.swift, the Windows app's UpdateSignature.cs)
hold the public key, read <download address>.sig, and build that text from the version they expect, not from the .sig:
so a file signed for one version can't be passed off as another, nor the Mac's zip as the Safari app's.

The private key is the repository's UPDATE_SIGNING_KEY secret (a PKCS#8 PEM; see docs/update-signing.md). Without it
nothing is signed and nothing fails. A file already signed is left alone unless it was replaced after its signature.

    python3 scripts/sign-releases.py              every release
    python3 scripts/sign-releases.py --tag TAG    one release (right after a release script makes it)

Needs gh (GH_TOKEN: a token that can write releases) and openssl.
"""
import base64
import json
import os
import re
import subprocess
import sys
import tempfile

REPO = os.environ.get("GITHUB_REPOSITORY") or "26VirenS/BetterCourseViewer"
TARGETS = [
    (re.compile(r"^mac-v(\d+(?:\.\d+)+)$"), lambda m: f"Simpl-Mac-{m.group(1)}.zip"),
    (re.compile(r"^v(\d+\.\d+\.\d+)$"), lambda m: f"Simpl-Courses-Mac-{m.group(1)}.zip"),
    (re.compile(r"^windows-fluent-(?:beta-)?v\d+$"), lambda m: "Simpl-Setup.exe"),
]


def statement(tag, name, sha):
    return f"simpl-update-v1\n{tag}\n{name}\n{sha.lower()}\n".encode()


def gh(*args, capture=True):
    r = subprocess.run(["gh", *args], capture_output=capture, text=True)
    if r.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args[:3])}: {(r.stderr or '').strip()[:300]}")
    return r.stdout


def releases(tag):
    if tag:
        return [json.loads(gh("api", f"repos/{REPO}/releases/tags/{tag}"))]
    out, page = [], 1
    while True:
        batch = json.loads(gh("api", f"repos/{REPO}/releases?per_page=100&page={page}"))
        out += batch
        if len(batch) < 100:
            return out
        page += 1


def main():
    tag = None
    if "--tag" in sys.argv:
        tag = sys.argv[sys.argv.index("--tag") + 1]
    pem = os.environ.get("UPDATE_SIGNING_KEY", "").strip()
    if not pem:
        print("update signing: no UPDATE_SIGNING_KEY, nothing signed")
        return 0
    with tempfile.TemporaryDirectory() as work:
        key = os.path.join(work, "key.pem")
        fd = os.open(key, os.O_WRONLY | os.O_CREAT, 0o600)
        with os.fdopen(fd, "w") as f:
            f.write(pem + "\n")
        pub = os.path.join(work, "pub.pem")
        subprocess.run(["openssl", "pkey", "-in", key, "-pubout", "-out", pub], check=True, capture_output=True)
        signed = kept = 0
        for rel in releases(tag):
            if rel.get("draft"):
                continue
            name = next((f(m) for rx, f in TARGETS for m in [rx.match(rel["tag_name"])] if m), None)
            if not name:
                continue
            assets = {a["name"]: a for a in rel.get("assets", [])}
            file, sig = assets.get(name), assets.get(name + ".sig")
            if not file:
                continue
            digest = file.get("digest") or ""
            if not digest.startswith("sha256:"):
                print(f"update signing: {rel['tag_name']}/{name} has no SHA-256 from GitHub, left unsigned")
                continue
            if sig and sig.get("updated_at", "") >= file.get("updated_at", ""):
                kept += 1
                continue
            sha = digest[7:].lower()
            text = os.path.join(work, "statement")
            with open(text, "wb") as f:
                f.write(statement(rel["tag_name"], name, sha))
            der = os.path.join(work, "sig.der")
            subprocess.run(["openssl", "dgst", "-sha256", "-sign", key, "-out", der, text], check=True, capture_output=True)
            # (checked with the public key before it goes out: a signature that would not verify is never published)
            subprocess.run(["openssl", "dgst", "-sha256", "-verify", pub, "-signature", der, text], check=True, capture_output=True)
            folder = os.path.join(work, rel["tag_name"])
            os.makedirs(folder, exist_ok=True)
            out = os.path.join(folder, name + ".sig")
            with open(der, "rb") as f:
                body = {"v": 1, "tag": rel["tag_name"], "file": name, "sha256": sha, "sig": base64.b64encode(f.read()).decode()}
            with open(out, "w") as f:
                json.dump(body, f)
            gh("release", "upload", rel["tag_name"], out, "--clobber", "-R", REPO)
            signed += 1
            print(f"update signing: {rel['tag_name']}/{name} signed")
        print(f"update signing: {signed} signed, {kept} already signed")
    return 0


if __name__ == "__main__":
    sys.exit(main())

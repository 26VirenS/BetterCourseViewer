#!/usr/bin/env python3
"""Turns the source manifest (Safari's, which also loads in Firefox) into the Chrome / Edge one, in place.

    python3 scripts/chrome-manifest.py path/to/manifest.json

Used by scripts/package.sh for the Chrome Web Store zip, and by scripts/dev/chrome-setup-test.mjs,
which loads the result in Chromium and checks the Chrome build finds Canvas on its own."""
import json
import sys

path = sys.argv[1]
m = json.load(open(path))
bg = m.get('background', {})
bg.pop('scripts', None)      # Firefox / older-Safari background page; Chrome MV3 runs the service worker
bg.pop('persistent', None)
m['background'] = bg
m.pop('author', None)        # not a Chrome key (it would only produce an "unrecognized key" warning)
# Safari draws the toolbar button from the icon's alpha channel, so the source manifest points it at the
# mark-only glyphs; Chrome shows the toolbar icon in colour, so the Chrome build uses the blue tile there.
m.setdefault('action', {})['default_icon'] = {s: f'icons/icon-{s}.png' for s in ('48', '96', '128')}
# The Chrome build finds Canvas on its own: schools host Canvas at addresses of their own, so it may
# look at any page — one small script (content/sniff.js) that reads the page's markup for Canvas's and,
# finding it, turns the interface on for that site; on every other page it does nothing. Safari asks
# for each site as it is opened, so the source manifest keeps to Canvas's own domain plus the sites
# added by hand, and the sniffer is not in it.
m['host_permissions'] = ['*://*/*']
m.pop('optional_host_permissions', None)
if not any('content/sniff.js' in (cs.get('js') or []) for cs in m.get('content_scripts', [])):
    m.setdefault('content_scripts', []).append({
        'matches': ['*://*/*'],
        'exclude_matches': ['*://*.instructure.com/*'],
        'run_at': 'document_idle',
        'js': ['content/sniff.js'],
    })
assert len(m['description']) <= 132, 'the Chrome Web Store uses the manifest description as the summary (132 characters max)'
with open(path, 'w') as f:
    json.dump(m, f, indent=2)
    f.write('\n')

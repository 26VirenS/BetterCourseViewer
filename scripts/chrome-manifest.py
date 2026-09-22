#!/usr/bin/env python3
"""Turns the source manifest (Safari's, which also loads in Firefox) into the Chrome / Edge one, in place.

    python3 scripts/chrome-manifest.py path/to/manifest.json [--no-sniffer]

Used by scripts/package.sh for the Chrome Web Store zips, and by scripts/dev/chrome-setup-test.mjs,
which loads the result in Chromium and checks the Chrome build finds Canvas on its own.

--no-sniffer makes the quiet build: Canvas's own domain and nothing else, no run of every site and
no script that looks at one, for a listing that would rather not ask for the broad permission. A
school's own Canvas address is added by hand there (the toolbar button's Enable on this site)."""
import json
import sys

path = sys.argv[1]
sniffer = '--no-sniffer' not in sys.argv[2:]
m = json.load(open(path))
bg = m.get('background', {})
bg.pop('scripts', None)      # Firefox / older-Safari background page; Chrome MV3 runs the service worker
bg.pop('persistent', None)
m['background'] = bg
m.pop('author', None)        # not a Chrome key (it would only produce an "unrecognized key" warning)
# Safari draws the toolbar button from the icon's alpha channel, so the source manifest points it at the
# mark-only glyphs; Chrome shows the toolbar icon in colour, so the Chrome build uses the blue tile there.
m.setdefault('action', {})['default_icon'] = {s: f'icons/icon-{s}.png' for s in ('48', '96', '128')}
# The extension finds Canvas on its own: schools host Canvas at addresses of their own, so one small
# script (content/sniff.js, a content script in the source manifest for every build) reads the markup
# of any page it may look at for Canvas's and, finding it, turns the interface on for that site; on
# every other page it does nothing. Chrome lets it look at every site from the start (host_permissions
# below); Safari and Firefox let it look at the sites they are told it may — every website at once
# from the page after install, or a site at a time. The append below is kept for a source manifest
# without the sniffer.
m['permissions'] = [p for p in m.get('permissions', []) if p != 'nativeMessaging']  # Safari's line to the Mac app; Chrome has no app to talk to
if sniffer:
    m['host_permissions'] = ['*://*/*']
    m.pop('optional_host_permissions', None)
    if not any('content/sniff.js' in (cs.get('js') or []) for cs in m.get('content_scripts', [])):
        m.setdefault('content_scripts', []).append({
            'matches': ['*://*/*'],
            'exclude_matches': ['*://*.instructure.com/*'],
            'run_at': 'document_idle',
            'js': ['content/sniff.js'],
        })
else:
    # the quiet build: Canvas's own domain, and every other site asked for one at a time
    m['host_permissions'] = ['*://*.instructure.com/*']
    m['optional_host_permissions'] = ['*://*/*']
    quiet_out = ('content/sniff.js', 'content/popout.js')  # both look at sites the quiet build never asks for
    m['content_scripts'] = [cs for cs in m.get('content_scripts', []) if not any(f in (cs.get('js') or []) for f in quiet_out)]
assert len(m['description']) <= 132, 'the Chrome Web Store uses the manifest description as the summary (132 characters max)'
with open(path, 'w') as f:
    json.dump(m, f, indent=2)
    f.write('\n')

#!/usr/bin/env python3
"""Turns the source manifest (Safari's, which also loads in Firefox) into the Chrome / Edge one, in place.

    python3 scripts/chrome-manifest.py path/to/manifest.json [--no-sniffer]

Used by scripts/package.sh for the Chrome Web Store zips, and by scripts/dev/chrome-setup-test.mjs,
which loads the result in Chromium and checks the Chrome build finds Canvas on its own.

--no-sniffer makes the quiet build: Canvas's own domain and nothing else, no run of every site and
no script that looks at one, for a listing that would rather not ask for the broad permission. A
school's own Canvas address is added by hand there (the toolbar button's Enable on this site).

Either build asks for nothing that carries a warning beyond its site access. Chrome switches every
installed copy OFF at an update that brings a permission with a new warning — "The newest version
has been disabled because it requires more permissions" — until its owner presses Re-enable, and a
host named by a content script counts among the sites the extension reads. Two updates did exactly
that (tabs, "Read your browsing history", at 2.8; the on-demand modules' never-matching host at
2.90), so both are left out below, and scripts/dev/chrome-setup-test.mjs holds each build's
permission list and host list exactly. Nothing with a warning goes in without that in mind."""
import json
import sys

path = sys.argv[1]
sniffer = '--no-sniffer' not in sys.argv[2:]
LAZY = 'https://lazy.simplcourses.invalid/*'
m = json.load(open(path))
bg = m.get('background', {})
bg.pop('scripts', None)      # Firefox / older-Safari background page; Chrome MV3 runs the service worker
bg.pop('persistent', None)
m['background'] = bg
m.pop('author', None)        # not a Chrome key (it would only produce an "unrecognized key" warning)
# Safari draws the toolbar button from the icon's alpha channel, so the source manifest points it at the
# mark-only glyphs; Chrome shows the toolbar icon in colour, so the Chrome build uses the blue tile there.
m.setdefault('action', {})['default_icon'] = {s: f'icons/icon-{s}.png' for s in ('48', '96', '128')}
# Permissions with a warning stay out. nativeMessaging is Safari's line to the Mac app (Chrome has no app
# to talk to). tabs is what lets Safari and Firefox see a tab arrive on a Canvas the extension is not yet
# allowed on; Chrome reads a tab's address wherever it has the site anyway, and the popup reads the tab it
# is over from the press itself (activeTab). storage, unlimitedStorage, scripting and activeTab carry none.
m['permissions'] = [p for p in m.get('permissions', []) if p not in ('nativeMessaging', 'tabs')]
# The on-demand modules (content/app/lazy.js) sit, in the source manifest, in a content-script group whose
# match never fires: Safari parses none of them until asked, and the iPhone app injects the group whole.
# Chrome would list that host among the sites the extension reads — a new site at an update — so the group
# goes; the background loads the same files from content/app/lazy-modules.js, the list both of them read.
m['content_scripts'] = [cs for cs in m.get('content_scripts', []) if LAZY not in (cs.get('matches') or [])]
# The extension finds Canvas on its own: schools host Canvas at addresses of their own, so one small
# script (content/sniff.js, a content script in the source manifest for every build) reads the markup
# of any page it may look at for Canvas's and, finding it, turns the interface on for that site; on
# every other page it does nothing. Chrome lets it look at every site from the start (host_permissions
# below); Safari and Firefox let it look at the sites they are told it may — every website at once
# from the page after install, or a site at a time. The append below is kept for a source manifest
# without the sniffer.
if sniffer:
    m['host_permissions'] = ['*://*/*']
    m.pop('optional_host_permissions', None)
    if not any('content/sniff.js' in (cs.get('js') or []) for cs in m['content_scripts']):
        m['content_scripts'].append({
            'matches': ['*://*/*'],
            'exclude_matches': ['*://*.instructure.com/*'],
            'run_at': 'document_idle',
            'js': ['content/sniff.js'],
        })
else:
    # the quiet build: Canvas's own domain, and every other site asked for one at a time
    m['host_permissions'] = ['*://*.instructure.com/*']
    m['optional_host_permissions'] = ['*://*/*']
    m['content_scripts'] = [cs for cs in m['content_scripts'] if 'content/sniff.js' not in (cs.get('js') or [])]  # it looks at sites the quiet build never asks for
    # The bar over a tool's own tab stays, narrowed to what this build asks for: Canvas's own domain
    # (where a launch lands) and whatever site the reader has since said yes to, one at a time.
    for cs in m['content_scripts']:
        if 'content/toolbar.js' in (cs.get('js') or []):
            cs['matches'] = ['*://*.instructure.com/*']
assert len(m['description']) <= 132, 'the Chrome Web Store uses the manifest description as the summary (132 characters max)'
with open(path, 'w') as f:
    json.dump(m, f, indent=2)
    f.write('\n')

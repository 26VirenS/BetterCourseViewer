/* The extension finds Canvas on its own. Schools host Canvas at addresses of their own choosing
 * (canvas.school.edu, learn.school.edu), so the extension may look at any page it is allowed on —
 * Chrome allows every site from the start; Safari and Firefox allow each site as they are asked, or
 * every website at once from the page after install — and this is everything it does there: a
 * handful of reads of the page's own markup, at idle, to tell whether it is a Canvas page. On any
 * other page it does nothing, keeps nothing and asks for nothing. On a Canvas page — signed in, or
 * Canvas's own sign-in page — it tells the background once, which turns the interface on for that
 * site (exactly what Enable on this site does from the toolbar) and loads the page again so the
 * interface, and the setup until it is done, come up on it now.
 * Canvas's own domain (*.instructure.com) is built in and never comes here; a site already on has
 * the interface's scripts in this world, and this does nothing there either.
 * (2.99.22) Brightspace too: a school's own Brightspace address (d2l.school.edu, learn.school.edu) is a
 * page under /d2l/ that names who is signed in on <html data-global-context>, or Brightspace's sign-in
 * page; Brightspace's own domain (*.brightspace.com) is built in like Canvas's. */
(function () {
  if (window.self !== window.top) return; // a framed page is someone else's
  if (/^(www\.)?simplcourses\.com$/.test(location.hostname) && /^\/community(\/|$)/.test(location.pathname)) { community(); return; }
  if (self.BCV) return; // the interface is on for this site already
  const doc = document;
  const app = doc.getElementById('application');
  // Canvas's own page wrapper or its global navigation. Both are Canvas's markup and nobody else's,
  // which is the point: a tool launched out of Canvas is a site of its own, and one that wears the
  // school's Canvas theme carries Canvas's stylesheet bundle with it. That bundle used to be enough
  // to pass for Canvas here, and a tool's own address was registered as a Canvas site on the
  // strength of it. It is not enough on its own any more.
  const canvas = !!(app && app.classList.contains('ic-app'))
    || !!doc.querySelector('#global_nav_tray_container, header.ic-app-header, #mobile-header.ic-app-header');
  // Brightspace: its pages, and nothing else, live under /d2l/ with its context on the root element (or its own navigation)
  const d2l = !canvas && /^\/d2l(\/|$)/i.test(location.pathname)
    && (doc.documentElement.hasAttribute('data-global-context') || !!doc.querySelector('d2l-navigation, d2l-navigation-s, .d2l-page-main, form[action*="/d2l/lp/auth/login/"]'));
  if (d2l) {
    let user = null;
    try { user = JSON.parse(doc.documentElement.getAttribute('data-global-context') || '{}')?.userId || null; } catch { /* none */ }
    const atLogin = /^\/d2l\/(login|lp\/auth\/login)/i.test(location.pathname);
    if (!user && !atLogin) return;
    try {
      const rt = (self.browser || self.chrome).runtime;
      Promise.resolve(rt.sendMessage({ type: 'canvasSeen', origin: location.origin, signedIn: !!user, lms: 'd2l' })).catch(() => {});
    } catch { /* no runtime here */ }
    return;
  }
  if (!canvas) return;
  // signed in: Canvas's inline ENV carries the user's id
  let signedIn = false;
  for (const s of doc.querySelectorAll('script:not([src])')) {
    const t = s.textContent || '';
    if (!/\bENV\s*=\s*\{/.test(t.slice(0, 4000))) continue;
    signedIn = /"current_user_id"\s*:\s*"?\d/.test(t);
    break;
  }
  // or it is Canvas's own sign-in page: .ic-Login is that page's own wrapper. A plain form pointing
  // at /login/ is not — half the web has one, and a tool's sign-in was passing for Canvas's.
  const login = !!doc.querySelector('.ic-Login');
  if (!signedIn && !login) return; // a public page of some other school's Canvas: nothing to set up
  try {
    const rt = (self.browser || self.chrome).runtime;
    Promise.resolve(rt.sendMessage({ type: 'canvasSeen', origin: location.origin, signedIn })).catch(() => {});
  } catch { /* no runtime here */ }
  /* (2.99.106) Simpl Community (simplcourses.com/community): Simpl in this browser marks the page, which then puts an Add
   * beside each Preview, and answers that Add — the theme read from the Community's own list (only its id comes from the
   * page), worn in full by the theme code every Canvas page has (lib/theme.js wearCommunity, put into this page by the
   * background when first needed), and the page told how it went. Nothing else is read or kept here. */
  function community() {
    const say = (m) => window.postMessage({ source: 'simpl-extension', ...m }, location.origin);
    document.documentElement.dataset.simplExtension = '1';
    say({ type: 'ready' });
    let busy = false;
    window.addEventListener('message', async (e) => {
      const d = e.data;
      if (e.source !== window || e.origin !== location.origin || !d || d.source !== 'simpl-community' || d.type !== 'add') return;
      const id = String(d.id || '');
      if (!/^[a-z0-9]{4,24}$/.test(id)) return;
      if (busy) { say({ type: 'added', id, ok: false, message: 'Another theme is being added. Try again in a moment.' }); return; }
      busy = true;
      try {
        if (!self.BCV?.theme?.wearCommunity) {
          const r = await (self.browser || self.chrome).runtime.sendMessage({ type: 'communityKit' });
          if (!r?.ok || !self.BCV?.theme?.wearCommunity) throw new Error('Simpl could not get ready on this page. Reload it and try again.');
        }
        const res = await fetch(`${location.origin}/api/community/themes`, { cache: 'no-store' });
        const theme = res.ok ? ((await res.json()).themes || []).find((t) => t.id === id) : null;
        if (!theme) throw new Error('That theme is not in the Community any more.');
        await self.BCV.theme.wearCommunity(theme);
        say({ type: 'added', id, ok: true });
      } catch (err) {
        say({ type: 'added', id, ok: false, message: String(err?.message || 'That did not work. Try again.') });
      }
      busy = false;
    });
  }
})();

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
 * the interface's scripts in this world, and this does nothing there either. */
(function () {
  if (window.self !== window.top) return; // a framed page is someone else's
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
})();

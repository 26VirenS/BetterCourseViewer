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
  // Canvas's page wrapper, its stylesheet bundle, or its global navigation: any of them is Canvas's own markup
  const canvas = !!(app && app.classList.contains('ic-app'))
    || !!doc.querySelector('link[rel="stylesheet"][href*="brandable_css"]')
    || !!doc.querySelector('#global_nav_tray_container, header.ic-app-header, #mobile-header.ic-app-header');
  if (!canvas) return;
  // signed in: Canvas's inline ENV carries the user's id; the sign-in page carries its own form
  let signedIn = false;
  for (const s of doc.querySelectorAll('script:not([src])')) {
    const t = s.textContent || '';
    if (!/\bENV\s*=\s*\{/.test(t.slice(0, 4000))) continue;
    signedIn = /"current_user_id"\s*:\s*"?\d/.test(t);
    break;
  }
  const login = !!doc.querySelector('.ic-Login, #login_form, form[action*="/login/"]');
  if (!signedIn && !login) return; // a public page of some other school's Canvas: nothing to set up
  try {
    const rt = (self.browser || self.chrome).runtime;
    Promise.resolve(rt.sendMessage({ type: 'canvasSeen', origin: location.origin, signedIn })).catch(() => {});
  } catch { /* no runtime here */ }
})();

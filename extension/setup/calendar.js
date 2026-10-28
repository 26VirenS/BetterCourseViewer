/* (2.99.107) The press that lets Simpl read a linked calendar's site (setup/calendar.html): the browser's question is
 * asked from it, then the calendar is linked as it would have been from the Calendar's own sheet, which hears of it
 * through storage and shows it. The address is after the # (never sent anywhere by being there). */
(function () {
  const api = typeof browser !== 'undefined' && browser.runtime ? browser : chrome;
  const $ = (id) => document.getElementById(id);
  // (2.99.124) #account:<kind>:<the sites, as JSON>: a calendar account about to be signed in to in Sync (lib/calaccounts.js),
  // whose sites the browser asks about from this press; the sign-in itself is pressed again in Sync, where the
  // password was typed (never carried here)
  const acct = /^#account:(icloud|google|microsoft):(.+)$/.exec(location.hash);
  if (acct) {
    const NAMES = { icloud: 'iCloud Calendar', google: 'Google Calendar', microsoft: 'Outlook' };
    let origins = [];
    try { origins = JSON.parse(decodeURIComponent(acct[2])).filter((o) => typeof o === 'string'); } catch { origins = []; }
    document.title = 'Sign in to a calendar';
    $('title').textContent = `Let Simpl reach ${NAMES[acct[1]]}`;
    $('where').textContent = `Your browser asks before Simpl reaches a new site. Allow it, then go back to Simpl's Calendar and press Sign In again.`;
    $('allow').textContent = 'Allow';
    let done = false;
    $('allow').addEventListener('click', async () => {
      if (done) { window.close(); return; }
      let ok = false;
      try { ok = await api.permissions.request({ origins }); } catch (e) { $('msg').textContent = `The browser did not ask (${e?.message || e}).`; return; } // (straight from the press)
      if (!ok) { $('msg').textContent = 'Not allowed, so Simpl cannot sign in to it.'; return; }
      done = true;
      document.documentElement.dataset.state = 'ok';
      $('title').textContent = 'Allowed';
      $('where').textContent = `Go back to Simpl's Calendar → Sync and press Sign In again.`;
      $('msg').textContent = '';
      $('allow').textContent = 'Close this tab';
    });
    return;
  }
  let href = '';
  try { href = decodeURIComponent(location.hash.slice(1)); } catch { href = ''; }
  let host = '';
  try { host = new URL(href).hostname; } catch { host = ''; }
  if (!host) {
    $('title').textContent = 'Nothing to link';
    $('where').textContent = 'Open Calendar → Sync in Simpl and paste your calendar\'s link there.';
    $('allow').hidden = true;
    return;
  }
  $('title').textContent = `Let Simpl read ${host}`;
  let linked = false;
  $('allow').addEventListener('click', async () => {
    if (linked) { window.close(); return; }
    const btn = $('allow');
    let ok = false;
    try {
      ok = await api.permissions.request({ origins: [`${new URL(href).origin}/*`] }); // (straight from the press: the browser takes it only then)
    } catch (e) {
      $('msg').textContent = `The browser did not ask (${e?.message || e}).`;
      return;
    }
    if (!ok) { $('msg').textContent = 'Not allowed, so the calendar is not linked.'; return; }
    btn.disabled = true;
    $('msg').textContent = 'Reading your calendar…';
    let r = null;
    try { r = await api.runtime.sendMessage({ type: 'cal', op: 'add', url: href }); } catch (e) { r = { ok: false, message: e?.message || String(e) }; }
    btn.disabled = false;
    if (!r?.ok) { $('msg').textContent = r?.message || 'That calendar could not be linked.'; return; }
    linked = true;
    document.documentElement.dataset.state = 'ok';
    $('title').textContent = `${r.link.name} is linked`;
    $('where').textContent = `${r.count === 1 ? 'One event' : `${r.count} events`} read. It shows on Simpl's Calendar now, and plans keep clear of it.`;
    $('msg').textContent = '';
    btn.textContent = 'Close this tab';
  });
})();

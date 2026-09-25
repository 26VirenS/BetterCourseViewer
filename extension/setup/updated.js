/* The page after this update (setup/updated.html): whether the build's site access is still withheld
 * — Chrome keeps an updated extension running but withholds site access it did not have before,
 * until the puzzle piece's prompt is accepted — and the one press that asks for it here instead.
 * Opened once by background.js (openUpdated) on the Chrome build's first run after 2.98.15. */
(function () {
  const api = typeof browser !== 'undefined' && browser.runtime ? browser : chrome;
  const $ = (id) => document.getElementById(id);
  const declared = api.runtime.getManifest().host_permissions || [];
  const wanted = { origins: declared.includes('*://*/*') ? ['*://*/*'] : declared }; // (the Chrome build: every site; another build: what its manifest names)
  const state = (ok) => {
    document.documentElement.dataset.state = ok ? 'ok' : 'ask';
    $('ask').textContent = ok ? 'All set — nothing left to accept.' : 'Please accept the permissions.';
    $('where').hidden = ok;
    $('allow').textContent = ok ? 'Close this tab' : 'Allow Simpl to run';
  };
  async function has() {
    if (!wanted.origins.length) return true;
    try { return await api.permissions.contains(wanted); } catch { return false; }
  }
  $('allow').addEventListener('click', async () => {
    if (document.documentElement.dataset.state === 'ok') { window.close(); return; }
    let ok = false;
    try {
      ok = await api.permissions.request(wanted); // (straight from the press: the browser takes it only then)
    } catch (e) {
      $('msg').textContent = `Chrome did not take the request here (${e?.message || e}). Press the puzzle piece at the top right and accept there.`;
      return;
    }
    if (!ok) { $('msg').textContent = 'Not allowed. Press the puzzle piece at the top right and accept there.'; return; }
    state(true);
    $('msg').textContent = 'Simpl is running. Reload your Canvas tab to see it.';
  });
  has().then(state);
})();

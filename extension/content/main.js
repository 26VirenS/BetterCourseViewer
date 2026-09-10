/* Content entry point: loads settings, initialises features in order and
 * relays settings changes and popup messages. */
(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  if (BCV.__mainStarted) return;
  BCV.__mainStarted = true;

  async function start() {
    const page = BCV.pageContext.refresh();
    if (!page.isCanvas) return; // login pages, error pages, non-Canvas hosts
    const settings = await BCV.settings.get();
    const ctx = { settings, page };
    const order = ['theme', 'declutter', 'shell', 'due-dates', 'dashboard', 'course-home', 'todo', 'keyboard', 'embeds', 'smart'];
    const features = [...(BCV.features || [])].sort((a, b) => order.indexOf(a.id) - order.indexOf(b.id));
    const run = async (f) => {
      try {
        await f.init(ctx);
      } catch (e) {
        console.warn('[BetterCourseViewer] feature failed:', f.id, e);
      }
    };
    // The shell mounts the top bar that other features attach buttons to, so it
    // starts first (its own network calls happen after mounting). Everything
    // else runs concurrently so one slow Canvas API call does not delay the rest.
    const shell = features.find((f) => f.id === 'shell');
    if (shell) {
      const p = run(shell);
      await Promise.race([p, new Promise((r) => setTimeout(r, 50))]);
    }
    await Promise.allSettled(features.filter((f) => f !== shell).map(run));
    BCV.settings.onChange((next) => {
      ctx.settings = next;
      for (const f of features) {
        try {
          f.onSettings?.(next);
        } catch (e) {
          console.warn('[BetterCourseViewer] settings update failed:', f.id, e);
        }
      }
    });
  }

  // Messages from the popup (toggle panels, etc.)
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || typeof msg !== 'object') return false;
    switch (msg.type) {
      case 'ping':
        sendResponse({ ok: true, canvas: !!BCV.page?.isCanvas, kind: BCV.page?.kind });
        return false;
      case 'togglePanel':
        if (msg.panel === 'todo') BCV.todo?.toggle();
        else if (msg.panel === 'smart') BCV.smart?.toggle();
        sendResponse({ ok: true });
        return false;
      case 'openPalette':
        BCV.keyboard?.openPalette();
        sendResponse({ ok: true });
        return false;
      case 'openHelp':
        BCV.keyboard?.openHelp();
        sendResponse({ ok: true });
        return false;
      default:
        return false;
    }
  });

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();

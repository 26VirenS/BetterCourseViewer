/* A word from inside a framed tool, the moment it asks for a window of its own.
 *
 * A tool in the popup over Canvas sometimes wants a browser window — a sign-in, a viewer, a "this
 * needs to open in a new window" button. It is let through. Taking it away and pulling the address
 * into a frame instead looked tidy and broke the thing it was for: a sign-in is several pages and
 * talks back to the window that opened it, so a window that is not real, or is closed halfway,
 * leaves it stuck. The background watches the window instead and reclaims it once it has settled.
 *
 * What this is for is the moment before: only the frame knows a window is about to be asked for,
 * and that is when the popup should say so and ask for no other windows. It runs in the page's own
 * world — a content script in the usual isolated world has a window of its own, and would see
 * nothing of the page's window.open.
 *
 * Reaching it is not one hop. Canvas launches a tool into a frame of its own and the tool's page
 * sits in a frame inside that, so the hello is passed down: a frame that hears it passes it to its
 * own children. A frame that loads late would miss it, which is what made the first try in a popup
 * do nothing and the try after a Reload work — so a frame also asks upward when it starts, and is
 * answered. The hello carries a token which goes back on anything said, and a page that was never
 * greeted cannot say it.
 */
(function () {
  const HELLO = 'bcv:popout:hello';
  const ASK = 'bcv:popout:ask';
  const OPENING = 'bcv:popout:opening';
  if (window.top === window.self) return; // the popup's own page is not what this is for
  let token = '';

  /** Say a window is being asked for, so the popup can put its notice up before it appears. */
  const saying = (url) => {
    if (!token) return;
    let href = '';
    try { href = new URL(url || '', location.href).href; } catch { href = String(url || ''); }
    try { window.top.postMessage({ type: OPENING, token, url: href }, '*'); } catch { /* the top went */ }
  };

  /** Pass the hello on: the tool's own page is usually a frame inside the frame Canvas launched. */
  function passOn() {
    for (const f of document.querySelectorAll('iframe, frame')) {
      try { f.contentWindow?.postMessage({ type: HELLO, token }, '*'); } catch { /* not ours to reach */ }
    }
  }

  function arm(t) {
    const first = !token;
    token = t;
    if (!first) { passOn(); return; } // (a later hello only needs passing on again)
    const realOpen = window.open;
    window.open = function bcvOpen(url, name, features) {
      saying(url); // said first, so the notice is up before the window is
      return realOpen.call(window, url, name, features); // and the window is the tool's own, real, with its opener intact
    };
    for (const [ev, pick] of [['click', (e) => e.target?.closest?.('a[target="_blank"], a[target="_new"]')?.href],
      ['submit', (e) => (/^_(blank|new)$/.test(e.target?.getAttribute?.('target') || '') ? e.target.action : '')]]) {
      document.addEventListener(ev, (e) => { const u = pick(e); if (u) saying(u); }, true);
    }
    passOn();
    for (const ms of [150, 600, 1500, 3000]) setTimeout(passOn, ms); // (frames that arrive later)
  }

  window.addEventListener('message', (e) => {
    if (e.data?.type === HELLO && e.data.token) { arm(String(e.data.token)); return; }
    if (e.data?.type === ASK && token && e.source && e.source !== window) { // a child asking to be armed
      try { e.source.postMessage({ type: HELLO, token }, '*'); } catch { /* it will ask again */ }
    }
  });

  // ask to be armed, rather than only waiting to be told: a frame that loads after the hellos have
  // stopped would otherwise never hear one, and this is the first thing that runs in it
  const askUp = () => {
    if (token) return;
    for (const w of [window.parent, window.top]) {
      try { w?.postMessage({ type: ASK }, '*'); } catch { /* not ours to reach */ }
    }
  };
  askUp();
  for (const ms of [100, 400, 1000, 2500, 5000]) setTimeout(askUp, ms);
  window.addEventListener('load', askUp);
})();

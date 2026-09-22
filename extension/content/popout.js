/* A window a framed tool opens for itself, taken before the browser makes one.
 *
 * A tool in the popup over Canvas sometimes wants a window of its own — a sign-in, a viewer, a
 * "this needs to open in a new browser window" button. Catching that from the page is impossible:
 * the frame is another origin and its window.open is out of reach from outside. Catching the tab
 * afterwards, from the background, is possible but always late — the browser has already made the
 * tab and the best that can be done is take it away again, which is a flash of a tab either way.
 *
 * This runs inside the frame, in the page's own world — which it has to be: a content script in the
 * usual isolated world has a window of its own, and overriding window.open there leaves the page's
 * untouched. It has no extension API and wants none; postMessage is all it uses. It is asleep on
 * every page in the world until the popup that framed it says who it is, and stays asleep elsewhere.
 * Where a browser will not run a script in the page's world, the key is ignored, this ends up in the
 * isolated one and does nothing — and the background's catch of the tab afterwards is what is left.
 *
 * Reaching it is not one hop. Canvas launches a tool into a frame of its own, and the tool's page
 * sits in a frame inside that, so the popup's hello has to be passed down: a frame that is woken
 * passes the same hello to its own children, and keeps offering it for a while, since the inner
 * page often arrives after the outer one. The hello carries a token, the address is sent back up
 * to the top window with that token on it, and the popup takes it from nobody who cannot say it —
 * which is what stands in for knowing which frame a message really came from.
 */
(function () {
  const HELLO = 'bcv:popout:hello';
  const OPEN = 'bcv:popout:open';
  if (window.top === window.self) return; // the popup's own page is not what this is for
  let token = '';

  /** Enough of a window for a tool that checks whether its popup was blocked. */
  const stub = () => {
    const noop = () => {};
    const w = {
      closed: false, opener: null, name: '',
      close() { w.closed = true; }, focus: noop, blur: noop, postMessage: noop,
      location: { href: '', assign: noop, replace: noop },
      document: { write: noop, writeln: noop, close: noop, open: () => w.document },
    };
    return w;
  };

  const handOver = (url) => {
    if (!token) return false;
    let href = '';
    try { href = new URL(url || '', location.href).href; } catch { href = String(url || ''); }
    if (!/^https?:/i.test(href)) return false; // about:blank and the like: nothing to hand over
    try { window.top.postMessage({ type: OPEN, token, url: href }, '*'); } catch { return false; }
    return true;
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
      // a tool asking for a window of its own: the popup above gets the address instead
      if (handOver(url)) return stub();
      return realOpen.call(window, url, name, features); // nothing to hand over: let the browser have it
    };
    // a link the tool would send to a new tab is the same request in markup
    document.addEventListener('click', (e) => {
      const a = e.target?.closest?.('a[target="_blank"], a[target="_new"]');
      if (!a || !a.href || e.defaultPrevented) return;
      if (handOver(a.href)) e.preventDefault();
    }, true);
    // and a form the tool aims at a new window (a GET can be replayed from its address; a POST cannot)
    document.addEventListener('submit', (e) => {
      const f = e.target;
      const t2 = f?.getAttribute?.('target') || '';
      if (!/^_(blank|new)$/.test(t2) || !f.action) return;
      if ((f.method || 'get').toLowerCase() !== 'get') return;
      if (handOver(f.action)) e.preventDefault();
    }, true);
    passOn();
    for (const ms of [150, 600, 1500, 3000]) setTimeout(passOn, ms); // (frames that arrive later)
  }

  window.addEventListener('message', (e) => {
    if (e.data?.type !== HELLO || !e.data.token) return;
    arm(String(e.data.token));
  });
})();

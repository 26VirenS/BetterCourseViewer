/* Simpl Courses — the sign-in reader. Runs in a world of its own (WKContentWorld "SimplLogin") on
 * every page the app's web view opens, apart from Canvas's own pages other than its login: the page's
 * scripts cannot see it, call it, or hear what it says to the app.
 *
 * It looks for a sign-in form — one password field and the username field that goes with it, or the
 * first step of a two-step sign-in (a username alone), or the second (a password alone) — and tells
 * the app what it found (webkit.messageHandlers.bcvLogin), once per change, for as long as the page
 * keeps drawing itself (a sign-in page that is an app draws its form late). It tells the app "none"
 * once the page has loaded with no form, so a code page (two-factor) is shown rather than covered.
 *
 * SimplLogin.fill({ user, pass }) puts what the app holds into the fields the reader found and
 * presses the form's own button, as typing and tapping would. It never reads a field back, and it
 * fills nothing on its own: the app decides, and only on the page where the details were given.
 * The Canvas host is filled in by the app (the placeholder on the first line below). */
(function () {
  'use strict';
  const CANVAS = __CANVAS_HOST__;
  if (self.SimplLogin) return;
  const post = (msg) => { try { webkit.messageHandlers.bcvLogin.postMessage(msg); } catch { /* no app */ } };
  // Canvas's own pages are not sign-in pages, apart from its login (the "canvas" login, or a school's /login/ldap)
  const onCanvas = location.hostname === CANVAS;
  if (onCanvas && !/^\/login(\/|$)/.test(location.pathname)) return;

  const shown = (el) => {
    if (!el || el.disabled || el.readOnly || el.type === 'hidden') return false;
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4) return false;
    const cs = getComputedStyle(el);
    return cs.visibility !== 'hidden' && cs.display !== 'none' && Number(cs.opacity) > 0.05;
  };
  const TEXTY = new Set(['', 'text', 'email', 'tel']);
  const USERISH = /user|login|email|e-mail|netid|uid|account|identifier|loginfmt|j_username|ucinetid|ldap|sso|id$/i;
  const fields = (root) => [...(root || document).querySelectorAll('input')];
  const userishOf = (el) => (el.autocomplete || '').includes('username') || el.type === 'email' || USERISH.test(`${el.name} ${el.id} ${el.getAttribute('aria-label') || ''} ${el.placeholder || ''}`);

  /** What the page asks for: { kind: 'full' | 'user' | 'pass' | 'none', user, pass, form } */
  function read() {
    const passes = fields().filter((el) => el.type === 'password' && shown(el));
    if (passes.length > 1) return { kind: 'none' }; // two password fields: a new password being set, not a sign-in
    if (passes.length === 1) {
      const pass = passes[0];
      const scope = pass.form || pass.closest('form, [role=form], main, body') || document;
      const before = fields(scope).filter((el) => TEXTY.has(el.type) && shown(el) && (el.compareDocumentPosition(pass) & Node.DOCUMENT_POSITION_FOLLOWING));
      const user = before.filter(userishOf).pop() || before.pop() || null;
      return { kind: user ? 'full' : 'pass', user, pass, form: pass.form || null };
    }
    // no password field: the first step of a two-step sign-in, where a username (or e-mail) is asked alone
    const texts = fields().filter((el) => TEXTY.has(el.type) && shown(el));
    if (texts.length >= 1 && texts.length <= 2) {
      const user = texts.find((el) => (el.autocomplete || '').includes('username') || el.type === 'email' || /^(user(name)?|login|email|loginfmt|identifier|j_username|netid|uid)$/i.test(el.name || el.id || ''));
      if (user && (user.form || document.querySelector('button, input[type=submit]'))) return { kind: 'user', user, pass: null, form: user.form || null };
    }
    return { kind: 'none' };
  }

  /** The words of an error the page shows beside its form (a wrong password), if it shows one. */
  function errorText() {
    const sel = '[role=alert], .alert-danger, .alert-error, .error, .errors, .form-error, #error, #errorText, .login-error, .text-danger, [class*="error-message"], [id*="error" i]';
    for (const el of document.querySelectorAll(sel)) {
      if (!shown(el) && !(el.offsetWidth || el.offsetHeight)) continue;
      const t = (el.textContent || '').replace(/\s+/g, ' ').trim();
      if (t && t.length > 3 && t.length < 240) return t.slice(0, 160);
    }
    return '';
  }

  // A single sign-on page that says the sign-in it was asked for has gone stale (Shibboleth's "Stale Request" after a
  // Back, or a request that expired while the app sat in the background): only starting again from Canvas mends it.
  const STALE = /\bstale request\b|\b(saml|authentication|login|sign-?in) request (has )?(expired|is no longer valid)\b|\brequest (has )?expired\b/i;
  const stale = () => { try { return STALE.test(`${document.title || ''} ${(document.body?.innerText || '').slice(0, 1500)}`); } catch { return false; } };

  let said = '';
  const msgOf = (r) => ({ kind: r.kind, host: location.hostname, path: location.pathname, title: document.title || '', error: r.kind === 'none' ? '' : errorText(), secure: location.protocol === 'https:', stale: r.kind === 'none' && stale() });
  function report(final = false) {
    const r = read();
    if (r.kind === 'none' && !final) return; // ("none" is only said once the page has finished drawing)
    const msg = msgOf(r);
    const key = JSON.stringify(msg);
    if (key === said) return;
    said = key;
    post(msg);
  }

  // as typing would: the value set through the field's own setter, then the events a page listens for
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
  function type(el, v) {
    el.focus();
    setter.call(el, v);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.blur();
  }
  /** The form's own button: its submit, else a button that says sign in / log in / next / continue. */
  function press(r) {
    const scope = r.form || (r.pass || r.user)?.closest('form, [role=form], main, body') || document;
    const buttons = [...scope.querySelectorAll('button, input[type=submit], input[type=button], [role=button]')].filter(shown);
    const named = (b) => /^(sign ?in|log ?in|login|next|continue|submit|verify|go)\b/i.test((b.value || b.textContent || b.getAttribute('aria-label') || '').trim());
    const btn = buttons.find((b) => (b.type === 'submit' && named(b))) || buttons.find((b) => b.type === 'submit') || buttons.find(named);
    if (btn) { btn.click(); return true; }
    if (r.form) { try { r.form.requestSubmit ? r.form.requestSubmit() : r.form.submit(); return true; } catch { return false; } }
    return false;
  }

  self.SimplLogin = {
    /** Fill what this page asks for from what the app holds, and press its button. Returns what was done. */
    fill(creds) {
      const r = read();
      if (r.kind === 'none' || !creds) return { done: 'none' };
      if (r.user && creds.user != null) type(r.user, String(creds.user));
      if (r.pass && creds.pass != null) type(r.pass, String(creds.pass));
      // the form as it stands now is already said: the page busy with the press is not news, an
      // error it then shows (a wrong password) or the next step's form is
      said = JSON.stringify({ ...msgOf(r), error: '' });
      const pressed = press(r);
      return { done: r.kind, pressed };
    },
    read: () => read().kind,
  };

  // the page is read as it arrives, and as it keeps drawing (a sign-in page that is an app), for a while
  let t = 0;
  const soon = () => { clearTimeout(t); t = setTimeout(() => report(false), 250); };
  const start = () => {
    report(false);
    try {
      const mo = new MutationObserver(soon);
      mo.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style', 'hidden', 'type'] });
      setTimeout(() => mo.disconnect(), 20000);
    } catch { /* read once */ }
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
  // loaded with nothing to fill: said so, once, a beat after the page has settled
  const settled = () => setTimeout(() => report(true), 700);
  if (document.readyState === 'complete') settled();
  else window.addEventListener('load', settled, { once: true });
})();

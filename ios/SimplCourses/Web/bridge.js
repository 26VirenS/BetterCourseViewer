/* Simpl Courses — the bridge that stands in for the browser-extension APIs when the extension's
 * scripts run inside the app's web view (or anywhere without an extension runtime). Injected first,
 * before lib/settings.js, it defines the `browser` object with the subset the scripts use:
 *
 *   storage.local get/set/remove/clear + storage.onChanged  → the app's storage file (native)
 *   runtime.sendMessage/onMessage, connect/onConnect        → an in-page message bus: background.js runs
 *                                                            in the same page, so its ports work exactly
 *                                                            as they do from the extension
 *   runtime.getManifest / openOptionsPage                    → the embedded manifest / the app's settings
 *   action, permissions, tabs                                → inert stubs (no badge, no extra sites)
 *
 * Every fetch is the page's own, with the Canvas session. Without a native side (tests, other hosts)
 * storage falls back to localStorage. The manifest placeholder on the first line below is filled in by
 * the app (never mention it up here: the manifest's host patterns contain the characters that end a comment). */
(function () {
  'use strict';
  if (self.browser && self.browser.__simpl) return;
  const MANIFEST = __MANIFEST__;
  const native = (typeof webkit !== 'undefined' && webkit.messageHandlers && webkit.messageHandlers.bcv) || null;
  const call = (msg) => (native ? Promise.resolve(native.postMessage(msg)) : Promise.reject(new Error('Simpl Courses: no native bridge')));
  const clone = (v) => (v === undefined ? undefined : JSON.parse(JSON.stringify(v)));
  const noop = () => {};
  const warn = (what, e) => console.error(`[Simpl Courses] ${what}`, e);

  // ---- storage ------------------------------------------------------------------------------
  const changeListeners = new Set();
  function emitChanged(changes) {
    if (!changes || typeof changes !== 'object' || !Object.keys(changes).length) return;
    for (const fn of [...changeListeners]) {
      try { fn(changes, 'local'); } catch (e) { warn('storage listener', e); }
    }
  }
  const keyList = (keys) => (keys == null ? null : Array.isArray(keys) ? keys.map(String) : typeof keys === 'object' ? Object.keys(keys) : [String(keys)]);
  function pick(data, keys) {
    if (keys == null) return clone(data);
    const out = {};
    if (typeof keys === 'object' && !Array.isArray(keys)) {
      for (const [k, d] of Object.entries(keys)) out[k] = k in data ? clone(data[k]) : clone(d);
    } else {
      for (const k of keyList(keys)) if (k in data) out[k] = clone(data[k]);
    }
    return out;
  }
  // No app underneath (tests, other hosts): the same contract on top of localStorage.
  let local = null;
  function fallback() {
    if (local) return local;
    let data = {};
    try { data = JSON.parse(localStorage.getItem('bcv:storage') || '{}') || {}; } catch { data = {}; }
    const persist = () => { try { localStorage.setItem('bcv:storage', JSON.stringify(data)); } catch { /* full or blocked */ } };
    const later = (changes) => queueMicrotask(() => emitChanged(changes));
    local = {
      get: async (keys) => pick(data, keys),
      set: async (items) => {
        const changes = {};
        for (const [k, v] of Object.entries(items || {})) {
          changes[k] = { ...(k in data ? { oldValue: clone(data[k]) } : {}), newValue: clone(v) };
          data[k] = clone(v);
        }
        persist();
        later(changes);
      },
      remove: async (keys) => {
        const changes = {};
        for (const k of keyList(keys) || []) if (k in data) { changes[k] = { oldValue: clone(data[k]) }; delete data[k]; }
        persist();
        later(changes);
      },
      clear: async () => {
        const changes = {};
        for (const k of Object.keys(data)) changes[k] = { oldValue: clone(data[k]) };
        data = {};
        persist();
        later(changes);
      },
    };
    return local;
  }
  const storage = {
    local: {
      get: (keys) => (native ? call({ op: 'storage.get', keys: keys === undefined ? null : keys }).then((r) => r || {}) : fallback().get(keys)),
      set: (items) => (native ? call({ op: 'storage.set', items: items || {} }).then(noop) : fallback().set(items)),
      remove: (keys) => (native ? call({ op: 'storage.remove', keys: keyList(keys) || [] }).then(noop) : fallback().remove(keys)),
      clear: () => (native ? call({ op: 'storage.clear' }).then(noop) : fallback().clear()),
    },
    onChanged: {
      addListener: (fn) => { changeListeners.add(fn); },
      removeListener: (fn) => { changeListeners.delete(fn); },
      hasListener: (fn) => changeListeners.has(fn),
    },
  };

  // ---- runtime: one-shot messages and ports, all inside this page --------------------------
  const messageListeners = new Set();
  const connectListeners = new Set();
  const sender = { id: 'simpl-courses', url: location.href };
  function sendMessage(message) {
    return new Promise((resolve, reject) => {
      const msg = clone(message);
      let handled = false;
      for (const fn of [...messageListeners]) {
        let responded = false;
        const respond = (r) => { if (responded) return; responded = true; resolve(r === undefined ? undefined : clone(r)); };
        let ret;
        try { ret = fn(msg, sender, respond); } catch (e) { reject(e); return; }
        if (ret === true) { handled = true; break; } // sendResponse comes later
        if (ret && typeof ret.then === 'function') { handled = true; ret.then(respond, reject); break; }
        if (responded) { handled = true; break; }
      }
      if (!handled) resolve(undefined);
    });
  }
  function makePorts(name) {
    const make = () => {
      const port = { name, _peer: null, _open: true, _msg: new Set(), _dis: new Set(), sender: undefined };
      port.onMessage = { addListener: (fn) => { port._msg.add(fn); }, removeListener: (fn) => { port._msg.delete(fn); }, hasListener: (fn) => port._msg.has(fn) };
      port.onDisconnect = { addListener: (fn) => { port._dis.add(fn); }, removeListener: (fn) => { port._dis.delete(fn); }, hasListener: (fn) => port._dis.has(fn) };
      port.postMessage = (m) => {
        const peer = port._peer;
        if (!port._open || !peer) throw new Error('Attempting to use a disconnected port object');
        const copy = clone(m);
        queueMicrotask(() => {
          if (!peer._open) return;
          for (const fn of [...peer._msg]) { try { fn(copy, peer); } catch (e) { warn('port listener', e); } }
        });
      };
      port.disconnect = () => {
        if (!port._open) return;
        port._open = false;
        const peer = port._peer;
        if (peer && peer._open) {
          peer._open = false;
          queueMicrotask(() => { for (const fn of [...peer._dis]) { try { fn(peer); } catch (e) { warn('port disconnect listener', e); } } });
        }
      };
      return port;
    };
    const a = make(), b = make();
    a._peer = b;
    b._peer = a;
    return [a, b];
  }
  function connect(info) {
    const [mine, theirs] = makePorts((info && info.name) || '');
    theirs.sender = sender;
    queueMicrotask(() => { for (const fn of [...connectListeners]) { try { fn(theirs); } catch (e) { warn('onConnect listener', e); } } });
    return mine;
  }
  const inert = () => ({ addListener: noop, removeListener: noop, hasListener: () => false });
  const runtime = {
    id: 'simpl-courses',
    sendMessage,
    connect,
    onMessage: { addListener: (fn) => { messageListeners.add(fn); }, removeListener: (fn) => { messageListeners.delete(fn); }, hasListener: (fn) => messageListeners.has(fn) },
    onConnect: { addListener: (fn) => { connectListeners.add(fn); }, removeListener: (fn) => { connectListeners.delete(fn); }, hasListener: (fn) => connectListeners.has(fn) },
    onInstalled: inert(),
    onStartup: inert(),
    getManifest: () => clone(MANIFEST),
    getURL: (path) => `simpl-courses://app/${String(path || '').replace(/^\/+/, '')}`,
    openOptionsPage: () => (native ? call({ op: 'openOptions' }).then(noop) : Promise.resolve()),
  };
  const action = { setBadgeText: async () => {}, setBadgeBackgroundColor: async () => {}, setTitle: async () => {} };
  const permissions = { contains: async () => true, request: async () => false, remove: async () => true, getAll: async () => ({ origins: [], permissions: [] }) };
  const tabs = { create: async () => ({}), query: async () => [], reload: async () => {} };
  // `scripting` is deliberately absent: the school is chosen in the app, not registered per site.
  self.browser = { __simpl: true, storage, runtime, action, permissions, tabs };

  // ---- what the app calls back into the page ------------------------------------------------------
  const BCVBridge = {
    /** What only the app can do (null in a browser or the test harness): the account sheet's "Sign out". */
    native: native ? { signOut: () => call({ op: 'signOut' }).then(noop) } : null,
    storageChanged: (changes) => emitChanged(changes),
  };
  self.BCVBridge = BCVBridge;
})();

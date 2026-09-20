/* The bridge under the settings page when it runs in the Mac app's window (ViewController.swift
 * injects this before the page's own scripts). It stands in for the browser's extension APIs with
 * the subset the page uses, each call a message to the app that answers with a promise:
 *
 *   browser.storage.local get/set/remove/clear + onChanged  → the shared store the extension reads
 *   browser.runtime.sendMessage / getManifest / getURL      → answered by the app (sites, a wipe…)
 *   browser.tabs.create                                     → the link opens in Safari
 *   browser.permissions                                     → yes: Safari asks for a site itself
 *
 * and SimplApp, the page's line to the app itself: its state (the extension in Safari, updates,
 * open at login) and the buttons that act on it. The manifest placeholder on the next line is
 * filled in by the app. */
(function () {
  'use strict';
  const MANIFEST = __MANIFEST__;
  const handler = (typeof webkit !== 'undefined' && webkit.messageHandlers && webkit.messageHandlers.simpl) || null;
  const call = (cmd, args) => (handler ? Promise.resolve(handler.postMessage({ cmd, ...(args || {}) })) : Promise.reject(new Error('Simpl Courses: no app underneath')));
  const storageListeners = new Set();
  const stateListeners = new Set();
  let state = null;

  const browser = {
    __simplApp: true,
    storage: {
      local: {
        get: (keys) => call('storage.get', { keys: keys === undefined ? null : keys }),
        set: (items) => call('storage.set', { items }).then(() => undefined),
        remove: (keys) => call('storage.remove', { keys: Array.isArray(keys) ? keys : [keys] }).then(() => undefined),
        clear: () => call('storage.clear').then(() => undefined),
      },
      onChanged: {
        addListener: (fn) => storageListeners.add(fn),
        removeListener: (fn) => storageListeners.delete(fn),
        hasListener: (fn) => storageListeners.has(fn),
      },
    },
    runtime: {
      sendMessage: (message) => call('runtime.sendMessage', { message }),
      getManifest: () => MANIFEST,
      getURL: (path) => new URL(path, location.href).href,
      openOptionsPage: () => Promise.resolve(),
      onMessage: { addListener() {}, removeListener() {}, hasListener: () => false },
      lastError: null,
    },
    tabs: {
      create: ({ url }) => call('open', { url }).then(() => ({ id: 0, url })),
      query: () => Promise.resolve([]),
      reload: () => Promise.resolve(),
      update: (_id, { url }) => (url ? call('open', { url }) : Promise.resolve()).then(() => ({ id: 0 })),
    },
    permissions: {
      contains: () => Promise.resolve(true),
      request: () => Promise.resolve(true),
      remove: () => Promise.resolve(true),
    },
    action: { setBadgeText: () => Promise.resolve(), setBadgeBackgroundColor: () => Promise.resolve() },
  };
  self.browser = browser;
  self.chrome = browser;

  // the app pushes both of these: a change to the store (the extension wrote), and its own state
  self.__simplStorageChanged = (changes) => { for (const fn of [...storageListeners]) { try { fn(changes, 'local'); } catch (e) { console.error('[Simpl Courses] storage listener', e); } } };
  self.__simplApp = (next) => { state = next; for (const fn of [...stateListeners]) { try { fn(next); } catch (e) { console.error('[Simpl Courses] state listener', e); } } };

  self.SimplApp = {
    call,
    /** The app's state now (asked for), and every change after. */
    onState(fn) { stateListeners.add(fn); if (state) fn(state); else call('app.state').then((s) => { if (s && !state) { state = s; fn(s); } }).catch(() => {}); },
    state: () => state,
    openSafariSettings: () => call('app.openSafariSettings'),
    checkUpdates: () => call('app.checkUpdates'),
    installUpdate: () => call('app.installUpdate'),
    setAutoUpdate: (on) => call('app.setAutoUpdate', { on: !!on }),
    setLoginItem: (on) => call('app.setLoginItem', { on: !!on }),
    openStore: () => call('app.openStore'),
    saveFile: (name, text) => call('file.save', { name, text }),
    openFile: (types) => call('file.open', { types: types || [] }),
  };
})();

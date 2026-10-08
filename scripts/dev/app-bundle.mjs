// The interface as the apps load it, for the tests that run it so (ios-test.mjs on the mock Canvas, d2l-test.mjs on the
// mock Brightspace): the extension's scripts bundled the way ios/SimplCourses/Web/ScriptBundle.swift bundles them, each
// kept to the school's host, and a stand-in for the app's native half (webkit.messageHandlers.bcv: its storage, its
// alert and action sheet, its file viewer, its swipe-back switch), every call kept for the checks.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * `initScript` loads the interface over the page as the app does; `shellScript` the same with the app's own chrome on
 * (the app fills the placeholder in: the native screens, the page as their engine); `nativeStub` goes first.
 */
export function appBundle({ root, host }) {
  const ext = join(root, 'extension');
  const manifest = JSON.parse(readFileSync(join(ext, 'manifest.json'), 'utf8'));
  const file = (rel) => readFileSync(join(ext, rel), 'utf8');
  const swift = (rel) => readFileSync(join(root, 'ios', 'SimplCourses', rel), 'utf8');
  const guardJS = `if(location.hostname!==${JSON.stringify(host)})return;`;
  const seen = new Set();
  const wrap = (rel) => {
    if (seen.has(rel)) return '';
    seen.add(rel);
    return `(function(){${guardJS}try{\n${file(rel)}\n}catch(e){console.error('[Simpl Courses] ${rel} failed',e)}})();\n`;
  };
  const bridge = swift('Web/bridge.js').split('__MANIFEST__').join(JSON.stringify(manifest));
  // the viewport the app gives Canvas's pages, read from ScriptBundle.swift itself
  const viewport = (swift('Web/ScriptBundle.swift').match(/static let viewport = "([^"]+)"/) || [])[1] || '';
  const start = [`(function(){${guardJS}\n${bridge}\n})();\n`, `(function(){${guardJS}var m=document.querySelector('meta[name=viewport]');if(!m){m=document.createElement('meta');m.name='viewport';(document.head||document.documentElement).appendChild(m);}m.content=${JSON.stringify(viewport)};})();\n`];
  const end = [];
  for (const rel of manifest.background?.scripts || ['lib/settings.js', 'lib/devcode.js', 'background.js']) start.push(wrap(rel));
  const css = [];
  for (const cs of manifest.content_scripts || []) {
    if ((cs.js || []).includes('content/sniff.js')) continue;
    const list = cs.run_at === 'document_start' ? start : end;
    for (const rel of cs.js || []) list.push(wrap(rel));
    for (const rel of cs.css || []) css.push(file(rel));
  }
  start.splice(1, 0, `(function(){${guardJS}var s=document.createElement('style');s.id='bcv-css';s.textContent=${JSON.stringify(css.join('\n'))};(document.head||document.documentElement).appendChild(s);})();\n`);
  end.push(`(function(){var m=document.querySelector('meta[name=viewport]');if(m&&location.hostname===${JSON.stringify(host)})m.content=${JSON.stringify(viewport)};})();\n`);
  end.push("(function(){var s=document.createElement('style');s.textContent='html{touch-action:manipulation}';(document.head||document.documentElement).appendChild(s);})();\n");
  end.push('(function(){var s=document.getElementById(\'bcv-css\');if(s&&document.body)document.body.appendChild(s);})();\n');
  const initScript = `(function () {
  function start() {\n${start.join('\n')}\n}
  if (document.documentElement) start();
  else new MutationObserver(function (m, o) { if (document.documentElement) { o.disconnect(); start(); } }).observe(document, { childList: true });
  document.addEventListener('DOMContentLoaded', function () {\n${end.join('\n')}\n});
})();`;
  const shellScript = initScript.split("'__SHELL__' === 'on'").join("'on' === 'on'");

  // ---- the app's native half, stood in for: storage (with its broadcast), the alert, the action sheet,
  // the file viewer and the swipe-back switch, every call kept for the checks
  const setupFlow = Number((file('background.js').match(/const SETUP_FLOW = (\d+)/) || [])[1]) || 0;
  const nativeStub = `(function () {
  var KEY = 'bcv:native-store';
  var load = function () { try { return JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { return {}; } };
  var save = function (d) { try { localStorage.setItem(KEY, JSON.stringify(d)); } catch (e) {} };
  if (!localStorage.getItem(KEY)) save({ 'setup:flow': ${setupFlow}, 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tools:welcomed': true, 'whatsnew:seen': ${JSON.stringify(manifest.version)} });
  var calls = self.__nativeCalls = [];
  self.__nativeAnswers = { ask: true, menu: 0 };
  var tell = function (changes) { setTimeout(function () { try { self.BCVBridge && self.BCVBridge.storageChanged(changes); } catch (e) {} }, 0); };
  self.webkit = { messageHandlers: { bcv: { postMessage: function (msg) {
    calls.push(JSON.parse(JSON.stringify(msg)));
    var d = load(), changes = {}, k;
    switch (msg.op) {
      case 'storage.get': {
        var keys = msg.keys;
        if (keys == null) return Promise.resolve(d);
        var list = Array.isArray(keys) ? keys : (typeof keys === 'object' ? Object.keys(keys) : [keys]);
        var res = {};
        list.forEach(function (key) { if (key in d) res[key] = d[key]; else if (keys && typeof keys === 'object' && !Array.isArray(keys)) res[key] = keys[key]; });
        return Promise.resolve(res);
      }
      case 'storage.set':
        for (k in msg.items) { changes[k] = { oldValue: d[k], newValue: msg.items[k] }; d[k] = msg.items[k]; }
        save(d); tell(changes); return Promise.resolve(null);
      case 'storage.remove':
        (msg.keys || []).forEach(function (key) { if (key in d) { changes[key] = { oldValue: d[key] }; delete d[key]; } });
        save(d); tell(changes); return Promise.resolve(null);
      case 'storage.clear': save({}); return Promise.resolve(null);
      case 'ask': return Promise.resolve({ ok: !!self.__nativeAnswers.ask });
      case 'menu': return Promise.resolve({ index: self.__nativeAnswers.menu });
      case 'previewFile': return Promise.resolve({ ok: true });
      default: return Promise.resolve(null);
    }
  } } } };
})();`;

  return { manifest, file, swift, viewport, initScript, shellScript, nativeStub };
}

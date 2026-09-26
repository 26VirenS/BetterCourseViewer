/* Code on demand. The parts of the interface most pages never touch — the guided setup with its
 * stylesheet and Personalize, the quiz flow, the hand-in block and the feedback screen, the tools'
 * own bodies, the What's New notes, the phone layout — used to be parsed on every Canvas page
 * (some seven hundred kilobytes of it, held in memory by every tab). Each is a stub here until it
 * is asked for; the first call loads the real module — the background lands its files in this
 * page's isolated world, the way the converter's libraries arrive — and hands over to it.
 *
 * The list of modules — each name, its files in order, what it needs first — is content/app/
 * lazy-modules.js, a data script the background reads too: it lands the files in this page's
 * isolated world, and loads nothing outside that list. The source manifest names the same files in
 * a content-script group whose match never fires (lazy.simplcourses.invalid), so Safari parses none
 * of them until asked; where the whole extension goes in at once — the iPhone app has no scripting
 * API, so it injects every group from the manifest — the real modules land right after these stubs
 * and replace them, and load() finds them already here. The Chrome builds leave that group out
 * (Chrome would list its host as a site the extension reads) and load from the list alone. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = BCV.api;
  BCV.screens = BCV.screens || {};

  /** How to tell a module is here, by name; its files and what it needs first are lazy-modules.js's. */
  const HAS = {
    setupcss: () => typeof self.BCV_SETUP_CSS === 'string',
    setup: () => !!BCV.setup && !BCV.setup.__stub,
    quiz: () => !!BCV.screens.quiz && !BCV.screens.quiz.__stub,
    submit: () => !!BCV.screens.submit && !BCV.screens.submit.__stub && !!BCV.screens.feedback && !BCV.screens.feedback.__stub,
    phone: () => !!BCV.phone,
    notes: () => Array.isArray(self.BCV_WHATS_NEW),
    hub: () => !!BCV.hub,
    widgets: () => !!BCV.widgets,
    starters: () => Array.isArray(self.BCV_WIDGET_STARTERS),
    'tool:cite': () => !!BCV.toolsCite,
    'tool:fc': () => !!BCV.toolsCards,
    'tool:conv': () => !!BCV.toolsConvert,
    'tool:need': () => !!BCV.toolsNeed,
    'tool:pdfx': () => !!BCV.toolsPdfs,
    'tool:mark': () => !!BCV.toolsMark,
    'tool:ocr': () => !!BCV.toolsOcr,
    'tool:ptable': () => !!BCV.toolsPtable,
  };
  /** name → its files, in order, what it needs first, and how to tell the module is here. */
  const MODULES = {};
  for (const [name, m] of Object.entries(self.BCV_LAZY_MODULES || {})) MODULES[name] = { needs: m.needs || [], files: m.files || [], has: HAS[name] };
  const loading = {};
  /** The module, loaded if it is not here yet: resolves once its globals are in place. Rejects when
   *  the page cannot ask for it (no background to ask) and it is not here. */
  function load(name) {
    const m = MODULES[name];
    if (!m || !m.has) return Promise.reject(new Error(`No such module: ${name}`));
    if (m.has()) return Promise.resolve(true);
    if (loading[name]) return loading[name];
    loading[name] = (async () => {
      try {
        for (const dep of m.needs || []) await load(dep); // what it draws with, first
        let r = null;
        const fail = (msg) => { BCV.errors?.failed?.('LOAD'); return Object.assign(new Error(msg), { kind: 'LOAD' }); }; // (lib/errors.js: SC-…-LOAD)
        try { r = await api.runtime.sendMessage({ type: 'load', files: m.files }); } catch (e) { throw fail(`Could not load ${name}: ${e?.message || e}`); }
        if (!r?.ok) throw fail(r?.message || `Could not load ${name}`);
        if (!m.has()) throw fail(`${name} did not load`);
        return true;
      } finally { delete loading[name]; }
    })();
    return loading[name];
  }
  const has = (name) => !!MODULES[name]?.has?.();
  /** The module a tool's key lives in, if its body is one of the on-demand ones. */
  const toolModule = (key) => (MODULES[`tool:${key}`] ? `tool:${key}` : null);

  // ---- the stubs: the same names, loading on the first call --------------------------------------
  const stub = (obj) => Object.assign(obj, { __stub: true });
  if (!BCV.setup) BCV.setup = stub({ active: () => false, open: async (...a) => { await load('setup'); return BCV.setup.open(...a); } });
  if (!BCV.personalize) BCV.personalize = stub({ open: async (...a) => { await load('setup'); return BCV.personalize.open(...a); }, close: () => {} });
  for (const [key, name] of [['quiz', 'quiz'], ['submit', 'submit'], ['feedback', 'submit']]) {
    if (!BCV.screens[key]) BCV.screens[key] = stub({ render: async (...a) => { await load(name); return BCV.screens[key].render(...a); } });
  }

  BCV.lazy = { load, has, toolModule, MODULES };
})();

/* Code on demand. The parts of the interface most pages never touch — the guided setup with its
 * stylesheet and Personalize, the quiz flow, the hand-in block and the feedback screen, the tools'
 * own bodies, the What's New notes, the phone layout — used to be parsed on every Canvas page
 * (some seven hundred kilobytes of it, held in memory by every tab). Each is a stub here until it
 * is asked for; the first call loads the real module — the background lands its files in this
 * page's isolated world, the way the converter's libraries arrive — and hands over to it.
 *
 * The manifest lists these files in a content-script group of their own whose match never fires
 * (lazy.simplcourses.invalid), so a browser parses none of them until asked, and the background
 * loads only what that group names. Where the whole extension goes in at once — the iPhone app has
 * no scripting API, so it injects every group from the manifest — the real modules land right after
 * these stubs and replace them, and load() finds them already here. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = BCV.api;
  BCV.screens = BCV.screens || {};

  /** name → its files, in order, and how to tell the module is here. */
  const MODULES = {
    setupcss: { files: ['setup/setup-css.js'], has: () => typeof self.BCV_SETUP_CSS === 'string' }, // the setup's stylesheet: the setup, Personalize and What's New draw with it
    setup: { needs: ['setupcss'], files: ['content/app/personalize.js', 'content/app/setup.js'], has: () => !!BCV.setup && !BCV.setup.__stub },
    quiz: { files: ['content/app/quiz-page.js', 'content/app/screens/quiz.js'], has: () => !!BCV.screens.quiz && !BCV.screens.quiz.__stub },
    submit: { files: ['content/app/screens/submit.js', 'content/app/screens/feedback.js'], has: () => !!BCV.screens.submit && !BCV.screens.submit.__stub && !!BCV.screens.feedback && !BCV.screens.feedback.__stub },
    phone: { files: ['content/app/phone.js'], has: () => !!BCV.phone },
    notes: { needs: ['setupcss'], files: ['content/app/whatsnew-notes.js'], has: () => Array.isArray(self.BCV_WHATS_NEW) },
    hub: { files: ['content/app/hub.js'], has: () => !!BCV.hub }, // the search box's commands, answers and row actions: loaded when the box is focused
    'tool:cite': { files: ['content/app/tools/cite.js'], has: () => !!BCV.toolsCite },
    'tool:fc': { files: ['content/app/tools/cards.js'], has: () => !!BCV.toolsCards },
    'tool:conv': { files: ['content/app/tools/convert.js'], has: () => !!BCV.toolsConvert },
    'tool:need': { files: ['content/app/tools/need.js'], has: () => !!BCV.toolsNeed },
    'tool:pdfx': { files: ['content/app/tools/pdfs.js'], has: () => !!BCV.toolsPdfs },
    'tool:mark': { files: ['content/app/tools/mark.js'], has: () => !!BCV.toolsMark },
    'tool:ocr': { files: ['content/app/tools/ocr.js'], has: () => !!BCV.toolsOcr },
    'tool:ptable': { files: ['content/app/tools/ptable-data.js', 'content/app/tools/ptable.js'], has: () => !!BCV.toolsPtable },
  };
  const loading = {};
  /** The module, loaded if it is not here yet: resolves once its globals are in place. Rejects when
   *  the page cannot ask for it (no background to ask) and it is not here. */
  function load(name) {
    const m = MODULES[name];
    if (!m) return Promise.reject(new Error(`No such module: ${name}`));
    if (m.has()) return Promise.resolve(true);
    if (loading[name]) return loading[name];
    loading[name] = (async () => {
      try {
        for (const dep of m.needs || []) await load(dep); // what it draws with, first
        let r = null;
        try { r = await api.runtime.sendMessage({ type: 'load', files: m.files }); } catch (e) { throw new Error(`Could not load ${name}: ${e?.message || e}`); }
        if (!r?.ok) throw new Error(r?.message || `Could not load ${name}`);
        if (!m.has()) throw new Error(`${name} did not load`);
        return true;
      } finally { delete loading[name]; }
    })();
    return loading[name];
  }
  const has = (name) => !!MODULES[name]?.has();
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

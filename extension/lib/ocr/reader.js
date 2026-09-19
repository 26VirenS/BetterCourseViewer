/* The text reader's engine room. Reading the words off a picture is Tesseract (lib/vendor/tesseract/):
 * WebAssembly in a worker, and a content script can start neither, so the Image to text tool
 * frames this page — the extension's own — into the Canvas tab, hidden, and talks to it with
 * postMessage: a picture in, the words back, progress on the way. The engine, its worker and the
 * English model travel with the extension; nothing is fetched and nothing leaves the device. One
 * picture at a time, in the order they arrive; the worker is started once and kept. */
(function () {
  const base = new URL('../vendor/tesseract/', location.href).href;
  const post = (msg) => window.parent.postMessage({ bcv: 'ocr', ...msg }, '*');
  let workerP = null;
  let current = null; // the job whose progress is being reported
  function worker() {
    if (!workerP) {
      workerP = self.Tesseract.createWorker('eng', 1, {
        workerPath: `${base}worker.min.js`,
        corePath: `${base}tesseract-core-simd-lstm.js`, // (the SIMD build alone: every browser this runs in has it)
        langPath: `${base}lang`,
        cacheMethod: 'none', // the model is local: nothing to cache
        workerBlobURL: false, // the worker from its own URL (a blob: worker is outside the extension's script policy)
        gzip: true,
        logger: (m) => { if (m && m.status && current) post({ type: 'progress', id: current, status: m.status, progress: Number(m.progress) || 0 }); },
      }).catch((e) => { workerP = null; throw e; });
    }
    return workerP;
  }
  let chain = Promise.resolve();
  window.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || m.bcv !== 'ocr' || m.type !== 'read' || e.source !== window.parent) return;
    chain = chain.then(async () => {
      current = m.id;
      try {
        const w = await worker();
        const { data } = await w.recognize(m.blob);
        post({ type: 'done', id: m.id, text: String(data?.text || ''), confidence: Number(data?.confidence) || 0 });
      } catch (err) {
        post({ type: 'error', id: m.id, message: err?.message || 'The text could not be read.' });
      }
      current = null;
    });
  });
  post({ type: 'ready' });
})();

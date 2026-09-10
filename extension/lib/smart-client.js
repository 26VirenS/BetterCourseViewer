/* Content-script side of the smart assistant: streams replies from the
 * background script over a runtime port. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = BCV.api;

  /**
   * Stream a chat completion.
   * @param {{system:string, messages:Array<{role:string,content:string}>}} payload
   * @param {{onStart?:Function, onDelta?:Function}} handlers
   * @returns {{promise: Promise<{text:string, model:string, provider:string}>, abort: Function}}
   */
  function stream(payload, handlers = {}) {
    const id = BCV.utils.uid();
    let port;
    let settled = false;
    let text = '';
    const promise = new Promise((resolve, reject) => {
      try {
        port = api.runtime.connect({ name: 'bcv-smart' });
      } catch (e) {
        reject(new Error('The extension background is unavailable. Reload the page.'));
        return;
      }
      port.onMessage.addListener((msg) => {
        if (!msg || msg.id !== id) return;
        if (msg.type === 'start') handlers.onStart?.(msg);
        else if (msg.type === 'delta') {
          text += msg.text;
          handlers.onDelta?.(msg.text, text);
        } else if (msg.type === 'done') {
          settled = true;
          resolve({ text, model: msg.model, provider: msg.provider });
          port.disconnect();
        } else if (msg.type === 'error') {
          settled = true;
          reject(new Error(msg.message || 'Something went wrong.'));
          port.disconnect();
        }
      });
      port.onDisconnect.addListener(() => {
        if (!settled) {
          settled = true;
          if (text) resolve({ text, model: null, provider: null, interrupted: true });
          else reject(new Error('Connection to the extension was lost. Try again.'));
        }
      });
      port.postMessage({ type: 'chat', id, ...payload });
    });
    const abort = () => {
      if (settled) return;
      try {
        port?.postMessage({ type: 'abort', id });
        port?.disconnect();
      } catch {
        /* ignore */
      }
    };
    return { promise, abort };
  }

  /** Promise-only messaging: Safari, Firefox and Chrome MV3 all return a
   *  promise when no callback is passed (Safari rejects a callback argument). */
  function send(message) {
    return new Promise((resolve) => {
      try {
        const p = api.runtime.sendMessage(message);
        if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
        else resolve(p ?? null);
      } catch {
        resolve(null);
      }
    });
  }

  const status = () => send({ type: 'providerStatus' });
  const openOptions = () => send({ type: 'openOptions' });
  const setBadge = (count) => send({ type: 'setBadge', count });

  BCV.smartClient = { stream, send, status, openOptions, setBadge };
})();

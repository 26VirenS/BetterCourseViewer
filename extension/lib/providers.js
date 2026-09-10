/* Smart-feature providers (runs in the background context).
 * Talks to the Claude Messages API and the ChatGPT (OpenAI) Chat
 * Completions API directly over fetch, streaming Server-Sent Events. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const CLAUDE_URL = 'https://api.anthropic.com/v1/messages';
  const CLAUDE_MODELS_URL = 'https://api.anthropic.com/v1/models';
  const OPENAI_URL = 'https://api.openai.com/v1/chat/completions';
  const OPENAI_MODELS_URL = 'https://api.openai.com/v1/models';

  // "Response depth" setting -> provider effort levels.
  const EFFORT = { quick: 'low', balanced: 'medium', thorough: 'high' };
  const MAX_TOKENS = 8192;

  class ProviderError extends Error {
    constructor(message, status, provider) {
      super(message);
      this.status = status;
      this.provider = provider;
    }
  }

  function friendlyStatus(status, provider, raw) {
    const name = provider === 'openai' ? 'ChatGPT' : 'Claude';
    if (status === 401) return `${name} rejected the key. Check it in Settings.`;
    if (status === 403) return `${name} refused this request (permission or region).`;
    if (status === 402) return `${name} says billing is required on that account.`;
    if (status === 404) return `${name} could not find that model. Check the model name in Settings.`;
    if (status === 429) return `${name} is rate limiting this key or the account is out of credit. Try again shortly.`;
    if (status === 529 || status === 503) return `${name} is overloaded right now. Try again in a moment.`;
    if (status >= 500) return `${name} had a server error (${status}). Try again.`;
    return raw || `${name} returned HTTP ${status}.`;
  }

  async function readErrorBody(response) {
    try {
      const text = await response.text();
      try {
        const json = JSON.parse(text);
        return json?.error?.message || json?.message || text;
      } catch {
        return text;
      }
    } catch {
      return '';
    }
  }

  /** Async generator over SSE events: yields {event, data} with parsed JSON data. */
  async function* sseEvents(response, signal) {
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let event = null;
    let dataLines = [];
    const flush = () => {
      if (!dataLines.length) return null;
      const raw = dataLines.join('\n');
      const out = { event, raw };
      event = null;
      dataLines = [];
      if (raw === '[DONE]') return { ...out, done: true };
      try {
        out.data = JSON.parse(raw);
      } catch {
        out.data = null;
      }
      return out;
    };
    while (true) {
      if (signal?.aborted) break;
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let idx;
      while ((idx = buffer.indexOf('\n')) >= 0) {
        let line = buffer.slice(0, idx);
        buffer = buffer.slice(idx + 1);
        if (line.endsWith('\r')) line = line.slice(0, -1);
        if (line === '') {
          const ev = flush();
          if (ev) yield ev;
          continue;
        }
        if (line.startsWith(':')) continue;
        const colon = line.indexOf(':');
        const field = colon < 0 ? line : line.slice(0, colon);
        let fieldValue = colon < 0 ? '' : line.slice(colon + 1);
        if (fieldValue.startsWith(' ')) fieldValue = fieldValue.slice(1);
        if (field === 'event') event = fieldValue;
        else if (field === 'data') dataLines.push(fieldValue);
      }
    }
    const last = flush();
    if (last) yield last;
  }

  /**
   * Stream a Claude response.
   * @param {object} opts {apiKey, model, system, messages, depth, signal, onDelta, onStart}
   * @returns {Promise<{text:string, model:string, stopReason:string}>}
   */
  async function streamClaude(opts) {
    const { apiKey, model, system, messages, depth, signal, onDelta, onStart } = opts;
    const body = {
      model,
      max_tokens: MAX_TOKENS,
      stream: true,
      system,
      messages: messages.map((m) => ({ role: m.role, content: m.content })),
      output_config: { effort: EFFORT[depth] || 'medium' },
      // Server-side refusal fallbacks: if the safety classifier declines a
      // request, the API re-runs it on Anthropic's recommended substitute
      // model inside the same call instead of returning nothing.
      fallbacks: 'default',
    };
    let response;
    try {
      response = await fetch(CLAUDE_URL, {
        method: 'POST',
        signal,
        headers: {
          'content-type': 'application/json',
          'x-api-key': apiKey,
          'anthropic-version': '2023-06-01',
          'anthropic-beta': 'server-side-fallback-2026-07-01',
          'anthropic-dangerous-direct-browser-access': 'true',
        },
        body: JSON.stringify(body),
      });
    } catch (e) {
      if (e.name === 'AbortError') throw e;
      throw new ProviderError('Could not reach Claude. Check your connection.', 0, 'claude');
    }
    if (!response.ok) {
      const raw = await readErrorBody(response);
      throw new ProviderError(friendlyStatus(response.status, 'claude', raw), response.status, 'claude');
    }
    let text = '';
    let servedModel = model;
    let stopReason = null;
    for await (const ev of sseEvents(response, signal)) {
      const d = ev.data;
      if (!d) continue;
      switch (d.type) {
        case 'message_start':
          servedModel = d.message?.model || servedModel;
          onStart?.({ model: servedModel });
          break;
        case 'content_block_delta':
          if (d.delta?.type === 'text_delta' && d.delta.text) {
            text += d.delta.text;
            onDelta?.(d.delta.text);
          }
          break;
        case 'message_delta':
          if (d.delta?.stop_reason) stopReason = d.delta.stop_reason;
          break;
        case 'error':
          throw new ProviderError(d.error?.message || 'Claude returned an error.', 0, 'claude');
        default:
          break;
      }
    }
    if (stopReason === 'refusal') {
      throw new ProviderError('Claude declined to answer this request.', 0, 'claude');
    }
    return { text, model: servedModel, stopReason };
  }

  function isReasoningModel(model) {
    const m = String(model || '').toLowerCase();
    if (m.includes('chat')) return false;
    return /^(gpt-5|o\d)/.test(m);
  }

  /** Stream a ChatGPT (OpenAI Chat Completions) response. Same contract as streamClaude. */
  async function streamOpenAI(opts) {
    const { apiKey, model, system, messages, depth, signal, onDelta, onStart } = opts;
    const body = {
      model,
      stream: true,
      max_completion_tokens: MAX_TOKENS,
      messages: [{ role: 'system', content: system }, ...messages.map((m) => ({ role: m.role, content: m.content }))],
    };
    if (isReasoningModel(model)) body.reasoning_effort = EFFORT[depth] || 'medium';
    let response;
    try {
      response = await fetch(OPENAI_URL, {
        method: 'POST',
        signal,
        headers: {
          'content-type': 'application/json',
          authorization: `Bearer ${apiKey}`,
        },
        body: JSON.stringify(body),
      });
    } catch (e) {
      if (e.name === 'AbortError') throw e;
      throw new ProviderError('Could not reach ChatGPT. Check your connection.', 0, 'openai');
    }
    if (!response.ok) {
      const raw = await readErrorBody(response);
      throw new ProviderError(friendlyStatus(response.status, 'openai', raw), response.status, 'openai');
    }
    let text = '';
    let servedModel = model;
    let stopReason = null;
    let started = false;
    for await (const ev of sseEvents(response, signal)) {
      if (ev.done) break;
      const d = ev.data;
      if (!d) continue;
      if (d.error) throw new ProviderError(d.error.message || 'ChatGPT returned an error.', 0, 'openai');
      if (!started) {
        started = true;
        servedModel = d.model || servedModel;
        onStart?.({ model: servedModel });
      }
      const choice = d.choices?.[0];
      if (!choice) continue;
      const delta = choice.delta?.content;
      if (delta) {
        text += delta;
        onDelta?.(delta);
      }
      if (choice.finish_reason) stopReason = choice.finish_reason;
    }
    if (stopReason === 'content_filter') {
      throw new ProviderError('ChatGPT declined to answer this request.', 0, 'openai');
    }
    return { text, model: servedModel, stopReason };
  }

  /** Cheap key check: list models. Returns {ok, message}. */
  async function testKey(provider, apiKey) {
    const key = (apiKey || '').trim();
    if (!key) return { ok: false, message: 'Enter a key first.' };
    try {
      const response = provider === 'openai'
        ? await fetch(OPENAI_MODELS_URL, { headers: { authorization: `Bearer ${key}` } })
        : await fetch(CLAUDE_MODELS_URL, {
            headers: {
              'x-api-key': key,
              'anthropic-version': '2023-06-01',
              'anthropic-dangerous-direct-browser-access': 'true',
            },
          });
      if (!response.ok) {
        const raw = await readErrorBody(response);
        return { ok: false, message: friendlyStatus(response.status, provider, raw) };
      }
      const json = await response.json();
      const count = Array.isArray(json?.data) ? json.data.length : 0;
      return { ok: true, message: `Key works${count ? ` (${count} models available)` : ''}.` };
    } catch (e) {
      return { ok: false, message: `Could not reach ${provider === 'openai' ? 'ChatGPT' : 'Claude'}: ${e.message}` };
    }
  }

  BCV.providers = { streamClaude, streamOpenAI, testKey, ProviderError, EFFORT, MAX_TOKENS };
})();

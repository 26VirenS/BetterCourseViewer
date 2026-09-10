#!/usr/bin/env node
// Unit test for the SSE parsing in lib/providers.js using a stubbed fetch.
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
globalThis.self = globalThis;
globalThis.chrome = { storage: { local: {}, onChanged: { addListener() {} } } };
require('../../extension/lib/settings.js');
require('../../extension/lib/providers.js');
const P = self.BCV.providers;

function sseResponse(chunks, ok = true, status = 200) {
  const enc = new TextEncoder();
  const stream = new ReadableStream({
    start(c) {
      for (const ch of chunks) c.enqueue(enc.encode(ch));
      c.close();
    },
  });
  return new Response(stream, { status, headers: { 'content-type': ok ? 'text/event-stream' : 'application/json' } });
}

let calls = [];
let failed = 0;
const assert = (cond, label) => { console.log(`${cond ? '  ✓' : '  ✗'} ${label}`); if (!cond) failed++; };

// Claude: split events across chunk boundaries on purpose
globalThis.fetch = async (url, init) => {
  calls.push({ url, init });
  return sseResponse([
    'event: message_start\ndata: {"type":"message_start","message":{"id":"m1","model":"claude-opus-5"}}\n\n',
    'event: content_block_start\ndata: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}\n\nevent: content_block_delta\ndata: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","te',
    'xt":"Hello"}}\n\nevent: content_block_delta\ndata: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}\n\n',
    'event: message_delta\ndata: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":3}}\n\nevent: message_stop\ndata: {"type":"message_stop"}\n\n',
  ]);
};
const deltas = [];
const r1 = await P.streamClaude({ apiKey: 'k', model: 'claude-opus-5', system: 'sys', messages: [{ role: 'user', content: 'hi' }], depth: 'quick', onDelta: (d) => deltas.push(d) });
assert(r1.text === 'Hello world' && deltas.join('') === 'Hello world', 'Claude SSE reassembled across chunks');
assert(r1.model === 'claude-opus-5' && r1.stopReason === 'end_turn', 'Claude model/stop reason captured');
const body = JSON.parse(calls[0].init.body);
assert(body.fallbacks === 'default' && calls[0].init.headers['anthropic-beta'] === 'server-side-fallback-2026-07-01', 'fallbacks:default + beta header sent');
assert(body.output_config.effort === 'low' && body.stream === true && body.system === 'sys', 'effort/stream/system in body');
assert(calls[0].init.headers['anthropic-dangerous-direct-browser-access'] === 'true', 'browser-access header sent');

// Claude refusal
globalThis.fetch = async () => sseResponse(['event: message_start\ndata: {"type":"message_start","message":{"model":"claude-opus-5"}}\n\nevent: message_delta\ndata: {"type":"message_delta","delta":{"stop_reason":"refusal"}}\n\n']);
let refused = null;
try { await P.streamClaude({ apiKey: 'k', model: 'claude-opus-5', system: '', messages: [], depth: 'balanced' }); } catch (e) { refused = e; }
assert(refused && /declined/.test(refused.message), 'refusal surfaces as a friendly error');

// Claude 401
globalThis.fetch = async () => new Response('{"error":{"message":"invalid x-api-key"}}', { status: 401 });
let err401 = null;
try { await P.streamClaude({ apiKey: 'k', model: 'claude-opus-5', system: '', messages: [], depth: 'balanced' }); } catch (e) { err401 = e; }
assert(err401?.status === 401 && /rejected the key/.test(err401.message), '401 mapped to friendly message');

// OpenAI
calls = [];
globalThis.fetch = async (url, init) => {
  calls.push({ url, init });
  return sseResponse([
    'data: {"id":"c1","model":"gpt-5","choices":[{"delta":{"role":"assistant","content":""},"finish_reason":null}]}\n\n',
    'data: {"choices":[{"delta":{"content":"Hi "},"finish_reason":null}]}\n\ndata: {"choices":[{"delta":{"content":"there"},"finish_reason":null}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n',
  ]);
};
const r2 = await P.streamOpenAI({ apiKey: 'k', model: 'gpt-5', system: 'sys', messages: [{ role: 'user', content: 'hi' }], depth: 'thorough' });
assert(r2.text === 'Hi there' && r2.model === 'gpt-5' && r2.stopReason === 'stop', 'OpenAI SSE parsed, [DONE] handled');
const b2 = JSON.parse(calls[0].init.body);
assert(b2.messages[0].role === 'system' && b2.reasoning_effort === 'high' && b2.max_completion_tokens > 0 && !('max_tokens' in b2), 'OpenAI body uses system message, reasoning_effort, max_completion_tokens');
assert(calls[0].init.headers.authorization === 'Bearer k', 'OpenAI bearer auth');

// non-reasoning model should not get reasoning_effort
calls = [];
globalThis.fetch = async (url, init) => { calls.push({ url, init }); return sseResponse(['data: [DONE]\n\n']); };
await P.streamOpenAI({ apiKey: 'k', model: 'gpt-4.1', system: 's', messages: [], depth: 'quick' });
assert(!('reasoning_effort' in JSON.parse(calls[0].init.body)), 'no reasoning_effort for gpt-4.1');

console.log(failed ? `\n${failed} provider check(s) failed` : '\nProvider checks passed.');
process.exit(failed ? 1 : 0);

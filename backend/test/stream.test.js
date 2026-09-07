import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readSseTokens } from '../src/services/sseTokens.js';
import { streamCompletion as groq } from '../src/services/groqClient.js';
import { streamCompletion as gemini } from '../src/services/geminiClient.js';
import { streamCompletion as openrouter } from '../src/services/openRouterClient.js';

async function collect(source) { const tokens = []; for await (const token of source) tokens.push(token); return tokens.join(''); }
test('stream survives split UTF-8 bytes and final data without newline', async () => {
  const data = JSON.stringify({ choices: [{ delta: { content: 'Hello ' + String.fromCodePoint(0x1f600) } }] });
  const bytes = new TextEncoder().encode('data: ' + data + '\n\ndata: {"choices":[{"delta":{"content":" world"}}]}');
  const response = new Response(new ReadableStream({ start(controller) {
    for (let i = 0; i < bytes.length; i += 3) controller.enqueue(bytes.slice(i, i + 3));
    controller.close();
  } }));
  assert.equal(await collect(readSseTokens(response)), 'Hello ' + String.fromCodePoint(0x1f600) + ' world');
});
test('done sentinel stops parsing and provider error events are errors', async () => {
  assert.equal(await collect(readSseTokens(new Response('data: [DONE]\ndata: broken'))), '');
  await assert.rejects(collect(readSseTokens(new Response('data: {"error":{"message":"failed"}}'))), /stream error/);
});
test('missing provider configuration cannot masquerade as a completed explanation', async () => {
  for (const key of ['GROQ_API_KEY', 'GROQ_API_KEY_FALLBACK', 'GROQ_API_KEYS', 'GEMINI_API_KEY', 'GOOGLE_API_KEY', 'OPENROUTER_API_KEY']) delete process.env[key];
  for (const provider of [groq, gemini, openrouter]) await assert.rejects(collect(provider('system', 'user')), /not configured/);
});

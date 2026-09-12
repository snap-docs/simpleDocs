import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { serve } from '@hono/node-server';
import jwt from 'jsonwebtoken';
import WebSocket from 'ws';
import { createApp } from '../src/app.js';
import { validateRuntimeConfig } from '../src/config/validateRuntimeConfig.js';

process.env.SKIP_AUTH = 'false';
process.env.ACCESS_TOKEN_SECRET = 'synthetic-test-secret-never-used-in-production';
const token = jwt.sign({ sub: 'local-dev', participant_id: 'local-dev', type: 'access' }, process.env.ACCESS_TOKEN_SECRET, { expiresIn: '5m' });
const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
async function* fakeCompletion() { yield 'Synthetic explanation'; }

test('protected routes reject missing tokens and malformed JSON bodies', async () => {
  const { app, wss } = createApp({ explainStreamCompletion: fakeCompletion });
  try {
    assert.equal((await app.request('/api/health')).status, 200);
    assert.equal((await app.request('/api/explain', { method: 'POST' })).status, 401);
    for (const body of ['null', '[]', '"text"', '{}', '{broken']) {
      assert.equal((await app.request('/api/explain', { method: 'POST', headers, body })).status, 400);
    }
    const explanation = await app.request('/api/explain', { method: 'POST', headers, body: JSON.stringify({ selected_text: 'const answer = 42;' }) });
    assert.equal(explanation.status, 200);
    assert.equal((await explanation.json()).response_text, 'Synthetic explanation');
    assert.equal((await app.request('/api/explain', { method: 'POST', headers, body: 'x'.repeat(70000) })).status, 413);
  } finally { wss.close(); }
});

test('production rejects missing credentials and auth bypass', () => {
  delete process.env.AUTH_MODE;
  process.env.SKIP_AUTH = 'true';
  try { assert.throws(() => validateRuntimeConfig('production'), /Unsafe production/); }
  finally { process.env.SKIP_AUTH = 'false'; }
});

test('intentional anonymous production mode accepts requests without login', async () => {
  const previous = {
    authMode: process.env.AUTH_MODE,
    provider: process.env.AI_PROVIDER,
    groqKey: process.env.GROQ_API_KEY
  };
  process.env.AUTH_MODE = 'anonymous';
  process.env.SKIP_AUTH = 'false';
  process.env.AI_PROVIDER = 'groq';
  process.env.GROQ_API_KEY = 'synthetic-test-provider-key';
  try {
    assert.doesNotThrow(() => validateRuntimeConfig('production'));
    const { app, wss } = createApp({ explainStreamCompletion: fakeCompletion });
    try {
      const response = await app.request('/api/explain', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ selected_text: 'const anonymous = true;' })
      });
      assert.equal(response.status, 200);
      assert.equal((await response.json()).response_text, 'Synthetic explanation');
    } finally { wss.close(); }
  } finally {
    if (previous.authMode === undefined) delete process.env.AUTH_MODE; else process.env.AUTH_MODE = previous.authMode;
    if (previous.provider === undefined) delete process.env.AI_PROVIDER; else process.env.AI_PROVIDER = previous.provider;
    if (previous.groqKey === undefined) delete process.env.GROQ_API_KEY; else process.env.GROQ_API_KEY = previous.groqKey;
  }
});

test('REST fallback reports provider failures without exposing provider details', async () => {
  async function* failedCompletion() { throw new Error('synthetic private provider detail'); }
  const { app, wss } = createApp({ explainStreamCompletion: failedCompletion });
  try {
    const response = await app.request('/api/explain', {
      method: 'POST',
      headers,
      body: JSON.stringify({ selected_text: 'const fallback = true;' })
    });
    assert.equal(response.status, 502);
    const body = await response.json();
    assert.match(body.error, /could not complete/i);
    assert.doesNotMatch(body.error, /synthetic private provider detail/i);
  } finally { wss.close(); }
});

test('real WebSocket transport rejects null and oversized payloads', async () => {
  const { app, injectWebSocket, wss } = createApp();
  const server = serve({ fetch: app.fetch, port: 0, hostname: '127.0.0.1' });
  injectWebSocket(server);
  await once(server, 'listening');
  const url = `ws://127.0.0.1:${server.address().port}/ws/stream`;
  try {
    const ws = new WebSocket(url, { headers });
    await once(ws, 'open');
    const response = once(ws, 'message');
    ws.send('null');
    assert.equal(JSON.parse((await response)[0]).type, 'error');
    ws.terminate();
    const large = new WebSocket(url, { headers });
    await once(large, 'open');
    const closed = once(large, 'close');
    large.send('x'.repeat(70000));
    assert.equal((await closed)[0], 1009);
  } finally {
    for (const client of wss.clients) client.terminate();
    wss.close();
    await new Promise(resolve => server.close(resolve));
  }
});

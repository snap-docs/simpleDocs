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

test('protected routes reject missing tokens and malformed JSON bodies', async () => {
  const { app, wss } = createApp();
  try {
    assert.equal((await app.request('/api/health')).status, 200);
    assert.equal((await app.request('/api/explain', { method: 'POST' })).status, 401);
    for (const body of ['null', '[]', '"text"', '{}', '{broken']) {
      assert.equal((await app.request('/api/explain', { method: 'POST', headers, body })).status, 400);
    }
    assert.equal((await app.request('/api/explain', { method: 'POST', headers, body: JSON.stringify({ selected_text: 'const answer = 42;' }) })).status, 200);
    assert.equal((await app.request('/api/explain', { method: 'POST', headers, body: 'x'.repeat(70000) })).status, 413);
  } finally { wss.close(); }
});

test('production rejects missing credentials and auth bypass', () => {
  process.env.SKIP_AUTH = 'true';
  try { assert.throws(() => validateRuntimeConfig('production'), /Unsafe production/); }
  finally { process.env.SKIP_AUTH = 'false'; }
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

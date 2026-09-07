const { test } = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { startBridge } = require('../bridge');

test('named pipe authenticates requests, bounds input and removes its manifest on close', { skip: process.platform !== 'win32' }, async () => {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'simpledocs-bridge-test-'));
  let reads = 0;
  const bridge = await startBridge(directory, selected => { reads++; return { selected_text: selected }; });
  try {
    const [file] = await fs.readdir(directory);
    const manifest = JSON.parse(await fs.readFile(path.join(directory, file)));
    async function request(payload) {
      return new Promise((resolve, reject) => {
        const socket = net.connect(`\\\\.\\pipe\\${manifest.pipe}`);
        let response = '';
        socket.setTimeout(1500, () => { socket.destroy(); reject(new Error('timeout')); });
        socket.on('connect', () => socket.end(payload + '\n'));
        socket.on('data', data => { response += data; });
        socket.on('end', () => resolve(response));
        socket.on('error', reject);
      });
    }
    assert.deepEqual(JSON.parse(await request(JSON.stringify({ type: 'capture', token: 'wrong' }))), {});
    assert.equal(reads, 0);
    assert.equal(JSON.parse(await request(JSON.stringify({ type: 'capture', token: manifest.token, selected_text: 'real selection' }))).selected_text, 'real selection');
    assert.equal(reads, 1);
    assert.equal(await request('x'.repeat(33000)), '');
    assert.equal(reads, 1);
  } finally {
    await bridge.dispose();
    assert.deepEqual(await fs.readdir(directory), []);
    await fs.rmdir(directory);
  }
});

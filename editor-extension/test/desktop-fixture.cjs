const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const net = require('node:net');
const { sendToDesktop } = require('../desktop');

async function main() {
  const directory = process.argv[2];
  const [name] = await fs.readdir(directory);
  const manifest = JSON.parse(await fs.readFile(path.join(directory, name), 'utf8'));
  const snapshot = text => ({ selected_text: text, background_context: 'before\n' + text + '\nafter',
    document_name: 'unsaved.py', editor: 'code' });
  async function raw(payload) {
    return new Promise((resolve, reject) => {
      const socket = net.connect('\\'.repeat(2) + '.\\pipe\\' + manifest.pipe);
      let response = '';
      socket.setTimeout(4000, () => { socket.destroy(); reject(new Error('timeout')); });
      socket.on('error', reject);
      socket.on('connect', () => socket.write(JSON.stringify(payload) + '\n'));
      socket.on('data', chunk => {
        response += chunk.toString();
        if (response.includes('\n')) { resolve(JSON.parse(response).status); socket.destroy(); }
      });
    });
  }
  assert.equal(await raw({ ...snapshot('bad-token'), type: 'explain', token: 'wrong' }), 'invalid');
  assert.equal(await raw({ ...snapshot('x'.repeat(12001)), type: 'explain', token: manifest.token }), 'invalid');
  assert.equal(await raw({ ...snapshot('not-in-context'), background_context: 'different', type: 'explain', token: manifest.token }), 'invalid');
  for (const text of ['first', 'second', 'first']) assert.equal(await sendToDesktop(snapshot(text), directory), true);
  await assert.rejects(sendToDesktop(snapshot('__busy__'), directory), /finishing/);
  assert.equal(await sendToDesktop(snapshot('after-busy'), directory), true);
  console.log('PASS authenticated direct commands, validation, fresh selections and busy recovery');
}
main().catch(error => { console.error(error); process.exitCode = 1; });

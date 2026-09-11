const net = require('node:net');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

async function startBridge(directory, capture) {
  const suffix = crypto.randomBytes(16).toString('hex');
  const pipe = `simpleDocs-editor-${process.pid}-${suffix}`;
  const token = crypto.randomBytes(32).toString('hex');
  const manifest = path.join(directory, `${pipe}.json`);
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.on('error', () => {});
    socket.setTimeout(1000, () => socket.destroy());
    let buffer = '';
    let handled = false;
    socket.setEncoding('utf8');
    socket.on('data', data => {
      if (handled) return;
      buffer += data;
      if (Buffer.byteLength(buffer) > 32768) { socket.destroy(); return; }
      if (!buffer.includes('\n')) return;
      handled = true;
      try {
        const request = JSON.parse(buffer.slice(0, buffer.indexOf('\n')));
        if (request.token !== token || request.type !== 'capture') { socket.end('{}\n'); return; }
        const result = capture(request.selected_text, request);
        socket.end(JSON.stringify(result || {}) + '\n');
      } catch { socket.end('{}\n'); }
    });
  });
  await fs.mkdir(directory, { recursive: true });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(`\\\\.\\pipe\\${pipe}`, resolve);
  });
  server.on('error', () => {});
  try { await fs.writeFile(manifest, JSON.stringify({ pipe, token }), { mode: 0o600 }); }
  catch (error) { server.close(); throw error; }
  return {
    async dispose() {
      for (const socket of sockets) socket.destroy();
      await new Promise(resolve => server.close(resolve));
      await fs.rm(manifest, { force: true });
    }
  };
}

module.exports = { startBridge };

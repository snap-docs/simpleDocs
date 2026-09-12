const net = require('node:net');
const fs = require('node:fs/promises');
const path = require('node:path');

function sendToPipe(manifest, snapshot) {
  return new Promise((resolve, reject) => {
    let connected = false;
    let settled = false;
    let response = '';
    const socket = net.connect('\\'.repeat(2) + '.\\pipe\\' + manifest.pipe);
    function finish(error, result) {
      if (settled) return;
      settled = true;
      socket.destroy();
      if (error) {
        error.notDelivered = !connected;
        reject(error);
      } else resolve(result);
    }
    socket.setTimeout(3000, () => finish(new Error('Desktop acknowledgement timed out. Check the overlay before retrying.')));
    socket.on('error', error => finish(error));
    socket.on('connect', () => {
      connected = true;
      socket.write(JSON.stringify({ ...snapshot, type: 'explain', token: manifest.token }) + '\n');
    });
    socket.on('data', data => {
      response += data.toString();
      if (response.length > 4096) return finish(new Error('Invalid desktop acknowledgement.'));
      if (!response.includes('\n')) return;
      try { finish(null, JSON.parse(response.slice(0, response.indexOf('\n')))); }
      catch { finish(new Error('Invalid desktop acknowledgement.')); }
    });
    socket.on('end', () => finish(new Error('Desktop closed before acknowledging. Check the overlay before retrying.')));
  });
}

async function sendToDesktop(snapshot, directory = process.env.SIMPLEDOCS_DESKTOP_BRIDGE_DIR ||
    path.join(process.env.LOCALAPPDATA || '', 'CodeExplainer', 'desktop-bridge')) {
  let files;
  try {
    files = (await fs.readdir(directory)).filter(name => /^simpleDocs-desktop-\d+-[a-f0-9]{32}\.json$/.test(name));
  } catch { throw new Error('Start the updated simpleDocs desktop app, then try Explain Selection again.'); }
  const candidates = await Promise.all(files.map(async name => {
    const file = path.join(directory, name);
    try { return { file, stat: await fs.stat(file) }; } catch { return null; }
  }));
  for (const candidate of candidates.filter(Boolean).sort((a, b) => b.stat.mtimeMs - a.stat.mtimeMs).slice(0, 8)) {
    if (candidate.stat.size > 2048) continue;
    let manifest;
    try { manifest = JSON.parse(await fs.readFile(candidate.file, 'utf8')); } catch { continue; }
    if (manifest.protocol !== 1 || !/^simpleDocs-desktop-\d+-[a-f0-9]{32}$/.test(manifest.pipe)
        || !/^[a-f0-9]{64}$/.test(manifest.token)) continue;
    let result;
    try { result = await sendToPipe(manifest, snapshot); }
    catch (error) {
      // Retry stale discovery files only before delivery. Never duplicate an acknowledged or uncertain request.
      if (error.notDelivered) continue;
      throw error;
    }
    if (result.status === 'accepted') return true;
    if (result.status === 'busy') throw new Error('simpleDocs is finishing the current explanation. Try again when it finishes.');
    throw new Error('simpleDocs rejected the selection. Select one region of up to 5,000 characters and retry.');
  }
  throw new Error('Start the updated simpleDocs desktop app, then try Explain Selection again.');
}

module.exports = { sendToDesktop };

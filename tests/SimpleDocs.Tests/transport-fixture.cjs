const net = require('node:net');
const crypto = require('node:crypto');
let request = 0;
const sockets = new Set();
const server = net.createServer(socket => {
  sockets.add(socket);
  socket.on('close', () => sockets.delete(socket));
  socket.on('error', () => {});
  let buffer = '';
  let upgraded = false;
  let sent = false;
  socket.on('data', chunk => {
    if (!upgraded) {
      buffer += chunk.toString();
      if (!buffer.includes('\r\n\r\n')) return;
      const keyMatch = /Sec-WebSocket-Key: (.*)\r\n/i.exec(buffer);
      if (!keyMatch) {
        const headerEnd = buffer.indexOf('\r\n\r\n');
        const contentLength = Number(/Content-Length: (\d+)/i.exec(buffer)?.[1] || 0);
        if (buffer.length < headerEnd + 4 + contentLength) return;
        const body = JSON.stringify({ response_text: 'HTTP fallback explanation' });
        socket.end(`HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ${Buffer.byteLength(body)}\r\nConnection: close\r\n\r\n${body}`);
        return;
      }
      const key = keyMatch[1].trim();
      const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
      socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
      upgraded = true;
    } else if (!sent) {
      sent = true;
      request++;
      if (request === 3) {
        socket.write(Buffer.from([0x88, 0x02, 0x03, 0xe8]));
        return;
      }
      const message = request === 1 ? { type: 'error', message: 'Synthetic provider failure' } : { type: 'complete' };
      const data = Buffer.from(JSON.stringify(message));
      socket.write(Buffer.concat([Buffer.from([0x81, data.length]), data]));
      if (request === 4) socket.end();
      // Intentionally ignore close frames to reproduce a peer that never completes shutdown.
    }
  });
});
server.listen(0, '127.0.0.1', () => console.log(server.address().port));
process.stdin.once('data', () => { for (const socket of sockets) socket.destroy(); server.close(); });

import { Hono } from 'hono';
import { cors } from 'hono/cors';
import { createNodeWebSocket } from '@hono/node-ws';
import { healthRoute } from './routes/health.js';
import { createExplainRoute } from './routes/explain.js';
import { authMiddleware } from './middleware/auth.js';

export function createApp() {
  const app = new Hono();
  const { injectWebSocket, upgradeWebSocket } = createNodeWebSocket({ app });

  // ── Global middleware ─────────────────────────
  app.use('*', cors());

  // ── Routes ────────────────────────────────────
  app.route('/api', healthRoute);

  // Auth middleware for protected routes
  app.use('/api/explain', authMiddleware);
  app.use('/ws/*', authMiddleware);

  // REST endpoint for explain
  const explainRoute = createExplainRoute();
  app.route('/api', explainRoute);

  // WebSocket endpoint for streaming
  app.get('/ws/stream', upgradeWebSocket((c) => {
    return {
      onMessage: async (event, ws) => {
        try {
          const data = JSON.parse(event.data.toString());
          const { handleStreamRequest } = await import('./services/streamHandler.js');
          await handleStreamRequest(data, ws);
        } catch (err) {
          ws.send(JSON.stringify({ error: err.message }));
          ws.close();
        }
      },
      onClose: () => {
        // cleanup if needed
      }
    };
  }));

  return { app, injectWebSocket };
}

import { Hono } from 'hono';
import { cors } from 'hono/cors';
import { createNodeWebSocket } from '@hono/node-ws';
import { healthRoute } from './routes/health.js';
import { createExplainRoute } from './routes/explain.js';
import { createAuthRoute } from './routes/auth.js';
import { authMiddleware } from './middleware/auth.js';
import { authenticateRequest } from './services/authService.js';

export function createApp() {
  const app = new Hono();
  const { injectWebSocket, upgradeWebSocket } = createNodeWebSocket({ app });

  // ── Global middleware ─────────────────────────
  app.use('*', cors());

  // ── Routes ────────────────────────────────────
  app.route('/api', healthRoute);
  app.route('/auth', createAuthRoute());

  // Auth middleware for protected routes
  app.use('/api/explain', authMiddleware);

  // REST endpoint for explain
  const explainRoute = createExplainRoute();
  app.route('/api', explainRoute);

  // WebSocket endpoint for streaming
  app.get('/ws/stream', async (c) => {
    const auth = authenticateRequest(c, { allowQueryToken: true });
    if (!auth.ok) {
      return c.json({ error: auth.message }, auth.status);
    }

    c.set('user', auth.user);

    return upgradeWebSocket(() => {
      return {
        onMessage: async (event, ws) => {
          try {
            const data = JSON.parse(event.data.toString());
            const { handleStreamRequest } = await import('./services/streamHandler.js');
            await handleStreamRequest(data, ws, auth.user);
          } catch (err) {
            ws.send(JSON.stringify({ error: err.message }));
            ws.close();
          }
        },
        onClose: () => {
          // cleanup if needed
        }
      };
    })(c);
  });

  return { app, injectWebSocket };
}

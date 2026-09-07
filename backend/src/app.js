import { Hono } from 'hono';
import { cors } from 'hono/cors';
import { bodyLimit } from 'hono/body-limit';
import { createNodeWebSocket } from '@hono/node-ws';
import { healthRoute } from './routes/health.js';
import { createExplainRoute } from './routes/explain.js';
import { createAuthRoute } from './routes/auth.js';
import { createFeedbackRoute } from './routes/feedback.js';
import { authMiddleware } from './middleware/auth.js';
import { authenticateRequest } from './services/authService.js';

export function createApp() {
  const app = new Hono();
  const { injectWebSocket, upgradeWebSocket, wss } = createNodeWebSocket({ app });
  wss.options.maxPayload = 64 * 1024;

  // ── Global middleware ─────────────────────────
  app.use('*', cors());
  app.use('*', bodyLimit({ maxSize: 64 * 1024 }));

  // ── Routes ────────────────────────────────────
  app.route('/api', healthRoute);
  app.route('/auth', createAuthRoute());

  // Auth middleware for protected routes
  app.use('/api/explain', authMiddleware);
  app.use('/api/feedback', authMiddleware);

  // REST endpoint for explain
  const explainRoute = createExplainRoute();
  app.route('/api', explainRoute);
  app.route('/api', createFeedbackRoute());

  // WebSocket endpoint for streaming
  app.get('/ws/stream', async (c) => {
    const auth = authenticateRequest(c);
    if (!auth.ok) {
      return c.json({ error: auth.message }, auth.status);
    }

    c.set('user', auth.user);

    return upgradeWebSocket(() => {
      let handledMessage = false;
      const disconnected = new AbortController();

      return {
        onMessage: async (event, ws) => {
          try {
            if (handledMessage) {
              ws.send(JSON.stringify({ type: 'error', message: 'Only one request is allowed per connection.' }));
              ws.close();
              return;
            }

            handledMessage = true;
            const rawMessage = event.data.toString();
            if (Buffer.byteLength(rawMessage, 'utf8') > 64 * 1024) {
              ws.send(JSON.stringify({ type: 'error', message: 'Request payload is too large.' }));
              ws.close();
              return;
            }

            const data = JSON.parse(rawMessage);
            if (!data || typeof data !== 'object' || Array.isArray(data)) {
              ws.send(JSON.stringify({ type: 'error', message: 'JSON body must be an object.' }));
              ws.close();
              return;
            }
            const { handleStreamRequest } = await import('./services/streamHandler.js');
            await handleStreamRequest(data, ws, auth.user, { signal: AbortSignal.any([disconnected.signal, AbortSignal.timeout(90000)]) });
          } catch (err) {
            console.error('[WS] Request failed:', err?.message || err);
            if (ws.readyState === 1) {
              ws.send(JSON.stringify({ type: 'error', message: 'The request could not be completed. Please retry.' }));
            }
            ws.close();
          }
        },
        onClose: () => {
          disconnected.abort();
        }
      };
    })(c);
  });

  return { app, injectWebSocket, wss };
}

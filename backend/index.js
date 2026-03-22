import 'dotenv/config';
import { serve } from '@hono/node-server';
import { createApp } from './src/app.js';
import { logger } from './src/utils/logger.js';

const port = parseInt(process.env.PORT || '3000', 10);
const { app, injectWebSocket } = createApp();

const server = serve({ fetch: app.fetch, port }, (info) => {
  logger.info(`Code Explainer backend running on http://localhost:${info.port}`);
});

injectWebSocket(server);

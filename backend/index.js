import { serve } from '@hono/node-server';
import { loadEnvironment } from './src/config/loadEnv.js';
import { createApp } from './src/app.js';
import { validateRuntimeConfig } from './src/config/validateRuntimeConfig.js';
import { logger } from './src/utils/logger.js';

const environmentName = loadEnvironment();
validateRuntimeConfig(environmentName);
const port = parseInt(process.env.PORT || '3000', 10);
const { app, injectWebSocket } = createApp();

const server = serve({ fetch: app.fetch, port }, (info) => {
  logger.info(`Code Explainer backend running on port ${info.port} (${environmentName})`);
});

injectWebSocket(server);

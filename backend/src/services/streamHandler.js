/**
 * WebSocket stream handler.
 * Receives a request payload, runs classification + prompt + AI streaming,
 * and sends tokens back over the WebSocket connection.
 */

import { classify } from './classifier.js';
import { buildPrompt } from './promptEngine.js';
import { streamCompletion, getModelName } from './openRouterClient.js';
import { logInteraction } from '../db/supabase.js';
import { logger } from '../utils/logger.js';

/**
 * Handle a streaming explain request over WebSocket.
 * @param {Object} data - { selected_text, full_context, app_type }
 * @param {Object} ws - WebSocket instance
 */
export async function handleStreamRequest(data, ws) {
  const startTime = Date.now();

  const { selected_text, full_context, app_type } = data;

  // Validate
  if (!selected_text || typeof selected_text !== 'string' || selected_text.trim().length === 0) {
    ws.send(JSON.stringify({ error: 'selected_text is required' }));
    ws.close();
    return;
  }

  // Sanitize
  const cleanSelected = selected_text.trim();
  const cleanContext = (full_context || '').substring(0, 15000);
  const cleanAppType = ['editor', 'browser', 'terminal', 'unknown'].includes(app_type)
    ? app_type
    : 'unknown';

  // Classify
  const caseType = classify(cleanSelected);
  logger.info(`[WS] Case ${caseType} | app: ${cleanAppType}`);

  // Build prompt
  const { systemPrompt, userPrompt } = buildPrompt(caseType, cleanSelected, cleanContext, cleanAppType);

  // Stream from OpenRouter
  try {
    for await (const token of streamCompletion(systemPrompt, userPrompt)) {
      ws.send(token);
    }
  } catch (err) {
    logger.error(`Stream error: ${err.message}`);
    ws.send(`\n[Stream error: ${err.message}]`);
  }

  // Send done sentinel
  ws.send('[DONE]');

  // Log interaction
  const responseTimeMs = Date.now() - startTime;
  logInteraction({
    caseType,
    appType: cleanAppType,
    responseTimeMs,
    modelUsed: getModelName(),
    fallbackUsed: false
  }).catch(err => logger.error(`Logging failed: ${err.message}`));

  logger.info(`[WS] Complete in ${responseTimeMs}ms`);
}

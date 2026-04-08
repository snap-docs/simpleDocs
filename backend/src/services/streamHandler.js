/**
 * WebSocket stream handler.
 * Receives a request payload, runs classification + prompt + AI streaming,
 * and sends tokens back over the WebSocket connection.
 */

import { classify } from './classifier.js';
import { buildPrompt } from './promptEngine.js';
import * as openRouterClient from './openRouterClient.js';
import * as groqClient from './groqClient.js';
import { logInteraction } from '../db/supabase.js';
import { logger } from '../utils/logger.js';

/**
 * Handle a streaming explain request over WebSocket.
 * @param {Object} data
 * @param {Object} ws - WebSocket instance
 */
export async function handleStreamRequest(data, ws) {
  const startTime = Date.now();

  const {
    selected_text,
    background_context,
    window_title,
    process_name,
    environment_type,
    selected_method,
    background_method,
    is_partial,
    is_unsupported,
    status_message,
    ocr_used,
    ocr_confidence
  } = data;

  if (!selected_text || typeof selected_text !== 'string' || selected_text.trim().length === 0) {
    ws.send(JSON.stringify({ type: 'error', message: 'selected_text is required' }));
    ws.close();
    return;
  }

  const cleanSelected = selected_text.trim();
  const cleanBackground = (background_context || '').substring(0, 12000);
  const cleanWindowTitle = (window_title || '').substring(0, 400);
  const cleanProcessName = (process_name || '').substring(0, 100);
  const cleanEnvironmentType = [
    'ide_editor',
    'ide_embedded_terminal',
    'browser_chromium',
    'browser_firefox',
    'classic_terminal',
    'modern_terminal',
    'electron',
    'external',
    'unknown'
  ].includes(environment_type)
    ? environment_type
    : 'unknown';
  const cleanSelectedMethod = typeof selected_method === 'string' ? selected_method.substring(0, 64) : 'unknown';
  const cleanBackgroundMethod = typeof background_method === 'string' ? background_method.substring(0, 64) : 'unknown';
  const cleanStatusMessage = typeof status_message === 'string' ? status_message.substring(0, 240) : '';
  const cleanOcrUsed = Boolean(ocr_used);
  const cleanOcrConfidence = typeof ocr_confidence === 'number' ? Math.min(Math.max(ocr_confidence, 0), 1) : 0;

  const caseType = classify(cleanSelected, cleanBackground);
  logger.info(`[WS] Case ${caseType} | environment: ${cleanEnvironmentType} | selected_method: ${cleanSelectedMethod} | background_method: ${cleanBackgroundMethod}`);
  logger.info(`[WS] Payload -> selected=${cleanSelected.length} chars | background=${cleanBackground.length} chars | process=${cleanProcessName} | title="${cleanWindowTitle}"`);

  const { systemPrompt, userPrompt } = buildPrompt(
    caseType,
    cleanSelected,
    cleanBackground,
    cleanWindowTitle,
    cleanProcessName,
    cleanEnvironmentType,
    cleanOcrUsed,
    cleanOcrConfidence);

  const provider = process.env.AI_PROVIDER === 'groq' ? groqClient : openRouterClient;
  const ocrSuffix = cleanOcrUsed ? ` | OCR(${Math.round(cleanOcrConfidence * 100)}%)` : '';
  const metaLabel = `${cleanEnvironmentType} | ${cleanSelectedMethod} + ${cleanBackgroundMethod}${is_partial ? ' | partial' : ''}${ocrSuffix}`;
  ws.send(JSON.stringify({
    type: 'meta',
    label: metaLabel,
    is_partial: Boolean(is_partial),
    is_unsupported: Boolean(is_unsupported),
    status_message: cleanStatusMessage
  }));

  try {
    for await (const token of provider.streamCompletion(systemPrompt, userPrompt)) {
      ws.send(JSON.stringify({ type: 'token', content: token }));
    }
  } catch (err) {
    logger.error(`Stream error: ${err.message}`);
    ws.send(JSON.stringify({ type: 'error', message: `Stream error: ${err.message}` }));
  }

  ws.send(JSON.stringify({ type: 'complete' }));

  const responseTimeMs = Date.now() - startTime;
  logInteraction({
    caseType,
    environmentType: cleanEnvironmentType,
    responseTimeMs,
    modelUsed: provider.getModelName(),
    fallbackUsed: false
  }).catch(err => logger.error(`Logging failed: ${err.message}`));

  logger.info(`[WS] Complete in ${responseTimeMs}ms`);
}

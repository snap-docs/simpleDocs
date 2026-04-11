/**
 * WebSocket stream handler.
 * Receives a request payload, runs classification + prompt + AI streaming,
 * and sends tokens back over the WebSocket connection.
 */

import { classify } from './classifier.js';
import { buildPrompt } from './promptEngine.js';
import * as openRouterClient from './openRouterClient.js';
import * as groqClient from './groqClient.js';
import { logCompletedRequest } from '../db/requestLogs.js';
import { sanitizeBackgroundText, sanitizeSelectedText, sanitizeMetadataText } from '../utils/textSanitizer.js';
import { logger } from '../utils/logger.js';

/**
 * Handle a streaming explain request over WebSocket.
 * @param {Object} data
 * @param {Object} ws - WebSocket instance
 */
export async function handleStreamRequest(data, ws, authUser = null) {
  const startTime = Date.now();
  const timestampIso = new Date().toISOString();
  let requestStatus = 'completed';

  const {
    request_id,
    usage_context,
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

  const cleanSelected = sanitizeSelectedText(selected_text.trim(), 5000);
  const cleanBackground = sanitizeBackgroundText(background_context || '', 12000);
  const cleanWindowTitle = sanitizeMetadataText(window_title || '', 400);
  const cleanProcessName = sanitizeMetadataText(process_name || '', 100);
  const cleanRequestId = typeof request_id === 'string' && request_id.trim().length > 0
    ? request_id.trim().substring(0, 120)
    : `req_${Date.now()}`;
  const cleanUsageContext = typeof usage_context === 'string' && usage_context.trim().length > 0
    ? usage_context.trim().substring(0, 255)
    : fallbackUsageContext(environment_type, cleanProcessName);
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
  const cleanStatusMessage = typeof status_message === 'string' ? sanitizeMetadataText(status_message, 240) : '';
  const cleanOcrUsed = Boolean(ocr_used);
  const cleanOcrConfidence = typeof ocr_confidence === 'number' ? Math.min(Math.max(ocr_confidence, 0), 1) : 0;
  const participantId = String(authUser?.participant_id || authUser?.sub || 'unknown');

  const caseType = classify(cleanSelected, cleanBackground);
  const taskType = mapTaskType(caseType);
  const responseParts = [];
  let timeToFirstTokenMs = null;
  logger.info(`[WS] Case ${caseType} | environment: ${cleanEnvironmentType} | selected_method: ${cleanSelectedMethod} | background_method: ${cleanBackgroundMethod}`);
  logger.info(`[WS] Payload -> request_id=${cleanRequestId} selected=${cleanSelected.length} chars | background=${cleanBackground.length} chars | process=${cleanProcessName} | title="${cleanWindowTitle}"`);

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
      if (timeToFirstTokenMs === null) {
        timeToFirstTokenMs = Date.now() - startTime;
      }

      responseParts.push(token);
      ws.send(JSON.stringify({ type: 'token', content: token }));
    }
  } catch (err) {
    requestStatus = 'stream_error';
    logger.error(`Stream error: ${err.message}`);
    ws.send(JSON.stringify({ type: 'error', message: `Stream error: ${err.message}` }));
  }

  ws.send(JSON.stringify({ type: 'complete' }));

  const totalResponseTimeMs = Date.now() - startTime;
  if (requestStatus === 'completed') {
    requestStatus = determineRequestStatus({
      isPartial: Boolean(is_partial),
      isUnsupported: Boolean(is_unsupported),
      hasResponseText: responseParts.join('').trim().length > 0
    });
  }

  void logCompletedRequest({
    participant_id: participantId,
    request_id: cleanRequestId,
    timestamp: timestampIso,
    environment_type: cleanEnvironmentType,
    process_name: cleanProcessName,
    usage_context: cleanUsageContext,
    window_title: cleanWindowTitle,
    background_context: cleanBackground,
    selected_method: cleanSelectedMethod,
    background_method: cleanBackgroundMethod,
    task_type: taskType,
    time_to_first_token_ms: timeToFirstTokenMs,
    total_response_time_ms: totalResponseTimeMs,
    selected_text: cleanSelected,
    response_text: responseParts.join(''),
    status: requestStatus
  });

  logger.info(`[WS] Complete in ${totalResponseTimeMs}ms`);
}

function mapTaskType(caseType) {
  switch (caseType) {
    case 1:
      return 'code_explanation';
    case 2:
      return 'error_explanation';
    case 3:
      return 'terminal_explanation';
    default:
      return 'text_explanation';
  }
}

function fallbackUsageContext(environmentType, processName) {
  switch (environmentType) {
    case 'ide_editor':
      return `ide_editor|${processName || 'unknown'}`;
    case 'ide_embedded_terminal':
      return `ide_terminal|${processName || 'unknown'}`;
    case 'modern_terminal':
      return `modern_terminal|${processName || 'unknown'}`;
    case 'classic_terminal':
      return `classic_terminal|${processName || 'unknown'}`;
    case 'browser_chromium':
    case 'browser_firefox':
      return `browser|${processName || 'unknown'}|unknown`;
    default:
      return `${environmentType || 'unknown'}|${processName || 'unknown'}`;
  }
}

function determineRequestStatus({ isPartial, isUnsupported, hasResponseText }) {
  if (isUnsupported) {
    return 'unsupported';
  }

  if (isPartial) {
    return 'partial';
  }

  if (!hasResponseText) {
    return 'empty_response';
  }

  return 'completed';
}

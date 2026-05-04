/**
 * WebSocket stream handler.
 * Receives a request payload, runs classification + prompt + AI streaming,
 * and sends tokens back over the WebSocket connection.
 */

import { classifyRequest } from './classifier.js';
import { buildPrompt, buildVisionPrompt } from './promptEngine.js';
import * as openRouterClient from './openRouterClient.js';
import * as groqClient from './groqClient.js';
import * as anthropicVisionClient from './anthropicVisionClient.js';
import { canUseVision, getVisionCircuitState, recordVisionFailure, recordVisionSuccess } from './visionCircuitBreaker.js';
import { logCompletedRequest } from '../db/requestLogs.js';
import { sanitizeBackgroundText, sanitizeSelectedText, sanitizeMetadataText } from '../utils/textSanitizer.js';
import { logger } from '../utils/logger.js';

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

function mapTaskType(caseType) {
  switch (caseType) {
    case 1:
      return 'code_explanation';
    case 2:
      return 'error_explanation';
    case 3:
      return 'terminal_explanation';
    case 5:
      return 'vision_context';
    default:
      return 'text_explanation';
  }
}

function isVisionEnabled() {
  return process.env.ENABLE_VISION_PIPELINE !== 'false';
}

function normalizeEnvironmentType(environmentType) {
  return [
    'ide_editor',
    'ide_embedded_terminal',
    'browser_chromium',
    'browser_firefox',
    'classic_terminal',
    'modern_terminal',
    'electron',
    'external',
    'unknown'
  ].includes(environmentType)
    ? environmentType
    : 'unknown';
}

function normalizeVisionPayload(vision) {
  if (!vision || typeof vision !== 'object') {
    return {
      cursorRegionBase64: '',
      activePanelBase64: '',
      fullWindowBase64: ''
    };
  }

  return {
    cursorRegionBase64: typeof (vision.cursor_region_base64 ?? vision.cursorRegionBase64) === 'string'
      ? (vision.cursor_region_base64 ?? vision.cursorRegionBase64).trim()
      : '',
    activePanelBase64: typeof (vision.active_panel_base64 ?? vision.activePanelBase64) === 'string'
      ? (vision.active_panel_base64 ?? vision.activePanelBase64).trim()
      : '',
    fullWindowBase64: typeof (vision.full_window_base64 ?? vision.fullWindowBase64) === 'string'
      ? (vision.full_window_base64 ?? vision.fullWindowBase64).trim()
      : ''
  };
}

function bytesFromBase64(data) {
  if (!data) {
    return 0;
  }

  try {
    return Buffer.from(data, 'base64').length;
  } catch {
    return 0;
  }
}

function listVisionLayers(vision) {
  const layers = [];
  if (vision.cursorRegionBase64) layers.push('cursor');
  if (vision.activePanelBase64) layers.push('panel');
  if (vision.fullWindowBase64) layers.push('window');
  return layers;
}

function estimateVisionCostUsd(totalImageBytes) {
  const inputCostPerMillion = Number.parseFloat(process.env.ANTHROPIC_INPUT_COST_PER_MILLION_USD || '3');
  if (!Number.isFinite(inputCostPerMillion) || totalImageBytes <= 0) {
    return 0;
  }

  // Heuristic: roughly one input token per ~750 bytes after client-side downsampling/compression.
  const estimatedInputTokens = Math.ceil(totalImageBytes / 750);
  return Number(((estimatedInputTokens / 1_000_000) * inputCostPerMillion).toFixed(6));
}

function createTextProvider() {
  return process.env.AI_PROVIDER === 'groq' ? groqClient : openRouterClient;
}

async function* streamTextResponse(caseType, selectedText, backgroundContext, windowTitle, processName, environmentType, ocrUsed, ocrConfidence) {
  const provider = createTextProvider();
  const { systemPrompt, userPrompt } = buildPrompt(
    caseType,
    selectedText,
    backgroundContext,
    windowTitle,
    processName,
    environmentType,
    ocrUsed,
    ocrConfidence);

  for await (const token of provider.streamCompletion(systemPrompt, userPrompt)) {
    yield token;
  }
}

async function* streamVisionResponse({
  selectedText,
  backgroundContext,
  ocrText,
  processName,
  windowTitle,
  cursorPosition,
  captureMethodExtended,
  vision
}) {
  const { systemPrompt, userPrompt } = buildVisionPrompt({
    selectedText,
    backgroundContext,
    ocrText,
    processName,
    windowTitle,
    cursorPosition,
    captureMethodExtended
  });

  for await (const token of anthropicVisionClient.streamVisionCompletion({
    systemPrompt,
    userPrompt,
    cursorRegionBase64: vision.cursorRegionBase64,
    activePanelBase64: vision.activePanelBase64,
    fullWindowBase64: vision.fullWindowBase64
  })) {
    yield token;
  }
}

/**
 * Handle a streaming explain request over WebSocket.
 * @param {Object} data
 * @param {Object} ws - WebSocket instance
 */
export async function handleStreamRequest(data, ws, authUser = null) {
  const startTime = Date.now();
  const timestampIso = new Date().toISOString();
  let requestStatus = 'completed';

  const cleanRequestId = typeof (data.request_id ?? data.requestId) === 'string' && (data.request_id ?? data.requestId).trim().length > 0
    ? (data.request_id ?? data.requestId).trim().substring(0, 120)
    : `req_${Date.now()}`;
  const cleanSelected = sanitizeSelectedText(data.selected_text ?? data.selectedText ?? '', 5000);
  const cleanBackground = sanitizeBackgroundText(data.background_context ?? data.backgroundContext ?? '', 12000);
  const cleanOcrText = sanitizeBackgroundText(data.ocr_text ?? data.ocrText ?? '', 12000);
  const cleanWindowTitle = sanitizeMetadataText(data.window_title ?? data.windowTitle ?? '', 400);
  const cleanProcessName = sanitizeMetadataText(data.process_name ?? data.processName ?? '', 100);
  const cleanEnvironmentType = normalizeEnvironmentType(data.environment_type ?? data.environmentType ?? 'unknown');
  const cleanSelectedMethod = typeof (data.selected_method ?? data.selectedMethod) === 'string'
    ? (data.selected_method ?? data.selectedMethod).substring(0, 64)
    : 'unknown';
  const cleanBackgroundMethod = typeof (data.background_method ?? data.backgroundMethod) === 'string'
    ? (data.background_method ?? data.backgroundMethod).substring(0, 64)
    : 'unknown';
  const cleanStatusMessage = typeof (data.status_message ?? data.statusMessage) === 'string'
    ? sanitizeMetadataText(data.status_message ?? data.statusMessage, 240)
    : '';
  const cleanUsageContext = typeof (data.usage_context ?? data.usageContext) === 'string' && (data.usage_context ?? data.usageContext).trim().length > 0
    ? (data.usage_context ?? data.usageContext).trim().substring(0, 255)
    : fallbackUsageContext(cleanEnvironmentType, cleanProcessName);
  const cleanOcrUsed = Boolean(data.ocr_used ?? data.ocrUsed ?? cleanOcrText);
  const cleanOcrConfidence = typeof (data.ocr_confidence ?? data.ocrConfidence) === 'number'
    ? Math.min(Math.max(data.ocr_confidence ?? data.ocrConfidence, 0), 1)
    : 0;
  const cursorPosition = data.cursor_position ?? data.cursorPosition ?? null;
  const participantId = String(authUser?.participant_id || authUser?.sub || 'unknown');

  const vision = normalizeVisionPayload(data.vision);
  const visionLayers = listVisionLayers(vision);
  const totalImageBytesSent =
    bytesFromBase64(vision.cursorRegionBase64) +
    bytesFromBase64(vision.activePanelBase64) +
    bytesFromBase64(vision.fullWindowBase64);
  const hasVisionPayload = isVisionEnabled() && visionLayers.length > 0;
  const captureMethodExtended = typeof (data.capture_method_extended ?? data.captureMethodExtended) === 'string'
    ? (data.capture_method_extended ?? data.captureMethodExtended).trim().substring(0, 40)
    : cleanSelected
      ? (hasVisionPayload ? 'vision_augmented' : 'text_only')
      : (hasVisionPayload ? 'vision_only' : 'text_only');

  if (!cleanSelected && !cleanOcrText && !hasVisionPayload) {
    ws.send(JSON.stringify({ type: 'error', message: 'selected_text, ocr_text, or vision payload is required' }));
    ws.close();
    return;
  }

  const classification = classifyRequest({
    selectedText: cleanSelected,
    backgroundContext: cleanBackground,
    ocrText: cleanOcrText,
    hasVision: hasVisionPayload
  });
  const caseType = classification.caseType;
  const taskType = mapTaskType(caseType);
  const responseParts = [];
  let timeToFirstTokenMs = null;

  const useVisionProvider = hasVisionPayload && captureMethodExtended !== 'text_only';
  const metaLabel = [
    cleanEnvironmentType,
    `${cleanSelectedMethod} + ${cleanBackgroundMethod}`,
    captureMethodExtended,
    cleanOcrUsed ? `OCR(${Math.round(cleanOcrConfidence * 100)}%)` : null,
    useVisionProvider ? anthropicVisionClient.getModelName() : createTextProvider().getModelName?.()
  ].filter(Boolean).join(' | ');

  ws.send(JSON.stringify({
    type: 'meta',
    label: metaLabel,
    is_partial: Boolean(data.is_partial ?? data.isPartial),
    is_unsupported: Boolean(data.is_unsupported ?? data.isUnsupported),
    status_message: cleanStatusMessage
  }));

  logger.info(
    `[WS] request_id=${cleanRequestId} case=${caseType} source=${classification.textSource} ` +
    `capture=${captureMethodExtended} vision_layers=${visionLayers.join(',') || 'none'} ` +
    `selected=${cleanSelected.length} background=${cleanBackground.length} ocr=${cleanOcrText.length} image_bytes=${totalImageBytesSent}`);

  const streamTokens = async (iterator) => {
    for await (const token of iterator) {
      if (timeToFirstTokenMs === null) {
        timeToFirstTokenMs = Date.now() - startTime;
      }

      responseParts.push(token);
      ws.send(JSON.stringify({ type: 'token', content: token }));
    }
  };

  try {
    if (useVisionProvider && canUseVision()) {
      await streamTokens(streamVisionResponse({
        selectedText: cleanSelected,
        backgroundContext: cleanBackground,
        ocrText: cleanOcrText,
        processName: cleanProcessName,
        windowTitle: cleanWindowTitle,
        cursorPosition,
        captureMethodExtended,
        vision
      }));
      recordVisionSuccess();
    } else {
      if (useVisionProvider && !canUseVision()) {
        logger.warn(`[WS] Vision circuit breaker open for request ${cleanRequestId}: ${JSON.stringify(getVisionCircuitState())}`);
      }

      await streamTokens(streamTextResponse(
        caseType === 5 ? 4 : caseType,
        cleanSelected || cleanOcrText,
        cleanBackground,
        cleanWindowTitle,
        cleanProcessName,
        cleanEnvironmentType,
        cleanOcrUsed,
        cleanOcrConfidence));
    }
  } catch (err) {
    if (useVisionProvider) {
      recordVisionFailure();
      logger.error(`Vision stream error for ${cleanRequestId}: ${err.message}`);

      if (cleanSelected || cleanOcrText) {
        try {
          await streamTokens(streamTextResponse(
            caseType === 5 ? 4 : caseType,
            cleanSelected || cleanOcrText,
            cleanBackground,
            cleanWindowTitle,
            cleanProcessName,
            cleanEnvironmentType,
            cleanOcrUsed,
            cleanOcrConfidence));
        } catch (fallbackErr) {
          requestStatus = 'stream_error';
          ws.send(JSON.stringify({ type: 'error', message: `Stream error: ${fallbackErr.message}` }));
        }
      } else {
        requestStatus = 'stream_error';
        ws.send(JSON.stringify({ type: 'error', message: `Stream error: ${err.message}` }));
      }
    } else {
      requestStatus = 'stream_error';
      logger.error(`Stream error: ${err.message}`);
      ws.send(JSON.stringify({ type: 'error', message: `Stream error: ${err.message}` }));
    }
  }

  ws.send(JSON.stringify({ type: 'complete' }));

  const totalResponseTimeMs = Date.now() - startTime;
  const estimatedCostUsd = estimateVisionCostUsd(totalImageBytesSent);
  if (requestStatus === 'completed') {
    requestStatus = determineRequestStatus({
      isPartial: Boolean(data.is_partial ?? data.isPartial),
      isUnsupported: Boolean(data.is_unsupported ?? data.isUnsupported),
      hasResponseText: responseParts.join('').trim().length > 0
    });
  }

  logger.info(
    `[WS] Complete request_id=${cleanRequestId} duration_ms=${totalResponseTimeMs} ` +
    `ttft_ms=${timeToFirstTokenMs ?? -1} image_bytes=${totalImageBytesSent} estimated_cost_usd=${estimatedCostUsd}`);

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
    status: requestStatus,
    capture_method_extended: captureMethodExtended,
    vision_layers_used: visionLayers.join(','),
    total_image_bytes_sent: totalImageBytesSent
  });
}

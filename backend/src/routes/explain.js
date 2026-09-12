import { Hono } from 'hono';
import { classify } from '../services/classifier.js';
import { buildPrompt } from '../services/promptEngine.js';
import { getModelName as getGeminiModelName } from '../services/geminiClient.js';
import { getModelName as getGroqModelName } from '../services/groqClient.js';
import { getModelName as getOpenRouterModelName } from '../services/openRouterClient.js';
import { validateExplainRequest } from '../middleware/validate.js';
import { logger } from '../utils/logger.js';
import { getProviderClient } from '../services/streamHandler.js';

export function createExplainRoute({ streamCompletion } = {}) {
  const route = new Hono();

  route.post('/explain', validateExplainRequest, async (c) => {
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
      is_unsupported
    } = c.get('validatedBody');

    const caseType = classify(selected_text, background_context);
    logger.info(`Classified as Case ${caseType} (environment: ${environment_type})`);

    const { systemPrompt, userPrompt } = buildPrompt(
      caseType,
      selected_text,
      background_context,
      window_title,
      process_name,
      environment_type);

    const completion = streamCompletion
      ?? ((system, user, options) => getProviderClient(process.env.AI_PROVIDER).streamCompletion(system, user, options));
    const responseParts = [];

    try {
      for await (const token of completion(systemPrompt, userPrompt, { signal: AbortSignal.timeout(90000) })) {
        responseParts.push(token);
      }
    } catch (error) {
      logger.error(`REST explanation failed: ${error?.message || error}`);
      return c.json({ error: 'The AI provider could not complete this request. Please retry.' }, 502);
    }

    const responseText = responseParts.join('').trim();
    if (!responseText) {
      return c.json({ error: 'The AI provider returned an empty response. Please retry.' }, 502);
    }

    const responseTimeMs = Date.now() - startTime;
    const modelUsed = getConfiguredModelName(process.env.AI_PROVIDER);

    return c.json({
      case: caseType,
      response_time_ms: responseTimeMs,
      model_used: modelUsed,
      environment_type,
      selected_method,
      background_method,
      is_partial,
      is_unsupported,
      response_text: responseText,
      prompt_preview: {
        system_prompt_length: systemPrompt.length,
        user_prompt_length: userPrompt.length
      }
    });
  });

  return route;
}

function getConfiguredModelName(providerName) {
  switch ((providerName || '').trim().toLowerCase()) {
    case 'gemini':
    case 'google':
      return getGeminiModelName();
    case 'groq':
      return getGroqModelName();
    case 'openrouter':
    default:
      return getOpenRouterModelName();
  }
}

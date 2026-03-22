import { Hono } from 'hono';
import { classify } from '../services/classifier.js';
import { buildPrompt } from '../services/promptEngine.js';
import { streamCompletion } from '../services/openRouterClient.js';
import { logInteraction } from '../db/supabase.js';
import { validateExplainRequest } from '../middleware/validate.js';
import { logger } from '../utils/logger.js';

export function createExplainRoute() {
  const route = new Hono();

  route.post('/explain', validateExplainRequest, async (c) => {
    const startTime = Date.now();

    const { selected_text, full_context, app_type } = c.req.valid('json') || await c.req.json();

    // Step 1: Classify the selected text
    const caseType = classify(selected_text);
    logger.info(`Classified as Case ${caseType} (app: ${app_type})`);

    // Step 2: Build the prompt
    const { systemPrompt, userPrompt } = buildPrompt(caseType, selected_text, full_context, app_type);

    // Step 3: Calculate response time
    const responseTimeMs = Date.now() - startTime;
    const modelUsed = process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';

    // Step 4: Log to Supabase (async, don't block response)
    logInteraction({
      caseType,
      appType: app_type,
      responseTimeMs,
      modelUsed,
      fallbackUsed: false
    }).catch(err => logger.error(`Logging failed: ${err.message}`));

    // Step 5: Return metadata (streaming happens over WebSocket)
    return c.json({
      case: caseType,
      response_time_ms: responseTimeMs,
      model_used: modelUsed
    });
  });

  return route;
}

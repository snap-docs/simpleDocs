import { Hono } from 'hono';
import { classify } from '../services/classifier.js';
import { buildPrompt } from '../services/promptEngine.js';
import { validateExplainRequest } from '../middleware/validate.js';
import { logger } from '../utils/logger.js';

export function createExplainRoute() {
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

    const responseTimeMs = Date.now() - startTime;
    const modelUsed = process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';

    return c.json({
      case: caseType,
      response_time_ms: responseTimeMs,
      model_used: modelUsed,
      environment_type,
      selected_method,
      background_method,
      is_partial,
      is_unsupported,
      prompt_preview: {
        system_prompt_length: systemPrompt.length,
        user_prompt_length: userPrompt.length
      }
    });
  });

  return route;
}

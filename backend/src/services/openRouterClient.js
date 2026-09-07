import { readSseTokens } from './sseTokens.js';

/**
 * OpenRouter API client with streaming support.
 * Model is configurable via OPENROUTER_MODEL environment variable.
 */

const OPENROUTER_API_URL = 'https://openrouter.ai/api/v1/chat/completions';
const APP_REFERER = process.env.PUBLIC_APP_URL || 'https://code-explainer.local';
const APP_TITLE = process.env.PUBLIC_APP_TITLE || 'simpleDocs';

/**
 * Stream a chat completion from OpenRouter.
 * Yields text tokens as they arrive.
 * 
 * @param {string} systemPrompt - System message
 * @param {string} userPrompt - User message
 * @returns {AsyncGenerator<string>} Token stream
 */
export async function* streamCompletion(systemPrompt, userPrompt, { signal = AbortSignal.timeout(90000) } = {}) {
  const apiKey = process.env.OPENROUTER_API_KEY;
  if (!apiKey || apiKey === 'your_openrouter_api_key_here') {
    throw new Error('openRouter API key is not configured.');
  }

  const model = process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';

  let response;
  try {
    response = await fetch(OPENROUTER_API_URL, {
      method: 'POST',
      signal,
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json',
        'HTTP-Referer': APP_REFERER,
        'X-Title': APP_TITLE
      },
      body: JSON.stringify({
        model,
        messages: [
          { role: 'system', content: systemPrompt },
          { role: 'user', content: userPrompt }
        ],
        stream: true,
        max_tokens: 1024,
        temperature: 0.3
      })
    });
  } catch (err) {
    const causeCode = err?.cause?.code ? ` (${err.cause.code})` : '';
    throw new Error(`OpenRouter network error: ${err?.message || 'fetch failed'}${causeCode}`);
  }

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error('openRouter provider request failed: ' + response.status);
  }

  yield* readSseTokens(response);
}

/**
 * Get the currently configured model name.
 */
export function getModelName() {
  return process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';
}

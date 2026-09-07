import { readSseTokens } from './sseTokens.js';

/**
 * Gemini API client with streaming support.
 * Model is configurable via GEMINI_MODEL environment variable.
 */

const GEMINI_API_URL = 'https://generativelanguage.googleapis.com/v1beta/openai/chat/completions';

function getApiKey() {
  const apiKey = process.env.GOOGLE_API_KEY || process.env.GEMINI_API_KEY || '';
  const trimmed = typeof apiKey === 'string' ? apiKey.trim() : '';
  if (!trimmed || trimmed === 'your_gemini_api_key_here') {
    return '';
  }

  return trimmed;
}

async function requestCompletion(apiKey, systemPrompt, userPrompt, model, signal) {
  try {
    return await fetch(GEMINI_API_URL, {
      method: 'POST',
      signal,
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        model,
        messages: [
          {
            role: 'system',
            content: systemPrompt
          },
          {
            role: 'user',
            content: userPrompt
          }
        ],
        stream: true,
        max_tokens: 1024,
        temperature: 0.3
      })
    });
  } catch (err) {
    const causeCode = err?.cause?.code ? ` (${err.cause.code})` : '';
    throw new Error(`Gemini network error: ${err?.message || 'fetch failed'}${causeCode}`);
  }
}

/**
 * Stream a chat completion from Gemini.
 *
 * @param {string} systemPrompt - System message
 * @param {string} userPrompt - User message
 * @returns {AsyncGenerator<string>} Token stream
 */
export async function* streamCompletion(systemPrompt, userPrompt, { signal = AbortSignal.timeout(90000) } = {}) {
  const apiKey = getApiKey();
  if (!apiKey) {
    throw new Error('gemini API key is not configured.');
  }

  const model = getModelName();
  const response = await requestCompletion(apiKey, systemPrompt, userPrompt, model, signal);

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error('gemini provider request failed: ' + response.status);
  }

  yield* readSseTokens(response);
}

/**
 * Get the currently configured model name.
 */
export function getModelName() {
  return process.env.GEMINI_MODEL || 'gemini-3.5-flash';
}

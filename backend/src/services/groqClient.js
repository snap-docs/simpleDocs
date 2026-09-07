import { readSseTokens } from './sseTokens.js';

/**
 * Groq API client with streaming support.
 * Model is configurable via GROQ_MODEL environment variable.
 */

const GROQ_API_URL = 'https://api.groq.com/openai/v1/chat/completions';

function getApiKeys() {
  const keys = [];
  const addKey = (value) => {
    const trimmed = typeof value === 'string' ? value.trim() : '';
    if (!trimmed || trimmed === 'your_groq_api_key_here' || keys.includes(trimmed)) {
      return;
    }

    keys.push(trimmed);
  };

  addKey(process.env.GROQ_API_KEY);
  addKey(process.env.GROQ_API_KEY_FALLBACK);

  const extraKeys = process.env.GROQ_API_KEYS
    ?.split(',')
    .map((item) => item.trim())
    .filter(Boolean) ?? [];

  for (const key of extraKeys) {
    addKey(key);
  }

  return keys;
}

async function requestCompletion(apiKey, systemPrompt, userPrompt, model, signal) {
  try {
    return await fetch(GROQ_API_URL, {
      method: 'POST',
      signal,
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json'
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
    throw new Error(`Groq network error: ${err?.message || 'fetch failed'}${causeCode}`);
  }
}

/**
 * Stream a chat completion from Groq.
 * Yields text tokens as they arrive.
 * 
 * @param {string} systemPrompt - System message
 * @param {string} userPrompt - User message
 * @returns {AsyncGenerator<string>} Token stream
 */
export async function* streamCompletion(systemPrompt, userPrompt, { signal = AbortSignal.timeout(90000) } = {}) {
  const apiKeys = getApiKeys();
  if (apiKeys.length === 0) {
    throw new Error('GROQ_API_KEY is not configured.');
  }

  const model = process.env.GROQ_MODEL || 'openai/gpt-oss-120b';

  let response;
  let lastError = null;
  for (let i = 0; i < apiKeys.length; i++) {
    try {
      response = await requestCompletion(apiKeys[i], systemPrompt, userPrompt, model, signal);
    } catch (err) {
      lastError = err;
      if (i < apiKeys.length - 1) {
        continue;
      }

      throw err;
    }

    if (response.ok) {
      break;
    }

    const errorBody = await response.text();
    lastError = new Error(`Groq error ${response.status}: ${errorBody}`);
    if (i < apiKeys.length - 1) {
      continue;
    }

    throw lastError;
  }

  if (!response) {
    throw lastError ?? new Error('Groq request failed before a response was received.');
  }

  yield* readSseTokens(response);
}

/**
 * Get the currently configured model name.
 */
export function getModelName() {
  return process.env.GROQ_MODEL || 'openai/gpt-oss-120b';
}

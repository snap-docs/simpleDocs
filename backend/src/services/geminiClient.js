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

async function requestCompletion(apiKey, systemPrompt, userPrompt, model) {
  try {
    return await fetch(GEMINI_API_URL, {
      method: 'POST',
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
export async function* streamCompletion(systemPrompt, userPrompt) {
  const apiKey = getApiKey();
  if (!apiKey) {
    yield '[Error: GEMINI_API_KEY not configured. Set GEMINI_API_KEY or GOOGLE_API_KEY in backend/.env]';
    return;
  }

  const model = getModelName();
  const response = await requestCompletion(apiKey, systemPrompt, userPrompt, model);

  if (!response.ok) {
    const errorBody = await response.text();
    yield `[Gemini error ${response.status}: ${errorBody}]`;
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed || trimmed === 'data: [DONE]') {
          continue;
        }

        if (!trimmed.startsWith('data: ')) {
          continue;
        }

        try {
          const payload = JSON.parse(trimmed.slice(6));
          const content = payload.choices?.[0]?.delta?.content;
          if (content) {
            yield content;
          }
        } catch {
          // Skip malformed SSE chunks.
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}

/**
 * Get the currently configured model name.
 */
export function getModelName() {
  return process.env.GEMINI_MODEL || 'gemini-3.5-flash';
}

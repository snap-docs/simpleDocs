/**
 * OpenRouter API client with streaming support.
 * Model is configurable via OPENROUTER_MODEL environment variable.
 */

const OPENROUTER_API_URL = 'https://openrouter.ai/api/v1/chat/completions';

/**
 * Stream a chat completion from OpenRouter.
 * Yields text tokens as they arrive.
 * 
 * @param {string} systemPrompt - System message
 * @param {string} userPrompt - User message
 * @returns {AsyncGenerator<string>} Token stream
 */
export async function* streamCompletion(systemPrompt, userPrompt) {
  const apiKey = process.env.OPENROUTER_API_KEY;
  if (!apiKey || apiKey === 'your_openrouter_api_key_here') {
    yield '[Error: OPENROUTER_API_KEY not configured. Set it in backend/.env]';
    return;
  }

  const model = process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';

  const response = await fetch(OPENROUTER_API_URL, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${apiKey}`,
      'Content-Type': 'application/json',
      'HTTP-Referer': 'https://code-explainer.local',
      'X-Title': 'Code Explainer'
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

  if (!response.ok) {
    const errorBody = await response.text();
    yield `[OpenRouter error ${response.status}: ${errorBody}]`;
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Process SSE lines
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';  // Keep incomplete line in buffer

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed || trimmed === 'data: [DONE]') continue;
        if (!trimmed.startsWith('data: ')) continue;

        try {
          const json = JSON.parse(trimmed.slice(6));
          const content = json.choices?.[0]?.delta?.content;
          if (content) {
            yield content;
          }
        } catch {
          // Skip malformed SSE chunks
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
  return process.env.OPENROUTER_MODEL || 'anthropic/claude-3.5-haiku';
}

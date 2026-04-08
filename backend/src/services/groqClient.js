/**
 * Groq API client with streaming support.
 * Model is configurable via GROQ_MODEL environment variable.
 */

const GROQ_API_URL = 'https://api.groq.com/openai/v1/chat/completions';

/**
 * Stream a chat completion from Groq.
 * Yields text tokens as they arrive.
 * 
 * @param {string} systemPrompt - System message
 * @param {string} userPrompt - User message
 * @returns {AsyncGenerator<string>} Token stream
 */
export async function* streamCompletion(systemPrompt, userPrompt) {
  const apiKey = process.env.GROQ_API_KEY;
  if (!apiKey || apiKey === 'your_groq_api_key_here') {
    yield '[Error: GROQ_API_KEY not configured. Set it in backend/.env]';
    return;
  }

  const model = process.env.GROQ_MODEL || 'llama-3.3-70b-versatile';

  let response;
  try {
    response = await fetch(GROQ_API_URL, {
      method: 'POST',
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

  if (!response.ok) {
    const errorBody = await response.text();
    yield `[Groq error ${response.status}: ${errorBody}]`;
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
  return process.env.GROQ_MODEL || 'llama-3.3-70b-versatile';
}

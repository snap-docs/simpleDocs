// Providers return SSE events across arbitrary UTF-8/network chunk boundaries.
export async function* readSseTokens(response) {
  if (!response.body) throw new Error('AI provider returned no response body.');
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  function parse(line) {
    const value = line.trim();
    if (!value.startsWith('data:')) return {};
    const data = value.slice(5).trim();
    if (data === '[DONE]') return { done: true };
    if (!data) return {};
    const event = JSON.parse(data);
    if (event.error) throw new Error('AI provider reported a stream error.');
    return { token: event.choices?.[0]?.delta?.content };
  }
  try {
    while (true) {
      const { done, value } = await reader.read();
      buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      if (buffer.length > 256 * 1024) throw new Error('AI stream event exceeded its size limit.');
      const lines = buffer.split('\n');
      buffer = done ? '' : lines.pop();
      for (const line of lines) {
        const event = parse(line);
        if (event.done) return;
        if (typeof event.token === 'string' && event.token) yield event.token;
      }
      if (done) break;
    }
  } finally {
    try { await reader.cancel(); } catch { }
    reader.releaseLock();
  }
}

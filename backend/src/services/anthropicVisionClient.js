const ANTHROPIC_API_URL = 'https://api.anthropic.com/v1/messages';
const DEFAULT_MODEL = process.env.ANTHROPIC_VISION_MODEL || 'claude-sonnet-4-20250514';

function buildImageContent(data) {
  if (!data || typeof data !== 'string' || data.trim().length === 0) {
    return null;
  }

  return {
    type: 'image',
    source: {
      type: 'base64',
      media_type: 'image/png',
      data
    }
  };
}

function parseAnthropicEvent(block) {
  const lines = block.split('\n');
  let event = '';
  let data = '';

  for (const line of lines) {
    if (line.startsWith('event:')) {
      event = line.slice('event:'.length).trim();
      continue;
    }

    if (line.startsWith('data:')) {
      data += line.slice('data:'.length).trim();
    }
  }

  if (!data) {
    return null;
  }

  try {
    return {
      event,
      payload: JSON.parse(data)
    };
  } catch {
    return null;
  }
}

export async function* streamVisionCompletion({
  systemPrompt,
  userPrompt,
  cursorRegionBase64,
  activePanelBase64,
  fullWindowBase64
}) {
  const apiKey = process.env.ANTHROPIC_API_KEY;
  if (!apiKey || apiKey === 'your_anthropic_api_key_here') {
    yield '[Error: ANTHROPIC_API_KEY not configured. Set it in backend/.env]';
    return;
  }

  const content = [
    buildImageContent(cursorRegionBase64),
    buildImageContent(activePanelBase64),
    buildImageContent(fullWindowBase64),
    { type: 'text', text: userPrompt }
  ].filter(Boolean);

  let response;
  try {
    response = await fetch(ANTHROPIC_API_URL, {
      method: 'POST',
      headers: {
        'x-api-key': apiKey,
        'anthropic-version': '2023-06-01',
        'content-type': 'application/json'
      },
      body: JSON.stringify({
        model: DEFAULT_MODEL,
        system: systemPrompt,
        max_tokens: 300,
        temperature: 0.3,
        stream: true,
        messages: [
          {
            role: 'user',
            content
          }
        ]
      })
    });
  } catch (err) {
    const causeCode = err?.cause?.code ? ` (${err.cause.code})` : '';
    throw new Error(`Anthropic network error: ${err?.message || 'fetch failed'}${causeCode}`);
  }

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(`Anthropic error ${response.status}: ${errorBody}`);
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
      const blocks = buffer.split('\n\n');
      buffer = blocks.pop() || '';

      for (const block of blocks) {
        const parsed = parseAnthropicEvent(block.trim());
        if (!parsed) {
          continue;
        }

        const { payload } = parsed;
        const deltaText = payload?.delta?.text || payload?.delta?.partial_json || payload?.delta?.thinking || '';
        if (typeof deltaText === 'string' && deltaText.length > 0) {
          yield deltaText;
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}

export function getModelName() {
  return DEFAULT_MODEL;
}

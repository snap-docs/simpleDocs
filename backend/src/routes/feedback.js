import { Hono } from 'hono';
import { saveRequestFeedback } from '../db/requestFeedback.js';

async function parseJsonBody(c) {
  try {
    return await c.req.json();
  } catch {
    return null;
  }
}

export function createFeedbackRoute() {
  const route = new Hono();

  route.post('/feedback', async (c) => {
    const body = await parseJsonBody(c);
    if (!body) {
      return c.json({ error: 'Invalid JSON body' }, 400);
    }

    const requestId = typeof body.request_id === 'string' ? body.request_id.trim().substring(0, 120) : '';
    const reaction = typeof body.reaction === 'string' ? body.reaction.trim().toLowerCase() : '';

    if (!requestId) {
      return c.json({ error: 'request_id is required' }, 400);
    }

    if (reaction !== 'up' && reaction !== 'down') {
      return c.json({ error: 'reaction must be up or down' }, 400);
    }

    const user = c.get('user');
    const participantId = String(user?.participant_id || user?.sub || 'unknown');
    const saved = await saveRequestFeedback({
      participant_id: participantId,
      request_id: requestId,
      reaction,
      created_at: new Date().toISOString()
    });

    if (!saved) {
      return c.json({ error: 'Failed to store feedback' }, 500);
    }

    return c.json({ success: true });
  });

  return route;
}

import { Hono } from 'hono';
import { createAuthErrorResponse, redeemCode, refreshAccessToken, logoutRefreshToken } from '../services/authService.js';

async function parseJsonBody(c) {
  try {
    return await c.req.json();
  } catch {
    return null;
  }
}

export function createAuthRoute() {
  const route = new Hono();

  route.post('/redeem-code', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await redeemCode(body.code);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/refresh', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await refreshAccessToken(body.refresh_token);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/logout', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      await logoutRefreshToken(body.refresh_token);
      return c.json({ success: true });
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  return route;
}

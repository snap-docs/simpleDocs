import { Hono } from 'hono';
import {
  authenticateRequest,
  completeGoogleLinkFlowForAuthenticatedUser,
  completeGoogleLoginFlow,
  createAuthErrorResponse,
  getCurrentAuthState,
  linkEmailPassword,
  linkRedeemCode,
  loginWithEmailPassword,
  logoutRefreshToken,
  prepareGoogleLinkFlow,
  prepareGoogleLoginFlow,
  redeemCode,
  refreshAccessToken,
  registerEmailPassword
} from '../services/authService.js';

async function parseJsonBody(c) {
  try {
    return await c.req.json();
  } catch {
    return null;
  }
}

function requireAuthenticatedUser(c) {
  const auth = authenticateRequest(c);
  if (!auth.ok) {
    return {
      ok: false,
      response: c.json({ error: auth.message }, auth.status)
    };
  }

  return {
    ok: true,
    user: auth.user
  };
}

export function createAuthRoute() {
  const route = new Hono();

  const redeemCodeLoginHandler = async (c) => {
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
  };

  route.post('/redeem-code', redeemCodeLoginHandler);
  route.post('/providers/redeem-code/login', redeemCodeLoginHandler);

  route.post('/providers/redeem-code/link', async (c) => {
    const auth = requireAuthenticatedUser(c);
    if (!auth.ok) {
      return auth.response;
    }

    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await linkRedeemCode(body.code, auth.user);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/providers/google/login/prepare', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await prepareGoogleLoginFlow(body.redirect_uri);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  const googleLoginCompleteHandler = async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await completeGoogleLoginFlow(body.code, body.state, body.flow_token);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  };

  route.post('/providers/google/login/complete', googleLoginCompleteHandler);
  route.post('/google/exchange', googleLoginCompleteHandler);

  route.post('/providers/google/link/prepare', async (c) => {
    const auth = requireAuthenticatedUser(c);
    if (!auth.ok) {
      return auth.response;
    }

    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await prepareGoogleLinkFlow(body.redirect_uri, auth.user);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/providers/google/link/complete', async (c) => {
    const auth = requireAuthenticatedUser(c);
    if (!auth.ok) {
      return auth.response;
    }

    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await completeGoogleLinkFlowForAuthenticatedUser(body.code, body.state, body.flow_token, auth.user);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/providers/email-password/login', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await loginWithEmailPassword(body.email, body.password);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/providers/email-password/register', async (c) => {
    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await registerEmailPassword(body.email, body.password, body.display_name);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.post('/providers/email-password/link', async (c) => {
    const auth = requireAuthenticatedUser(c);
    if (!auth.ok) {
      return auth.response;
    }

    try {
      const body = await parseJsonBody(c);
      if (!body) {
        return c.json({ error: 'Invalid JSON body' }, 400);
      }

      const result = await linkEmailPassword(body.email, body.password, body.display_name, auth.user);
      return c.json(result);
    } catch (error) {
      const response = createAuthErrorResponse(error);
      return c.json(response.body, response.status);
    }
  });

  route.get('/me', async (c) => {
    const auth = requireAuthenticatedUser(c);
    if (!auth.ok) {
      return auth.response;
    }

    try {
      const result = await getCurrentAuthState(auth.user);
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

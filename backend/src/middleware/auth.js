/**
 * Access-token authentication middleware.
 * Can be skipped with SKIP_AUTH=true for local development.
 */

import { authenticateRequest } from '../services/authService.js';

export async function authMiddleware(c, next) {
  if (c.req.path === '/api/health' || c.req.path.startsWith('/auth/')) {
    return next();
  }

  const auth = authenticateRequest(c);
  if (!auth.ok) {
    return c.json({ error: auth.message }, auth.status);
  }

  c.set('user', auth.user);
  return next();
}

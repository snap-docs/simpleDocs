/**
 * JWT authentication middleware.
 * Verifies Bearer token using Supabase JWT secret.
 * Can be skipped with SKIP_AUTH=true for local development.
 */

import jwt from 'jsonwebtoken';
import { logger } from '../utils/logger.js';

export async function authMiddleware(c, next) {
  // Skip auth for health endpoint
  if (c.req.path === '/api/health') {
    return next();
  }

  // Skip auth in development mode
  if (process.env.SKIP_AUTH === 'true') {
    return next();
  }

  const jwtSecret = process.env.SUPABASE_JWT_SECRET;
  if (!jwtSecret || jwtSecret === 'your_supabase_jwt_secret_here') {
    logger.warn('SUPABASE_JWT_SECRET not configured — auth is disabled');
    return next();
  }

  const authHeader = c.req.header('Authorization');
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return c.json({ error: 'Missing or invalid Authorization header' }, 401);
  }

  const token = authHeader.substring(7);

  try {
    const decoded = jwt.verify(token, jwtSecret);
    c.set('user', decoded);
    return next();
  } catch (err) {
    logger.warn(`JWT verification failed: ${err.message}`);
    return c.json({ error: 'Invalid or expired token' }, 401);
  }
}

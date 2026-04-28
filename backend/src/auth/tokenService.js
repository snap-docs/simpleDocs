import crypto from 'node:crypto';
import jwt from 'jsonwebtoken';
import { getAccessTokenSecret, getAccessTokenTtl, getFlowTokenSecret, getGoogleFlowTtlSeconds } from './config.js';
import { AuthError } from './errors.js';

export function createAccessToken({ participantId, sessionId, providerLinkId, providerType, email = null }) {
  const secret = getAccessTokenSecret();
  return jwt.sign(
    {
      sub: String(participantId),
      user_id: String(participantId),
      participant_id: String(participantId),
      session_id: sessionId ? String(sessionId) : null,
      provider_link_id: providerLinkId ? String(providerLinkId) : null,
      auth_provider: providerType || null,
      email: email || null,
      type: 'access'
    },
    secret,
    {
      expiresIn: getAccessTokenTtl()
    }
  );
}

export function verifyAccessToken(token) {
  try {
    const payload = jwt.verify(token, getAccessTokenSecret());
    if (!payload || typeof payload !== 'object' || payload.type !== 'access') {
      throw new AuthError(401, 'Invalid access token', 'access_token_invalid');
    }

    return payload;
  } catch (error) {
    if (error instanceof AuthError) {
      throw error;
    }

    throw new AuthError(401, 'Invalid or expired access token', 'access_token_invalid');
  }
}

export function createRefreshTokenValue() {
  return crypto.randomBytes(48).toString('base64url');
}

export function hashRefreshToken(token) {
  return crypto.createHash('sha256').update(token).digest('hex');
}

export function createRandomBase64Url(byteCount = 48) {
  return crypto.randomBytes(byteCount).toString('base64url');
}

export function createCodeChallenge(codeVerifier) {
  const hash = crypto.createHash('sha256').update(codeVerifier).digest();
  return hash.toString('base64url');
}

export function createGoogleFlowToken({ purpose, participantId = null, state, codeVerifier, redirectUri }) {
  return jwt.sign(
    {
      type: 'google_flow',
      purpose,
      participant_id: participantId || null,
      state,
      code_verifier: codeVerifier,
      redirect_uri: redirectUri
    },
    getFlowTokenSecret(),
    {
      expiresIn: `${getGoogleFlowTtlSeconds()}s`
    }
  );
}

export function verifyGoogleFlowToken(token) {
  if (typeof token !== 'string' || token.trim().length === 0) {
    throw new AuthError(400, 'flow_token is required', 'flow_token_missing');
  }

  try {
    const payload = jwt.verify(token.trim(), getFlowTokenSecret());
    if (!payload || typeof payload !== 'object' || payload.type !== 'google_flow') {
      throw new AuthError(400, 'Google sign-in flow token is invalid', 'flow_token_invalid');
    }

    return payload;
  } catch (error) {
    if (error instanceof AuthError) {
      throw error;
    }

    throw new AuthError(400, 'Google sign-in flow token is invalid or expired', 'flow_token_invalid');
  }
}

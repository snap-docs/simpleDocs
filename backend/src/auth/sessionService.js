import { AUTH_CLIENT_KIND_DESKTOP, AUTH_CLIENT_LABEL_SIMPLE_DOCS, AUTH_SESSION_STATUSES } from './constants.js';
import { getRefreshTokenLifetimeMs } from './config.js';
import { AuthError } from './errors.js';
import {
  createAuthSession,
  createRefreshToken,
  findPreferredProviderLinkForParticipant,
  getAuthSessionById,
  getParticipantById,
  getProviderLinkById,
  getRefreshTokenByHash,
  logAuthRepositoryWarning,
  updateAuthSession,
  updateParticipant,
  updateProviderLink,
  updateRefreshToken
} from './repository.js';
import {
  createAccessToken,
  createRefreshTokenValue,
  hashRefreshToken,
  verifyAccessToken
} from './tokenService.js';

function normalizeRefreshToken(refreshToken) {
  return typeof refreshToken === 'string' ? refreshToken.trim() : '';
}

function ensureActiveRefreshToken(tokenRow) {
  if (!tokenRow) {
    throw new AuthError(401, 'Invalid refresh token', 'refresh_token_invalid');
  }

  if (tokenRow.revoked_at) {
    throw new AuthError(401, 'Refresh token has been revoked', 'refresh_token_revoked');
  }

  if (!tokenRow.expires_at || Number.isNaN(Date.parse(tokenRow.expires_at)) || Date.parse(tokenRow.expires_at) <= Date.now()) {
    throw new AuthError(401, 'Refresh token has expired', 'refresh_token_expired');
  }
}

function ensureActiveSession(sessionRow) {
  if (!sessionRow) {
    throw new AuthError(401, 'Session is no longer valid', 'auth_session_invalid');
  }

  if (sessionRow.revoked_at || sessionRow.status === AUTH_SESSION_STATUSES.REVOKED) {
    throw new AuthError(401, 'Session has been revoked', 'auth_session_revoked');
  }
}

async function hydrateRefreshTokenContext(refreshTokenRow) {
  let providerLink = null;
  if (refreshTokenRow.auth_provider_link_id) {
    providerLink = await getProviderLinkById(refreshTokenRow.auth_provider_link_id);
  }

  if (!providerLink) {
    providerLink = await findPreferredProviderLinkForParticipant(refreshTokenRow.participant_id);
    if (!providerLink) {
      throw new AuthError(401, 'Session has no active auth method', 'provider_link_missing');
    }

    await updateRefreshToken(refreshTokenRow.id, {
      authProviderLinkId: providerLink.id
    });
  }

  let session = null;
  if (refreshTokenRow.auth_session_id) {
    session = await getAuthSessionById(refreshTokenRow.auth_session_id);
  }

  if (!session) {
    session = await createAuthSession({
      participantId: refreshTokenRow.participant_id,
      providerLinkId: providerLink.id,
      providerType: providerLink.provider_type,
      clientKind: AUTH_CLIENT_KIND_DESKTOP,
      clientLabel: 'legacy-refresh-session',
      createdAt: refreshTokenRow.created_at || new Date().toISOString(),
      lastSeenAt: refreshTokenRow.last_used_at || refreshTokenRow.created_at || new Date().toISOString()
    });

    await updateRefreshToken(refreshTokenRow.id, {
      authSessionId: session.id,
      authProviderLinkId: providerLink.id
    });
  }

  return { session, providerLink };
}

export async function startAuthenticatedSession({ participant, providerLink }) {
  const nowIso = new Date().toISOString();
  const session = await createAuthSession({
    participantId: participant.id,
    providerLinkId: providerLink.id,
    providerType: providerLink.provider_type,
    clientKind: AUTH_CLIENT_KIND_DESKTOP,
    clientLabel: AUTH_CLIENT_LABEL_SIMPLE_DOCS,
    createdAt: nowIso,
    lastSeenAt: nowIso
  });

  const refreshToken = createRefreshTokenValue();
  await createRefreshToken({
    participantId: participant.id,
    sessionId: session.id,
    providerLinkId: providerLink.id,
    tokenHash: hashRefreshToken(refreshToken),
    expiresAt: new Date(Date.now() + getRefreshTokenLifetimeMs()).toISOString(),
    createdAt: nowIso,
    lastUsedAt: nowIso
  });

  const updatedParticipant = await updateParticipant(participant.id, {
    lastAuthenticatedAt: nowIso
  });
  const updatedProviderLink = await updateProviderLink(providerLink.id, {
    lastAuthenticatedAt: nowIso
  });

  return {
    accessToken: createAccessToken({
      participantId: updatedParticipant.id,
      sessionId: session.id,
      providerLinkId: updatedProviderLink.id,
      providerType: updatedProviderLink.provider_type,
      email: updatedParticipant.email || updatedProviderLink.provider_email || null
    }),
    refreshToken,
    participant: updatedParticipant,
    providerLink: updatedProviderLink,
    session
  };
}

export async function refreshAccessTokenFromRefreshToken(refreshToken) {
  const normalizedRefreshToken = normalizeRefreshToken(refreshToken);
  if (!normalizedRefreshToken) {
    throw new AuthError(400, 'refresh_token is required', 'refresh_token_missing');
  }

  const tokenRow = await getRefreshTokenByHash(hashRefreshToken(normalizedRefreshToken));
  ensureActiveRefreshToken(tokenRow);

  const { session, providerLink } = await hydrateRefreshTokenContext(tokenRow);
  ensureActiveSession(session);

  const participant = await getParticipantById(tokenRow.participant_id);
  const nowIso = new Date().toISOString();

  await Promise.all([
    updateRefreshToken(tokenRow.id, { lastUsedAt: nowIso }),
    updateAuthSession(session.id, { lastSeenAt: nowIso }),
    updateParticipant(participant.id, { lastAuthenticatedAt: nowIso }),
    updateProviderLink(providerLink.id, { lastAuthenticatedAt: nowIso })
  ]);

  return {
    access_token: createAccessToken({
      participantId: participant.id,
      sessionId: session.id,
      providerLinkId: providerLink.id,
      providerType: providerLink.provider_type,
      email: participant.email || providerLink.provider_email || null
    })
  };
}

export async function logoutSessionByRefreshToken(refreshToken) {
  const normalizedRefreshToken = normalizeRefreshToken(refreshToken);
  if (!normalizedRefreshToken) {
    throw new AuthError(400, 'refresh_token is required', 'refresh_token_missing');
  }

  const nowIso = new Date().toISOString();
  const tokenHash = hashRefreshToken(normalizedRefreshToken);
  const tokenRow = await getRefreshTokenByHash(tokenHash);
  if (!tokenRow) {
    return;
  }

  await updateRefreshToken(tokenRow.id, { revokedAt: nowIso });

  if (tokenRow.auth_session_id) {
    try {
      await updateAuthSession(tokenRow.auth_session_id, {
        status: AUTH_SESSION_STATUSES.REVOKED,
        revokedAt: nowIso,
        lastSeenAt: nowIso
      });
    } catch (error) {
      logAuthRepositoryWarning(`Failed to revoke auth session ${tokenRow.auth_session_id}: ${error.message}`);
    }
  }
}

export function authenticateRequest(c, { allowQueryToken = false } = {}) {
  if (process.env.SKIP_AUTH === 'true') {
    return {
      ok: true,
      user: {
        sub: 'local-dev',
        user_id: 'local-dev',
        participant_id: 'local-dev',
        session_id: 'local-dev-session',
        auth_provider: 'local-dev',
        type: 'access',
        bypassed: true
      }
    };
  }

  const authHeader = c.req.header('Authorization');
  let token = '';

  if (authHeader && authHeader.startsWith('Bearer ')) {
    token = authHeader.substring(7).trim();
  }

  if (!token && allowQueryToken) {
    token = c.req.query('access_token')?.trim() || '';
  }

  if (!token) {
    return {
      ok: false,
      status: 401,
      message: 'Missing access token'
    };
  }

  try {
    const payload = verifyAccessToken(token);
    return {
      ok: true,
      user: payload
    };
  } catch (error) {
    return {
      ok: false,
      status: error.status || 401,
      message: error.message || 'Invalid or expired access token'
    };
  }
}

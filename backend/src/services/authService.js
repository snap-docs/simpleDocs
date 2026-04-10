import crypto from 'node:crypto';
import jwt from 'jsonwebtoken';
import { createClient } from '@supabase/supabase-js';
import { logger } from '../utils/logger.js';

const DEFAULT_REFRESH_LIFETIME_MS = 30 * 24 * 60 * 60 * 1000;

let adminClient = null;

class AuthError extends Error {
  constructor(status, message) {
    super(message);
    this.name = 'AuthError';
    this.status = status;
  }
}

function getAuthConfig() {
  return {
    codesTable: process.env.AUTH_CODES_TABLE || 'redeem_codes',
    codesCodeColumn: process.env.AUTH_CODES_CODE_COLUMN || 'code',
    codesParticipantIdColumn: process.env.AUTH_CODES_PARTICIPANT_ID_COLUMN || 'participant_id',
    codesUsedAtColumn: process.env.AUTH_CODES_USED_AT_COLUMN || 'used_at',
    codesIsUsedColumn: process.env.AUTH_CODES_IS_USED_COLUMN || 'is_used',
    participantsTable: process.env.AUTH_PARTICIPANTS_TABLE || 'participants',
    participantsIdColumn: process.env.AUTH_PARTICIPANTS_ID_COLUMN || 'id',
    participantsCodeColumn: process.env.AUTH_PARTICIPANTS_CODE_COLUMN || '',
    refreshTokensTable: process.env.AUTH_REFRESH_TOKENS_TABLE || 'refresh_tokens',
    refreshTokenHashColumn: process.env.AUTH_REFRESH_TOKENS_HASH_COLUMN || 'token_hash',
    refreshParticipantIdColumn: process.env.AUTH_REFRESH_TOKENS_PARTICIPANT_ID_COLUMN || 'participant_id',
    refreshExpiresAtColumn: process.env.AUTH_REFRESH_TOKENS_EXPIRES_AT_COLUMN || 'expires_at',
    refreshRevokedAtColumn: process.env.AUTH_REFRESH_TOKENS_REVOKED_AT_COLUMN || 'revoked_at',
    refreshCreatedAtColumn: process.env.AUTH_REFRESH_TOKENS_CREATED_AT_COLUMN || 'created_at'
  };
}

function getAccessTokenSecret() {
  return process.env.ACCESS_TOKEN_SECRET || process.env.SUPABASE_JWT_SECRET || '';
}

function getAccessTokenTtl() {
  return process.env.ACCESS_TOKEN_TTL || '15m';
}

function getRefreshTokenLifetimeMs() {
  const raw = Number.parseInt(process.env.REFRESH_TOKEN_TTL_DAYS || '30', 10);
  if (!Number.isFinite(raw) || raw <= 0) {
    return DEFAULT_REFRESH_LIFETIME_MS;
  }

  return raw * 24 * 60 * 60 * 1000;
}

function getAdminClient() {
  if (adminClient) {
    return adminClient;
  }

  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY || process.env.SUPABASE_ANON_KEY;

  if (!url || !key ||
      url === 'https://your-project.supabase.co' ||
      key === 'your_supabase_service_role_key_here' ||
      key === 'your_supabase_anon_key_here') {
    return null;
  }

  adminClient = createClient(url, key, {
    auth: {
      autoRefreshToken: false,
      persistSession: false
    }
  });

  return adminClient;
}

function requireAdminClient() {
  const client = getAdminClient();
  if (!client) {
    throw new AuthError(500, 'Auth database is not configured');
  }

  return client;
}

function requireAccessTokenSecret() {
  const secret = getAccessTokenSecret();
  if (!secret || secret === 'your_supabase_jwt_secret_here') {
    throw new AuthError(500, 'Access token secret is not configured');
  }

  return secret;
}

function createAccessToken(participantId) {
  const secret = requireAccessTokenSecret();
  return jwt.sign(
    {
      sub: String(participantId),
      participant_id: participantId,
      type: 'access'
    },
    secret,
    {
      expiresIn: getAccessTokenTtl()
    });
}

function hashRefreshToken(token) {
  return crypto.createHash('sha256').update(token).digest('hex');
}

function createRefreshTokenValue() {
  return crypto.randomBytes(48).toString('base64url');
}

function normalizeCode(code) {
  return typeof code === 'string' ? code.trim() : '';
}

function normalizeRefreshToken(refreshToken) {
  return typeof refreshToken === 'string' ? refreshToken.trim() : '';
}

function isCodeUsed(codeRow, config) {
  if (!codeRow) {
    return false;
  }

  return Boolean(
    codeRow[config.codesUsedAtColumn] ||
    codeRow[config.codesIsUsedColumn]
  );
}

async function loadRedeemCodeRow(client, code, config) {
  const { data, error } = await client
    .from(config.codesTable)
    .select('*')
    .eq(config.codesCodeColumn, code)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to read redeem code: ${error.message}`);
  }

  return data;
}

async function createParticipant(client, code, config) {
  const payload = {};
  if (config.participantsCodeColumn) {
    payload[config.participantsCodeColumn] = code;
  }

  let query = client
    .from(config.participantsTable)
    .insert(payload)
    .select(config.participantsIdColumn)
    .single();

  let result = await query;
  if (result.error && Object.keys(payload).length > 0) {
    result = await client
      .from(config.participantsTable)
      .insert({})
      .select(config.participantsIdColumn)
      .single();
  }

  if (result.error) {
    throw new AuthError(500, `Failed to create participant: ${result.error.message}`);
  }

  return result.data?.[config.participantsIdColumn];
}

async function markRedeemCodeUsed(client, code, participantId, config) {
  const payload = {
    [config.codesUsedAtColumn]: new Date().toISOString(),
    [config.codesParticipantIdColumn]: participantId
  };

  if (config.codesIsUsedColumn) {
    payload[config.codesIsUsedColumn] = true;
  }

  let query = client
    .from(config.codesTable)
    .update(payload)
    .eq(config.codesCodeColumn, code);

  if (config.codesUsedAtColumn) {
    query = query.is(config.codesUsedAtColumn, null);
  }

  const { data, error } = await query
    .select(config.codesCodeColumn)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to redeem code: ${error.message}`);
  }

  if (!data) {
    throw new AuthError(409, 'Redeem code has already been used');
  }
}

async function storeRefreshToken(client, participantId, refreshToken, config) {
  const nowIso = new Date().toISOString();
  const expiresAtIso = new Date(Date.now() + getRefreshTokenLifetimeMs()).toISOString();

  const payload = {
    [config.refreshTokenHashColumn]: hashRefreshToken(refreshToken),
    [config.refreshParticipantIdColumn]: participantId,
    [config.refreshExpiresAtColumn]: expiresAtIso
  };

  if (config.refreshCreatedAtColumn) {
    payload[config.refreshCreatedAtColumn] = nowIso;
  }

  if (config.refreshRevokedAtColumn) {
    payload[config.refreshRevokedAtColumn] = null;
  }

  const { error } = await client
    .from(config.refreshTokensTable)
    .insert(payload);

  if (error) {
    throw new AuthError(500, `Failed to store refresh token: ${error.message}`);
  }
}

async function loadRefreshTokenRow(client, refreshToken, config) {
  const tokenHash = hashRefreshToken(refreshToken);

  const { data, error } = await client
    .from(config.refreshTokensTable)
    .select('*')
    .eq(config.refreshTokenHashColumn, tokenHash)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to read refresh token: ${error.message}`);
  }

  return data;
}

function ensureActiveRefreshToken(tokenRow, config) {
  if (!tokenRow) {
    throw new AuthError(401, 'Invalid refresh token');
  }

  if (tokenRow[config.refreshRevokedAtColumn]) {
    throw new AuthError(401, 'Refresh token has been revoked');
  }

  const expiresAt = tokenRow[config.refreshExpiresAtColumn];
  if (!expiresAt || Number.isNaN(Date.parse(expiresAt)) || Date.parse(expiresAt) <= Date.now()) {
    throw new AuthError(401, 'Refresh token has expired');
  }
}

export function authenticateRequest(c, { allowQueryToken = false } = {}) {
  if (process.env.SKIP_AUTH === 'true') {
    return {
      ok: true,
      user: {
        sub: 'local-dev',
        participant_id: 'local-dev',
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
    const secret = requireAccessTokenSecret();
    const payload = jwt.verify(token, secret);
    if (!payload || typeof payload !== 'object' || payload.type !== 'access') {
      throw new AuthError(401, 'Invalid access token');
    }

    return {
      ok: true,
      user: payload
    };
  } catch (error) {
    if (error instanceof AuthError) {
      logger.warn(`Access token rejected: ${error.message}`);
      return {
        ok: false,
        status: error.status,
        message: error.message
      };
    }

    logger.warn(`Access token verification failed: ${error.message}`);
    return {
      ok: false,
      status: 401,
      message: 'Invalid or expired access token'
    };
  }
}

export async function redeemCode(code) {
  const normalizedCode = normalizeCode(code);
  if (!normalizedCode) {
    throw new AuthError(400, 'code is required');
  }

  const client = requireAdminClient();
  const config = getAuthConfig();
  const codeRow = await loadRedeemCodeRow(client, normalizedCode, config);

  if (!codeRow) {
    throw new AuthError(401, 'Invalid redeem code');
  }

  if (isCodeUsed(codeRow, config)) {
    throw new AuthError(409, 'Redeem code has already been used');
  }

  let participantId = codeRow[config.codesParticipantIdColumn];
  if (!participantId) {
    participantId = await createParticipant(client, normalizedCode, config);
  }

  await markRedeemCodeUsed(client, normalizedCode, participantId, config);

  const refreshToken = createRefreshTokenValue();
  await storeRefreshToken(client, participantId, refreshToken, config);

  return {
    access_token: createAccessToken(participantId),
    refresh_token: refreshToken
  };
}

export async function refreshAccessToken(refreshToken) {
  const normalizedRefreshToken = normalizeRefreshToken(refreshToken);
  if (!normalizedRefreshToken) {
    throw new AuthError(400, 'refresh_token is required');
  }

  const client = requireAdminClient();
  const config = getAuthConfig();
  const tokenRow = await loadRefreshTokenRow(client, normalizedRefreshToken, config);
  ensureActiveRefreshToken(tokenRow, config);

  return {
    access_token: createAccessToken(tokenRow[config.refreshParticipantIdColumn])
  };
}

export async function logoutRefreshToken(refreshToken) {
  const normalizedRefreshToken = normalizeRefreshToken(refreshToken);
  if (!normalizedRefreshToken) {
    throw new AuthError(400, 'refresh_token is required');
  }

  const client = requireAdminClient();
  const config = getAuthConfig();
  const tokenHash = hashRefreshToken(normalizedRefreshToken);

  const payload = {
    [config.refreshRevokedAtColumn]: new Date().toISOString()
  };

  let query = client
    .from(config.refreshTokensTable)
    .update(payload)
    .eq(config.refreshTokenHashColumn, tokenHash);

  if (config.refreshRevokedAtColumn) {
    query = query.is(config.refreshRevokedAtColumn, null);
  }

  const { error } = await query;
  if (error) {
    throw new AuthError(500, `Failed to revoke refresh token: ${error.message}`);
  }
}

export function createAuthErrorResponse(error) {
  if (error instanceof AuthError) {
    return {
      status: error.status,
      body: { error: error.message }
    };
  }

  logger.error(`Unexpected auth error: ${error.message}`);
  return {
    status: 500,
    body: { error: 'Authentication failed' }
  };
}

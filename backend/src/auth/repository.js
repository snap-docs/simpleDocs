import { createClient } from '@supabase/supabase-js';
import { getAuthRuntimeConfig } from './config.js';
import { AuthError } from './errors.js';
import { logger } from '../utils/logger.js';

let adminClient = null;

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
    throw new AuthError(500, 'Auth database is not configured', 'auth_database_missing');
  }

  return client;
}

function normalizeOptionalString(value) {
  if (value === null || value === undefined) {
    return null;
  }

  const normalized = String(value).trim();
  return normalized.length > 0 ? normalized : null;
}

function buildParticipantPayload(profile = {}) {
  const payload = {};

  if ('email' in profile) {
    payload.email = normalizeOptionalString(profile.email);
  }

  if ('emailVerified' in profile) {
    payload.email_verified = Boolean(profile.emailVerified);
  }

  if ('displayName' in profile) {
    payload.display_name = normalizeOptionalString(profile.displayName);
  }

  if ('givenName' in profile) {
    payload.given_name = normalizeOptionalString(profile.givenName);
  }

  if ('familyName' in profile) {
    payload.family_name = normalizeOptionalString(profile.familyName);
  }

  if ('avatarUrl' in profile) {
    payload.avatar_url = normalizeOptionalString(profile.avatarUrl);
  }

  if ('lastAuthenticatedAt' in profile) {
    payload.last_authenticated_at = profile.lastAuthenticatedAt || null;
  }

  return payload;
}

function buildProviderLinkPayload(link = {}) {
  return {
    participant_id: link.participantId,
    provider_type: link.providerType,
    provider_subject: link.providerSubject,
    provider_email: normalizeOptionalString(link.providerEmail),
    password_hash: link.passwordHash || null,
    password_algorithm: link.passwordAlgorithm || null,
    profile_json: link.profile || {},
    metadata_json: link.metadata || {},
    is_enabled: link.isEnabled ?? true,
    created_at: link.createdAt || new Date().toISOString(),
    linked_at: link.linkedAt || new Date().toISOString(),
    last_authenticated_at: link.lastAuthenticatedAt || null,
    last_verified_at: link.lastVerifiedAt || null
  };
}

function buildSessionPayload(session = {}) {
  return {
    participant_id: session.participantId,
    auth_provider_link_id: session.providerLinkId || null,
    auth_provider_type: session.providerType,
    client_kind: session.clientKind || 'desktop',
    client_label: normalizeOptionalString(session.clientLabel),
    metadata_json: session.metadata || {},
    status: session.status || 'active',
    created_at: session.createdAt || new Date().toISOString(),
    last_seen_at: session.lastSeenAt || null,
    revoked_at: session.revokedAt || null
  };
}

function buildRefreshTokenPayload(token = {}) {
  return {
    participant_id: token.participantId,
    auth_session_id: token.sessionId || null,
    auth_provider_link_id: token.providerLinkId || null,
    token_hash: token.tokenHash,
    expires_at: token.expiresAt,
    revoked_at: token.revokedAt || null,
    created_at: token.createdAt || new Date().toISOString(),
    last_used_at: token.lastUsedAt || null
  };
}

function mapInsertError(error, conflictMessage, fallbackPrefix) {
  if (!error) {
    return;
  }

  if (error.code === '23505') {
    throw new AuthError(409, conflictMessage, 'auth_conflict');
  }

  throw new AuthError(500, `${fallbackPrefix}: ${error.message}`, 'auth_storage_error');
}

export async function getParticipantById(participantId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.participantsTable)
    .select('*')
    .eq('id', participantId)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load user identity: ${error.message}`, 'participant_read_failed');
  }

  if (!data) {
    throw new AuthError(404, 'Authenticated user was not found', 'participant_not_found');
  }

  return data;
}

export async function createParticipant(profile = {}) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const payload = buildParticipantPayload(profile);
  const { data, error } = await client
    .from(config.participantsTable)
    .insert(payload)
    .select('*')
    .single();

  if (error) {
    throw new AuthError(500, `Failed to create user identity: ${error.message}`, 'participant_create_failed');
  }

  return data;
}

export async function updateParticipant(participantId, profile = {}) {
  const payload = buildParticipantPayload(profile);
  if (Object.keys(payload).length === 0) {
    return getParticipantById(participantId);
  }

  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.participantsTable)
    .update(payload)
    .eq('id', participantId)
    .select('*')
    .single();

  if (error) {
    throw new AuthError(500, `Failed to update user identity: ${error.message}`, 'participant_update_failed');
  }

  return data;
}

export async function listParticipantsByEmail(email) {
  const normalizedEmail = normalizeOptionalString(email)?.toLowerCase();
  if (!normalizedEmail) {
    return [];
  }

  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.participantsTable)
    .select('*')
    .ilike('email', normalizedEmail)
    .order('created_at', { ascending: true });

  if (error) {
    throw new AuthError(500, `Failed to search user identities by email: ${error.message}`, 'participant_lookup_failed');
  }

  return data || [];
}

export async function findProviderLink(providerType, providerSubject) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.providerLinksTable)
    .select('*')
    .eq('provider_type', providerType)
    .eq('provider_subject', providerSubject)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load auth method: ${error.message}`, 'provider_link_read_failed');
  }

  return data;
}

export async function getProviderLinkById(providerLinkId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.providerLinksTable)
    .select('*')
    .eq('id', providerLinkId)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load auth method: ${error.message}`, 'provider_link_read_failed');
  }

  if (!data) {
    throw new AuthError(404, 'Auth method was not found', 'provider_link_not_found');
  }

  return data;
}

export async function listProviderLinksByParticipantId(participantId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.providerLinksTable)
    .select('*')
    .eq('participant_id', participantId)
    .order('linked_at', { ascending: true });

  if (error) {
    throw new AuthError(500, `Failed to list auth methods: ${error.message}`, 'provider_link_list_failed');
  }

  return data || [];
}

export async function findProviderLinksByParticipantAndType(participantId, providerType) {
  const methods = await listProviderLinksByParticipantId(participantId);
  return methods.filter((method) => method.provider_type === providerType);
}

export async function findPreferredProviderLinkForParticipant(participantId) {
  const methods = await listProviderLinksByParticipantId(participantId);
  const enabled = methods.filter((method) => method.is_enabled !== false);
  if (enabled.length === 0) {
    return null;
  }

  enabled.sort((left, right) => {
    const leftLast = Date.parse(left.last_authenticated_at || left.linked_at || left.created_at || 0);
    const rightLast = Date.parse(right.last_authenticated_at || right.linked_at || right.created_at || 0);
    return rightLast - leftLast;
  });

  return enabled[0];
}

export async function createProviderLink(link) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const payload = buildProviderLinkPayload(link);
  const { data, error } = await client
    .from(config.providerLinksTable)
    .insert(payload)
    .select('*')
    .single();

  mapInsertError(error, 'That sign-in method is already linked to another account', 'Failed to create auth method');
  return data;
}

export async function updateProviderLink(providerLinkId, patch = {}) {
  const payload = {};

  if ('providerEmail' in patch) {
    payload.provider_email = normalizeOptionalString(patch.providerEmail);
  }

  if ('passwordHash' in patch) {
    payload.password_hash = patch.passwordHash || null;
  }

  if ('passwordAlgorithm' in patch) {
    payload.password_algorithm = patch.passwordAlgorithm || null;
  }

  if ('profile' in patch) {
    payload.profile_json = patch.profile || {};
  }

  if ('metadata' in patch) {
    payload.metadata_json = patch.metadata || {};
  }

  if ('isEnabled' in patch) {
    payload.is_enabled = patch.isEnabled;
  }

  if ('lastAuthenticatedAt' in patch) {
    payload.last_authenticated_at = patch.lastAuthenticatedAt || null;
  }

  if ('lastVerifiedAt' in patch) {
    payload.last_verified_at = patch.lastVerifiedAt || null;
  }

  if (Object.keys(payload).length === 0) {
    return getProviderLinkById(providerLinkId);
  }

  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.providerLinksTable)
    .update(payload)
    .eq('id', providerLinkId)
    .select('*')
    .single();

  mapInsertError(error, 'That sign-in method is already linked to another account', 'Failed to update auth method');
  return data;
}

export async function loadRedeemCode(code) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.redeemCodesTable)
    .select('*')
    .eq('code', code)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load redeem code: ${error.message}`, 'redeem_code_read_failed');
  }

  return data;
}

export async function markRedeemCodeUsed(code, participantId, providerLinkId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const payload = {
    participant_id: participantId,
    used_at: new Date().toISOString(),
    is_used: true
  };

  if (providerLinkId) {
    payload.auth_provider_link_id = providerLinkId;
  }

  const { data, error } = await client
    .from(config.redeemCodesTable)
    .update(payload)
    .eq('code', code)
    .is('used_at', null)
    .select('*')
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to redeem code: ${error.message}`, 'redeem_code_update_failed');
  }

  if (!data) {
    throw new AuthError(409, 'Redeem code has already been used', 'redeem_code_already_used');
  }

  return data;
}

export async function attachRedeemCodeProviderLink(code, providerLinkId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { error } = await client
    .from(config.redeemCodesTable)
    .update({ auth_provider_link_id: providerLinkId })
    .eq('code', code);

  if (error) {
    throw new AuthError(500, `Failed to attach redeem code auth method: ${error.message}`, 'redeem_code_link_failed');
  }
}

export async function createAuthSession(session) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const payload = buildSessionPayload(session);
  const { data, error } = await client
    .from(config.sessionsTable)
    .insert(payload)
    .select('*')
    .single();

  if (error) {
    throw new AuthError(500, `Failed to create auth session: ${error.message}`, 'auth_session_create_failed');
  }

  return data;
}

export async function getAuthSessionById(sessionId) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.sessionsTable)
    .select('*')
    .eq('id', sessionId)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load auth session: ${error.message}`, 'auth_session_read_failed');
  }

  if (!data) {
    throw new AuthError(401, 'Session is no longer valid', 'auth_session_not_found');
  }

  return data;
}

export async function updateAuthSession(sessionId, patch = {}) {
  const payload = {};
  if ('status' in patch) {
    payload.status = patch.status;
  }

  if ('lastSeenAt' in patch) {
    payload.last_seen_at = patch.lastSeenAt || null;
  }

  if ('revokedAt' in patch) {
    payload.revoked_at = patch.revokedAt || null;
  }

  if ('authProviderLinkId' in patch) {
    payload.auth_provider_link_id = patch.authProviderLinkId || null;
  }

  if (Object.keys(payload).length === 0) {
    return getAuthSessionById(sessionId);
  }

  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.sessionsTable)
    .update(payload)
    .eq('id', sessionId)
    .select('*')
    .single();

  if (error) {
    throw new AuthError(500, `Failed to update auth session: ${error.message}`, 'auth_session_update_failed');
  }

  return data;
}

export async function createRefreshToken(token) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const payload = buildRefreshTokenPayload(token);
  const { data, error } = await client
    .from(config.refreshTokensTable)
    .insert(payload)
    .select('*')
    .single();

  mapInsertError(error, 'Refresh token already exists', 'Failed to store refresh token');
  return data;
}

export async function getRefreshTokenByHash(tokenHash) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.refreshTokensTable)
    .select('*')
    .eq('token_hash', tokenHash)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to load refresh token: ${error.message}`, 'refresh_token_read_failed');
  }

  return data;
}

export async function updateRefreshToken(refreshTokenId, patch = {}) {
  const payload = {};
  if ('authSessionId' in patch) {
    payload.auth_session_id = patch.authSessionId || null;
  }

  if ('authProviderLinkId' in patch) {
    payload.auth_provider_link_id = patch.authProviderLinkId || null;
  }

  if ('lastUsedAt' in patch) {
    payload.last_used_at = patch.lastUsedAt || null;
  }

  if ('revokedAt' in patch) {
    payload.revoked_at = patch.revokedAt || null;
  }

  if (Object.keys(payload).length === 0) {
    return null;
  }

  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.refreshTokensTable)
    .update(payload)
    .eq('id', refreshTokenId)
    .select('*')
    .single();

  if (error) {
    throw new AuthError(500, `Failed to update refresh token: ${error.message}`, 'refresh_token_update_failed');
  }

  return data;
}

export async function revokeRefreshTokenByHash(tokenHash, revokedAt) {
  const client = requireAdminClient();
  const config = getAuthRuntimeConfig();
  const { data, error } = await client
    .from(config.refreshTokensTable)
    .update({ revoked_at: revokedAt })
    .eq('token_hash', tokenHash)
    .is('revoked_at', null)
    .select('*')
    .maybeSingle();

  if (error) {
    throw new AuthError(500, `Failed to revoke refresh token: ${error.message}`, 'refresh_token_revoke_failed');
  }

  return data;
}

export function logAuthRepositoryWarning(message) {
  logger.warn(message);
}

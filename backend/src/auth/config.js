import { AuthError } from './errors.js';

const DEFAULT_REFRESH_LIFETIME_MS = 30 * 24 * 60 * 60 * 1000;
const DEFAULT_GOOGLE_FLOW_TTL_SECONDS = 10 * 60;

function isPlaceholder(value) {
  const normalized = String(value || '').trim();
  return (
    normalized === '' ||
    normalized.includes('your_project') ||
    normalized.includes('your-project') ||
    normalized.includes('example.com') ||
    normalized.endsWith('_here')
  );
}

export function getAccessTokenSecret() {
  const secret = process.env.ACCESS_TOKEN_SECRET || process.env.SUPABASE_JWT_SECRET || '';
  if (!secret || isPlaceholder(secret)) {
    throw new AuthError(500, 'Access token secret is not configured', 'access_token_secret_missing');
  }

  return secret;
}

export function getFlowTokenSecret() {
  return process.env.AUTH_FLOW_TOKEN_SECRET || getAccessTokenSecret();
}

export function getAccessTokenTtl() {
  return process.env.ACCESS_TOKEN_TTL || '15m';
}

export function getRefreshTokenLifetimeMs() {
  const raw = Number.parseInt(process.env.REFRESH_TOKEN_TTL_DAYS || '30', 10);
  if (!Number.isFinite(raw) || raw <= 0) {
    return DEFAULT_REFRESH_LIFETIME_MS;
  }

  return raw * 24 * 60 * 60 * 1000;
}

export function getGoogleFlowTtlSeconds() {
  const raw = Number.parseInt(process.env.GOOGLE_FLOW_TTL_SECONDS || `${DEFAULT_GOOGLE_FLOW_TTL_SECONDS}`, 10);
  if (!Number.isFinite(raw) || raw <= 0) {
    return DEFAULT_GOOGLE_FLOW_TTL_SECONDS;
  }

  return raw;
}

export function getPasswordMinLength() {
  const raw = Number.parseInt(process.env.AUTH_PASSWORD_MIN_LENGTH || '10', 10);
  if (!Number.isFinite(raw) || raw < 8) {
    return 10;
  }

  return raw;
}

export function getAuthRuntimeConfig() {
  return {
    participantsTable: process.env.AUTH_PARTICIPANTS_TABLE || 'participants',
    redeemCodesTable: process.env.AUTH_CODES_TABLE || 'redeem_codes',
    providerLinksTable: process.env.AUTH_PROVIDER_LINKS_TABLE || 'auth_provider_links',
    sessionsTable: process.env.AUTH_SESSIONS_TABLE || 'auth_sessions',
    refreshTokensTable: process.env.AUTH_REFRESH_TOKENS_TABLE || 'refresh_tokens',
    requestLogsTable: process.env.REQUEST_LOGS_TABLE || 'request_logs'
  };
}

export function getGoogleOAuthConfig() {
  return {
    clientId: (process.env.GOOGLE_CLIENT_ID || '').trim(),
    clientSecret: (process.env.GOOGLE_CLIENT_SECRET || '').trim(),
    allowedEmailDomain: (process.env.GOOGLE_ALLOWED_EMAIL_DOMAIN || '').trim().toLowerCase(),
    allowedLoopbackHosts: (process.env.AUTH_ALLOWED_LOOPBACK_HOSTS || '127.0.0.1,localhost')
      .split(',')
      .map((value) => value.trim().toLowerCase())
      .filter(Boolean)
  };
}

export function requireGoogleOAuthConfig() {
  const config = getGoogleOAuthConfig();
  if (!config.clientId || isPlaceholder(config.clientId)) {
    throw new AuthError(500, 'Google sign-in is not configured', 'google_client_id_missing');
  }

  return config;
}

export function requireLoopbackRedirectUri(redirectUri) {
  const value = typeof redirectUri === 'string' ? redirectUri.trim() : '';
  if (!value) {
    throw new AuthError(400, 'redirect_uri is required', 'redirect_uri_missing');
  }

  let parsed;
  try {
    parsed = new URL(value);
  } catch {
    throw new AuthError(400, 'redirect_uri is invalid', 'redirect_uri_invalid');
  }

  const googleConfig = getGoogleOAuthConfig();
  const hostname = parsed.hostname.trim().toLowerCase();
  const isAllowedHost =
    googleConfig.allowedLoopbackHosts.includes(hostname) ||
    hostname === '::1';

  if (parsed.protocol !== 'http:' || !isAllowedHost || !parsed.port) {
    throw new AuthError(400, 'redirect_uri must be a loopback callback URL', 'redirect_uri_not_allowed');
  }

  return parsed.toString();
}

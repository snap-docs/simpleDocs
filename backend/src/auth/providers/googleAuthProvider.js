import crypto from 'node:crypto';
import { AUTH_PROVIDER_TYPES, GOOGLE_FLOW_PURPOSES } from '../constants.js';
import { requireGoogleOAuthConfig, requireLoopbackRedirectUri } from '../config.js';
import { AuthError } from '../errors.js';
import {
  createParticipant,
  createProviderLink,
  findProviderLink,
  findProviderLinksByParticipantAndType,
  getParticipantById,
  listParticipantsByEmail,
  updateParticipant,
  updateProviderLink
} from '../repository.js';
import { createCodeChallenge, createGoogleFlowToken, createRandomBase64Url, verifyGoogleFlowToken } from '../tokenService.js';

const GOOGLE_TOKEN_ENDPOINT = 'https://oauth2.googleapis.com/token';
const GOOGLE_JWKS_ENDPOINT = 'https://www.googleapis.com/oauth2/v3/certs';
let googleKeyCache = {
  expiresAt: 0,
  keys: []
};

function normalizeOptionalText(value) {
  if (value === null || value === undefined) {
    return null;
  }

  const normalized = String(value).trim();
  return normalized.length > 0 ? normalized : null;
}

function normalizeEmail(email) {
  return typeof email === 'string' ? email.trim().toLowerCase() : '';
}

function buildGoogleAuthorizationUrl(clientId, redirectUri, state, codeChallenge) {
  const scope = encodeURIComponent('openid email profile');
  return [
    'https://accounts.google.com/o/oauth2/v2/auth',
    `?client_id=${encodeURIComponent(clientId)}`,
    `&redirect_uri=${encodeURIComponent(redirectUri)}`,
    '&response_type=code',
    `&scope=${scope}`,
    `&state=${encodeURIComponent(state)}`,
    `&code_challenge=${encodeURIComponent(codeChallenge)}`,
    '&code_challenge_method=S256',
    '&prompt=select_account'
  ].join('');
}

function buildGoogleProviderProfile(identity) {
  return {
    display_name: identity.displayName || null,
    given_name: identity.givenName || null,
    family_name: identity.familyName || null,
    avatar_url: identity.avatarUrl || null
  };
}

function buildParticipantPatchFromGoogleIdentity(participant, identity) {
  const patch = {
    lastAuthenticatedAt: new Date().toISOString()
  };

  const normalizedEmail = normalizeEmail(identity.email);
  if (
    normalizedEmail &&
    (!participant.email || participant.email === normalizedEmail || participant.email_verified !== true)
  ) {
    patch.email = normalizedEmail;
    patch.emailVerified = identity.emailVerified;
  }

  if (!participant.display_name && identity.displayName) {
    patch.displayName = identity.displayName;
  }

  if (!participant.given_name && identity.givenName) {
    patch.givenName = identity.givenName;
  }

  if (!participant.family_name && identity.familyName) {
    patch.familyName = identity.familyName;
  }

  if (!participant.avatar_url && identity.avatarUrl) {
    patch.avatarUrl = identity.avatarUrl;
  }

  return patch;
}

function normalizeBoolean(value) {
  return value === true || value === 'true' || value === 1 || value === '1';
}

function base64UrlDecodeToBuffer(value) {
  const normalized = String(value || '').replace(/-/g, '+').replace(/_/g, '/');
  const remainder = normalized.length % 4;
  const padded = remainder === 0
    ? normalized
    : normalized + '='.repeat(4 - remainder);
  return Buffer.from(padded, 'base64');
}

function parseGoogleJwksCacheTtl(headers) {
  const cacheControl = headers.get('cache-control') || '';
  const maxAgeMatch = cacheControl.match(/max-age=(\d+)/i);
  if (!maxAgeMatch) {
    return 60 * 60 * 1000;
  }

  const seconds = Number.parseInt(maxAgeMatch[1], 10);
  return Number.isFinite(seconds) && seconds > 0
    ? seconds * 1000
    : 60 * 60 * 1000;
}

async function loadGoogleSigningKeys() {
  if (googleKeyCache.expiresAt > Date.now() && googleKeyCache.keys.length > 0) {
    return googleKeyCache.keys;
  }

  let response;
  try {
    response = await fetch(GOOGLE_JWKS_ENDPOINT);
  } catch (error) {
    throw new AuthError(502, `Unable to load Google signing keys: ${error.message}`, 'google_keys_unavailable');
  }

  let body = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }

  if (!response.ok || !Array.isArray(body?.keys) || body.keys.length === 0) {
    throw new AuthError(502, 'Google signing keys were unavailable', 'google_keys_unavailable');
  }

  googleKeyCache = {
    expiresAt: Date.now() + parseGoogleJwksCacheTtl(response.headers),
    keys: body.keys
  };

  return googleKeyCache.keys;
}

async function verifyGoogleIdToken(idToken) {
  const parts = String(idToken || '').split('.');
  if (parts.length !== 3) {
    throw new AuthError(401, 'Google sign-in returned an invalid identity token', 'google_id_token_invalid');
  }

  let header;
  let claims;
  try {
    header = JSON.parse(base64UrlDecodeToBuffer(parts[0]).toString('utf8'));
    claims = JSON.parse(base64UrlDecodeToBuffer(parts[1]).toString('utf8'));
  } catch {
    throw new AuthError(401, 'Google sign-in returned an unreadable identity token', 'google_id_token_invalid');
  }

  if (header?.alg !== 'RS256' || typeof header?.kid !== 'string' || !header.kid.trim()) {
    throw new AuthError(401, 'Google sign-in returned an unsupported identity token', 'google_id_token_invalid');
  }

  const keys = await loadGoogleSigningKeys();
  const signingKey = keys.find((entry) => entry.kid === header.kid && entry.kty === 'RSA' && entry.use === 'sig');
  if (!signingKey) {
    googleKeyCache = {
      expiresAt: 0,
      keys: []
    };
    throw new AuthError(401, 'Google sign-in key id was not recognized', 'google_id_token_invalid');
  }

  const signature = base64UrlDecodeToBuffer(parts[2]);
  const signedContent = Buffer.from(`${parts[0]}.${parts[1]}`, 'utf8');
  const publicKey = crypto.createPublicKey({
    key: signingKey,
    format: 'jwk'
  });
  const verified = crypto.verify('RSA-SHA256', signedContent, publicKey, signature);
  if (!verified) {
    throw new AuthError(401, 'Google sign-in token signature was invalid', 'google_id_token_invalid');
  }

  return claims;
}

async function exchangeGoogleCodeForIdentity({ code, codeVerifier, redirectUri }) {
  const oauthConfig = requireGoogleOAuthConfig();
  const requestBody = new URLSearchParams({
    client_id: oauthConfig.clientId,
    code,
    code_verifier: codeVerifier,
    grant_type: 'authorization_code',
    redirect_uri: redirectUri
  });

  if (oauthConfig.clientSecret) {
    requestBody.set('client_secret', oauthConfig.clientSecret);
  }

  let response;
  try {
    response = await fetch(GOOGLE_TOKEN_ENDPOINT, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      body: requestBody.toString()
    });
  } catch (error) {
    throw new AuthError(502, `Google token exchange failed: ${error.message}`, 'google_exchange_failed');
  }

  let body = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }

  if (!response.ok) {
    const detail = typeof body?.error_description === 'string'
      ? body.error_description
      : typeof body?.error === 'string'
        ? body.error
        : 'Google sign-in was rejected';
    throw new AuthError(401, detail, 'google_exchange_failed');
  }

  const idToken = typeof body?.id_token === 'string' ? body.id_token.trim() : '';
  if (!idToken) {
    throw new AuthError(401, 'Google sign-in did not return an identity token', 'google_id_token_missing');
  }

  const claims = await verifyGoogleIdToken(idToken);

  const issuer = String(claims.iss || '').trim();
  if (issuer !== 'accounts.google.com' && issuer !== 'https://accounts.google.com') {
    throw new AuthError(401, 'Google sign-in issuer was invalid', 'google_issuer_invalid');
  }

  const audience = claims.aud;
  const audienceMatches = Array.isArray(audience)
    ? audience.some((entry) => String(entry || '').trim() === oauthConfig.clientId)
    : String(audience || '').trim() === oauthConfig.clientId;
  if (!audienceMatches) {
    throw new AuthError(401, 'Google sign-in audience did not match this application', 'google_audience_invalid');
  }

  const subject = String(claims.sub || '').trim();
  if (!subject) {
    throw new AuthError(401, 'Google sign-in did not include a user id', 'google_subject_missing');
  }

  const expRaw = claims.exp;
  const expSeconds = typeof expRaw === 'number' ? expRaw : Number.parseInt(String(expRaw || ''), 10);
  if (!Number.isFinite(expSeconds) || expSeconds * 1000 <= Date.now()) {
    throw new AuthError(401, 'Google sign-in token has expired', 'google_token_expired');
  }

  const email = normalizeEmail(claims.email);
  const emailVerified = normalizeBoolean(claims.email_verified);
  if (oauthConfig.allowedEmailDomain) {
    const allowedSuffix = `@${oauthConfig.allowedEmailDomain}`;
    if (!email.endsWith(allowedSuffix) || !emailVerified) {
      throw new AuthError(403, 'This Google account is not allowed for this environment', 'google_email_domain_rejected');
    }
  }

  return {
    subject,
    email,
    emailVerified,
    displayName: normalizeOptionalText(claims.name),
    givenName: normalizeOptionalText(claims.given_name),
    familyName: normalizeOptionalText(claims.family_name),
    avatarUrl: normalizeOptionalText(claims.picture)
  };
}

async function upsertGoogleProviderLink(participant, identity, existingLink = null) {
  const participantGoogleMethods = await findProviderLinksByParticipantAndType(participant.id, AUTH_PROVIDER_TYPES.GOOGLE);
  const conflictingGoogleMethod = participantGoogleMethods.find(
    (method) => method.id !== existingLink?.id && method.provider_subject !== identity.subject
  );
  if (conflictingGoogleMethod) {
    throw new AuthError(409, 'This account already has Google sign-in configured', 'google_provider_exists');
  }

  const participantPatch = buildParticipantPatchFromGoogleIdentity(participant, identity);
  const updatedParticipant = await updateParticipant(participant.id, participantPatch);
  const providerProfile = buildGoogleProviderProfile(identity);

  if (existingLink) {
    const updatedLink = await updateProviderLink(existingLink.id, {
      providerEmail: identity.email || null,
      profile: providerProfile,
      lastAuthenticatedAt: new Date().toISOString(),
      lastVerifiedAt: identity.emailVerified ? new Date().toISOString() : null
    });

    return {
      participant: updatedParticipant,
      providerLink: updatedLink
    };
  }

  const providerLink = await createProviderLink({
    participantId: updatedParticipant.id,
    providerType: AUTH_PROVIDER_TYPES.GOOGLE,
    providerSubject: identity.subject,
    providerEmail: identity.email || null,
    profile: providerProfile,
    lastVerifiedAt: identity.emailVerified ? new Date().toISOString() : null
  });

  return {
    participant: updatedParticipant,
    providerLink
  };
}

export async function prepareGoogleLogin(redirectUri) {
  const oauthConfig = requireGoogleOAuthConfig();
  const normalizedRedirectUri = requireLoopbackRedirectUri(redirectUri);
  const state = createRandomBase64Url(24);
  const codeVerifier = createRandomBase64Url(64);
  const codeChallenge = createCodeChallenge(codeVerifier);

  return {
    authorization_url: buildGoogleAuthorizationUrl(
      oauthConfig.clientId,
      normalizedRedirectUri,
      state,
      codeChallenge
    ),
    flow_token: createGoogleFlowToken({
      purpose: GOOGLE_FLOW_PURPOSES.LOGIN,
      state,
      codeVerifier,
      redirectUri: normalizedRedirectUri
    })
  };
}

export async function prepareGoogleLink(redirectUri, participantId) {
  const oauthConfig = requireGoogleOAuthConfig();
  const normalizedRedirectUri = requireLoopbackRedirectUri(redirectUri);
  const state = createRandomBase64Url(24);
  const codeVerifier = createRandomBase64Url(64);
  const codeChallenge = createCodeChallenge(codeVerifier);

  return {
    authorization_url: buildGoogleAuthorizationUrl(
      oauthConfig.clientId,
      normalizedRedirectUri,
      state,
      codeChallenge
    ),
    flow_token: createGoogleFlowToken({
      purpose: GOOGLE_FLOW_PURPOSES.LINK,
      participantId,
      state,
      codeVerifier,
      redirectUri: normalizedRedirectUri
    })
  };
}

export async function completeGoogleLogin({ code, state, flowToken }) {
  const normalizedCode = typeof code === 'string' ? code.trim() : '';
  const normalizedState = typeof state === 'string' ? state.trim() : '';
  if (!normalizedCode) {
    throw new AuthError(400, 'code is required', 'google_code_missing');
  }

  const flow = verifyGoogleFlowToken(flowToken);
  if (flow.purpose !== GOOGLE_FLOW_PURPOSES.LOGIN) {
    throw new AuthError(400, 'Google sign-in flow was not created for login', 'google_flow_purpose_invalid');
  }

  if (!normalizedState || normalizedState !== flow.state) {
    throw new AuthError(400, 'Google sign-in state did not match this app session', 'google_state_invalid');
  }

  const identity = await exchangeGoogleCodeForIdentity({
    code: normalizedCode,
    codeVerifier: flow.code_verifier,
    redirectUri: flow.redirect_uri
  });

  const existingLink = await findProviderLink(AUTH_PROVIDER_TYPES.GOOGLE, identity.subject);
  if (existingLink) {
    const participant = await getParticipantById(existingLink.participant_id);
    return upsertGoogleProviderLink(participant, identity, existingLink);
  }

  const emailMatches = identity.email
    ? await listParticipantsByEmail(identity.email)
    : [];
  if (emailMatches.length > 0) {
    throw new AuthError(
      409,
      'That Google account matches an existing user. Sign in with an existing method first, then link Google from the account menu.',
      'google_link_required'
    );
  }

  const participant = await createParticipant({
    email: identity.email || null,
    emailVerified: identity.emailVerified,
    displayName: identity.displayName,
    givenName: identity.givenName,
    familyName: identity.familyName,
    avatarUrl: identity.avatarUrl,
    lastAuthenticatedAt: new Date().toISOString()
  });

  return upsertGoogleProviderLink(participant, identity, null);
}

export async function completeGoogleLink({ code, state, flowToken, expectedParticipantId = null }) {
  const normalizedCode = typeof code === 'string' ? code.trim() : '';
  const normalizedState = typeof state === 'string' ? state.trim() : '';
  if (!normalizedCode) {
    throw new AuthError(400, 'code is required', 'google_code_missing');
  }

  const flow = verifyGoogleFlowToken(flowToken);
  if (flow.purpose !== GOOGLE_FLOW_PURPOSES.LINK) {
    throw new AuthError(400, 'Google sign-in flow was not created for linking', 'google_flow_purpose_invalid');
  }

  if (!normalizedState || normalizedState !== flow.state) {
    throw new AuthError(400, 'Google sign-in state did not match this app session', 'google_state_invalid');
  }

  if (!flow.participant_id) {
    throw new AuthError(400, 'Google link flow is missing the target account', 'google_link_target_missing');
  }

  if (expectedParticipantId && flow.participant_id !== expectedParticipantId) {
    throw new AuthError(403, 'Google link flow does not match the signed-in user', 'google_link_user_mismatch');
  }

  const participant = await getParticipantById(flow.participant_id);
  const identity = await exchangeGoogleCodeForIdentity({
    code: normalizedCode,
    codeVerifier: flow.code_verifier,
    redirectUri: flow.redirect_uri
  });

  const existingLink = await findProviderLink(AUTH_PROVIDER_TYPES.GOOGLE, identity.subject);
  if (existingLink && existingLink.participant_id !== participant.id) {
    throw new AuthError(409, 'That Google account is already linked to another user', 'google_already_linked');
  }

  return upsertGoogleProviderLink(participant, identity, existingLink);
}

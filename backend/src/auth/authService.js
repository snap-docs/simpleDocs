import { AUTH_PROVIDER_TYPES } from './constants.js';
import { AuthError } from './errors.js';
import {
  findProviderLinksByParticipantAndType,
  getParticipantById,
  listProviderLinksByParticipantId
} from './repository.js';
import {
  authenticateRequest,
  logoutSessionByRefreshToken,
  refreshAccessTokenFromRefreshToken,
  startAuthenticatedSession
} from './sessionService.js';
import { completeGoogleLegacyLogin, completeGoogleLink, completeGoogleLogin, prepareGoogleLink, prepareGoogleLogin } from './providers/googleAuthProvider.js';
import { authenticateWithEmailPassword, linkEmailPasswordToParticipant, registerWithEmailPassword } from './providers/passwordAuthProvider.js';
import { authenticateWithRedeemCode, linkRedeemCodeToParticipant } from './providers/redeemCodeAuthProvider.js';

function getParticipantIdFromAuthUser(authUser) {
  const participantId = authUser?.participant_id || authUser?.user_id || authUser?.sub;
  if (!participantId || participantId === 'local-dev') {
    throw new AuthError(401, 'Authenticated user is unavailable', 'participant_missing');
  }

  return String(participantId);
}

function maskRedeemCode(code) {
  const normalized = String(code || '').trim();
  if (normalized.length <= 4) {
    return normalized;
  }

  return `Code ending ${normalized.substring(normalized.length - 4)}`;
}

function buildMethodLabel(method) {
  switch (method.provider_type) {
    case AUTH_PROVIDER_TYPES.REDEEM_CODE:
      return maskRedeemCode(method.provider_subject);
    case AUTH_PROVIDER_TYPES.GOOGLE:
      return method.provider_email || method.profile_json?.display_name || 'Google';
    case AUTH_PROVIDER_TYPES.PASSWORD:
      return method.provider_email || 'Email/password';
    default:
      return method.provider_type;
  }
}

function buildUserSummary(participant) {
  return {
    id: participant.id,
    display_name: participant.display_name || null,
    email: participant.email || null,
    email_verified: Boolean(participant.email_verified),
    given_name: participant.given_name || null,
    family_name: participant.family_name || null,
    avatar_url: participant.avatar_url || null,
    last_authenticated_at: participant.last_authenticated_at || null
  };
}

function buildMethodSummary(method) {
  return {
    id: method.id,
    provider: method.provider_type,
    label: buildMethodLabel(method),
    email: method.provider_email || null,
    linked_at: method.linked_at || method.created_at || null,
    last_authenticated_at: method.last_authenticated_at || null,
    is_enabled: method.is_enabled !== false
  };
}

async function buildAuthStateForParticipant(participantId) {
  const participant = await getParticipantById(participantId);
  const methods = await listProviderLinksByParticipantId(participantId);

  return {
    user: buildUserSummary(participant),
    methods: methods.map(buildMethodSummary)
  };
}

async function buildLoginResponse(authenticationResult) {
  const session = await startAuthenticatedSession(authenticationResult);
  const authState = await buildAuthStateForParticipant(authenticationResult.participant.id);

  return {
    access_token: session.accessToken,
    refresh_token: session.refreshToken,
    user: authState.user,
    methods: authState.methods
  };
}

export async function redeemCode(code) {
  return buildLoginResponse(await authenticateWithRedeemCode(code));
}

export async function loginWithEmailPassword(email, password) {
  return buildLoginResponse(await authenticateWithEmailPassword({ email, password }));
}

export async function registerEmailPassword(email, password, displayName = null) {
  return buildLoginResponse(await registerWithEmailPassword({ email, password, displayName }));
}

export async function prepareGoogleLoginFlow(redirectUri) {
  return prepareGoogleLogin(redirectUri);
}

export async function completeGoogleLoginFlow(code, state, flowToken) {
  return buildLoginResponse(await completeGoogleLogin({ code, state, flowToken }));
}

export async function completeGoogleLegacyLoginFlow(code, codeVerifier, redirectUri) {
  return buildLoginResponse(await completeGoogleLegacyLogin({
    code,
    codeVerifier,
    redirectUri
  }));
}

export async function prepareGoogleLinkFlow(redirectUri, authUser) {
  return prepareGoogleLink(redirectUri, getParticipantIdFromAuthUser(authUser));
}

export async function completeGoogleLinkFlow(code, state, flowToken) {
  const linked = await completeGoogleLink({ code, state, flowToken });
  return buildAuthStateForParticipant(linked.participant.id);
}

export async function completeGoogleLinkFlowForAuthenticatedUser(code, state, flowToken, authUser) {
  const participantId = getParticipantIdFromAuthUser(authUser);
  const linked = await completeGoogleLink({
    code,
    state,
    flowToken,
    expectedParticipantId: participantId
  });
  return buildAuthStateForParticipant(linked.participant.id);
}

export async function linkRedeemCode(code, authUser) {
  const participantId = getParticipantIdFromAuthUser(authUser);
  await linkRedeemCodeToParticipant(code, participantId);
  return buildAuthStateForParticipant(participantId);
}

export async function linkEmailPassword(email, password, displayName, authUser) {
  const participantId = getParticipantIdFromAuthUser(authUser);
  await linkEmailPasswordToParticipant({
    participantId,
    email,
    password,
    displayName
  });

  return buildAuthStateForParticipant(participantId);
}

export async function getCurrentAuthState(authUser) {
  return buildAuthStateForParticipant(getParticipantIdFromAuthUser(authUser));
}

export async function refreshAccessToken(refreshToken) {
  return refreshAccessTokenFromRefreshToken(refreshToken);
}

export async function logoutRefreshToken(refreshToken) {
  return logoutSessionByRefreshToken(refreshToken);
}

export async function getLinkedPasswordProviderCount(authUser) {
  const participantId = getParticipantIdFromAuthUser(authUser);
  const methods = await findProviderLinksByParticipantAndType(participantId, AUTH_PROVIDER_TYPES.PASSWORD);
  return methods.length;
}

export { authenticateRequest };

export function createAuthErrorResponse(error) {
  if (error instanceof AuthError) {
    return {
      status: error.status,
      body: {
        error: error.message,
        code: error.code
      }
    };
  }

  return {
    status: 500,
    body: {
      error: 'Authentication failed',
      code: 'auth_unexpected_error'
    }
  };
}

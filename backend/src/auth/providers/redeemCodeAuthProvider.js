import { AUTH_PROVIDER_TYPES } from '../constants.js';
import { AuthError } from '../errors.js';
import {
  attachRedeemCodeProviderLink,
  createParticipant,
  createProviderLink,
  findProviderLink,
  getParticipantById,
  loadRedeemCode,
  markRedeemCodeUsed
} from '../repository.js';

function normalizeRedeemCode(code) {
  return typeof code === 'string' ? code.trim().toUpperCase() : '';
}

async function ensureRedeemProviderLink(participantId, normalizedCode) {
  const existingLink = await findProviderLink(AUTH_PROVIDER_TYPES.REDEEM_CODE, normalizedCode);
  if (existingLink) {
    if (existingLink.participant_id !== participantId) {
      throw new AuthError(409, 'That redeem code is already linked to another account', 'redeem_code_conflict');
    }

    return existingLink;
  }

  return createProviderLink({
    participantId,
    providerType: AUTH_PROVIDER_TYPES.REDEEM_CODE,
    providerSubject: normalizedCode,
    metadata: {
      redeem_code: normalizedCode
    }
  });
}

export async function authenticateWithRedeemCode(code) {
  const normalizedCode = normalizeRedeemCode(code);
  if (!normalizedCode) {
    throw new AuthError(400, 'code is required', 'redeem_code_missing');
  }

  const redeemCodeRow = await loadRedeemCode(normalizedCode);
  if (!redeemCodeRow) {
    throw new AuthError(401, 'Invalid redeem code', 'redeem_code_invalid');
  }

  if (redeemCodeRow.used_at || redeemCodeRow.is_used) {
    throw new AuthError(409, 'Redeem code has already been used', 'redeem_code_already_used');
  }

  const participant = redeemCodeRow.participant_id
    ? await getParticipantById(redeemCodeRow.participant_id)
    : await createParticipant({});

  const providerLink = await ensureRedeemProviderLink(participant.id, normalizedCode);
  await markRedeemCodeUsed(normalizedCode, participant.id, providerLink.id);
  await attachRedeemCodeProviderLink(normalizedCode, providerLink.id);

  return {
    participant,
    providerLink
  };
}

export async function linkRedeemCodeToParticipant(code, participantId) {
  const normalizedCode = normalizeRedeemCode(code);
  if (!normalizedCode) {
    throw new AuthError(400, 'code is required', 'redeem_code_missing');
  }

  const redeemCodeRow = await loadRedeemCode(normalizedCode);
  if (!redeemCodeRow) {
    throw new AuthError(401, 'Invalid redeem code', 'redeem_code_invalid');
  }

  if (redeemCodeRow.used_at || redeemCodeRow.is_used) {
    if (redeemCodeRow.participant_id === participantId) {
      return ensureRedeemProviderLink(participantId, normalizedCode);
    }

    throw new AuthError(409, 'Redeem code has already been used', 'redeem_code_already_used');
  }

  if (redeemCodeRow.participant_id && redeemCodeRow.participant_id !== participantId) {
    throw new AuthError(409, 'That redeem code belongs to a different account', 'redeem_code_wrong_account');
  }

  const providerLink = await ensureRedeemProviderLink(participantId, normalizedCode);
  await markRedeemCodeUsed(normalizedCode, participantId, providerLink.id);
  await attachRedeemCodeProviderLink(normalizedCode, providerLink.id);
  return providerLink;
}

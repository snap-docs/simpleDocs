import crypto from 'node:crypto';
import { AUTH_PROVIDER_TYPES } from '../constants.js';
import { getPasswordMinLength } from '../config.js';
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

const SCRYPT_KEY_LENGTH = 64;
const SCRYPT_COST = 16384;
const SCRYPT_BLOCK_SIZE = 8;
const SCRYPT_PARALLELISM = 1;
const SCRYPT_ALGORITHM_NAME = `scrypt-v1:${SCRYPT_COST}:${SCRYPT_BLOCK_SIZE}:${SCRYPT_PARALLELISM}:${SCRYPT_KEY_LENGTH}`;

function normalizeEmail(email) {
  return typeof email === 'string' ? email.trim().toLowerCase() : '';
}

function normalizeOptionalText(value) {
  if (value === null || value === undefined) {
    return null;
  }

  const normalized = String(value).trim();
  return normalized.length > 0 ? normalized : null;
}

function validateEmail(email) {
  if (!email) {
    throw new AuthError(400, 'email is required', 'email_missing');
  }

  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  if (!emailRegex.test(email)) {
    throw new AuthError(400, 'email is invalid', 'email_invalid');
  }
}

function validatePassword(password) {
  const value = typeof password === 'string' ? password : '';
  if (!value) {
    throw new AuthError(400, 'password is required', 'password_missing');
  }

  if (value.length < getPasswordMinLength()) {
    throw new AuthError(400, `password must be at least ${getPasswordMinLength()} characters`, 'password_too_short');
  }

  if (value.length > 200) {
    throw new AuthError(400, 'password is too long', 'password_too_long');
  }
}

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('base64url');
  const derivedKey = crypto.scryptSync(password, salt, SCRYPT_KEY_LENGTH, {
    N: SCRYPT_COST,
    r: SCRYPT_BLOCK_SIZE,
    p: SCRYPT_PARALLELISM
  });

  return `${SCRYPT_ALGORITHM_NAME}$${salt}$${derivedKey.toString('base64url')}`;
}

function verifyPassword(password, storedHash) {
  const parts = String(storedHash || '').split('$');
  if (parts.length !== 3) {
    return false;
  }

  const [algorithm, salt, hash] = parts;
  if (algorithm !== SCRYPT_ALGORITHM_NAME || !salt || !hash) {
    return false;
  }

  const expected = Buffer.from(hash, 'base64url');
  const actual = crypto.scryptSync(password, salt, expected.length, {
    N: SCRYPT_COST,
    r: SCRYPT_BLOCK_SIZE,
    p: SCRYPT_PARALLELISM
  });

  return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
}

async function upsertPasswordProviderLink(participant, email, password, displayName = null) {
  const normalizedEmail = normalizeEmail(email);
  validateEmail(normalizedEmail);
  validatePassword(password);

  const existingLink = await findProviderLink(AUTH_PROVIDER_TYPES.PASSWORD, normalizedEmail);
  if (existingLink && existingLink.participant_id !== participant.id) {
    throw new AuthError(409, 'That email address is already linked to another account', 'email_already_linked');
  }

  const participantPasswordMethods = await findProviderLinksByParticipantAndType(participant.id, AUTH_PROVIDER_TYPES.PASSWORD);
  const conflictingParticipantPasswordMethod = participantPasswordMethods.find(
    (method) => method.id !== existingLink?.id && method.provider_subject !== normalizedEmail
  );
  if (conflictingParticipantPasswordMethod) {
    throw new AuthError(409, 'This account already has email/password sign-in configured', 'password_provider_exists');
  }

  const passwordHash = hashPassword(password);
  const profilePatch = {
    displayName: normalizeOptionalText(displayName) || participant.display_name || null,
    email: !participant.email || participant.email === normalizedEmail || participant.email_verified !== true
      ? normalizedEmail
      : participant.email
  };

  const updatedParticipant = await updateParticipant(participant.id, profilePatch);

  if (existingLink) {
    const updatedLink = await updateProviderLink(existingLink.id, {
      providerEmail: normalizedEmail,
      passwordHash,
      passwordAlgorithm: SCRYPT_ALGORITHM_NAME,
      profile: {
        display_name: updatedParticipant.display_name || normalizedEmail
      }
    });

    return {
      participant: updatedParticipant,
      providerLink: updatedLink
    };
  }

  const providerLink = await createProviderLink({
    participantId: participant.id,
    providerType: AUTH_PROVIDER_TYPES.PASSWORD,
    providerSubject: normalizedEmail,
    providerEmail: normalizedEmail,
    passwordHash,
    passwordAlgorithm: SCRYPT_ALGORITHM_NAME,
    profile: {
      display_name: updatedParticipant.display_name || normalizedEmail
    }
  });

  return {
    participant: updatedParticipant,
    providerLink
  };
}

export async function registerWithEmailPassword({ email, password, displayName = null }) {
  const normalizedEmail = normalizeEmail(email);
  validateEmail(normalizedEmail);
  validatePassword(password);

  const existingLink = await findProviderLink(AUTH_PROVIDER_TYPES.PASSWORD, normalizedEmail);
  if (existingLink) {
    throw new AuthError(409, 'That email address is already linked to an account', 'email_already_linked');
  }

  const existingParticipants = await listParticipantsByEmail(normalizedEmail);
  if (existingParticipants.length > 0) {
    throw new AuthError(
      409,
      'That email address already belongs to an existing user. Sign in with that method first, then add email/password from the account menu.',
      'password_link_required'
    );
  }

  const participant = await createParticipant({
    email: normalizedEmail,
    emailVerified: false,
    displayName: normalizeOptionalText(displayName)
  });

  return upsertPasswordProviderLink(participant, normalizedEmail, password, displayName);
}

export async function authenticateWithEmailPassword({ email, password }) {
  const normalizedEmail = normalizeEmail(email);
  validateEmail(normalizedEmail);
  validatePassword(password);

  const providerLink = await findProviderLink(AUTH_PROVIDER_TYPES.PASSWORD, normalizedEmail);
  if (!providerLink || !providerLink.password_hash || providerLink.is_enabled === false) {
    throw new AuthError(401, 'Invalid email or password', 'password_auth_invalid');
  }

  if (!verifyPassword(password, providerLink.password_hash)) {
    throw new AuthError(401, 'Invalid email or password', 'password_auth_invalid');
  }

  const participant = await getParticipantById(providerLink.participant_id);
  return {
    participant,
    providerLink
  };
}

export async function linkEmailPasswordToParticipant({ participantId, email, password, displayName = null }) {
  const participant = await getParticipantById(participantId);
  return upsertPasswordProviderLink(participant, email, password, displayName);
}

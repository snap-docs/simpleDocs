import { logger } from '../utils/logger.js';

function isBlank(value) {
  return !value || !String(value).trim();
}

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

export function validateRuntimeConfig(environmentName = 'development') {
  const warnings = [];
  const errors = [];
  const normalizedEnvironment = String(environmentName).trim().toLowerCase();
  const isProduction = normalizedEnvironment === 'production';
  const isProtectedMode = process.env.SKIP_AUTH !== 'true';
  const provider = (process.env.AI_PROVIDER || 'openrouter').trim().toLowerCase();
  const supabaseUrl = process.env.SUPABASE_URL;
  const serviceRoleKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
  const anonKey = process.env.SUPABASE_ANON_KEY;
  const geminiApiKey = process.env.GOOGLE_API_KEY || process.env.GEMINI_API_KEY;
  const explicitAccessTokenSecret = process.env.ACCESS_TOKEN_SECRET;
  const fallbackJwtSecret = process.env.SUPABASE_JWT_SECRET;
  const accessTokenSecret = explicitAccessTokenSecret || fallbackJwtSecret;
  const addIssue = (message, productionError = false) => {
    if (isProduction && productionError) {
      errors.push(message);
      return;
    }

    warnings.push(message);
  };

  if (process.env.SKIP_AUTH === 'true') {
    addIssue('SKIP_AUTH=true is enabled. Auth is bypassed for protected backend routes.', true);
  }

  if (isBlank(supabaseUrl) || isPlaceholder(supabaseUrl)) {
    addIssue('SUPABASE_URL is missing or still a placeholder.', true);
  }

  if (isProtectedMode && (isBlank(accessTokenSecret) || isPlaceholder(accessTokenSecret))) {
    addIssue('ACCESS_TOKEN_SECRET (or SUPABASE_JWT_SECRET fallback) is missing for protected auth mode.', true);
  }
  else if (isProtectedMode && (isBlank(explicitAccessTokenSecret) || isPlaceholder(explicitAccessTokenSecret))) {
    addIssue('ACCESS_TOKEN_SECRET is not set explicitly. The backend is falling back to SUPABASE_JWT_SECRET.');
  }

  if (isProtectedMode && (isBlank(serviceRoleKey) || isPlaceholder(serviceRoleKey))) {
    addIssue('SUPABASE_SERVICE_ROLE_KEY is missing. Redeem-code auth and request logging may fail against hosted DB policies.', true);
  }

  if (!serviceRoleKey && anonKey) {
    addIssue('Using SUPABASE_ANON_KEY fallback for DB access. This is not recommended for deployment.');
  }

  if (['gemini', 'google'].includes(provider) && (isBlank(geminiApiKey) || isPlaceholder(geminiApiKey))) {
    addIssue('GEMINI_API_KEY or GOOGLE_API_KEY is missing while AI_PROVIDER=gemini.', true);
  }

  if (provider === 'groq') {
    const groqKeys = [process.env.GROQ_API_KEY, process.env.GROQ_API_KEY_FALLBACK, process.env.GROQ_API_KEYS]
      .filter((value) => !isBlank(value) && !isPlaceholder(value));
    if (groqKeys.length === 0) {
      addIssue('A Groq API key is missing while AI_PROVIDER=groq.', true);
    }
  }

  if (provider === 'openrouter' && (isBlank(process.env.OPENROUTER_API_KEY) || isPlaceholder(process.env.OPENROUTER_API_KEY))) {
    addIssue('OPENROUTER_API_KEY is missing while AI_PROVIDER=openrouter.', true);
  }

  if (!['gemini', 'google', 'groq', 'openrouter'].includes(provider)) {
    addIssue(`AI_PROVIDER=${provider} is not supported.`, true);
  }

  if (warnings.length === 0 && errors.length === 0) {
    logger.info(`Runtime config looks ready for ${environmentName}.`);
    return;
  }

  for (const warning of warnings) {
    logger.warn(`[config] ${warning}`);
  }

  for (const error of errors) {
    logger.error(`[config] ${error}`);
  }

  if (errors.length > 0) {
    throw new Error(`Unsafe production configuration: ${errors.length} error(s).`);
  }
}

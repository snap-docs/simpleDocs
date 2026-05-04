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
  const isProtectedMode = process.env.SKIP_AUTH !== 'true';
  const supabaseUrl = process.env.SUPABASE_URL;
  const serviceRoleKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
  const anonKey = process.env.SUPABASE_ANON_KEY;
  const explicitAccessTokenSecret = process.env.ACCESS_TOKEN_SECRET;
  const fallbackJwtSecret = process.env.SUPABASE_JWT_SECRET;
  const accessTokenSecret = explicitAccessTokenSecret || fallbackJwtSecret;
  const explicitFlowTokenSecret = process.env.AUTH_FLOW_TOKEN_SECRET;
  const googleClientId = process.env.GOOGLE_CLIENT_ID;
  const anthropicApiKey = process.env.ANTHROPIC_API_KEY;
  const visionEnabled = process.env.ENABLE_VISION_PIPELINE !== 'false';

  if (process.env.SKIP_AUTH === 'true') {
    warnings.push('SKIP_AUTH=true is enabled. Auth is bypassed for protected backend routes.');
  }

  if (isBlank(supabaseUrl) || isPlaceholder(supabaseUrl)) {
    warnings.push('SUPABASE_URL is missing or still a placeholder.');
  }

  if (isProtectedMode && (isBlank(accessTokenSecret) || isPlaceholder(accessTokenSecret))) {
    warnings.push('ACCESS_TOKEN_SECRET (or SUPABASE_JWT_SECRET fallback) is missing for protected auth mode.');
  }
  else if (isProtectedMode && (isBlank(explicitAccessTokenSecret) || isPlaceholder(explicitAccessTokenSecret))) {
    warnings.push('ACCESS_TOKEN_SECRET is not set explicitly. The backend is falling back to SUPABASE_JWT_SECRET.');
  }

  if (isProtectedMode && (isBlank(explicitFlowTokenSecret) || isPlaceholder(explicitFlowTokenSecret))) {
    warnings.push('AUTH_FLOW_TOKEN_SECRET is not set explicitly. OAuth flow tokens are falling back to the access-token secret.');
  }

  if (isProtectedMode && (isBlank(googleClientId) || isPlaceholder(googleClientId))) {
    warnings.push('GOOGLE_CLIENT_ID is missing. Google sign-in cannot complete in protected auth mode.');
  }

  if (visionEnabled && (isBlank(anthropicApiKey) || isPlaceholder(anthropicApiKey))) {
    warnings.push('ANTHROPIC_API_KEY is missing. Vision-augmented capture will fall back to text-only mode.');
  }

  if (isProtectedMode && (isBlank(serviceRoleKey) || isPlaceholder(serviceRoleKey))) {
    warnings.push('SUPABASE_SERVICE_ROLE_KEY is missing. Google sign-in, refresh-token storage, and request logging may fail against hosted DB policies.');
  }

  if (!serviceRoleKey && anonKey) {
    warnings.push('Using SUPABASE_ANON_KEY fallback for DB access. This is not recommended for deployment.');
  }

  if (warnings.length === 0) {
    logger.info(`Runtime config looks ready for ${environmentName}.`);
    return;
  }

  for (const warning of warnings) {
    logger.warn(`[config] ${warning}`);
  }
}

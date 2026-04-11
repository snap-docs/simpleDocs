import { loadEnvironment } from '../src/config/loadEnv.js';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const environmentName = loadEnvironment();

const checks = [];

let createClient;

function addResult(status, label, detail = '') {
  checks.push({ status, label, detail });
}

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

function printAndExit(code) {
  console.log(`DB config check (${environmentName})`);
  for (const item of checks) {
    const suffix = item.detail ? ` - ${item.detail}` : '';
    console.log(`[${item.status}] ${item.label}${suffix}`);
  }

  process.exit(code);
}

async function ensureSupabaseClientFactory() {
  if (createClient) {
    return createClient;
  }

  try {
    ({ createClient } = await import('@supabase/supabase-js'));
    return createClient;
  } catch (error) {
    if (error?.code !== 'ERR_MODULE_NOT_FOUND') {
      throw error;
    }

    const fallbackPath = path.resolve(
      import.meta.dirname,
      '../node_modules/@supabase/supabase-js/dist/index.mjs'
    );

    ({ createClient } = await import(pathToFileURL(fallbackPath).href));
    return createClient;
  }
}

async function checkTable(client, tableName) {
  const { error, count } = await client
    .from(tableName)
    .select('*', { count: 'exact' })
    .limit(1);

  if (error) {
    addResult('FAIL', `table:${tableName}`, error.message);
    return false;
  }

  addResult('OK', `table:${tableName}`, `reachable${typeof count === 'number' ? `, rows=${count}` : ''}`);
  return true;
}

async function main() {
  const supabaseUrl = process.env.SUPABASE_URL;
  const serviceRoleKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
  const anonKey = process.env.SUPABASE_ANON_KEY;
  const explicitAccessTokenSecret = process.env.ACCESS_TOKEN_SECRET;
  const fallbackJwtSecret = process.env.SUPABASE_JWT_SECRET;
  const accessTokenSecret = explicitAccessTokenSecret || fallbackJwtSecret;
  const skipAuth = process.env.SKIP_AUTH === 'true';

  if (skipAuth) {
    addResult('WARN', 'auth', 'SKIP_AUTH=true is enabled in backend env');
  } else {
    addResult('OK', 'auth', 'protected auth mode is enabled');
  }

  if (isBlank(supabaseUrl) || isPlaceholder(supabaseUrl)) {
    addResult('FAIL', 'SUPABASE_URL', 'missing or placeholder');
  } else {
    addResult('OK', 'SUPABASE_URL', 'configured');
  }

  if (isBlank(accessTokenSecret) || isPlaceholder(accessTokenSecret)) {
    addResult('FAIL', 'ACCESS_TOKEN_SECRET', 'missing for deployment auth');
  } else if (isBlank(explicitAccessTokenSecret) || isPlaceholder(explicitAccessTokenSecret)) {
    addResult('WARN', 'ACCESS_TOKEN_SECRET', 'not set explicitly; using SUPABASE_JWT_SECRET fallback');
  } else {
    addResult('OK', 'ACCESS_TOKEN_SECRET', 'configured');
  }

  if (isBlank(serviceRoleKey) || isPlaceholder(serviceRoleKey)) {
    addResult('WARN', 'SUPABASE_SERVICE_ROLE_KEY', 'missing; falling back to anon key if present');
  } else {
    addResult('OK', 'SUPABASE_SERVICE_ROLE_KEY', 'configured');
  }

  if (isBlank(anonKey) || isPlaceholder(anonKey)) {
    addResult('WARN', 'SUPABASE_ANON_KEY', 'missing');
  } else {
    addResult('OK', 'SUPABASE_ANON_KEY', 'configured');
  }

  if (checks.some((item) => item.status === 'FAIL' && item.label === 'SUPABASE_URL')) {
    printAndExit(1);
    return;
  }

  const keyToUse = !isBlank(serviceRoleKey) && !isPlaceholder(serviceRoleKey)
    ? serviceRoleKey
    : anonKey;

  if (isBlank(keyToUse) || isPlaceholder(keyToUse)) {
    addResult('FAIL', 'supabase-key', 'no usable Supabase key is configured');
    printAndExit(1);
    return;
  }

  const createClientFactory = await ensureSupabaseClientFactory();
  const client = createClientFactory(supabaseUrl, keyToUse, {
    auth: {
      autoRefreshToken: false,
      persistSession: false
    }
  });

  await checkTable(client, process.env.AUTH_PARTICIPANTS_TABLE || 'participants');
  await checkTable(client, process.env.AUTH_CODES_TABLE || 'redeem_codes');
  await checkTable(client, process.env.AUTH_REFRESH_TOKENS_TABLE || 'refresh_tokens');
  await checkTable(client, process.env.REQUEST_LOGS_TABLE || 'request_logs');
  await checkTable(client, process.env.REQUEST_FEEDBACK_TABLE || 'request_feedback');

  const hasFailures = checks.some((item) => item.status === 'FAIL');
  printAndExit(hasFailures ? 1 : 0);
}

main().catch((error) => {
  addResult('FAIL', 'db-check', error.message);
  printAndExit(1);
});

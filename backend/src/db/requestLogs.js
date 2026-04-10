import { createClient } from '@supabase/supabase-js';
import { logger } from '../utils/logger.js';

let adminClient = null;

function getClient() {
  if (adminClient) {
    return adminClient;
  }

  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY || process.env.SUPABASE_ANON_KEY;

  if (!url || !key ||
      url === 'https://your-project.supabase.co' ||
      key === 'your_supabase_service_role_key_here' ||
      key === 'your_supabase_anon_key_here') {
    logger.warn('Request logging DB is not configured');
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

function getConfig() {
  return {
    sessionsTable: process.env.SESSIONS_TABLE || 'sessions',
    requestLogsTable: process.env.REQUEST_LOGS_TABLE || 'request_logs'
  };
}

export async function logCompletedRequest(record) {
  if (!record?.participant_id || record.participant_id === 'unknown' || record.participant_id === 'local-dev') {
    logger.warn('Skipping request log because participant_id is unavailable');
    return;
  }

  const client = getClient();
  if (!client) {
    return;
  }

  const config = getConfig();
  const nowIso = new Date().toISOString();

  try {
    const { error: sessionError } = await client
      .from(config.sessionsTable)
      .upsert({
        session_id: record.session_id,
        participant_id: record.participant_id,
        created_at: nowIso,
        last_seen_at: nowIso
      }, {
        onConflict: 'session_id'
      });

    if (sessionError) {
      logger.error(`Session upsert failed: ${sessionError.message}`);
    }

    const { error } = await client
      .from(config.requestLogsTable)
      .insert(record);

    if (error) {
      logger.error(`Request log insert failed: ${error.message}`);
    }
  } catch (err) {
    logger.error(`Request logging failed: ${err.message}`);
  }
}

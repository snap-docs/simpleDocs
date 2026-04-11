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
    logger.warn('Request feedback DB is not configured');
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
    requestFeedbackTable: process.env.REQUEST_FEEDBACK_TABLE || 'request_feedback'
  };
}

export async function saveRequestFeedback(record) {
  if (!record?.participant_id || record.participant_id === 'unknown' || record.participant_id === 'local-dev') {
    logger.warn('Skipping request feedback because participant_id is unavailable');
    return false;
  }

  if (!record?.request_id || !record?.reaction) {
    logger.warn('Skipping request feedback because request_id or reaction is unavailable');
    return false;
  }

  const client = getClient();
  if (!client) {
    return false;
  }

  const config = getConfig();

  try {
    const { error } = await client
      .from(config.requestFeedbackTable)
      .upsert(record, {
        onConflict: 'request_id,participant_id'
      });

    if (error) {
      logger.error(`Request feedback upsert failed: ${error.message}`);
      return false;
    }

    return true;
  } catch (err) {
    logger.error(`Request feedback save failed: ${err.message}`);
    return false;
  }
}

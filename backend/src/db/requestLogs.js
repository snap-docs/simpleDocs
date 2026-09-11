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
    requestLogsTable: process.env.REQUEST_LOGS_TABLE || 'request_logs'
  };
}

export async function logCompletedRequest(record) {
  if (!record?.participant_id || ['unknown', 'local-dev', 'anonymous'].includes(record.participant_id)) {
    logger.warn('Skipping request log because participant_id is unavailable');
    return;
  }

  const client = getClient();
  if (!client) {
    return;
  }

  const config = getConfig();
  try {
    const { error } = await client
      .from(config.requestLogsTable)
      .insert(record)
      .abortSignal(AbortSignal.timeout(5000));

    if (error) {
      logger.error(`Request log insert failed: ${error.message}`);
    }
  } catch (err) {
    logger.error(`Request logging failed: ${err.message}`);
  }
}

export async function saveRequestFeedback(record) {
  if (!record?.participant_id || ['unknown', 'local-dev', 'anonymous'].includes(record.participant_id)) {
    logger.warn('Skipping request feedback because participant_id is unavailable');
    return false;
  }

  if (!record?.request_id || !record?.feedback_reaction) {
    logger.warn('Skipping request feedback because request_id or feedback_reaction is unavailable');
    return false;
  }

  const client = getClient();
  if (!client) {
    return false;
  }

  const config = getConfig();

  try {
    const { data, error } = await client
      .from(config.requestLogsTable)
      .update({
        feedback_reaction: record.feedback_reaction
      })
      .eq('request_id', record.request_id)
      .eq('participant_id', record.participant_id)
      .select('request_id')
      .maybeSingle()
      .abortSignal(AbortSignal.timeout(5000));

    if (error) {
      logger.error(`Request feedback update failed: ${error.message}`);
      return false;
    }

    if (!data) {
      logger.warn(`Request feedback skipped because request log was not found: ${record.request_id}`);
      return false;
    }

    return true;
  } catch (err) {
    logger.error(`Request feedback save failed: ${err.message}`);
    return false;
  }
}

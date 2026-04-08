/**
 * Supabase interaction logging.
 * Stores anonymous metadata only — never stores actual code or text content.
 * 
 * Table: interactions
 *   id: uuid
 *   timestamp: timestamptz
 *   case_type: integer (1-4)
 *   app_type: text
 *   response_time_ms: integer
 *   model_used: text
 *   fallback_used: boolean
 *   created_at: timestamptz
 */

import { createClient } from '@supabase/supabase-js';
import { logger } from '../utils/logger.js';

let supabase = null;

function getClient() {
  if (supabase) return supabase;

  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_ANON_KEY;

  if (!url || !key ||
      url === 'https://your-project.supabase.co' ||
      key === 'your_supabase_anon_key_here') {
    logger.warn('Supabase not configured — logging disabled');
    return null;
  }

  supabase = createClient(url, key);
  return supabase;
}

/**
 * Log an interaction to Supabase.
 * No-op if Supabase is not configured.
 * Never logs actual text content — anonymous metadata only.
 * 
 * @param {Object} params
 * @param {number} params.caseType - 1-4
 * @param {string} params.environmentType - ide, browser, classic_terminal, modern_terminal, external, unknown
 * @param {number} params.responseTimeMs
 * @param {string} params.modelUsed
 * @param {boolean} params.fallbackUsed
 */
export async function logInteraction({ caseType, environmentType, responseTimeMs, modelUsed, fallbackUsed }) {
  const client = getClient();
  if (!client) return;

  try {
    const { error } = await client
      .from('interactions')
      .insert({
        case_type: caseType,
        app_type: environmentType,
        response_time_ms: responseTimeMs,
        model_used: modelUsed,
        fallback_used: fallbackUsed
      });

    if (error) {
      logger.error(`Supabase insert error: ${error.message}`);
    }
  } catch (err) {
    logger.error(`Supabase logging failed: ${err.message}`);
  }
}

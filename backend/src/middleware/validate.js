/**
 * Request validation middleware for /api/explain.
 * Sanitises input, rejects empty selections, trims oversize content.
 */

export async function validateExplainRequest(c, next) {
  let body;
  try {
    body = await c.req.json();
  } catch {
    return c.json({ error: 'Invalid JSON body' }, 400);
  }

  const {
    selected_text,
    background_context,
    window_title,
    process_name,
    environment_type,
    selected_method,
    background_method,
    is_partial,
    is_unsupported,
    status_message
  } = body;

  // selected_text is required and must be non-empty
  if (!selected_text || typeof selected_text !== 'string' || selected_text.trim().length === 0) {
    return c.json({ error: 'selected_text is required and must be non-empty' }, 400);
  }

  // Trim oversize inputs
  const cleanSelected = stripNonPrintable(selected_text.trim()).substring(0, 5000);
  const cleanBackground = background_context
    ? stripNonPrintable(background_context).substring(0, 12000)
    : '';

  const cleanWindowTitle = typeof window_title === 'string'
    ? stripNonPrintable(window_title).substring(0, 400)
    : '';

  const cleanProcessName = typeof process_name === 'string'
    ? stripNonPrintable(process_name).substring(0, 100)
    : '';

  const validEnvironmentTypes = [
    'ide_editor',
    'ide_embedded_terminal',
    'browser_chromium',
    'browser_firefox',
    'classic_terminal',
    'modern_terminal',
    'electron',
    'external',
    'unknown'
  ];
  const cleanEnvironmentType = validEnvironmentTypes.includes(environment_type) ? environment_type : 'unknown';

  c.set('validatedBody', {
    selected_text: cleanSelected,
    background_context: cleanBackground,
    window_title: cleanWindowTitle,
    process_name: cleanProcessName,
    environment_type: cleanEnvironmentType,
    selected_method: typeof selected_method === 'string' ? selected_method.substring(0, 64) : 'unknown',
    background_method: typeof background_method === 'string' ? background_method.substring(0, 64) : 'unknown',
    is_partial: Boolean(is_partial),
    is_unsupported: Boolean(is_unsupported),
    status_message: typeof status_message === 'string' ? stripNonPrintable(status_message).substring(0, 240) : ''
  });

  return next();
}

/**
 * Strip non-printable characters (except newlines and tabs).
 */
function stripNonPrintable(str) {
  return str.replace(/[^\x09\x0A\x0D\x20-\x7E\u00A0-\uFFFF]/g, '');
}

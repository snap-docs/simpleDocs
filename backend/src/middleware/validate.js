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

  const { selected_text, full_context, app_type } = body;

  // selected_text is required and must be non-empty
  if (!selected_text || typeof selected_text !== 'string' || selected_text.trim().length === 0) {
    return c.json({ error: 'selected_text is required and must be non-empty' }, 400);
  }

  // Trim oversize inputs
  const cleanSelected = stripNonPrintable(selected_text.trim()).substring(0, 5000);
  const cleanContext = full_context
    ? stripNonPrintable(full_context).substring(0, 15000)
    : '';

  // Validate app_type
  const validAppTypes = ['editor', 'browser', 'terminal', 'unknown'];
  const cleanAppType = validAppTypes.includes(app_type) ? app_type : 'unknown';

  // Store validated data for route handler
  c.set('validatedBody', {
    selected_text: cleanSelected,
    full_context: cleanContext,
    app_type: cleanAppType
  });

  return next();
}

/**
 * Strip non-printable characters (except newlines and tabs).
 */
function stripNonPrintable(str) {
  return str.replace(/[^\x09\x0A\x0D\x20-\x7E\u00A0-\uFFFF]/g, '');
}

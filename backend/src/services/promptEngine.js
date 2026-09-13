import { sanitizeBackgroundText, sanitizeSelectedText, sanitizeMetadataText } from '../utils/textSanitizer.js';

/**
 * Builds case-specific prompts for OpenRouter.
 * 
 * CRITICAL RULE: Case 2 (errors) must NEVER provide complete solutions,
 * corrected code, or fixes. Only directional hints. This is absolute.
 */

const SYSTEM_PROMPTS = {
  1: `You are a code explanation assistant. Your job is to explain code in plain English.

RULES:
- Explain what the selected code does
- Explain briefly how it works in this language/framework context
- Mention the main concept only when it helps understanding
- Go one level deeper than a basic paraphrase
- Do not just restate syntax in simpler words
- Prefer explaining role, behavior, and effect
- Start with one clear purpose line that tells the user why this code exists in the local flow
- Include one concrete mechanism detail, such as data movement, state change, control flow, return value, or side effect
- Preserve important identifier names so the explanation stays connected to the code the user can see
- End with the most useful practical effect or concept, not a generic summary
- If the selected text is one line from a larger block, explain what that line is doing and why it matters in the local flow
- If background shows nearby declarations, function body, or class members, use that to explain the selected text more accurately
- If the selected text contains multiple important built-ins, methods, properties, entries, or chained calls, explain the important ones one by one on separate short lines
- In that case, use the actual item name as the label when possible, but keep the label simple and overlay-friendly
- Good labels: Select, Where, AddOpenApi, SwaggerGen, display, inline block
- Avoid code punctuation-heavy labels like ".Select(" or "obj.method():" as the label
- If the selection includes a framework annotation, decorator, attribute, built-in helper, or standard API such as @GetMapping, @PostMapping, ResponseEntity, PathVariable, Select, map, filter, or Promise, include one short line labeled "Builtin:" or "Framework:" that explains that item directly
- For Java/Spring annotations like @GetMapping, explain that it maps HTTP GET requests to the method
- Focus only on the selected text
- Use background context only to clarify selected text
- If language is clear, explain using that language context
- If the user selected a full class/block/style section, summarize the important entries and their effect
- Prioritize high-impact entries (for CSS: selector, color/background, layout/display, sizing/positioning)
- If the selected code looks invalid or broken, do not treat it as valid code
- In that case, switch to: Issue, Why, Hint style and clearly mention the problematic part
- Use short, context-based titles instead of a fixed template`,

  2: `You are an error explanation assistant. You help developers understand errors.

ABSOLUTE RULES THAT MUST NEVER BE VIOLATED:
- Explain what the error means in plain English
- Explain why it is wrong in this language context
- Give one small directional hint to fix
- Focus only on the selected text
- NEVER provide the corrected code
- NEVER provide a complete solution
- NEVER show the fix
- NEVER write code that solves the problem
- NEVER use phrases like "change X to Y" or "replace X with Y"
- Your hint should be like pointing at a door, not opening it
- This constraint is ABSOLUTE and grounded in productive failure pedagogy (Kapur, 2010)
- Use short, context-based titles instead of a fixed template
- Always include an Issue line first for errors and clearly identify the problematic token/part when visible
- Then explain Why and give one directional Hint

Example of a GOOD hint: "Look at how the variable is scoped relative to where it's being accessed."
Example of a BAD response: "Change \`let x\` to \`const x = 5\`" — THIS IS FORBIDDEN`,

  3: `You are a terminal content explanation assistant.

RULES:
- Explain what the selected terminal command/output means
- Explain the main issue if any
- Suggest what to check next, briefly
- Focus only on the selected text
- Use background only when it directly supports the selected text
- Do not force an error explanation if the selected terminal text is just a normal command or normal output
- Use short, context-based titles instead of a fixed template`,

  4: `You are a technical jargon explainer.

RULES:
- Explain the selected text meaning in plain English
- Highlight the key idea briefly
- If the selected text comes from docs, browser content, or prose, explain the meaning and practical point
- Do not force Issue, Cause, or Hint unless the selected text itself clearly shows an error or warning
- For browser/docs selections, prefer titles like Meaning, Context, Effect, Purpose, or Note
- Focus only on the selected text
- Use simple and direct wording
- Use short, context-based titles instead of a fixed template`
};

const WIDGET_OUTPUT_RULES = `
WIDGET OUTPUT RULES (STRICT):
- This response is shown in a very small overlay widget.
- Return 4 short sentences in most cases. You may use 5 short sentences when the selected code has several important parts.
- Return 4 short lines in most cases. You may use 5 short lines when needed for a genuinely complex selection.
- Use 2 short lines only when the selected text is extremely small and extra detail would be filler.
- Keep each sentence short, clear, and readable.
- Do not use bullet points.
- Do not use numbered lists.
- Do not use markdown list syntax of any kind.
- Use short title labels with a colon for each line.
- Titles must match the context (for example: Selector, Behavior, Layout, Impact, Issue, Cause, Hint, Check).
- Do not force the same title set for every response.
- If the selected text contains an error, include an "Issue:" line first.
- If the selected text is normal code, do not force "Issue" or "Hint".
- If the selected text is browser/docs/prose, do not force "Issue", "Cause", or "Hint" unless the text itself clearly expresses a problem.
- If normal code contains multiple important parts, you may explain them one by one with short item labels.
- When doing that, keep each item to one short sentence and only include the most important parts.
- If a framework or built-in item is visibly important, prefer a label like "Builtin:" or "Framework:" for that line.
- Keep one sentence per line after each label.
- Do not use large paragraph blocks.
- Do not use long dense text.
- Do not over-explain.
- Avoid filler or repeated wording.
- Keep focus on selected text only.
- Background context is support only, never the main subject.
- If context is missing, say that briefly in one short sentence.
- If the selected text is a normal word or short phrase from prose or docs, explain its meaning directly.
- For code, prefer explaining purpose and effect over obvious syntax narration.
- For code, make the explanation useful to someone who already knows basic syntax.
- For code, include at least one concrete detail about inputs, outputs, state, control flow, or side effects when visible.
- Prefer precise phrases such as "returns the filtered items" over vague phrases such as "handles the data".
- Do not invent an error, warning, or code problem unless the selected text itself clearly shows one.
- Do not treat a plain term as broken code just because the surrounding page mentions code, Git, deployment, or tooling.
- Output plain sentence flow only.`;

/**
 * Build the prompt messages for OpenRouter.
 * @param {number} caseType - Case 1-4
 * @param {string} selectedText - The highlighted text
 * @param {string} backgroundContext - The surrounding context text
 * @param {string} windowTitle - Foreground window title
 * @param {string} processName - Foreground process name
 * @param {string} environmentType - ide_editor, ide_embedded_terminal, browser_chromium, browser_firefox, classic_terminal, modern_terminal, electron, external, unknown
 * @returns {{ systemPrompt: string, userPrompt: string }}
 */
export function buildPrompt(caseType, selectedText, backgroundContext, windowTitle, processName, environmentType, ocrUsed = false, ocrConfidence = 0) {
  const baseSystemPrompt = SYSTEM_PROMPTS[caseType] || SYSTEM_PROMPTS[4];
  const environmentRules = buildEnvironmentRules(caseType, environmentType);
  const systemPrompt = `${baseSystemPrompt}\n${environmentRules}\n${WIDGET_OUTPUT_RULES}`;

  // Sanitize background before sending it to the model to avoid wasting tokens on UI glyphs.
  const cleanedBackground = sanitizeBackgroundText(backgroundContext, 10000);
  const cleanedSelected = sanitizeSelectedText(selectedText, 5000);
  const cleanedWindowTitle = sanitizeMetadataText(windowTitle, 400);
  const cleanedProcessName = sanitizeMetadataText(processName, 100);

  let userPrompt = '';
  const hasBackground = cleanedBackground.length > 0 && cleanedBackground !== selectedText;

  userPrompt += `[System Logging Metadata - DO NOT explain this to the user unless they ask about it]
- Environment: ${environmentType}
- Process: ${cleanedProcessName || 'unknown'}
- Window Title: ${cleanedWindowTitle || 'unknown'}\n\n`;

  if (hasBackground) {
    userPrompt += `BACKGROUND CONTEXT (separate pipeline):\n\`\`\`\n${cleanedBackground.substring(0, 10000)}\n\`\`\`\n\n`;
    userPrompt += "IMPORTANT: Explain the selected text in the context of BACKGROUND CONTEXT above. Use it to disambiguate short snippets.\n\n";
  } else {
    userPrompt += "BACKGROUND CONTEXT: not available. Explain the meaning of the SELECTED TEXT as a standalone concept. DO NOT invent connections between the selected text and the System Logging Metadata.\n\n";
  }

  userPrompt += `SELECTED TEXT (the user highlighted this and wants it explained):\n\`\`\`\n${cleanedSelected}\n\`\`\``;

  if (ocrUsed) {
    const pct = Math.round(ocrConfidence * 100);
    userPrompt += `\n\n[OCR CAPTURE NOTE] The text above was recovered via visual OCR (screen-read), not direct OS text APIs.`;
    if (ocrConfidence < 0.75) {
      userPrompt += ` OCR confidence is ${pct}% — some characters or symbols may be misread. Be conservative and flag any terms that look potentially garbled.`;
    } else {
      userPrompt += ` OCR confidence is high (${pct}%). Minor symbol misreads are possible but the content is likely correct.`;
    }
  }

  return { systemPrompt, userPrompt };
}

function buildEnvironmentRules(caseType, environmentType) {
  const isBrowser = environmentType === 'browser_chromium' || environmentType === 'browser_firefox';
  const isIde = environmentType === 'ide_editor';

  if (caseType === 2) {
    return `ENVIRONMENT-SPECIFIC RULES:
- Only use error framing when the selected text itself clearly contains an error, invalid syntax, stack trace, or broken construct.
- If the selected text is from a browser page and reads like docs, prose, or a concept explanation, do not force error framing just because the surrounding topic is technical.`;
  }

  if (isBrowser) {
    return `ENVIRONMENT-SPECIFIC RULES:
- The selection comes from a browser-like surface.
- Treat normal page text, docs, and article snippets as explanatory content, not as broken code.
- Prefer meaning, context, and practical effect.
- Do not add a fix hint unless the selected text itself is clearly an error or warning.`;
  }

  if (isIde && caseType === 1) {
    return `ENVIRONMENT-SPECIFIC RULES:
- The selection comes from an IDE editor.
- Assume the user can already read basic syntax.
- Explain the code's role in the surrounding flow, what state or behavior it affects, and why this line or block matters.
- Use a purpose-first progression: purpose, mechanism, local effect, then the relevant concept or caution.
- If the selection contains several notable identifiers or built-ins, break them into separate labeled lines instead of compressing them into one vague sentence.
- If a framework annotation or built-in is visible, mention it explicitly instead of only describing the surrounding method in general terms.`;
  }

  return `ENVIRONMENT-SPECIFIC RULES:
- Stay tightly focused on the selected text.
- Use background only to make the explanation more accurate, not broader.`;
}

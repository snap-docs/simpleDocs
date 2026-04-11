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
- Explain briefly how it works
- Mention the main concept when helpful
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
- Use short, context-based titles instead of a fixed template`,

  4: `You are a technical jargon explainer.

RULES:
- Explain the selected text meaning in plain English
- Highlight the key idea briefly
- Focus only on the selected text
- Use simple and direct wording
- Use short, context-based titles instead of a fixed template`
};

const WIDGET_OUTPUT_RULES = `
WIDGET OUTPUT RULES (STRICT):
- This response is shown in a very small overlay widget.
- Return 3 short sentences in most cases. You may use 4 short sentences when needed.
- Return 3 short lines in most cases. You may use 4 short lines when needed.
- Keep each sentence short, clear, and readable.
- Do not use bullet points.
- Do not use numbered lists.
- Do not use markdown list syntax of any kind.
- Use short title labels with a colon for each line.
- Titles must match the context (for example: Selector, Behavior, Layout, Impact, Issue, Cause, Hint, Check).
- Do not force the same title set for every response.
- If the selected text contains an error, include an "Issue:" line first.
- Keep one sentence per line after each label.
- Do not use large paragraph blocks.
- Do not use long dense text.
- Do not over-explain.
- Avoid filler or repeated wording.
- Keep focus on selected text only.
- Background context is support only, never the main subject.
- If context is missing, say that briefly in one short sentence.
- If the selected text is a normal word or short phrase from prose or docs, explain its meaning directly.
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
  const systemPrompt = `${baseSystemPrompt}\n${WIDGET_OUTPUT_RULES}`;

  // Strip object replacement chars (U+FFFC = ￼), other non-printable control chars,
  // and collapse excessive whitespace. Zero latency impact — pure CPU string op.
  const cleanedBackground = (backgroundContext || '')
    .replace(/\uFFFC/g, '')                        // remove ￼ (UIA embedded object placeholders)
    .replace(/[\x00-\x08\x0B\x0E-\x1F\x7F]/g, '') // remove non-printable control chars
    .replace(/[ \t]{4,}/g, '   ')                  // collapse excessive spaces/tabs
    .trim();

  let userPrompt = '';
  const hasBackground = cleanedBackground.length > 0 && cleanedBackground !== selectedText;

  userPrompt += `[System Logging Metadata - DO NOT explain this to the user unless they ask about it]
- Environment: ${environmentType}
- Process: ${processName || 'unknown'}
- Window Title: ${windowTitle || 'unknown'}\n\n`;

  if (hasBackground) {
    userPrompt += `BACKGROUND CONTEXT (separate pipeline):\n\`\`\`\n${cleanedBackground.substring(0, 10000)}\n\`\`\`\n\n`;
    userPrompt += "IMPORTANT: Explain the selected text in the context of BACKGROUND CONTEXT above. Use it to disambiguate short snippets.\n\n";
  } else {
    userPrompt += "BACKGROUND CONTEXT: not available. Explain the meaning of the SELECTED TEXT as a standalone concept. DO NOT invent connections between the selected text and the System Logging Metadata.\n\n";
  }

  userPrompt += `SELECTED TEXT (the user highlighted this and wants it explained):\n\`\`\`\n${selectedText}\n\`\`\``;

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

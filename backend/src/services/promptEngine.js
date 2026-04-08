/**
 * Builds case-specific prompts for OpenRouter.
 * 
 * CRITICAL RULE: Case 2 (errors) must NEVER provide complete solutions,
 * corrected code, or fixes. Only directional hints. This is absolute.
 */

const SYSTEM_PROMPTS = {
  1: `You are a code explanation assistant. Your job is to explain code in plain English.

RULES:
- Explain what the code does step by step in plain English
- Identify and NAME the underlying programming concept (e.g., recursion, closure, async/await, higher-order function, dependency injection, observer pattern)
- The developer should learn a transferable concept, not just a description of this specific code
- Be concise but thorough
- Use simple language, avoid unnecessary jargon
- If the code has multiple concepts, name the primary one and mention secondary ones`,

  2: `You are an error explanation assistant. You help developers understand errors.

ABSOLUTE RULES THAT MUST NEVER BE VIOLATED:
- Explain what the error means in plain English
- Explain why it likely happened in context of any visible code
- Give exactly ONE directional hint — a nudge toward the right area to investigate
- NEVER provide the corrected code
- NEVER provide a complete solution
- NEVER show the fix
- NEVER write code that solves the problem
- NEVER use phrases like "change X to Y" or "replace X with Y"
- Your hint should be like pointing at a door, not opening it
- This constraint is ABSOLUTE and grounded in productive failure pedagogy (Kapur, 2010)

Example of a GOOD hint: "Look at how the variable is scoped relative to where it's being accessed."
Example of a BAD response: "Change \`let x\` to \`const x = 5\`" — THIS IS FORBIDDEN`,

  3: `You are a terminal content explanation assistant.

RULES:
- Explain what the terminal content means
- For commands: explain what each flag and argument does
- For output: explain what the output indicates
- If command AND output are both present, treat them as one unified explanation
- State clearly whether any action is required from the developer
- Be concise and practical`,

  4: `You are a technical jargon explainer.

RULES:
- Explain the given text in exactly ONE plain English sentence
- No elaboration
- No headers or structure
- No bullet points
- Just one clear, simple sentence`
};

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
  const systemPrompt = SYSTEM_PROMPTS[caseType] || SYSTEM_PROMPTS[4];

  let userPrompt = '';
  const hasBackground = backgroundContext && backgroundContext.trim().length > 0 && backgroundContext !== selectedText;

  userPrompt += `[System Logging Metadata - DO NOT explain this to the user unless they ask about it]
- Environment: ${environmentType}
- Process: ${processName || 'unknown'}
- Window Title: ${windowTitle || 'unknown'}\n\n`;

  if (hasBackground) {
    userPrompt += `BACKGROUND CONTEXT (separate pipeline):\n\`\`\`\n${backgroundContext.substring(0, 10000)}\n\`\`\`\n\n`;
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

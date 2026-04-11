/**
 * 4-case content classifier.
 * 
 * Priority rules:
 * 1. Error patterns (Case 2) ALWAYS win — even if code is present
 * 2. Terminal patterns (Case 3) — commands + output treated as one unit
 * 3. Programming syntax (Case 1) — valid code constructs
 * 4. Everything else (Case 4)
 */

// ── Error patterns (Case 2) ─────────────────────
const ERROR_PATTERNS = [
  /\bError\b/i,
  /\bException\b/i,
  /\bTraceback\b/i,
  /\bFATAL\b/i,
  /\bfailed\b/i,
  /\bpanic\b/i,
  /\bSegmentation fault\b/i,
  /\bstack trace\b/i,
  /\bat line \d+/i,
  /\bline \d+, in\b/i,
  /\bSyntaxError\b/,
  /\bTypeError\b/,
  /\bReferenceError\b/,
  /\bValueError\b/,
  /\bKeyError\b/,
  /\bAttributeError\b/,
  /\bImportError\b/,
  /\bModuleNotFoundError\b/,
  /\bIndexError\b/,
  /\bRuntimeError\b/,
  /\bNullPointerException\b/,
  /\bClassNotFoundException\b/,
  /\bIOException\b/,
  /\bFileNotFoundException\b/,
  /\bCompilationError\b/i,
  /\bUnhandled\b/i,
  /\berror\[E\d+\]/,        // Rust errors
  /\berror TS\d+/,           // TypeScript errors
  /\bERROR\s+\d+/,           // SQL errors
  /\bexited with code [1-9]/, // Process exit codes
  /^\s*at\s+\S+\s+\(.*:\d+:\d+\)/m,  // JS/TS stack frames
  /^\s*File ".*", line \d+/m,         // Python stack frames
  /^\s*at\s+\S+\.\S+\(.*\.java:\d+\)/m  // Java stack frames
];

// ── Terminal patterns (Case 3) ──────────────────
const TERMINAL_PATTERNS = [
  /^\s*[\$#>]\s+/m,                    // Shell prompts: $ # >
  /^[A-Z]:\\.*>/m,                      // Windows prompt: C:\Users>
  /^\s*(git|npm|pip|docker|kubectl|cargo|yarn|pnpm|brew|apt|yum|dnf|pacman|choco|winget|terraform|aws|gcloud|az)\s+/m,
  /^\s*(cd|ls|dir|cat|echo|mkdir|rm|cp|mv|chmod|chown|curl|wget|ssh|scp|grep|find|awk|sed|tar|zip|unzip)\s+/m,
  /^\s*(node|python|python3|ruby|java|javac|go|rustc|gcc|g\+\+|make|cmake)\s+/m,
  /^\s*(npm run|npx|dotnet|mvn|gradle)\s+/m
];

// ── Code patterns (Case 1) ─────────────────────
const CODE_PATTERNS = [
  /\b(function|const|let|var|return|import|export|class|interface|type|enum)\b/,
  /\b(def|class|import|from|return|yield|async|await|lambda)\b/,
  /\b(public|private|protected|static|void|int|string|bool|float|double)\b/,
  /\b(fn|let|mut|pub|impl|struct|trait|use|mod|match)\b/,
  /\b(package|func|type|struct|interface|go|defer|chan)\b/,
  /\b(SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|CREATE|ALTER|JOIN)\b/,
  /^\s*[.#]?[a-zA-Z][\w-]*\s*\{/m,      // CSS selector blocks
  /\b[a-zA-Z-]+\s*:\s*[^;\n]+;?/m,      // CSS declarations
  /[{}\[\]();]/,
  /=>/,
  /\b\w+\s*\([^)]*\)\s*[{:]/,        // function declaration patterns
  /(\/\/|#|\/\*|\*\/|<!--)/,           // comments
  /^\s*@\w+/m,                         // decorators/annotations
  /\.\w+\(/,                           // method calls
  /^\s*<\/?[a-zA-Z][\w-]*.*>/m        // HTML/JSX tags
];

function score(text, patterns) {
  return patterns.reduce((total, pattern) => total + (pattern.test(text) ? 1 : 0), 0);
}

function buildContextBlob(selectedText, backgroundContext) {
  const background = typeof backgroundContext === 'string' ? backgroundContext.trim() : '';
  if (background.length === 0 || background === selectedText) {
    return '';
  }

  return background.substring(0, 12000);
}

function isFragmentarySelection(text) {
  if (text.length <= 80) {
    return true;
  }

  const words = text.split(/\s+/).filter(Boolean).length;
  return words <= 12;
}

function looksLikePlainLanguageSelection(text) {
  if (!text || typeof text !== 'string') {
    return false;
  }

  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return false;
  }

  if (trimmed.length > 80) {
    return false;
  }

  if (/[\r\n]/.test(trimmed)) {
    return false;
  }

  if (/[{}[\]();<>`]/.test(trimmed) || trimmed.includes('=>') || trimmed.includes('::')) {
    return false;
  }

  if (/^\s*[.#@]/.test(trimmed)) {
    return false;
  }

  if (/[=:]/.test(trimmed)) {
    return false;
  }

  const words = trimmed.split(/\s+/).filter(Boolean);
  if (words.length > 6) {
    return false;
  }

  return words.every(word => /^[a-zA-Z][a-zA-Z0-9_-]*$/.test(word));
}

function hasUnbalancedDelimiters(text) {
  const counts = { '(': 0, ')': 0, '[': 0, ']': 0, '{': 0, '}': 0 };
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (counts[ch] !== undefined) {
      counts[ch] += 1;
    }
  }

  return counts['('] !== counts[')'] || counts['['] !== counts[']'] || counts['{'] !== counts['}'];
}

function hasMalformedCssLine(text) {
  const looksLikeCssBlock = text.includes('{') && text.includes('}') && /[a-zA-Z-]+\s*:\s*[^;\n]+;/.test(text);
  if (!looksLikeCssBlock) {
    return false;
  }

  const lines = text.replace(/\r\n/g, '\n').split('\n').map(line => line.trim()).filter(Boolean);
  let insideBlock = false;
  for (const line of lines) {
    if (line.includes('{')) {
      const tail = line.slice(line.indexOf('{') + 1).trim();
      if (tail.length > 0 && tail !== '}') {
        const isCommentTail = tail.startsWith('/*') || tail.startsWith('//');
        const hasDeclarationSignal = tail.includes(':');
        if (!isCommentTail && !hasDeclarationSignal) {
          return true;
        }
      }

      insideBlock = true;
      if (line.endsWith('{')) {
        continue;
      }
    }

    if (line === '}') {
      insideBlock = false;
      continue;
    }

    if (!insideBlock) {
      continue;
    }

    const isComment = line.startsWith('/*') || line.startsWith('*') || line.startsWith('//');
    const isValidDeclaration = /^[a-zA-Z-]+\s*:\s*[^;]+;?$/.test(line);
    if (!isComment && !isValidDeclaration) {
      return true;
    }
  }

  return false;
}

function hasLikelySyntaxIssue(text, codeScore) {
  if (codeScore < 2) {
    return false;
  }

  if (hasUnbalancedDelimiters(text)) {
    return true;
  }

  if (hasMalformedCssLine(text)) {
    return true;
  }

  return false;
}

/**
 * Classify selected text into one of 4 cases.
 * @param {string} selectedText - The selected text to classify
 * @param {string} backgroundContext - Optional surrounding context
 * @returns {number} Case number 1-4
 */
export function classify(selectedText, backgroundContext = '') {
  if (!selectedText || typeof selectedText !== 'string') return 4;

  const trimmed = selectedText.trim();
  if (trimmed.length === 0) return 4;
  const plainLanguageSelection = looksLikePlainLanguageSelection(trimmed);

  // Priority 1: Error detection — always wins
  const selectedErrorScore = score(trimmed, ERROR_PATTERNS);
  if (selectedErrorScore >= 1) return 2;

  // Priority 1b: Obvious syntax issues in selected code (lightweight heuristic).
  const selectedCodeScore = score(trimmed, CODE_PATTERNS);
  if (hasLikelySyntaxIssue(trimmed, selectedCodeScore)) return 2;

  // Priority 2: Terminal content
  const selectedTerminalScore = score(trimmed, TERMINAL_PATTERNS);
  if (selectedTerminalScore >= 1) return 3;

  // Priority 3: Source code
  if (selectedCodeScore >= 2) return 1; // Need at least 2 code signals to be confident

  // Context fallback: if the selected snippet is short/fragmentary, use background context
  // as a tie-breaker instead of defaulting straight to Case 4.
  if (isFragmentarySelection(trimmed)) {
    const contextBlob = buildContextBlob(trimmed, backgroundContext);
    if (contextBlob.length > 0) {
      const contextErrorScore = score(contextBlob, ERROR_PATTERNS);
      if (contextErrorScore >= 1) return 2;

      const contextTerminalScore = score(contextBlob, TERMINAL_PATTERNS);
      if (contextTerminalScore >= 1) return 3;

      const contextCodeScore = score(contextBlob, CODE_PATTERNS);
      if (!plainLanguageSelection && contextCodeScore >= 2) return 1;
    }
  }

  // Priority 4: Everything else
  return 4;
}

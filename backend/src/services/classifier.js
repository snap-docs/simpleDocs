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

  // Priority 1: Error detection — always wins
  const selectedErrorScore = score(trimmed, ERROR_PATTERNS);
  if (selectedErrorScore >= 1) return 2;

  // Priority 2: Terminal content
  const selectedTerminalScore = score(trimmed, TERMINAL_PATTERNS);
  if (selectedTerminalScore >= 1) return 3;

  // Priority 3: Source code
  const selectedCodeScore = score(trimmed, CODE_PATTERNS);
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
      if (contextCodeScore >= 2) return 1;
    }
  }

  // Priority 4: Everything else
  return 4;
}

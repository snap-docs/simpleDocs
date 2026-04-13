# Classifier Explained

## Purpose

This note explains how the system currently produces:

- `is_partial`
- `task_type`

These values are **not** produced by a separate LLM call.

The current implementation uses:

- client-side capture state for `is_partial`
- backend rule-based classification for `task_type`

## How `is_partial` works

`is_partial` is decided in the Windows client during capture.

The client tries to capture:

- the selected text
- the surrounding background context
- the capture method used

This happens in the client capture strategies such as:

- `client/Engine/Strategies/IDEStrategy.cs`
- `client/Engine/Strategies/BrowserStrategy.cs`
- `client/Engine/Strategies/ModernTerminalStrategy.cs`
- `client/Engine/Strategies/ClassicTerminalStrategy.cs`

The result is stored in `client/Engine/Models/CaptureResult.cs`.

### Meaning

- `is_partial = false`
  means the capture was considered complete enough
- `is_partial = true`
  means the capture was incomplete, weak, fallback-based, or metadata-only

### Typical reasons `is_partial` becomes true

- selected text could not be captured cleanly
- background context was only metadata fallback
- only part of the context was available
- unsupported capture paths forced a reduced result

So `is_partial` is basically a capture-quality flag.

Important current note:

- `is_partial` is still used in runtime behavior and overlay metadata
- it is no longer stored in the hosted `request_logs` table

## How `task_type` works

`task_type` is decided in the backend.

The backend does not ask an LLM to classify the request first.

Instead, it uses rule-based logic in:

- `backend/src/services/classifier.js`

That classifier looks at:

- `selected_text`
- `background_context`

and uses regex patterns and heuristics to decide what kind of content it is.

## The current classifier categories

The classifier returns one of four internal cases:

- `1` = code
- `2` = error
- `3` = terminal
- `4` = normal text

Then `backend/src/services/streamHandler.js` maps those cases to the stored `task_type` values:

- `1` -> `code_explanation`
- `2` -> `error_explanation`
- `3` -> `terminal_explanation`
- `4` -> `text_explanation`

## What rules the classifier uses

The classifier is rule-based.

It does not know all programming languages.

It only knows the patterns that were manually written into:

- `ERROR_PATTERNS`
- `TERMINAL_PATTERNS`
- `CODE_PATTERNS`

### Error examples

It checks for things like:

- `Error`
- `Exception`
- `Traceback`
- `SyntaxError`
- `TypeError`
- stack-trace line formats

### Terminal examples

It checks for things like:

- shell prompts such as `$`, `#`, `>`
- Windows prompts such as `C:\...>`
- commands such as `git`, `npm`, `pip`, `docker`, `dotnet`

### Code examples

It checks for things like:

- language keywords such as `function`, `class`, `import`, `return`
- syntax markers such as braces, parentheses, arrows
- CSS blocks and declarations
- HTML/JSX tags
- method call patterns

## Important limitation

This is not a compiler or parser.

The backend does not automatically know all syntax in all languages.

It only knows the patterns that were hardcoded into the classifier.

That means:

- it is fast
- it is cheap
- it works well for many common cases
- it can still miss edge cases
- it can still misclassify unusual text

## Why we do it this way

We do not use an LLM for `is_partial` or `task_type` because that would add:

- extra latency
- extra token cost
- extra complexity

The current design is:

1. client captures the text and context
2. client marks whether the capture is partial
3. backend classifies the content with rules
4. backend stores the final request-log fields
5. the LLM is used for the actual explanation only

## Short summary

- `is_partial` = client-side capture quality flag
- `task_type` = backend rule-based category
- `task_type` is stored in `request_logs`
- `is_partial` is runtime metadata but not stored in `request_logs`
- neither one uses a separate LLM classification step

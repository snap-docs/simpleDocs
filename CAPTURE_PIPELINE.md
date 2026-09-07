# Capture Architecture Pipeline

This document is the technical reference for the current capture architecture. The deployment, auth, feedback, and packaging work does not replace this capture design.

## Core Principle

The capture pipeline remains Windows-native and performance-first.

The surrounding work added around auth, logging, and deployment does not redesign the core capture path:

- no Web frontend replacement
- no Electron migration
- no browser-based capture conversion
- no blocking DB writes before first token
- no prompt or logging work inside the capture extraction path itself

## Core Technologies

- **Win32 APIs:** foreground/window detection and focus handling
- **UI Automation (UIA):** primary selected/background extraction path
- **MSAA / IAccessible:** secondary fallback path
- **Editor Bridge:** optional VS Code/Cursor extension that returns selection-matched active-buffer context
- **Clipboard Compatibility Mode:** safe selected-text copy fallback that never expands or moves the selection
- **Classic Console Buffer API:** legacy terminal support
- **Native Windows OCR Engine:** last-resort fallback for canvas, blocked, or visual-only surfaces

## Global Pipeline

1. Snapshot the active window and focused accessibility element in a short-lived capture scope.
2. Classify the environment.
3. Route to the matching capture strategy.
4. Extract selected text using the ordered fallback chain.
5. Extract background context using the strategy-specific fallback chain.
6. Reject the result if focus changed or the capture budget expired.
7. Build capture metadata including methods, partial/unsupported state, and usage context.
8. Send the final payload to the backend only after capture completes.

Accessibility provider calls run on a worker thread so a slow provider cannot freeze the WPF UI. Only one capture runs at a time, and the caller receives a bounded timeout result.

## IDE Capture Plan

1. Read the exact selection through UIA, an explicit MSAA selection, or a normal copy operation.
2. Ask the local editor bridge for context only when its active selection exactly matches the captured selection.
3. Otherwise build nearby context from an anchored UIA text range.
4. Use bounded OCR only when structured sources fail and exact editor bounds are available.
5. Never use `Ctrl+A`, cursor movement, or selection expansion to obtain background text.

The bridge uses a random per-session Windows named pipe and token. Manifests live in the current user's local application data, responses are size-limited, and the client accepts only exact selection matches.

## Clipboard And OCR Safety

Clipboard capture is serialized. The original clipboard is materialized before copy and restored only when no other application changed it during capture. Repeated copies of the same text remain valid.

OCR is background context, never proof of an exact selection. It uses focused accessibility bounds, intersects them with the virtual screen, scales oversized images to the Windows OCR limit, requires confidence, and rejects output that merely echoes the selection.

## Selected Text Fallback Order

### Generic order

1. UIA selection
2. MSAA explicit selection
3. MSAA focused-text fallback where allowed
4. clipboard compatibility where allowed
5. unsupported selected result

### IDE terminal special order

1. UIA selection
2. MSAA explicit selection
3. terminal-safe clipboard compatibility fallback
4. unsupported selected result

## Background Context Fallback Order

### IDE editor

- UIA visible ranges
- UIA document range
- UIA selection-anchored text range expansion
- nearest UIA container
- MSAA container
- OCR
- metadata fallback

For IDE editors, the pipeline now collects multiple structured candidates first and scores them before accepting a background result. This keeps OCR as the real last resort instead of the common VS Code fallback path.

### Terminal

- UIA visible ranges
- optional UIA document range for modern terminal
- OCR
- metadata fallback

### Browser / Electron

- nearest UIA container
- OCR
- metadata fallback

### Canvas-locked or external surfaces

- OCR-first fallback path

## Safety Rules

### Clipboard safety

- backup clipboard before copy simulation
- send only `Ctrl+C`
- detect clipboard change safely
- restore the original clipboard in `finally`

### Quality filtering

- reject UI junk and metadata noise
- prefer code-like or terminal-like content signals
- preserve partial results when useful selected text is missing but context is still helpful
- sanitize obvious junk characters before data is sent to the backend

## Capture Output Fields

The capture result currently includes fields such as:

- `selected_text`
- `background_context`
- `window_title`
- `process_name`
- `environment_type`
- `selected_method`
- `background_method`
- `is_partial`
- `is_unsupported`
- `status_message`
- `usage_context`

`is_partial` and `is_unsupported` still exist as runtime capture metadata, but they are no longer stored in the hosted request log table.

## Deployment Note

Auth restore, feedback sending, and DB logging happen around the capture system, not inside the capture extraction path itself. This keeps the core capture behavior and latency profile stable.

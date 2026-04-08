# Capture Architecture Pipeline

This document serves as the permanent technical specification for the finalized capture architecture of the context capture engine.

## Core Technologies

*   **Win32 APIs:** Foreground/window APIs for active app detection and focus handling (`GetForegroundWindow`, `GetWindowText`, `GetClassName`, `SetForegroundWindow`). 
*   **UI Automation (UIA):** Native automation via `TextPattern` and some `ValuePattern` probing for selection/background extraction.
*   **MSAA / IAccessible:** Fallback path for selection, focus, and container text.
*   **Clipboard Compatibility Mode:** Uses `Ctrl+C` simulation (`InputSimulator`) combined with clipboard integrity checks.
*   **Classic Console Buffer API:** Leverages `AttachConsole` + `ReadConsoleOutputCharacter` for classic terminals.
*   **Native Windows OCR Engine (`Windows.Media.Ocr.OcrEngine`):** The ultimate failure fallback. A silent hardware visual scanner natively built into Windows 10/11 that reads background text identically to how the human eye views it—completely bypassing Chrome DOM/Canvas/UI trees.

## Global Pipeline

1.  **Detect:** Active window + process + class (`ContextCaptureEngine.cs`).
2.  **Classify Environment:** Identify context patterns (e.g. `ide_editor`, `ide_embedded_terminal`, `browser_*`, `modern_terminal`, etc.) (`EnvironmentClassifier.cs`).
3.  **Dispatch to Strategy:** Route to specialized UI extractors based on classification hierarchy.

## Selected Text Fallback Plan

### Generic Selected Pipeline (Used by Most Strategies)
**Order of Execution:**
1.  UIA `GetSelection()`
2.  MSAA explicit selection (requires selection signal)
3.  MSAA focused-text fallback (if enabled for the strategy)
4.  Clipboard compatibility mode (if allowed by rules/environment)
5.  Unsupported selected result

**Special Strategy Behaviors:**
*   **Firefox** sets `preferMsaaFirst=true` (MSAA-first behavior).
*   **IDE Editor** disables MSAA focused-text fallback (`allowMsaaFocusedFallback: false`) to force clipboard compatibility in VS Code safely.

### IDE Embedded Terminal Special Selected Plan
Separate terminal-specific chain to handle embedded terminal constraints safely without accidental command execution.
**Order of Execution:**
1.  UIA selection
2.  MSAA explicit selection
3.  Terminal clipboard compatibility fallback (`clipboard_compat_terminal`). Ensures focus-stability checks before and after extraction.
4.  Unsupported selected if still empty.

## Background Text Fallback Plan

*   **IDE Editor Background:** 
    *   *Chain:* UIA document range -> UIA visible ranges -> UIA nearest container -> Native OCR Engine -> metadata fallback.
*   **Terminal Background:**
    *   *Chain:* UIA visible ranges -> optional UIA document range (modern terminal only) -> Native OCR Engine -> metadata.
*   **Browser/Electron Background:**
    *   *Chain:* UIA nearest container -> Native OCR Engine -> metadata.
*   **Canvas-Locked/External Background:**
    *   *Chain:* Drops straight to **Native OCR Engine** to read pixels, sidestepping DOM elements meant to block accessibility readers.

## Clipboard Compatibility Safety Plan

A core aspect of safely utilizing clipboard fallbacks is preventing destructive actions or loss of user clipboard state.
1.  Backup original clipboard object + original text.
2.  Capture clipboard sequence number before action.
3.  Clear clipboard.
4.  Send `Ctrl+C` only (no `Ctrl+A`, no undo hacks).
5.  Poll for clipboard text with timeout.
6.  Accept only if changed during request and differs from previous content.
7.  Restore original clipboard in `finally` block.

## Noise Rejection / Quality Filters

*   Removes known IDE/UI junk (e.g., `vscode-file://`, webview URIs, accessibility warnings, metadata text).
*   Rejects "chrome labels only" outputs (explorer, search, problems, etc.).
*   Keeps likely code-like content using code-signal heuristics.

## Status/Classification Rules (Current)

*   **`ide_editor`:** If selected text is missing but background exists, returns `partial=true`, `unsupported=false`. 
*   **`ide_embedded_terminal`:**
    *   If selected is missing but background exists => `partial=true`, `unsupported=false`.
    *   If both are missing => `unsupported=true`. 

## Config Knobs

*   `CODE_EXPLAINER_CLIPBOARD_COMPAT`: Editor/browser generic clipboard compat gate.
*   `CODE_EXPLAINER_CLIPBOARD_COMPAT_WHITELIST`: Compat whitelist override.

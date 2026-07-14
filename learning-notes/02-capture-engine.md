# Capture Engine

The capture engine is the most important technical feature in the project. It is the part that makes `simpleDocs` feel like an OS-level assistant instead of a normal chatbot.

The main entry point is `client/Engine/ContextCaptureEngine.cs`.

## High-level Pipeline

`ExecuteCaptureAsync` follows this order:

1. Detect the foreground window with `ActiveWindowDetector`.
2. Classify the app with `EnvironmentClassifier`.
3. Select a capture strategy from the environment type.
4. Execute the strategy.
5. Build a usage context string.
6. Return a `CaptureResult`.

The engine does not directly know all capture details. It delegates to strategies. This keeps the top-level pipeline easy to reason about while allowing browsers, editors, terminals, and external apps to have different fallback orders.

## Active Window Detection

`ActiveWindowDetector` gathers information about the active foreground window:

- window handle
- process id
- process name
- window title
- class name

That information is stored in `ActiveWindowInfo`.

The process name and class name matter because Windows apps expose accessibility data very differently. For example, VS Code and Chrome are both Chromium-based in some sense, but an editor capture strategy and a browser capture strategy should not behave the same way.

## Environment Classification

`EnvironmentClassifier` maps the active window into `EnvironmentType`.

The current categories include:

- `IDE`
- `IDEEmbeddedTerminal`
- `BrowserChromium`
- `BrowserFirefox`
- `ModernTerminal`
- `ClassicTerminal`
- `Electron`
- `External`
- `Unknown`

The classifier uses process names first. Examples:

- `code`, `cursor`, `devenv`, JetBrains IDE processes become IDEs.
- `chrome`, `msedge`, `brave`, and `opera` become Chromium browsers.
- `firefox` becomes Firefox.
- `WindowsTerminal` becomes a modern terminal.
- `cmd`, `powershell`, `pwsh`, and similar hosts become classic terminals.
- `Chrome_WidgetWin_1` class names are treated as Electron.

This classification decides which strategy handles capture.

## Capture Strategies

Strategies implement `ICaptureStrategy`. Each strategy returns a `CaptureResult`, but each one can use a different set of lower-level capture methods.

Examples:

- `IDEStrategy` focuses on selected code and nearby editor context.
- `BrowserStrategy` and `FirefoxStrategy` focus on selected page text and nearby page/document context.
- `ModernTerminalStrategy` and `ClassicTerminalStrategy` handle terminal output and command text.
- `ElectronStrategy` handles Chromium-like desktop apps that are not browsers.
- `ExternalAppStrategy` and `UnknownAppStrategy` are more conservative fallback routes.

This strategy pattern is useful because the app's behavior should be app-aware. A terminal selection, source-code selection, and browser docs selection should not produce the same context payload.

## Selected Text Capture

The selected-text pipeline is in `CapturePipelines.CaptureSelectedTextAsync`.

The normal fallback order is:

1. UI Automation selected text.
2. MSAA explicit selection.
3. MSAA focused-text fallback when allowed.
4. Clipboard compatibility mode when allowed.
5. OCR fallback when allowed.
6. Unsupported result.

UI Automation is preferred because it can read accessibility text directly without modifying user state. MSAA is older but still useful for apps that expose accessibility through `IAccessible`. Clipboard compatibility mode is more invasive because it simulates copy behavior, so it is guarded carefully.

The method also waits briefly and retries. That protects against timing issues where the hotkey fires while the selection state is still settling.

## Clipboard Compatibility Mode

`ClipboardCompatibilityMode` exists because some apps do not expose selected text reliably through UIA or MSAA, but `Ctrl+C` can still copy the user's current selection.

The compatibility path is careful:

- backs up clipboard contents
- simulates copy
- waits for clipboard text to change
- reads copied text
- restores the original clipboard

This is not the first option because it touches user clipboard state. But without this mode, many editors and browser-like surfaces would fail in real use.

Terminal-specific clipboard capture is separate because terminals can react differently to keyboard shortcuts and selection state.

## Background Context Capture

Background context is not the same as selected text. It is supporting material used to disambiguate the selected snippet.

For IDEs, the pipeline collects multiple candidates:

- selection-anchored neighbor lines
- UIA document range
- descendant document range
- visible ranges
- nearest UIA container
- MSAA container
- OCR
- metadata fallback

The important design detail is candidate scoring. The engine should prefer editor-local code context and reject UI chrome such as sidebars, file trees, notifications, or labels. VS Code and Cursor can expose sidebar text through UIA, so the capture layer has filters for known noise patterns.

The selection hint helps anchor context. If selected text is found inside a larger candidate, the pipeline can keep nearby lines around that anchor instead of sending the entire accessible document.

## OCR Fallback

OCR is the last-resort path for visual-only or accessibility-blocked surfaces.

The capture result includes:

- `ocr_used`
- `ocr_confidence`

The backend prompt receives an OCR note. If confidence is low, the model is told to be conservative and mention possible garbled symbols. This is important because OCR can misread punctuation, braces, underscores, and similar code characters.

## CaptureResult Fields

`CaptureResult` carries both text and diagnostic metadata:

- `SelectedText`
- `BackgroundContext`
- `WindowTitle`
- `ProcessName`
- `Type`
- `SelectedMethod`
- `BackgroundMethod`
- `IsPartial`
- `IsUnsupported`
- `StatusMessage`
- `OcrUsed`
- `OcrConfidence`
- `UsageContext`

These fields make the app explainable to the developer. When capture goes wrong, logs can show whether the issue was classification, selection capture, background capture, or backend streaming.

## Partial And Unsupported States

`IsPartial` means the app captured something useful but not everything it wanted. For example, selected text may exist but context may be weak.

`IsUnsupported` means the active surface cannot be handled usefully. The client shows a user-facing status instead of pretending it can explain missing text.

Keeping these states separate is important. A partial capture can still produce a helpful explanation. An unsupported capture should avoid sending a misleading request.

## Noise Filtering

The capture layer has many filters because accessibility APIs often return UI labels mixed with real content.

Filters remove or reject:

- app window titles
- VS Code file-tree dumps
- known accessibility warnings
- sidebar labels
- notification text
- project-system messages
- tiny UI labels with no code signal

The code uses signals such as braces, semicolons, keywords, indentation, and selected-text overlap to decide whether a candidate looks like code or just interface text.

This part of the project is intentionally heuristic. Native app capture is messy. The goal is not theoretical purity; the goal is reliable behavior across real apps.

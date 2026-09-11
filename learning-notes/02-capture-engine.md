# Capture Engine

The capture engine is the most important technical feature in the project. It is the part that makes `simpleDocs` feel like an OS-level assistant instead of a normal chatbot.

The main entry point is `client/Engine/ContextCaptureEngine.cs`.

## Repeated IDE Capture and the Editor Bridge

Some editors draw selected text without exposing it through Windows UI Automation (UIA) or
Microsoft Active Accessibility (MSAA). The old bridge flow first required native selected text
and only then asked the extension for context. When the first step failed, the extension was
never asked, so pressing the hotkey again repeated the same failure.

`IDEStrategy.CaptureAsync` now checks for an embedded terminal first, then asks the local
VS Code/Cursor extension for the selection itself. `EditorBridgeClient.TryCaptureAsync(null)`
means "read the current editor selection." Passing a string instead means "return context only
if it belongs to this selection." Native accessibility and clipboard compatibility remain the
fallback when the extension is unavailable.

`captureEditorContext` reads `activeTextEditor.selection` and a bounded range from its document
each time a request arrives. It does not cache a previous selection, read the saved file from
disk, or change the caret. This is why switching from one selected line to another and back
works, and why unsaved edits appear in context. Selections are limited to 5,000 characters and
the surrounding range to 10,000 characters. A document consisting entirely of selected text
is still a valid selection, but the desktop marks its extra background as unavailable.

The extension publishes a local named-pipe endpoint with a random token. The desktop validates
the pipe format, connects as the same Windows user, sends the foreground process ID, and checks
that the foreground window has not changed before accepting the result. The extension requires
its window to be focused and rejects a mismatched known editor process ID. These checks prevent
ordinary stale or other-window responses from being used; they do not isolate applications
running under the same Windows user from each other.

`CaptureScope.Focused` resolves the accessibility element only when a native path needs it.
The scope also imposes a time budget and ties capture to one foreground window. The overlay
uses `ShowActivated=false` and handles `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, allowing a click
to dismiss it without taking focus away from the source editor. Each new request hides the
old overlay before capturing.

Capture support is application-dependent. The bridge supports VS Code/Cursor text editors;
terminals, browsers, and other applications use their own capture strategies. A protected
window or an application that exposes neither selection nor copy support can still fail.
When pane focus is not exposed, a retained editor selection can be ambiguous. The app reports
selection failure instead of sending an empty selection as if it were valid content.

## Request Completion and Retry

`BackendClient.SendExplainRequest` treats a provider error and a successful completion as
different outcomes. `onError` ends loading and displays an error; `onComplete` renders the final
response. A socket closing before either terminal message is a connection error, even if some
tokens already arrived. This prevents a partial response from being labeled complete.

The WebSocket close handshake has a two-second timeout. After a final response, cleanup errors
cannot change that response's outcome. If the peer never acknowledges close, the client aborts
the socket so the method can return. `App.OnHotkeyPressed` then releases its in-progress flag
in `finally`, allowing the next hotkey request. Repeated presses during an active request update
the status without erasing the current response. An empty completed stream displays a retry
message instead of leaving the loading indicator running.

The transport fixture exercises provider failure, recovery, early close, and a successful response
followed by an abrupt transport close. The native fixture checks UIA selection, nearby context,
and clipboard restoration. The extension integration test checks three real editor selections
including a repeat, using an unsaved document. These checks verify those paths, not compatibility
with every Windows application or availability of the hosted AI service.

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
- accepts a valid copy even when it equals the previous clipboard text
- reads copied text
- restores materialized formats only if no other application changed the clipboard

This is not the first option because it touches user clipboard state. But without this mode, many editors and browser-like surfaces would fail in real use.

Terminal-specific clipboard capture is separate because terminals can react differently to keyboard shortcuts and selection state.

The compatibility path never expands the selection or moves the caret to collect background text. Exact IDE context instead comes from the optional editor bridge or selection-anchored UIA ranges.

## Focus Scope And Editor Bridge

`CaptureScope` freezes the foreground window and focused UIA element for one capture. UIA provider work runs away from the WPF UI thread, only one capture is active, and stale results are discarded after focus changes or timeout.

`ContextTextWindow` bounds context around the exact selection and rejects candidates that are only selection echoes.

`EditorBridgeClient` talks to the VS Code/Cursor extension over a user-only local named pipe. The extension reads the active editor buffer, including unsaved text, but responds only for a focused window with one non-empty selection. The desktop client independently requires an exact selection match.

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

OCR is the last-resort background path for visual-only or accessibility-blocked surfaces. It is not used to claim exact selected text.

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

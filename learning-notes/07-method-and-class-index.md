# Method And Class Index

This file is a guided index of the project methods and classes you are most likely to revisit. It does not list every private helper in the codebase, but it explains the important entry points, workflows, and technical responsibilities.

## Desktop App

### `App`

File: `client/App.xaml.cs`

`OnStartup` is the desktop app boot sequence. It loads config, configures backend clients, sets up Windows startup behavior, creates the hidden message window, creates the tray icon, creates the capture engine, creates the overlay, restores auth, and registers the global hotkey.

`SetupTrayIcon` creates the tray icon and context menu. It wires menu actions for startup toggle, sign in, logout, and exit.

`UpdateTrayHotkeyStatus` updates tray text and shows a notification if the app had to use a fallback hotkey.

`UpdateTrayAuthStatus` enables or disables sign-in/logout menu items based on whether auth is enabled and whether a stored session exists.

`UpdateTrayStartupStatus` syncs the tray menu checked state with `WindowsStartupManager`.

`ToggleStartupFromTray` flips the Windows auto-start preference and writes it to the registry.

`OnHotkeyPressed` is the hotkey event handler. It prevents overlapping requests, ensures auth, then calls `HandleExplainRequest`.

`HandleExplainRequest` is the main user request workflow. It waits for trigger keys to settle, runs capture, logs capture metadata, handles unsupported captures, obtains an access token, shows overlay loading state, and calls `BackendClient.SendExplainRequest`.

`SubmitFeedbackAsync` sends thumbs feedback for the current request. It obtains a valid access token and calls `BackendClient.SendFeedbackAsync`.

`EnsureAuthenticatedAsync` tries valid token, restored session, and interactive login in that order.

`PromptForLoginAsync` shows the redeem-code login window and loops until login succeeds or the user cancels.

`SignInFromTrayAsync` and `LogoutFromTrayAsync` expose auth actions from the tray menu.

`BuildStatusLabel` creates the small overlay status label from environment and capture method metadata.

`EscapeForSingleLineLog` prepares captured text for readable one-line logs.

`ExitApp` unregisters the hotkey, disposes tray resources, closes overlay/hidden windows, and shuts down WPF.

### `ClientConfig`

File: `client/ClientConfig.cs`

`Load` merges appsettings files and environment variable overrides into one runtime config.

`ReadConfigFile` loads a JSON config from the app base directory and returns an empty model if the file is missing or invalid.

`Merge` overlays one config model onto another.

`GetEnvOverride`, `ParseBoolOverride`, and `ParseIntOverride` implement environment variable overrides.

`DeriveWebSocketUrl` converts HTTP URLs into WebSocket URLs.

### `BackendClient`

File: `client/BackendClient.cs`

`Configure` stores client config for backend calls.

`SendExplainRequest` sanitizes payload fields, opens a WebSocket, receives streamed messages, and falls back to one HTTPS explanation request only when the WebSocket handshake fails.

`SendFeedbackAsync` posts feedback to `/api/feedback`.

`ConnectWithRetryAsync` connects to `/ws/stream`, retrying according to config.

`BuildWebSocketUri` builds the `/ws/stream` URL. When protected mode is enabled, `ConnectWithRetryAsync` sends the bearer token in the WebSocket request header.

`SendExplainRequestHttpAsync` posts the sanitized payload to `/api/explain`, validates `response_text`, and returns the complete fallback explanation.

`BuildConnectionErrorMessage` distinguishes a missing local development server, hosted HTTP rejection, and general network failure without exposing raw transport internals.

`MaskAccessToken` redacts access tokens from log URLs.

`HandleSocketMessage` handles `meta`, `token`, `error`, and `complete` messages from the backend.

`ReceiveFullMessageAsync` assembles a full WebSocket text message from frames.

### `OverlayWindow`

File: `client/OverlayWindow.xaml.cs`

`ShowLoading` resets overlay state, positions it near the cursor, and shows the loading UI.

`AppendToken` appends streamed text and switches from loading state to content state.

`SetStatus` updates the overlay status label.

`OnStreamComplete` marks the response done, applies compact label coloring, and shows feedback controls.

`ShowMessage` shows a non-streamed message such as unsupported capture or auth required.

`SubmitFeedbackAsync` handles local feedback state and calls the injected feedback handler.

`ApplyCompactColorFormatting`, `AppendFormattedLine`, `TryExtractTitleLabel`, and `GetLabelBrush` convert plain labeled text into colored WPF inline runs.

`IsFeedbackInteraction` prevents feedback clicks from dismissing the overlay.

## Capture System

### `ContextCaptureEngine`

File: `client/Engine/ContextCaptureEngine.cs`

`ExecuteCaptureAsync` is the top-level capture pipeline. It detects the active window, classifies the environment, chooses a strategy, executes capture, builds usage context, logs summary data, and returns `CaptureResult`.

`LogCaptureSummary` logs lengths, methods, partial state, unsupported state, and timing without recording captured user text.

### `EnvironmentClassifier`

File: `client/Engine/Classifiers/EnvironmentClassifier.cs`

`Classify` maps process names and class names to an `EnvironmentType`. This controls which capture strategy runs.

### `CapturePipelines`

File: `client/Engine/Strategies/CapturePipelines.cs`

`CaptureSelectedTextAsync` captures selected text through UIA, MSAA, focused text, clipboard compatibility, OCR, or unsupported fallback.

`CaptureEmbeddedTerminalSelectedTextAsync` handles terminal selection inside IDEs.

`CaptureEditorBackground` gathers and scores multiple editor background candidates.

`RefineEditorBackgroundForExternalUse` trims background context around selected text and removes noisy lines.

`CleanKnownUiNoise`, `ShouldDiscardEditorNoiseLine`, `LooksLikeSidebarContent`, and related helpers remove UI chrome that accessibility APIs often mix into text results.

`HasCodeSignal` checks whether text looks like real code rather than labels.

### `ClipboardCompatibilityMode`

File: `client/Engine/Strategies/ClipboardCompatibilityMode.cs`

The selected-text methods simulate copy behavior safely when accessibility APIs fail. They back up the clipboard, perform copy, read copied text, and restore the original clipboard.

Clipboard compatibility copies only the user's existing selection. It never moves the caret or expands selection to obtain background text.

### `CaptureScope`, `ContextTextWindow`, and `EditorBridgeClient`

Files: `client/Engine/Strategies/CaptureScope.cs`, `client/Engine/Strategies/ContextTextWindow.cs`, `client/Engine/Strategies/EditorBridgeClient.cs`

`CaptureScope` provides a fixed focus/window boundary and lifetime. `ContextTextWindow` creates selection-centered bounded context and rejects echoes. `EditorBridgeClient` authenticates a local VS Code/Cursor bridge response, routes to the foreground editor owner when known, remains compatible with older bridge protocols during updates, removes dead-host manifests, and accepts only bounded selection-matched context.

### `UiAutomationCapture`, `MsaaCapture`, and `OcrCapture`

Files: `client/Engine/Strategies/UiAutomationCapture.cs`, `client/Engine/Strategies/MsaaCapture.cs`, `client/Engine/Strategies/OcrCapture.cs`

These are the low-level capture adapters:

- UIA reads modern accessibility text patterns.
- MSAA reads older accessibility interfaces.
- OCR reads visual screen content when text APIs fail.

They are intentionally lower-level than strategies. Strategies decide when each adapter should be used.

## Auth

### `AuthSessionManager`

File: `client/AuthSessionManager.cs`

`TryRestoreSessionAsync` loads a DPAPI-stored session and validates or refreshes it.

`RedeemCodeAsync` exchanges a redeem code for tokens and persists them.

`EnsureValidAccessTokenAsync` returns a valid access token, refreshing early when needed.

`LogoutAsync` clears local session state and tries to revoke the refresh token on the backend.

`BuildRequestId` creates a unique request id using timestamp, sequence number, and random suffix.

`ParseToken` reads the JWT payload locally to find participant id and expiration.

### `AuthApiClient`

File: `client/AuthApiClient.cs`

This class performs HTTP calls to the backend auth routes. It is the transport layer for redeem, refresh, and logout.

### `SecureTokenStore`

File: `client/SecureTokenStore.cs`

This class stores and loads token bundles using Windows DPAPI. It protects refresh tokens at rest for the current Windows user.

### Backend Auth Service

File: `backend/src/services/authService.js`

`redeemCode` validates a one-time code, creates or links a participant, marks the code used, stores a refresh token hash, and returns tokens.

`refreshAccessToken` validates a refresh token and returns a new access token.

`logoutRefreshToken` marks a refresh token revoked.

`authenticateRequest` validates bearer tokens or query tokens for WebSocket use.

`createAuthErrorResponse` converts internal auth errors into HTTP responses.

## Backend

### `createApp`

File: `backend/src/app.js`

`createApp` creates the Hono app, applies CORS, registers REST routes, protects routes with auth middleware, and registers the `/ws/stream` WebSocket endpoint.

### `handleStreamRequest`

File: `backend/src/services/streamHandler.js`

This is the backend's main explanation method. It sanitizes payloads, classifies the request, builds the prompt, selects the provider, streams tokens, sends completion, and logs the completed request.

`getProviderClient` chooses Gemini, Groq, or OpenRouter from `AI_PROVIDER`.

`mapTaskType` converts classifier case numbers into log-friendly task type labels.

`fallbackUsageContext` builds a usage context if the client did not provide one.

`determineRequestStatus` classifies the finished request as completed, partial, unsupported, or empty.

### `buildPrompt`

File: `backend/src/services/promptEngine.js`

`buildPrompt` chooses case rules, environment rules, widget output rules, sanitizes text, and creates the final system and user prompts.

`buildEnvironmentRules` adds behavior specific to browser, IDE, and error contexts.

### Provider Clients

Files: `backend/src/services/groqClient.js`, `backend/src/services/openRouterClient.js`, `backend/src/services/geminiClient.js`

Each provider exposes `streamCompletion` and `getModelName`.

`streamCompletion` sends a streaming chat request to the provider, parses server-sent chunks, and yields text content.

`getModelName` returns the configured model fallback for status and diagnostics.

### Request Logs

File: `backend/src/db/requestLogs.js`

`logCompletedRequest` inserts a completed request row into Supabase.

`saveRequestFeedback` updates `feedback_reaction` for a matching participant and request id.

The functions skip local-dev or unknown participant ids because those should not be written as real study rows.

## Feature-to-file Map

Use this map when you want to change behavior:

- App startup or tray behavior: `client/App.xaml.cs`
- Hotkey issues: `client/Engine/Managers/GlobalHotkeyManager.cs`
- Capture strategy routing: `client/Engine/ContextCaptureEngine.cs`
- App classification: `client/Engine/Classifiers/EnvironmentClassifier.cs`
- Selected/background capture quality: `client/Engine/Strategies/CapturePipelines.cs`
- Clipboard fallback: `client/Engine/Strategies/ClipboardCompatibilityMode.cs`
- Overlay display: `client/OverlayWindow.xaml.cs` and `client/OverlayWindow.xaml`
- Client/backend WebSocket messages: `client/BackendClient.cs`
- Prompt quality: `backend/src/services/promptEngine.js`
- Provider or model behavior: `backend/src/services/*Client.js`
- Auth behavior: `client/AuthSessionManager.cs` and `backend/src/services/authService.js`
- Feedback behavior: `client/OverlayWindow.xaml.cs`, `client/BackendClient.cs`, and `backend/src/routes/feedback.js`
- Packaging: `publish-client.ps1`, `prepare-tester-bundle.ps1`, and `package-backend.ps1`

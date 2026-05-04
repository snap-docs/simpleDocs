# simpleDocs

This repository contains `simpleDocs`, a Windows-first OS-level assistant that explains highlighted code, browser text, and terminal output inside a floating overlay without forcing the user to switch windows.

## Current Architecture

The main architecture is intentionally preserved.

- C# / .NET 8 / WPF desktop client
- Native Windows capture pipeline using UIA, MSAA, clipboard fallback, console APIs, OCR, and layered window screenshots
- Node.js backend with Hono
- WebSocket streaming for live responses
- Hosted Supabase/Postgres for auth and study logging
- Azure App Service for the hosted backend
- Groq as the current primary text model provider, with OpenRouter available as a backend-side fallback path
- Anthropic Claude Sonnet 4 vision routing for multi-layer screenshot understanding

## Current Product Status

The project is now in pilot-ready implementation state with a few remaining rollout checks.

Implemented now:

- native capture pipeline is in place
- overlay streaming response flow is in place
- short overlay-focused explanation style is in place
- unified auth foundation is implemented
- redeem-code auth is implemented
- backend-controlled Google sign-in is implemented
- email/password sign-up and sign-in are implemented
- Windows secure token storage with DPAPI is implemented
- authenticated WebSocket explain flow is implemented
- hosted request logging is implemented
- thumbs up / thumbs down feedback is implemented
- thumbs feedback is stored in `request_logs.feedback_reaction`
- production client publish works
- tester zip package works
- client can start automatically with Windows using a Registry `Run` entry
- Windows auto-start is ON by default and can be toggled from the tray menu
- Azure App Service backend is live
- hosted `/api/health` is live and returning `ok`
- hosted redeem-code login, refresh, and logout flows are working
- provider-based auth routes now support Google and email/password on the same shared identity model
- hosted DB connectivity checks are working
- Groq fallback-key support is implemented in the backend
- layered vision-augmented capture is implemented for canvas-locked and visual-only apps
- backend vision routing now accepts cursor, panel, and full-window screenshots on the existing `/ws/stream` path

Current remaining rollout work:

- run one final clean-machine launch of the packaged client outside the dev machine
- validate the tester package on at least one additional Windows environment
- complete internal pilot monitoring and support workflow
- rotate any temporary development secrets before broader external rollout

## Runtime Flow

1. User launches the Windows client.
2. The client restores the stored session or prompts for sign-in.
3. The client stores tokens securely on Windows using DPAPI.
4. The app runs hidden to tray and registers the global hotkey.
5. The user highlights text and presses the hotkey.
6. The client runs the existing text capture path and the layered vision capture path in parallel.
7. The capture engine extracts selected text, surrounding context, OCR text, and up to three image layers.
8. The client sends the unified payload to the backend over an authenticated WebSocket.
9. The backend classifies the request and routes text-only traffic to the text model path or vision traffic to Anthropic.
10. The overlay renders the response immediately as tokens arrive.
11. After stream completion, the backend writes one final `request_logs` row to the hosted DB.
12. The user can submit a single thumbs up or thumbs down reaction for the visible response.

## Current Data Model

Hosted Supabase/Postgres is used for:

- `participants`
- `redeem_codes`
- `auth_provider_links`
- `auth_sessions`
- `refresh_tokens`
- `request_logs`

`request_logs` is the main study-analysis table and currently stores:

- `participant_id`
- `request_id`
- `timestamp`
- `environment_type`
- `process_name`
- `usage_context`
- `window_title`
- `selected_text`
- `background_context`
- `selected_method`
- `background_method`
- `task_type`
- `response_text`
- `time_to_first_token_ms`
- `total_response_time_ms`
- `status`
- `feedback_reaction`
- `capture_method_extended`
- `vision_layers_used`
- `total_image_bytes_sent`

The system no longer stores `session_id`, `is_partial`, `is_unsupported`, or `feedback_at` in the hosted request log table.

## Vision Pipeline

The text pipeline still stays in place and remains the first-class source when OS text extraction works.

The new vision path adds three screenshot layers plus OCR on the active window:

- Layer 1: `400x300` cursor-region detail crop
- Layer 2: active panel crop from UIA bounds, with a `1200x800` cursor-centered fallback
- Layer 3: full active window downsampled to at most `1024x768`
- Layer 4: OCR text extracted from the full captured window

The client sends one shared payload shape with:

- selected text when available
- background context when available
- OCR text when available
- three optional base64 image layers
- one shared `capture_method_extended` field: `text_only`, `vision_augmented`, or `vision_only`

The backend then keeps one `/ws/stream` entrypoint and decides whether to use:

- the current text model path
- the Anthropic vision path
- text fallback when the in-memory vision circuit breaker is open

## Repository Structure

```text
/client
  /Engine
    /Strategies
    /Classifiers
    /Detectors
    /Managers
    /Models
  App.xaml.cs
  BackendClient.cs
  AuthApiClient.cs
  AuthSessionManager.cs
  BrowserAuthCoordinator.cs
  AccountMethodsWindow.xaml
  SecureTokenStore.cs
  LoginWindow.xaml
  OverlayWindow.xaml
  WindowsStartupManager.cs

/backend
  /db
    /migrations
  /scripts
    check-db-config.js
  /src
    /auth
    /config
    /db
    /middleware
    /routes
    /services
  index.js
  .env
  .env.example

/dist
  simpleDocs-direct-exe-1.1.0-pilot.zip
```

## Packaging

The current tester package is a portable zip, not a traditional installer.

Current packaged contents:

- `app\CodeExplainer.exe`
- `app\appsettings.json`
- `docs\final-tester-package-guide.md`
- `docs\chatgpt-tester-plan-prompt.md`
- `README-FIRST.txt`

The main tester entry point is `app\CodeExplainer.exe`.

## Local Development

### Backend

```powershell
cd backend
npm install
npm run check
npm run test:vision
npm run check:db
npm run dev
```

### Client

```powershell
dotnet build client\CodeExplainer.csproj -nologo
dotnet run --project client\CodeExplainer.csproj
```

### Optional vision-debug export

Set `CODE_EXPLAINER_VISION_DEBUG_DIR` to any writable folder before launching the client to save Layer 1, Layer 2, and Layer 3 images for manual inspection during scenario testing.

## Scenario Testing

Use the vision debug export directory for the six manual scenarios:

1. DaVinci Resolve: select timeline text and confirm the saved full-window layer still shows the preview frame.
2. Google Docs: select document text and confirm the panel/window layers preserve surrounding paragraph context.
3. Figma: invoke on a layer or canvas element and confirm the panel layer contains the active design region.
4. Image in browser: invoke on an image and confirm the full-window layer captures the visible image content.
5. PDF in browser: invoke on rendered PDF text and confirm OCR plus layout context are both present.
6. Existing IDE case: verify selected text still routes cleanly through the text-first path.

## Hosted Deployment State

Current hosted backend shape:

- Azure App Service hosts the backend
- Supabase hosts auth and request-log tables
- WPF client stays local on each tester machine
- production client config points to the hosted Azure backend
- Windows client remains responsible for capture, hotkey, overlay, and local auth state

Verified now:

- hosted health endpoint responds successfully
- hosted auth flow works
- hosted request logging works
- feedback updates `request_logs.feedback_reaction`
- tester zip builds correctly from the current repo
- direct `CodeExplainer.exe` launch is the supported packaged path

Remaining operational validation:

- one clean-machine packaged launch outside the development machine
- broader multi-app pilot validation across real tester machines

## Immediate Next Steps

1. validate the latest zip on one clean Windows machine
2. issue redeem codes or provision the chosen sign-in method for internal pilot users
3. monitor `request_logs` and `feedback_reaction` during pilot use
4. collect capture-quality feedback from editors, browsers, and terminals
5. rotate temporary development secrets before a wider external rollout

## Important Documents

- `deplyplan.md`: current deployment topology and rollout plan
- `launch-docs.md`: local, hosted, and package launch guide
- `release-checklist.md`: release and verification checklist
- `final-tester-package-guide.md`: tester-facing package guide
- `pilot-user-guide.md`: short tester usage guide
- `balancework.md`: remaining rollout work after current implementation
- `CAPTURE_PIPELINE.md`: current capture architecture reference
- `classifier-explained.md`: how `is_partial` and `task_type` are produced

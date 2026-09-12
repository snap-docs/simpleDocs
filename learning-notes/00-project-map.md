# Project Map

This project is `simpleDocs`, a Windows-first assistant that explains selected text without asking the user to leave the app they are working in. The desktop client watches for a global hotkey, captures selected text plus nearby context from the active window, sends that payload to a backend, and renders the streamed explanation inside a floating overlay.

The important idea is that the product is not a web app with a normal text box. The hard part is local Windows capture. The backend is deliberately thin: it receives already-captured text, classifies it, builds a prompt, streams an AI response, and logs the result.

## Runtime Shape

```text
User app
  -> Windows foreground window
  -> C# WPF tray client
  -> capture engine
  -> anonymous WebSocket request or HTTPS fallback
  -> Node/Hono backend
  -> AI provider stream
  -> overlay token rendering
  -> request/feedback logging
```

The desktop client is in `client/`. The backend is in `backend/`. The root scripts and markdown files describe launch, packaging, deployment, and pilot operations.

## Main Technologies

The client uses C# with .NET 8 and WPF because the product needs native Windows APIs, a tray icon, global hotkeys, registry startup integration, and topmost overlay windows. WPF also gives the app a normal desktop lifecycle without forcing an Electron runtime.

The capture layer uses Windows UI Automation, MSAA, clipboard compatibility mode, console APIs, and OCR fallbacks. Each method exists because different app families expose text differently. Editors, browsers, terminals, Electron apps, and external apps all need slightly different capture behavior.

The backend uses Node.js with Hono because the server needs a compact HTTP and WebSocket surface. It handles health checks, optional auth routes, feedback routes, full HTTPS explanations, and the streaming endpoint.

Supabase/Postgres is used for redeem-code auth and request logs. The client stores local tokens with Windows DPAPI through `SecureTokenStore`, while the backend stores refresh token hashes and request data.

AI providers are isolated behind provider clients. The current working path is Groq, OpenRouter remains available, and Gemini support was added through `geminiClient.js`.

## Feature Areas

The feature docs in this folder are split by how the project actually behaves:

- `01-desktop-app-lifecycle.md` explains startup, tray behavior, hotkey registration, app settings, and Windows auto-start.
- `02-capture-engine.md` explains foreground-window detection, environment classification, selected-text capture, background capture, fallbacks, sanitization, and result metadata.
- `03-auth-and-session.md` explains redeem-code login, access tokens, refresh tokens, secure local token storage, and protected backend routes.
- `04-backend-streaming-and-ai.md` explains Hono routing, WebSocket streaming, provider clients, prompt construction, request validation, and response logging.
- `05-overlay-and-feedback.md` explains the WPF overlay, streaming token display, compact formatting, thumbs feedback, and backend feedback storage.
- `06-deployment-and-packaging.md` explains development launch, production config, packaging scripts, tester bundle shape, and hosted deployment expectations.

## How To Read The Code

Start with `client/App.xaml.cs`. That file is the desktop conductor. It loads config, creates the tray icon, restores auth, registers the hotkey, runs the capture engine, sends the backend request, and forwards streamed tokens to the overlay.

Then read `client/Engine/ContextCaptureEngine.cs`. That file shows the capture pipeline at the highest level: detect active window, classify environment, choose strategy, execute capture, build usage context, and return `CaptureResult`.

After that, read `backend/src/app.js` and `backend/src/services/streamHandler.js`. The app file shows the route surface; the stream handler shows the real backend workflow.

The docs are intentionally written as learning notes, not production API docs. They explain why the project is shaped this way, what each method is trying to protect against, and where to look when behavior needs to change.

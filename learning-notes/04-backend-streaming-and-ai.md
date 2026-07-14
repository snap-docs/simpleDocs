# Backend Streaming And AI

The backend is a Node.js service using Hono. It exposes REST endpoints for health, auth, feedback, and explain metadata, plus a WebSocket endpoint for live streamed explanations.

The main files are:

- `backend/index.js`
- `backend/src/app.js`
- `backend/src/services/streamHandler.js`
- `backend/src/services/promptEngine.js`
- provider clients in `backend/src/services/*Client.js`

## Server Startup

`backend/index.js` loads environment files with `loadEnvironment`, validates runtime config, creates the Hono app, starts the HTTP server, and injects WebSocket support.

Environment loading checks:

- `.env`
- `.env.{environment}`
- `.env.local`
- `.env.{environment}.local`

Later files override earlier files. This gives local development and deployment environments a simple configuration model.

## Hono App Shape

`createApp` in `backend/src/app.js` creates the app and registers routes.

The route surface is:

- `/api/health`
- `/auth/redeem-code`
- `/auth/refresh`
- `/auth/logout`
- `/api/explain`
- `/api/feedback`
- `/ws/stream`

The app uses CORS globally. Auth middleware protects explain and feedback REST routes. The WebSocket route authenticates manually before upgrading.

## WebSocket Stream Flow

`handleStreamRequest` is the real backend workflow.

It does these steps:

1. Receive a JSON payload from the client.
2. Validate that selected text exists.
3. Sanitize selected text, background context, metadata, request id, and usage context.
4. Normalize environment type and capture method fields.
5. Classify the request case.
6. Build prompt messages.
7. Select the AI provider client.
8. Send a `meta` message to the client.
9. Stream provider tokens back as `token` messages.
10. Send `complete`.
11. Insert the completed request log asynchronously.

The response protocol is simple JSON:

```json
{ "type": "meta", "label": "ide_editor | uia_selection + uia_visible_ranges" }
{ "type": "token", "content": "Definition:" }
{ "type": "complete" }
```

If the provider fails, the backend sends:

```json
{ "type": "error", "message": "..." }
```

## Why WebSockets

The overlay should start showing text as soon as the model produces it. A normal HTTP request would require waiting for the full response unless server-sent streaming were added.

WebSockets work well here because the desktop client can:

- connect
- send exactly one explain payload
- receive meta and token messages
- close after completion

The backend treats the socket as one request lifecycle rather than a shared chat session.

## Classification

The classifier maps selected text and background context into a case type:

- code explanation
- error explanation
- terminal explanation
- technical text/jargon explanation

That case type controls the system prompt style.

This is intentionally lightweight. The model still does most semantic interpretation, but classification gives it a strong starting behavior.

## Prompt Engine

`buildPrompt` builds a system prompt and user prompt.

The system prompt is assembled from:

- case-specific rules
- environment-specific rules
- strict overlay output rules

The user prompt includes:

- logging metadata that the model should not explain directly
- optional background context
- selected text
- optional OCR note

The prompt rules are built around the overlay constraint. The answer should be short, labeled, and directly useful in a small floating widget.

Important behavior:

- normal code should not be framed as an error
- browser prose should be explained as prose unless it really is an error
- errors should get Issue/Why/Hint style
- error hints should not give complete corrected code
- selected text remains the focus
- background is only support

## Provider Clients

Provider clients expose the same shape:

```js
streamCompletion(systemPrompt, userPrompt)
getModelName()
```

Current provider files:

- `groqClient.js`
- `openRouterClient.js`
- `geminiClient.js`

`streamHandler` chooses the client from `AI_PROVIDER`.

Groq supports a primary key, fallback key, and comma-separated extra keys. This is useful when one key is exhausted or fails. The client tries each key in order.

OpenRouter uses the OpenRouter chat completions API and includes referer/title headers.

Gemini support uses Google's OpenAI-compatible chat completions endpoint. A valid Gemini API key is still required at runtime.

## Request Logging

After streaming completes, `logCompletedRequest` inserts one row into `request_logs`.

The log includes:

- participant id
- request id
- timestamp
- environment type
- process name
- usage context
- window title
- selected text
- background context
- selected/background capture methods
- task type
- response text
- latency metrics
- status

The insert is intentionally not awaited as a blocking user-facing step. The stream should complete for the user even if logging has an issue.

## Status Calculation

`determineRequestStatus` maps the request to:

- `unsupported`
- `partial`
- `empty_response`
- `completed`

That status gives study/pilot analysis a cleaner signal than just "request happened".

## Sanitization

The backend sanitizes payload text again even though the client also sanitizes. This double boundary is useful because the backend is the trust boundary.

Sanitization limits:

- selected text length
- background context length
- process/window metadata length
- request id length
- known noisy text patterns

The backend should not blindly store or send unlimited client text to the model.

# Deployment Plan

## Current Stage

The project is now in implemented-and-hosted pilot stage.

The main application flow is already working in its intended structure:

- Windows WPF client
- native capture engine
- Node.js Hono backend
- WebSocket streaming
- floating overlay response UI
- hosted Supabase auth and request logging
- Azure-hosted backend

## Current Deployment Topology

### Client side

- WPF desktop client runs on the tester's Windows machine
- client handles capture, hotkey, local auth restore, tray UI, and overlay rendering
- client stores auth state locally with DPAPI
- client can auto-start with Windows using a Registry `Run` entry
- auto-start is enabled by default and can be toggled from the tray menu

### Backend side

- Azure App Service hosts the Node.js backend
- backend exposes auth endpoints and authenticated WebSocket streaming
- backend handles classification, prompt construction, provider routing, and post-response logging
- Groq is the primary AI provider
- backend now supports a fallback Groq key and can still be extended to use OpenRouter as a provider fallback

### Data side

- Supabase/Postgres stores auth and study data
- `participants`, `redeem_codes`, `refresh_tokens`, and `request_logs` are the active tables
- thumbs feedback is stored directly in `request_logs.feedback_reaction`

## What Is Already Implemented

### Core product

- hotkey-triggered native capture flow
- overlay streaming response flow
- compact overlay-oriented explanation style
- response feedback with thumbs up / thumbs down

### Auth and session

- redeem-code login
- refresh-token flow
- logout flow
- Windows secure token storage
- WebSocket auth at handshake
- protected backend routes

### Hosted data path

- Supabase/Postgres connectivity
- request log table path
- request id flow
- async post-response request logging
- redeem codes seeded in hosted DB
- feedback reaction saved in `request_logs`

### Packaging and support

- client publish script
- tester bundle script
- support docs and tester docs
- final direct-exe zip package

### Hosting

- Azure App Service backend deployment workflow exists
- Azure backend is live
- `/api/health` works from the hosted URL
- hosted redeem / refresh / logout flow works
- hosted authenticated WebSocket explanation works
- hosted request logging works
- client staging/production config points to the Azure backend

## What Still Needs To Be Done

### Must do before broader external pilot

1. Validate the latest packaged zip on one clean Windows machine.
2. Run a short internal pilot with real users across the main target apps.
3. Confirm support handling for tester issues and log collection.
4. Rotate any temporary development keys before sending the package to more users.

### Strongly recommended before 10 to 30 external users

1. Validate on multiple Windows versions and DPI settings.
2. Validate on VS Code, browser, Windows Terminal, and classic terminal.
3. Freeze one package version and one model configuration for the study period.
4. Track redeem-code issuance clearly per tester.

## Deployment Phases

### Phase 1: Completed foundation

- backend auth implemented
- hosted Supabase configured
- schema applied
- Azure backend deployed
- production client config points to hosted backend
- tester package build works

### Phase 2: Completed hosted validation

- hosted health endpoint works
- hosted redeem-code login works
- hosted refresh/logout works
- hosted authenticated WebSocket request works
- hosted DB logging works
- feedback logging works

### Phase 3: Current internal pilot step

- package the latest build
- validate on at least one clean Windows machine
- issue real redeem codes
- collect internal pilot feedback

### Phase 4: External pilot

- 10 to 30 users
- fixed build and fixed model configuration
- manual redeem-code issuance
- regular review of `request_logs` and `feedback_reaction`

## Current Risks

### Technical risks

- packaged build still needs one clean-machine verification outside the development machine
- capture quality still needs broader validation across real editor/browser/terminal combinations

### Operational risks

- temporary development API keys should be rotated
- support process must be consistent before broad rollout
- testers need clear privacy guidance about selected text and surrounding context

## Recommended Immediate Order

1. validate the current package on one clean Windows machine
2. start a small internal pilot
3. monitor logs and feedback reactions
4. fix high-signal pilot issues
5. expand to external testers

## Definition Of Pilot Ready

The system is pilot ready when all of these are true:

- packaged build launches cleanly
- redeem-code login works against the hosted backend
- session restores after restart
- authenticated WebSocket explain request works
- final request log row is written to hosted DB
- thumbs feedback works
- no critical capture or overlay regressions are present
- one clean-machine package verification has been completed

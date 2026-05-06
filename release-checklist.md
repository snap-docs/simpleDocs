# Release Checklist

## 1. Azure Backend Configuration

- confirm Azure App Service environment variables contain real values for:
  - `SUPABASE_URL`
  - `SUPABASE_ANON_KEY`
  - `SUPABASE_SERVICE_ROLE_KEY`
  - `ACCESS_TOKEN_SECRET`
  - `AI_PROVIDER`
  - `GROQ_API_KEY`
  - `GROQ_API_KEY_FALLBACK` if used
- confirm `SKIP_AUTH=false`
- confirm `PUBLIC_APP_URL` matches the live Azure URL
- save/apply the settings
- restart the Azure Web App
- confirm `https://<your-app>/api/health` returns `ok`
- confirm Google auth variables are configured before enabling Google-first sign-in in production:
  - `GOOGLE_CLIENT_ID`
  - `GOOGLE_CLIENT_SECRET` if required by the app registration
  - `AUTH_FLOW_TOKEN_SECRET`
- if legacy redeem-code support remains enabled for the pilot, confirm the redeem-code login route still works for support-managed accounts

## 2. Database Readiness

- confirm migration SQL is applied in the hosted DB
- if legacy redeem-code support remains enabled, confirm support-managed redeem codes exist in `redeem_codes`
- run `npm run check:db`
- confirm these tables are reachable:
  - `participants`
  - `redeem_codes`
  - `auth_provider_links`
  - `auth_sessions`
  - `refresh_tokens`
  - `request_logs`

## 3. Client Configuration

- confirm hosted URLs are present in:
  - `client/appsettings.Staging.json`
  - `client/appsettings.Production.json`
- confirm `ApiBaseUrl` and `WsBaseUrl` point to the Azure backend
- confirm auth is enabled in staging/production config

## 4. Build Output

### Client

1. run `./publish-client.ps1 -Configuration Release -Runtime win-x64 -EnvironmentName Production`
2. confirm output exists in `dist/client/`
3. confirm `CodeExplainer.exe` opens directly with hosted production settings
4. run `./prepare-tester-bundle.ps1 -EnvironmentName Production -CreateZip`
5. confirm output exists in `dist/tester-bundle/`
6. confirm the final zip exists in `dist/simpleDocs-tester-bundle.zip`
7. confirm the package contains the published runtime files under `app\`, including:
   - `app\CodeExplainer.exe`
   - `app\appsettings.json`
   - any runtime metadata/native files emitted by `dotnet publish`
   - `docs\final-tester-package-guide.md`
   - `docs\chatgpt-tester-plan-prompt.md`
   - `README-FIRST.txt`
8. launch once on a clean Windows machine

### Backend

1. run `npm run check`
2. run `npm run check:db`
3. confirm no local secrets are committed
4. confirm Azure deploy workflow still targets only `backend/`

## 5. Functional Verification

- Google sign-in works against the hosted backend
- if legacy redeem-code support remains enabled, redeem-code login still works for support-managed accounts
- email/password sign-up and sign-in work if email/password is enabled for the release
- access token refresh works after restart
- logout works
- authenticated WebSocket connect works
- one full explain request succeeds
- overlay renders correctly
- thumbs feedback works for one visible response
- final request log row is inserted after completion
- `feedback_reaction` updates when a thumb is clicked
- auto-start can be toggled from the tray menu

## 6. Data Verification

After one successful hosted test request, confirm these DB effects:

- one `participant` row exists or is reused
- one or more `auth_provider_links` rows exist for the chosen sign-in method
- one `auth_sessions` row exists
- one `refresh_tokens` row exists
- one `request_logs` row exists with expected values
- the row includes the expected `task_type` and `status`
- the row can later be updated with `feedback_reaction`

## 7. Operational Readiness

- tester guide is packaged
- sign-in method provisioning is tracked for the pilot group
- privacy/support contact is prepared
- `pilot-support-runbook.md` is reviewed and ready for issue triage
- internal pilot users are selected
- development keys are rotated if necessary

## 8. Go / No-Go

Release only if all are true:

- hosted health works
- hosted auth works
- build works
- DB logging works
- feedback logging works
- support process is ready
- no critical capture or overlay regressions remain
- at least one clean-machine package validation has been completed

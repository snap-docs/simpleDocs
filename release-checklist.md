# Release Checklist

## 1. Azure Backend Configuration

- confirm Azure App Service environment variables contain real values for:
  - `AI_PROVIDER`
  - `GROQ_API_KEY`
  - `GROQ_API_KEY_FALLBACK` if used
  - `GROQ_MODEL` set to a model currently available to the deployment key
- confirm `AUTH_MODE=anonymous`
- confirm `SKIP_AUTH=false`
- confirm `PUBLIC_APP_URL` matches the live Azure URL
- save/apply the settings
- restart the Azure Web App
- confirm `https://<your-app>/api/health` returns `ok`
- confirm an unauthenticated WebSocket handshake is accepted

## 2. Database Readiness

- anonymous production does not require redeem codes, refresh tokens, participant rows, or request-log writes
- before changing `AUTH_MODE` back to `protected`, apply the migrations and run `npm run check:db`
- do not treat unavailable Supabase auth tables as a blocker for the current no-login build

## 3. Client Configuration

- confirm hosted URLs are present in:
  - `client/appsettings.Staging.json`
  - `client/appsettings.Production.json`
- confirm `ApiBaseUrl` and `WsBaseUrl` point to the Azure backend
- confirm auth is disabled in staging/production config

## 4. Build Output

### Client

1. run `./publish-client.ps1 -Configuration Release -Runtime win-x64 -EnvironmentName Production` (self-contained by default)
2. confirm output exists in `dist/client/`
3. confirm `CodeExplainer.exe` opens directly with hosted production settings
4. run `./prepare-tester-bundle.ps1 -EnvironmentName Production`
5. confirm output exists in `dist/tester-bundle/`
6. create the final zip from `dist/tester-bundle/`
7. confirm the package contains only:
   - `app\CodeExplainer.exe`
   - `app\appsettings.json`
   - `docs\final-tester-package-guide.md`
   - `docs\chatgpt-tester-plan-prompt.md`
   - `README-FIRST.txt`
8. launch once on a clean Windows machine

### Backend

1. run `npm run check`
2. run `npm audit --omit=dev --audit-level=moderate`
3. confirm no local secrets are committed
4. confirm Azure deploy workflow still targets only `backend/`

## 5. Functional Verification

- app starts without a login window
- anonymous WebSocket connect works
- one full explain request succeeds
- overlay renders correctly
- account-linked feedback controls and request logging are not expected in anonymous mode
- auto-start can be toggled from the tray menu

## 6. Data Verification

After one successful hosted anonymous request, confirm no account, redeem code, or refresh token was required. Database row and feedback checks apply only to a future protected-auth deployment.

## 7. Operational Readiness

- tester guide is packaged
- Azure provider quotas and cost alerts are configured
- privacy/support contact is prepared
- internal pilot users are selected
- development keys are rotated if necessary

## 8. Go / No-Go

Release only if all are true:

- hosted health works
- hosted anonymous streaming works
- build works
- anonymous requests work without database identity or feedback storage
- support process is ready
- no critical capture or overlay regressions remain
- at least one clean-machine package validation has been completed

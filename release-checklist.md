# Release Checklist

## Client bundle

1. Run:
   `.\publish-client.ps1 -Configuration Release -Runtime win-x64 -EnvironmentName Production`
2. Confirm output exists in `dist/client/`
3. Confirm `Start-CodeExplainer.bat` is present
4. Confirm `appsettings.Production.json` is in the bundle
5. Confirm the app launches on a clean Windows machine

## Backend bundle

1. Run:
   `.\package-backend.ps1`
2. Confirm output exists in `dist/backend/`
3. Confirm `src/`, `db/`, `package.json`, and `.env.example` are present
4. Add real production `.env` values before deployment

## Before pilot

1. Set real backend URLs in the production client config
2. Configure the hosted backend environment
3. Verify auth endpoints
4. Verify one authenticated WebSocket request
5. Verify request logging after stream completion
6. Verify logout and session-expiry behavior

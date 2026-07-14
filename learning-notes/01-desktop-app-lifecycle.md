# Desktop App Lifecycle

The desktop client is a .NET 8 WPF app. It is designed to stay mostly invisible: it starts, restores a session if needed, registers a global hotkey, lives in the system tray, and only shows UI when the user asks for an explanation.

The central file is `client/App.xaml.cs`.

## Startup Flow

`OnStartup` is the main entry point. It performs these steps:

1. Writes startup information to `RuntimeLog`.
2. Loads configuration through `ClientConfig.Load()`.
3. Configures `BackendClient` with API and WebSocket URLs.
4. Creates `AuthSessionManager` and `WindowsStartupManager`.
5. Applies Windows auto-start preference.
6. Creates a hidden `MainWindow` to host the hotkey message loop.
7. Creates the tray icon and menu.
8. Creates `ContextCaptureEngine`.
9. Creates the overlay window.
10. Ensures authentication if auth is enabled.
11. Registers the global hotkey.

The hidden window is important. Global hotkeys on Windows are delivered through a window message pump. The user does not need to see a main window, but the app still needs a window handle to receive hotkey messages.

## Configuration Loading

`ClientConfig.Load()` reads:

- `appsettings.json`
- `appsettings.{Environment}.json`
- environment variable overrides

The base settings control:

- `ApiBaseUrl`
- `WsBaseUrl`
- auth enabled or disabled
- token refresh skew
- WebSocket retry behavior

The environment is selected by `CODE_EXPLAINER_ENV` or the `Environment` property in `appsettings.json`. In local development, auth can be disabled, which lets the client send WebSocket requests without a bearer token.

The WebSocket URL can be provided directly. If it is missing, it is derived from the HTTP URL by changing `http` to `ws` and `https` to `wss`.

## Tray Icon

`SetupTrayIcon()` creates a Windows Forms `NotifyIcon` even though the main UI is WPF. That is normal in desktop apps because WinForms has a mature tray icon API.

The tray menu includes:

- Start On Windows Login
- Sign In
- Logout
- Exit

`UpdateTrayAuthStatus()` enables or disables sign-in/logout based on whether auth is enabled and whether a session exists.

`UpdateTrayStartupStatus()` reflects the registry startup setting.

## Windows Auto-start

`WindowsStartupManager` stores the user preference under:

```text
HKCU\Software\simpleDocs
```

It applies startup behavior through:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The command depends on how the app is running. If it is running through `dotnet.exe`, the startup command includes the app assembly path. If it is a published executable, it stores the executable path directly.

Auto-start defaults to enabled when no preference has been saved. The tray menu can turn it off.

## Hotkey Registration

The global hotkey is handled by `GlobalHotkeyManager`. `App.xaml.cs` subscribes to `HotkeyPressed`, then calls `HandleExplainRequest`.

The default hotkey is shown in logs and tray status. If the preferred hotkey is already taken, the manager can register an alternate and show a balloon notification.

The app protects against duplicate requests with `_isExplainInProgress`. It uses `Interlocked.CompareExchange` so two rapid hotkey presses cannot run two capture and backend streams at the same time.

## Explain Request Flow

`OnHotkeyPressed` does lightweight control flow:

- checks whether another explanation is running
- increments a request sequence
- makes sure auth is valid
- calls `HandleExplainRequest`
- releases the in-progress flag

`HandleExplainRequest` does the actual work:

- waits for trigger keys to settle through `HotkeyReleaseGuard`
- calls `_captureEngine.ExecuteCaptureAsync`
- logs capture metadata and previews
- handles unsupported or empty capture results
- obtains a valid access token when auth is enabled
- builds a request id
- shows the overlay loading state
- calls `BackendClient.SendExplainRequest`

This split keeps the hotkey event handler small while putting the request workflow in one readable method.

## Why The Client Owns Capture

The backend cannot capture the user's active window. Only the local process can use UIA, MSAA, clipboard, Win32, console APIs, and OCR against the user's desktop session.

That is why the backend receives fields like:

- selected text
- background context
- process name
- window title
- environment type
- selected capture method
- background capture method
- partial/unsupported flags

The backend trusts the client to do operating-system capture and focuses on explanation.

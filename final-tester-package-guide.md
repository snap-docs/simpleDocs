# Final Tester Package Guide

## What this package is

This zip is a portable Windows app package for `simpleDocs`.

It is not a traditional installer.

The tester does not need to install it into `Program Files`.

The tester does not need to install the .NET runtime separately.

They only need to unzip it and run the app.

## What the tester will see after unzipping

After extracting the zip, the folder contains:

- `app\CodeExplainer.exe`
- `app\appsettings.json`
- `docs\final-tester-package-guide.md`
- `docs\chatgpt-tester-plan-prompt.md`
- `README-FIRST.txt`

The main file the tester should use is:

`app\CodeExplainer.exe`

## First-time setup for a tester

1. Extract the zip to a normal folder such as `Desktop\simpleDocs` or `Documents\simpleDocs`.
2. Open the extracted folder.
3. Open the `app` folder.
4. Double-click `CodeExplainer.exe`.
5. If Windows shows a security prompt, choose the option to continue if the tester trusts the app source.
6. No account, login, or redeem code is required.
7. Wait until the tray icon shows the app is ready.

## Daily use

The tester can simply:

1. Open the same extracted folder again.
2. Double-click `app\CodeExplainer.exe`.
3. Select text in an editor, browser, or terminal.
4. Press the app hotkey.
5. Read the overlay response.

By default, `simpleDocs` also starts automatically when the user signs in to Windows.

If they want to turn that off later, they can use the tray icon menu.

## Does the tester need to install it again every day

No.

They only need to unzip the package once.

After that, they can keep using the same extracted folder and launch `CodeExplainer.exe` whenever they want.

## What happens when we send a newer version later

If we send a new zip later, the tester should:

1. Close the current app if it is running.
2. Extract the new zip to a fresh folder, or replace the old extracted folder.
3. Launch the new `CodeExplainer.exe`.

No account state needs to be migrated between versions or machines.

## Best folder recommendation for testers

Tell testers not to run the app from inside the zip viewer.

They should extract it first.

Good locations:

- Desktop
- Documents
- a dedicated `simpleDocs` folder

Avoid temporary download folders if possible.

## How the tester should open the app

Use:

`app\CodeExplainer.exe`

Do not ask the tester to run terminal commands.

Do not ask the tester to use PowerShell for normal usage.

## What the tester should do inside the app

1. Highlight code, terminal output, or technical text.
2. Press the configured hotkey.
3. Wait for the floating overlay.
4. Read the explanation.
5. Account-linked thumbs feedback is not shown in the current no-login build.

## If the tester has trouble

Ask them to report:

- the app version
- the time of the issue
- the app they were using
- what they selected
- what happened instead
- a screenshot if possible

## Recommended message you can send to testers

Extract the zip first, then open `app\CodeExplainer.exe`. No login or redeem code is required. You can keep using the same app folder, and the app starts automatically with Windows by default unless you turn that off from the tray menu.

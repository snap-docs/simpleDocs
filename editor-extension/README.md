# simpleDocs Context

This Windows extension provides reliable selection and background capture in VS Code and Cursor.
It reads the active editor's in-memory document, so unsaved edits are included.
It does not move the caret, copy whole files, scan a repository, or call an AI API.

## Install and use

1. Install the packaged `simpledocs-context-0.3.0.vsix` using **Extensions: Install from VSIX** in VS Code or Cursor.
2. Reload that editor window, then start the simpleDocs desktop executable.
3. Highlight text in the editor and press the simpleDocs hotkey (normally Ctrl+Shift+Space).
4. The desktop app asks this extension for the current selection and surrounding context before trying native selection capture.

For the most reliable IDE workflow, select code and use the editor context menu command
**simpleDocs: Explain Selection**, or press **Ctrl+Alt+D**. This command snapshots the active
unsaved editor buffer inside VS Code/Cursor and sends it directly to the running simpleDocs
desktop app. It does not use clipboard, UI Automation, MSAA, or OCR. The existing desktop
**Ctrl+Shift+Space** hotkey remains available for browsers, terminals, and applications without
the extension.

Run **simpleDocs: Context Bridge Status** from the command palette to check readiness.
If the command is missing after installation, run **Developer: Reload Window**. For an unpackaged
development checkout, open this folder in VS Code and launch an Extension Development Host with
F5 after adding a standard extension launch configuration.

## How matching works

Each local extension host creates a randomly named Windows named pipe and a random authentication token.
The discovery file is stored under `%LOCALAPPDATA%\CodeExplainer\editor-bridge` and removed on normal shutdown.
The desktop app connects only to a pipe owned by the same Windows user. Stale or unavailable endpoints fall back to native capture. The desktop also publishes a separate authenticated
command pipe under `%LOCALAPPDATA%\\CodeExplainer\\desktop-bridge`; the extension uses that pipe
for **Explain Selection** and waits for an acknowledgement before reporting success.

The extension responds only when its window is focused and exactly one non-empty selection exists.
The desktop request includes the foreground process ID; an extension with a different known editor process ID rejects it.
For primary capture, no initial accessibility selection is needed. For fallback context requests that
already include selected text, the selection must match that text (allowing CRLF/LF normalization).
It reads at most 10,000 characters around that selection and accepts at most 5,000 selected characters.
No arbitrary file path can be requested. The pipe does not listen on a network port.

The extension sends text only when the desktop app requests a capture. The desktop app then sends that
selection/context to its configured backend and AI provider, just as with native capture. Do not select
content you do not want sent to that backend. Other applications running as your Windows user share
the same local trust boundary.

## Limits

The bridge supplies editor context, not terminal scrollback. Integrated terminals use native capture.
The VS Code API exposes active-editor and window focus, but not a reliable public editor-text-focus query;
the native terminal check runs before the bridge. If an application does not expose which pane has
focus, a retained editor selection can still be ambiguous. Select directly in the text editor before
invoking capture. With this extension active, disabled accessibility does not prevent reading editor
selections. Without it, native capture still depends on the application's accessibility or copy support.
Multiple selections fall back to native capture.
Remote documents are supported through the local UI extension host; remote-only extension hosts are not.

## Development

Run `npm test` for context and real Windows named-pipe tests. There are no runtime npm dependencies.
Package with `npx @vscode/vsce package --no-dependencies --out ../dist/simpledocs-context-0.3.0.vsix`.

For an interactive integration test, build `tests/SimpleDocs.Tests` in Release and launch VS Code
with this directory as `--extensionDevelopmentPath` and `test/integration.cjs` as
`--extensionTestsPath`. Use separate `--user-data-dir` and `--extensions-dir` directories, and keep
the test editor in the foreground. The test reads three changing selections from an unsaved Python
document through the real desktop capture engine, checks neighboring context and selection preservation,
and writes its result to `runlogs/editor-integration.json` in the repository.

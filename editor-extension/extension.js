const vscode = require('vscode');
const path = require('node:path');
const { startBridge } = require('./bridge');
const { captureEditorContext } = require('./context');
let bridge;

async function activate(context) {
  let status = 'The context bridge requires Windows.';
  if (process.platform === 'win32' && process.env.LOCALAPPDATA) {
    try {
      bridge = await startBridge(path.join(process.env.LOCALAPPDATA, 'CodeExplainer', 'editor-bridge'),
        selection => captureEditorContext(vscode, selection));
      status = 'Context bridge is ready. Select text, then use the simpleDocs desktop hotkey.';
    } catch { status = 'Context bridge could not start. Reload the editor window to retry.'; }
  }
  context.subscriptions.push(vscode.commands.registerCommand('simpleDocs.contextStatus', () =>
    vscode.window.showInformationMessage(status)));
}

async function deactivate() {
  if (bridge) await bridge.dispose();
}
module.exports = { activate, deactivate };

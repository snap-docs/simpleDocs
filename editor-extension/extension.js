const vscode = require('vscode');
const path = require('node:path');
const { startBridge } = require('./bridge');
const { captureEditorContext } = require('./context');
const { explainSelection } = require('./explain');
let bridge;

async function activate(context) {
  let status = 'The context bridge requires Windows.';
  if (process.platform === 'win32' && process.env.LOCALAPPDATA) {
    try {
      bridge = await startBridge(path.join(process.env.LOCALAPPDATA, 'CodeExplainer', 'editor-bridge'),
        (selection, request) => {
          const ownerPid = Number(process.env.VSCODE_PID);
          if (request.window_pid && ownerPid && request.window_pid !== ownerPid) return null;
          return captureEditorContext(vscode, selection);
        });
      status = 'Context bridge is ready. Select text, then use the simpleDocs desktop hotkey.';
    } catch { status = 'Context bridge could not start. Reload the editor window to retry.'; }
  }
  context.subscriptions.push(vscode.commands.registerCommand('simpleDocs.contextStatus', () =>
    vscode.window.showInformationMessage('simpleDocs Context ' + context.extension.packageJSON.version + ': ' + status)));
  context.subscriptions.push(vscode.commands.registerCommand('simpleDocs.explainSelection', async () => {
    try { return await explainSelection(vscode); }
    catch (error) {
      vscode.window.showErrorMessage(error.message || 'simpleDocs could not receive the selection.');
      return false;
    }
  }));
}

async function deactivate() {
  if (bridge) await bridge.dispose();
}
module.exports = { activate, deactivate };

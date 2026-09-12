const { captureEditorContext } = require('./context');
const { sendToDesktop } = require('./desktop');

async function explainSelection(vscode, send = sendToDesktop) {
  // Snapshot synchronously at the command gesture, before any asynchronous discovery or transport.
  const snapshot = captureEditorContext(vscode, null);
  if (!snapshot) {
    throw new Error('Select one region of up to 5,000 characters in the text editor first.');
  }
  return send({ ...snapshot, editor: /cursor/i.test(vscode.env.appName) ? 'cursor' : 'code' });
}

module.exports = { explainSelection };

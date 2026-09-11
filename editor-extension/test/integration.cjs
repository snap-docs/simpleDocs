const vscode = require('vscode');
const assert = require('node:assert/strict');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs');
const exec = promisify(execFile);

exports.run = async () => {
  const report = path.resolve(__dirname, '../../runlogs/editor-integration.json');
  fs.writeFileSync(report, JSON.stringify({ status: 'running' }));
  await vscode.extensions.getExtension('simpledocs.simpledocs-context').activate();
  const document = await vscode.workspace.openTextDocument({ language: 'python',
    content: '# NEIGHBOR_CONTEXT\nstudent1.check_result()\nprint("Student Failed")\n' });
  const editor = await vscode.window.showTextDocument(document);
  const runner = path.resolve(__dirname, '../../build/SimpleDocs.Tests/bin/Release/net8.0-windows10.0.19041.0/SimpleDocs.Tests.exe');
  try {
    // The desktop worker must read fresh selections from a real editor, including repeats.
    for (const line of [1, 2, 1]) {
      editor.selection = new vscode.Selection(line, 0, line, document.lineAt(line).text.length);
      await vscode.commands.executeCommand('workbench.action.focusActiveEditorGroup');
      for (let wait = 0; !vscode.window.state.focused && wait < 150; wait++)
        await new Promise(resolve => setTimeout(resolve, 200));
      assert.ok(vscode.window.state.focused, 'test editor must be in foreground');
      await new Promise(resolve => setTimeout(resolve, 250));
      const original = editor.selection;
      const { stdout } = await exec(runner, ['--live-editor', document.lineAt(line).text, process.env.VSCODE_PID],
        { windowsHide: true, timeout: 15000 });
      console.log(stdout);
      fs.appendFileSync(path.resolve(__dirname, '../../runlogs/editor-integration.log'), stdout);
      assert.ok(editor.selection.isEqual(original), 'capture must preserve editor selection');
    }
    fs.writeFileSync(report, JSON.stringify({ status: 'passed', selections: 3 }));
  } catch (error) {
    fs.writeFileSync(report, JSON.stringify({ status: 'failed', message: error.message }));
    throw error;
  } finally {
    await vscode.commands.executeCommand('workbench.action.revertAndCloseActiveEditor');
  }
};

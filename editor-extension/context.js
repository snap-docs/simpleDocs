const path = require('node:path');

function captureEditorContext(vscode, expectedSelection) {
  const editor = vscode.window.activeTextEditor;
  if (!vscode.window.state.focused || !editor || editor.selections.length !== 1
      || editor.selection.isEmpty) return null;
  if (expectedSelection != null && (typeof expectedSelection !== 'string' || !expectedSelection.trim())) return null;
  const document = editor.document;
  const selectedStart = document.offsetAt(editor.selection.start);
  const selectedEnd = document.offsetAt(editor.selection.end);
  if (selectedEnd - selectedStart > 5000) return null;
  const selected = document.getText(editor.selection);
  if (!selected.trim()) return null;
  if (expectedSelection != null && selected.replace(/\r\n/g, '\n').trim() !== expectedSelection.replace(/\r\n/g, '\n').trim()) return null;

  // Read a bounded range around the selection, including unsaved edits; never load other files.
  const budget = 10000;
  const before = Math.floor((budget - selected.length) / 2);
  const range = new vscode.Range(document.positionAt(Math.max(0, selectedStart - before)),
    document.positionAt(selectedEnd + budget - selected.length - before));
  return {
    selected_text: selected,
    background_context: document.getText(range),
    document_name: path.basename(document.fileName),
    language: document.languageId,
    version: document.version,
    is_dirty: document.isDirty
  };
}

module.exports = { captureEditorContext };

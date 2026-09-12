const { test } = require('node:test');
const assert = require('node:assert/strict');
const { explainSelection } = require('../explain');
const { sendToDesktop } = require('../desktop');
const os = require('node:os');
const path = require('node:path');

function editorApi() {
  const source = '# unsaved context\nfirst\nsecond\n';
  const selection = { start: 18, end: 23, isEmpty: false };
  return {
    env: { appName: 'Visual Studio Code' },
    Range: class { constructor(start, end) { this.start = start; this.end = end; } },
    window: { state: { focused: true }, activeTextEditor: {
      selection, selections: [selection], document: {
        getText: range => source.slice(range.start, range.end),
        offsetAt: offset => offset, positionAt: offset => Math.min(source.length, offset),
        fileName: 'unsaved.py', languageId: 'python', version: 3, isDirty: true
      }
    } }
  };
}

test('direct command snapshots fresh editor selection before asynchronous transport', async () => {
  const api = editorApi();
  const sent = [];
  const send = async snapshot => { sent.push(snapshot); return true; };
  const first = explainSelection(api, send);
  api.window.activeTextEditor.selection = { start: 24, end: 30, isEmpty: false };
  await first;
  await explainSelection(api, send);
  assert.deepEqual(sent.map(item => item.selected_text), ['first', 'second']);
  assert.ok(sent.every(item => item.background_context.includes('# unsaved context')));
  assert.ok(sent.every(item => item.editor === 'code'));
  api.env.appName = 'Cursor';
  await explainSelection(api, send);
  assert.equal(sent[2].editor, 'cursor');
});

test('direct command rejects missing selection without contacting desktop', async () => {
  const api = editorApi();
  api.window.activeTextEditor.selection.isEmpty = true;
  await assert.rejects(explainSelection(api, () => assert.fail('must not send')), /Select one region/);
});

test('missing desktop gives an actionable message', async () => {
  await assert.rejects(sendToDesktop({}, path.join(os.tmpdir(), 'missing-simpledocs-' + Date.now())), /Start the updated/);
});

const { test } = require('node:test');
const assert = require('node:assert/strict');
const { captureEditorContext } = require('../context');

function fixture(source, start, length) {
  const selection = { start, end: start + length, isEmpty: length === 0 };
  return {
    Range: class { constructor(start, end) { this.start = start; this.end = end; } },
    window: {
      state: { focused: true },
      activeTextEditor: { selection, selections: [selection], document: {
        getText: range => source.slice(range.start, range.end),
        offsetAt: offset => offset,
        positionAt: offset => Math.min(source.length, offset),
        fileName: 'unsaved.js', languageId: 'javascript', version: 7, isDirty: true
      } }
    }
  };
}

test('bounded context near end of a large unsaved buffer includes selection and neighbors', () => {
  const source = 'x'.repeat(100000) + 'before\nSELECTED\nafter' + 'y'.repeat(10000);
  const result = captureEditorContext(fixture(source, 100007, 8), 'SELECTED');
  assert.equal(result.background_context.length, 10000);
  assert.ok(result.background_context.includes('before\nSELECTED\nafter'));
  assert.equal(result.is_dirty, true);
  assert.equal(result.version, 7);
});
test('reject stale, empty, multiple or unfocused selections', () => {
  const api = fixture('SELECTED', 0, 8);
  assert.equal(captureEditorContext(api, 'OTHER'), null);
  assert.equal(captureEditorContext(api, ''), null);
  api.window.state.focused = false;
  assert.equal(captureEditorContext(api, 'SELECTED'), null);
  api.window.state.focused = true;
  api.window.activeTextEditor.selections.push({});
  assert.equal(captureEditorContext(api, 'SELECTED'), null);
});
test('CRLF selections match normalized native clipboard text', () => {
  const source = 'first\r\nsecond';
  assert.ok(captureEditorContext(fixture(source, 0, source.length), 'first\nsecond'));
});
test('oversized selection does not read an unbounded document', () => {
  const api = fixture('x'.repeat(6000), 0, 6000);
  api.window.activeTextEditor.document.getText = () => { throw new Error('unexpected read'); };
  assert.equal(captureEditorContext(api, 'x'), null);
});

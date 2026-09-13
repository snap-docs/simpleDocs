import assert from 'node:assert/strict';
import test from 'node:test';
import { buildPrompt } from '../src/services/promptEngine.js';
import { sanitizeSelectedText } from '../src/utils/textSanitizer.js';

test('IDE code prompts request concise but concrete explanations', () => {
  const { systemPrompt, userPrompt } = buildPrompt(
    1,
    'return students.filter(student => student.active);',
    'function activeStudents(students) { return students.filter(student => student.active); }',
    'students.js',
    'Code',
    'ide_editor'
  );

  assert.match(systemPrompt, /Start with one clear purpose line/);
  assert.match(systemPrompt, /data movement, state change, control flow, return value, or side effect/);
  assert.match(systemPrompt, /Return 4 short lines in most cases/);
  assert.match(systemPrompt, /purpose, mechanism, local effect/);
  assert.match(userPrompt, /SELECTED TEXT/);
  assert.match(userPrompt, /student\.active/);
});

test('richer prompts preserve the error no-solution boundary', () => {
  const { systemPrompt } = buildPrompt(
    2,
    'ReferenceError: total is not defined',
    '',
    'console',
    'Code',
    'ide_embedded_terminal'
  );

  assert.match(systemPrompt, /NEVER provide the corrected code/);
  assert.match(systemPrompt, /one small directional hint/);
  assert.match(systemPrompt, /Return 4 short lines in most cases/);
});

test('large equations remain intact up to the shared selected-text limit', () => {
  const equation = `\\int_0^1 ${'x+'.repeat(5500)} 0\\,dx`;
  assert.equal(sanitizeSelectedText(equation).length, equation.length);
  assert.equal(sanitizeSelectedText('x'.repeat(13000)).length, 12000);
  assert.match(buildPrompt(4, equation, '', 'Calculus', 'msedge', 'browser_chromium').userPrompt,
    new RegExp(`${'x\\+'.repeat(20)}`));
});

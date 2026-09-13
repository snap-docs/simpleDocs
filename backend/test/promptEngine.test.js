import assert from 'node:assert/strict';
import test from 'node:test';
import { buildPrompt } from '../src/services/promptEngine.js';

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

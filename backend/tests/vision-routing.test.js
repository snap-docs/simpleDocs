import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyRequest } from '../src/services/classifier.js';
import { buildVisionPrompt } from '../src/services/promptEngine.js';

const scenarios = [
  {
    name: 'DaVinci Resolve timeline selection routes as vision augmented code-like context',
    payload: {
      selectedText: 'Cross Dissolve 1.5s',
      backgroundContext: 'Timeline with clips and transitions around the selected edit point.',
      hasVision: true
    },
    expectedCase: 4
  },
  {
    name: 'Google Docs paragraph selection keeps text context and vision',
    payload: {
      selectedText: 'The migration will complete after the redirect callback returns.',
      backgroundContext: 'This paragraph sits inside a larger design document.',
      hasVision: true
    },
    expectedCase: 4
  },
  {
    name: 'Figma layer selection can fall back to OCR when selected text is unavailable',
    payload: {
      selectedText: '',
      ocrText: 'Layer list: Hero CTA Button, card shadow, 8px radius',
      backgroundContext: 'Design canvas with a marketing layout.',
      hasVision: true
    },
    expectedCase: 4
  },
  {
    name: 'Image in browser without extractable text becomes vision-only case 5',
    payload: {
      selectedText: '',
      ocrText: '',
      backgroundContext: '',
      hasVision: true
    },
    expectedCase: 5
  },
  {
    name: 'PDF in browser can classify from OCR text when selected text is missing',
    payload: {
      selectedText: '',
      ocrText: 'Section 4.2 API quotas and request limits',
      backgroundContext: 'Rendered PDF page with a two-column layout.',
      hasVision: true
    },
    expectedCase: 4
  },
  {
    name: 'Existing IDE selections still stay on the text-first code path',
    payload: {
      selectedText: 'const response = await fetch(url);',
      backgroundContext: 'async function loadData() { const response = await fetch(url); return response.json(); }',
      hasVision: false
    },
    expectedCase: 1
  }
];

test('vision routing scenarios classify into the expected cases', () => {
  for (const scenario of scenarios) {
    const result = classifyRequest(scenario.payload);
    assert.equal(result.caseType, scenario.expectedCase, scenario.name);
  }
});

test('vision prompt includes selected text, OCR text, and layer guidance', () => {
  const { systemPrompt, userPrompt } = buildVisionPrompt({
    selectedText: 'Cross Dissolve 1.5s',
    backgroundContext: 'Timeline panel around the selected transition.',
    ocrText: 'Inspector: duration 1.5s',
    processName: 'Resolve',
    windowTitle: 'DaVinci Resolve Studio',
    cursorPosition: { x: 920, y: 640 },
    captureMethodExtended: 'vision_augmented'
  });

  assert.match(systemPrompt, /Layer 1/i);
  assert.match(userPrompt, /Cross Dissolve 1\.5s/);
  assert.match(userPrompt, /Inspector: duration 1\.5s/);
  assert.match(userPrompt, /\(920, 640\)/);
});

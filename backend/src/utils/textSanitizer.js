function sanitizeText(input, maxChars = 10000) {
  const raw = typeof input === 'string' ? input : '';
  if (raw.length === 0) {
    return '';
  }

  const cleaned = raw
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .replace(/\uFFFC/g, '')
    .replace(/\uFFFD/g, '')
    .replace(/[\p{Co}\p{Cs}]/gu, '')
    .replace(/[\u200B-\u200D\uFEFF]/g, '')
    .replace(/[\x00-\x08\x0B\x0E-\x1F\x7F]/g, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();

  if (cleaned.length <= maxChars) {
    return cleaned;
  }

  return cleaned.substring(0, maxChars);
}

export function sanitizeSelectedText(input, maxChars = 5000) {
  return sanitizeText(input, maxChars);
}

export function sanitizeBackgroundText(input, maxChars = 12000) {
  return sanitizeText(input, maxChars);
}

export function sanitizeMetadataText(input, maxChars = 400) {
  return sanitizeText(input, maxChars);
}

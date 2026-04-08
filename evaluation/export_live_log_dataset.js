import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEFAULT_INPUT = path.resolve(__dirname, '..', 'runlogs', 'client_live.log');
const DEFAULT_RESULTS_DIR = path.resolve(__dirname, 'results');

function parseArgs(argv) {
  const out = {
    input: DEFAULT_INPUT,
    output: null,
    runId: null,
    includeIncomplete: false,
    format: null,
    latestRunOnly: false,
    modelName: null
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--input' && argv[i + 1]) {
      out.input = path.resolve(process.cwd(), argv[++i]);
    } else if (arg === '--output' && argv[i + 1]) {
      out.output = path.resolve(process.cwd(), argv[++i]);
    } else if (arg === '--run-id' && argv[i + 1]) {
      out.runId = argv[++i];
    } else if (arg === '--include-incomplete') {
      out.includeIncomplete = true;
    } else if (arg === '--format' && argv[i + 1]) {
      out.format = argv[++i];
    } else if (arg === '--latest-run-only') {
      out.latestRunOnly = true;
    } else if (arg === '--model-name' && argv[i + 1]) {
      out.modelName = argv[++i];
    }
  }

  return out;
}

function nowStamp() {
  const d = new Date();
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  const hh = String(d.getHours()).padStart(2, '0');
  const mi = String(d.getMinutes()).padStart(2, '0');
  const ss = String(d.getSeconds()).padStart(2, '0');
  return `${yyyy}${mm}${dd}_${hh}${mi}${ss}`;
}

function parseTimestamp(line) {
  const m = line.match(/^\[([^\]]+)\]/);
  return m ? m[1] : null;
}

function parseReqId(line) {
  const m = line.match(/\breq=(\d+)\b/);
  return m ? Number.parseInt(m[1], 10) : null;
}

function parseKeyValue(line, key) {
  const m = line.match(new RegExp(`\\b${key}=([^\\s]+)`));
  return m ? m[1] : null;
}

function parseQuotedStatus(line) {
  const m = line.match(/\bstatus="([^"]*)"/);
  return m ? m[1] : null;
}

function parseBool(raw) {
  if (!raw) return null;
  const lower = raw.toLowerCase();
  if (lower === 'true') return true;
  if (lower === 'false') return false;
  return null;
}

function parseIntValue(raw) {
  if (!raw) return null;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : null;
}

function normalizePreviewText(value) {
  if (value == null) return null;
  const trimmed = value.trim();
  if (!trimmed || trimmed === '<empty>') return null;
  return trimmed;
}

function pickLonger(existing, candidate) {
  if (!candidate) return existing;
  if (!existing) return candidate;
  if (candidate.length > existing.length) return candidate;
  return existing;
}

function createBlock(reqId, timestamp, seqNo) {
  return {
    reqId,
    sequence: seqNo,
    timestamp,
    environmentType: null,
    windowTitle: null,
    processName: null,
    selectedMethod: null,
    backgroundMethod: null,
    selectedText: null,
    backgroundText: null,
    selectedTextIsPreview: false,
    backgroundTextIsPreview: false,
    selectedChars: null,
    backgroundChars: null,
    isPartial: null,
    isUnsupported: null,
    statusMessage: null,
    latencyMs: null,
    modelName: null,
    response: null,
    category: null,
    streamCompleted: false,
    backendFailure: false,
    captureFailureSignal: false,
    finished: false
  };
}

function ensureBlockForLine(line, reqId, activeByReq, blocks, seqRef) {
  const isTrigger = line.includes('stage=hotkey_triggered');

  if (isTrigger) {
    const timestamp = parseTimestamp(line);
    const block = createBlock(reqId, timestamp, ++seqRef.value);
    const existing = activeByReq.get(reqId);
    if (existing) {
      existing.finished = true;
    }
    activeByReq.set(reqId, block);
    blocks.push(block);
    return block;
  }

  let block = activeByReq.get(reqId);
  if (!block) {
    block = createBlock(reqId, parseTimestamp(line), ++seqRef.value);
    blocks.push(block);
    activeByReq.set(reqId, block);
  }
  return block;
}

function updateBlockFromLine(block, line) {
  if (!block.timestamp) {
    block.timestamp = parseTimestamp(line);
  }

  const processTitleMatch = line.match(/\bprocess=(\S+)\s+title="([^"]*)"/);
  if (processTitleMatch) {
    block.processName = processTitleMatch[1];
    block.windowTitle = processTitleMatch[2];
  }

  const env = parseKeyValue(line, 'env') ?? parseKeyValue(line, 'environment');
  if (env) block.environmentType = env;

  const selectedMethod =
    parseKeyValue(line, 'selected_method') ??
    parseKeyValue(line, 'method');
  if (selectedMethod && selectedMethod !== 'fallback=True' && selectedMethod !== 'fallback=False') {
    block.selectedMethod = selectedMethod;
  }

  const backgroundMethod = parseKeyValue(line, 'background_method');
  if (backgroundMethod) block.backgroundMethod = backgroundMethod;

  const selectedChars =
    parseIntValue(parseKeyValue(line, 'selected_chars')) ??
    parseIntValue(parseKeyValue(line, 'selected'));
  if (selectedChars != null) block.selectedChars = selectedChars;

  const backgroundChars =
    parseIntValue(parseKeyValue(line, 'background_chars')) ??
    parseIntValue(parseKeyValue(line, 'visible_chars')) ??
    parseIntValue(parseKeyValue(line, 'visible')) ??
    parseIntValue(parseKeyValue(line, 'full_chars')) ??
    parseIntValue(parseKeyValue(line, 'full'));
  if (backgroundChars != null) block.backgroundChars = backgroundChars;

  const isPartial =
    parseBool(parseKeyValue(line, 'is_partial')) ??
    parseBool(parseKeyValue(line, 'partial'));
  if (isPartial != null) block.isPartial = isPartial;

  const isUnsupported =
    parseBool(parseKeyValue(line, 'is_unsupported')) ??
    parseBool(parseKeyValue(line, 'unsupported'));
  if (isUnsupported != null) block.isUnsupported = isUnsupported;

  const statusFromQuoted = parseQuotedStatus(line);
  if (statusFromQuoted) {
    block.statusMessage = pickLonger(block.statusMessage, statusFromQuoted);
  }

  const selectedPreview = line.match(/Selected preview:\s*(.*)$/);
  if (selectedPreview) {
    const text = normalizePreviewText(selectedPreview[1]);
    if (text) {
      block.selectedText = text;
      block.selectedTextIsPreview = true;
    }
  }

  const backgroundPreview = line.match(/Background preview:\s*(.*)$/);
  if (backgroundPreview) {
    const text = normalizePreviewText(backgroundPreview[1]);
    if (text) {
      block.backgroundText = text;
      block.backgroundTextIsPreview = true;
    }
  }

  const metaLabel = line.match(/stage=meta label="([^"]+)"/);
  if (metaLabel) {
    const label = metaLabel[1];
    const firstPipe = label.split('|')[0]?.trim();
    if (firstPipe && !block.environmentType) {
      block.environmentType = firstPipe;
    }
  }

  const streamFinished = line.match(/stage=stream_finished.*duration_ms=(\d+)/);
  if (streamFinished) {
    block.latencyMs = Number.parseInt(streamFinished[1], 10);
    block.streamCompleted = true;
  }

  const hotkeyFinished = line.match(/stage=hotkey_finished.*duration_ms=(\d+)/);
  if (hotkeyFinished && block.latencyMs == null) {
    block.latencyMs = Number.parseInt(hotkeyFinished[1], 10);
  }
  if (hotkeyFinished) {
    block.finished = true;
  }

  const caseMatch = line.match(/\bCase\s+([1-4])\b/i);
  if (caseMatch) {
    block.category = `case_${caseMatch[1]}`;
  }

  const modelMatch = line.match(/\bmodel(?:_used|_name)?=([^\s]+)/i);
  if (modelMatch) {
    block.modelName = modelMatch[1];
  }

  const streamError = line.match(/stage=stream_error.*message=(.+)$/);
  if (streamError) {
    block.backendFailure = true;
    block.statusMessage = streamError[1].trim();
  }

  const genericBackendError = line.match(/\bstage=error\b.*message=(.+)$/);
  if (genericBackendError) {
    block.backendFailure = true;
    block.statusMessage = genericBackendError[1].trim();
  }

  if (line.includes('Connection error')) {
    block.backendFailure = true;
    block.statusMessage = pickLonger(block.statusMessage, 'Connection error');
  }

  if (line.includes('No selected text was captured')) {
    block.captureFailureSignal = true;
    block.statusMessage = pickLonger(
      block.statusMessage,
      'No selected text was captured from this application.'
    );
  }

  if (line.includes('is_unsupported=True') || line.includes('unsupported=True')) {
    block.captureFailureSignal = true;
  }
}

function deriveStatus(block) {
  if (block.backendFailure) {
    return 'backend_failure';
  }

  if (block.isUnsupported === true || block.captureFailureSignal) {
    return 'capture_failure';
  }

  if (block.selectedChars === 0) {
    return 'capture_failure';
  }

  if (block.isPartial === true) {
    return 'partial';
  }

  if (block.streamCompleted || block.latencyMs != null) {
    return 'success';
  }

  return 'partial';
}

function isUsable(block) {
  return (
    block.selectedChars != null ||
    block.backgroundChars != null ||
    block.selectedText != null ||
    block.backgroundText != null ||
    block.environmentType != null ||
    block.selectedMethod != null ||
    block.backgroundMethod != null ||
    block.windowTitle != null ||
    block.processName != null ||
    block.statusMessage != null ||
    block.latencyMs != null ||
    block.isPartial != null ||
    block.isUnsupported != null
  );
}

function toRecord(block, runId, fallbackModelName = null) {
  const status = deriveStatus(block);
  return {
    run_id: runId,
    case_id: `${runId}-req-${block.reqId}-seq-${block.sequence}`,
    source: 'live_log',
    source_request_id: block.reqId,
    timestamp: block.timestamp ?? null,
    category: block.category ?? null,
    environment_type: block.environmentType ?? null,
    window_title: block.windowTitle ?? null,
    process_name: block.processName ?? null,
    selected_method: block.selectedMethod ?? null,
    background_method: block.backgroundMethod ?? null,
    selected_text: block.selectedText ?? null,
    background_context: block.backgroundText ?? null,
    selected_text_is_preview: Boolean(block.selectedTextIsPreview && block.selectedText),
    background_context_is_preview: Boolean(block.backgroundTextIsPreview && block.backgroundText),
    selected_chars: block.selectedChars ?? null,
    background_chars: block.backgroundChars ?? null,
    is_partial: block.isPartial ?? null,
    is_unsupported: block.isUnsupported ?? null,
    status_message: block.statusMessage ?? null,
    latency_ms: block.latencyMs ?? null,
    model_name: block.modelName ?? fallbackModelName ?? null,
    response: block.response ?? null,
    status
  };
}

function resolveFormat(outputPath, explicitFormat) {
  if (explicitFormat === 'json' || explicitFormat === 'jsonl') {
    return explicitFormat;
  }

  const ext = path.extname(outputPath).toLowerCase();
  if (ext === '.json') return 'json';
  return 'jsonl';
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const runId = args.runId || `live_export_${nowStamp()}`;
  const outputPath =
    args.output ||
    path.join(DEFAULT_RESULTS_DIR, `live_eval_dataset_${runId}.jsonl`);
  const format = resolveFormat(outputPath, args.format);

  if (!fs.existsSync(args.input)) {
    console.error(`Input log file not found: ${args.input}`);
    process.exit(1);
  }

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });

  const raw = fs.readFileSync(args.input, 'utf-8');
  const lines = raw.split(/\r?\n/);

  const activeByReq = new Map();
  const blocks = [];
  const seqRef = { value: 0 };

  for (const line of lines) {
    if (!line || !line.trim()) continue;
    const reqId = parseReqId(line);
    if (reqId == null) continue;

    const block = ensureBlockForLine(line, reqId, activeByReq, blocks, seqRef);
    updateBlockFromLine(block, line);

    if (block.finished) {
      activeByReq.delete(reqId);
    }
  }

  let scopedBlocks = blocks;
  if (args.latestRunOnly) {
    const lastReq1Index = blocks.map((b, i) => ({ b, i })).filter((x) => x.b.reqId === 1).map((x) => x.i).pop();
    if (lastReq1Index != null) {
      scopedBlocks = blocks.slice(lastReq1Index);
    }
  }

  const usableBlocks = scopedBlocks.filter((b) => args.includeIncomplete || isUsable(b));
  const records = usableBlocks.map((b) => toRecord(b, runId, args.modelName));

  if (format === 'json') {
    fs.writeFileSync(outputPath, JSON.stringify(records, null, 2) + '\n', 'utf-8');
  } else {
    const jsonl = records.map((r) => JSON.stringify(r)).join('\n');
    fs.writeFileSync(outputPath, jsonl + (records.length > 0 ? '\n' : ''), 'utf-8');
  }

  const counts = records.reduce(
    (acc, r) => {
      acc.total += 1;
      acc[r.status] = (acc[r.status] || 0) + 1;
      return acc;
    },
    { total: 0, success: 0, partial: 0, capture_failure: 0, backend_failure: 0 }
  );

  console.log(`Live log export complete.`);
  console.log(`Input:  ${args.input}`);
  console.log(`Output: ${outputPath}`);
  console.log(`Format: ${format}`);
  console.log(`Run ID: ${runId}`);
  console.log(`Latest run only: ${args.latestRunOnly ? 'yes' : 'no'}`);
  console.log(`Records: ${counts.total}`);
  console.log(
    `Status breakdown: success=${counts.success}, partial=${counts.partial}, capture_failure=${counts.capture_failure}, backend_failure=${counts.backend_failure}`
  );
}

main();

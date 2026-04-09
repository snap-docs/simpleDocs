# Evaluation Module Overview & Performance Report

## 📚 Overview of the Evaluation Framework

The **evaluation** module is an isolated lab workspace used to test the backend's generation logic without needing to manually capture text in the C# UI. 

### Key Components

| Component | Purpose | Details |
|-----------|---------|---------|
| `all_cases.json` | The test-case bank. | Stores "Question" scenarios (selected text, background, metadata). |
| `eval_runner.js` | The test executor. | Connects to the backend WebSocket, streams cases, and logs results. |
| `augment_all_cases.js` | The synchronizer. | Merges generated results (responses, latency) back into the input file. |
| `export_live_logs.js` | The extractor. | Pulls raw telemetry data from `client_live.log` into structured JSONL. |
| `results/` | Output directory. | Contains `eval_results.jsonl` (raw results) and live log exports. |

---

## 📊 Latest Performance Benchmarks

Based on an analysis of **261 capture events**, the system exhibits the following performance characteristics:

### 🚀 Overall Vital Signs
- **Success Rate:** 77.4%
- **Avg Capture Latency:** 1,751ms
- **Partial Captures:** 8.8%
- **Total Failures:** 13.8% (Targeting <10% in Prototype 5)

### 🖥️ Performance by Environment

| Environment Type | Success Rate | Avg Latency | Avg Selected | Avg Background |
|------------------|--------------|-------------|--------------|----------------|
| **Chromium Browser** | 95.5% | 1,746ms | 75 chars | 1,287 chars |
| **Terminal (Modern)** | 86.7% | 1,173ms | 45 chars | 2,945 chars |
| **IDE Editor** | 80.5% | 2,033ms | 129 chars | 1,650 chars |
| **IDE Terminal** | 85.2% | 1,279ms | 44 chars | 1,547 chars |
| **Electron Apps** | 100.0% | 1,905ms | 364 chars | 344 chars |

---

## 🛠️ Operational Guide

These scripts are available in the `evaluation/` directory:

1. **`npm run eval`**:
   Runs the test suite against the backend. Outputs results to `results/eval_results.jsonl`.
2. **`npm run export:live`**:
   Extracts telemetry from `runlogs/client_live.log`.
3. **`npm run augment-cases`**:
   Merges the results from `eval_results.jsonl` back into `all_cases.json` to populate missing fields (response text, model name, real latency).

---

## 📝 Ongoing Improvement Plan

1. **OCR Reliability Enhancement**:
   Currently, browser captures occasionally echo back the selected text as the background. We are forced to trigger the OCR engine in these cases (adding ~300ms overhead). 
2. **Context Compression**:
   Investigating "Semantic Slicing" for background context. Instead of sending 3,000 characters of raw text, we will eventually send only "high-signal" blocks (definitions, active function scopes).
3. **Automated Merging**:
   Standardize the `augment-cases` workflow so that every test run automatically updates the source JSON, building a long-term "Ground Truth" dataset for accuracy testing.

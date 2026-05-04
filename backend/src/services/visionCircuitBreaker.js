const DEFAULT_THRESHOLD = 3;
const DEFAULT_COOLDOWN_MS = 5 * 60 * 1000;

let consecutiveFailures = 0;
let disabledUntil = 0;

function threshold() {
  const parsed = Number.parseInt(process.env.VISION_CIRCUIT_BREAKER_THRESHOLD || '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_THRESHOLD;
}

function cooldownMs() {
  const parsed = Number.parseInt(process.env.VISION_CIRCUIT_BREAKER_COOLDOWN_MS || '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_COOLDOWN_MS;
}

export function canUseVision() {
  return Date.now() >= disabledUntil;
}

export function recordVisionSuccess() {
  consecutiveFailures = 0;
  disabledUntil = 0;
}

export function recordVisionFailure() {
  consecutiveFailures += 1;
  if (consecutiveFailures >= threshold()) {
    disabledUntil = Date.now() + cooldownMs();
    consecutiveFailures = 0;
  }
}

export function getVisionCircuitState() {
  return {
    enabled: canUseVision(),
    disabled_until: disabledUntil > 0 ? new Date(disabledUntil).toISOString() : null,
    cooldown_ms: cooldownMs(),
    threshold: threshold()
  };
}

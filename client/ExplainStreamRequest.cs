using CodeExplainer.ContextCapture.Vision;

namespace CodeExplainer
{
    internal sealed class ExplainStreamRequest
    {
        public string RequestId { get; init; } = string.Empty;
        public string UsageContext { get; init; } = string.Empty;
        public string? SelectedText { get; init; }
        public string BackgroundContext { get; init; } = string.Empty;
        public string? OcrText { get; init; }
        public string WindowTitle { get; init; } = string.Empty;
        public string ProcessName { get; init; } = string.Empty;
        public string EnvironmentType { get; init; } = "unknown";
        public string SelectedMethod { get; init; } = "unknown";
        public string BackgroundMethod { get; init; } = "unknown";
        public string CaptureMethodExtended { get; init; } = "text_only";
        public bool IsPartial { get; init; }
        public bool IsUnsupported { get; init; }
        public string? StatusMessage { get; init; }
        public bool OcrUsed { get; init; }
        public float OcrConfidence { get; init; }
        public VisionCaptureResult? Vision { get; init; }
        public CursorPositionPayload? CursorPosition { get; init; }
    }

    internal sealed class CursorPositionPayload
    {
        public int X { get; init; }
        public int Y { get; init; }
    }
}

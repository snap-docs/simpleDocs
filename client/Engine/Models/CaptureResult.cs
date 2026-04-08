namespace CodeExplainer.Engine.Models
{
    public class CaptureResult
    {
        public string SelectedText { get; }
        public string BackgroundContext { get; }
        public string WindowTitle { get; }
        public string ProcessName { get; }
        public EnvironmentType Type { get; }
        public CaptureMethod SelectedMethod { get; }
        public CaptureMethod BackgroundMethod { get; }
        public bool IsPartial { get; }
        public bool IsUnsupported { get; }
        public string StatusMessage { get; }
        /// <summary>True when OCR was used for either selected text or background context.</summary>
        public bool OcrUsed { get; }
        /// <summary>0.0 when OcrUsed is false; composite OCR confidence 0.0–1.0 otherwise.</summary>
        public float OcrConfidence { get; }

        public bool HasSelectedText => !string.IsNullOrWhiteSpace(SelectedText);

        public CaptureResult(
            string selectedText,
            string backgroundContext,
            string windowTitle,
            string processName,
            EnvironmentType type,
            CaptureMethod selectedMethod,
            CaptureMethod backgroundMethod,
            bool isPartial,
            bool isUnsupported,
            string statusMessage,
            bool ocrUsed = false,
            float ocrConfidence = 0f)
        {
            SelectedText = selectedText;
            BackgroundContext = backgroundContext;
            WindowTitle = windowTitle;
            ProcessName = processName;
            Type = type;
            SelectedMethod = selectedMethod;
            BackgroundMethod = backgroundMethod;
            IsPartial = isPartial;
            IsUnsupported = isUnsupported;
            StatusMessage = statusMessage;
            OcrUsed = ocrUsed;
            OcrConfidence = ocrConfidence;
        }

        public static CaptureResult Unsupported(
            ActiveWindowInfo window,
            EnvironmentType type,
            CaptureMethod selectedMethod,
            CaptureMethod backgroundMethod,
            string statusMessage,
            string? backgroundContext = null)
        {
            string safeBackground = string.IsNullOrWhiteSpace(backgroundContext)
                ? BuildMetadataContext(window)
                : backgroundContext;

            return new CaptureResult(
                selectedText: string.Empty,
                backgroundContext: safeBackground,
                windowTitle: window.Title,
                processName: window.ProcessName,
                type: type,
                selectedMethod: selectedMethod,
                backgroundMethod: backgroundMethod,
                isPartial: true,
                isUnsupported: true,
                statusMessage: statusMessage);
        }

        public static CaptureResult Partial(
            ActiveWindowInfo window,
            EnvironmentType type,
            string selectedText,
            string backgroundContext,
            CaptureMethod selectedMethod,
            CaptureMethod backgroundMethod,
            string statusMessage)
        {
            return new CaptureResult(
                selectedText: selectedText,
                backgroundContext: backgroundContext,
                windowTitle: window.Title,
                processName: window.ProcessName,
                type: type,
                selectedMethod: selectedMethod,
                backgroundMethod: backgroundMethod,
                isPartial: true,
                isUnsupported: false,
                statusMessage: statusMessage);
        }

        public static string BuildMetadataContext(ActiveWindowInfo window)
        {
            return $"WindowTitle: {window.Title}\nProcessName: {window.ProcessName}";
        }
    }
}

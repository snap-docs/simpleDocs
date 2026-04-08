using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public class ExternalAppStrategy : ICaptureStrategy
    {
        private readonly ClipboardCompatibilityMode _compatibilityMode;

        public ExternalAppStrategy(ClipboardCompatibilityMode compatibilityMode)
        {
            _compatibilityMode = compatibilityMode;
        }

        public async Task<CaptureResult> CaptureAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureSelectedTextAsync(
                window,
                _compatibilityMode,
                preferMsaaFirst: false);

            CapturePipelines.BackgroundCaptureOutcome background = CapturePipelines.CaptureUnknownBackground(window);

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Unsupported(
                    window,
                    EnvironmentType.External,
                    selected.Method,
                    background.Method,
                    status,
                    background.Text);
            }

            bool isPartial = background.IsMetadataFallback;
            string combinedStatus = $"{selected.Status} {background.Status}".Trim();

            return new CaptureResult(
                selectedText: selected.Text,
                backgroundContext: background.Text,
                windowTitle: window.Title,
                processName: window.ProcessName,
                type: EnvironmentType.External,
                selectedMethod: selected.Method,
                backgroundMethod: background.Method,
                isPartial: isPartial,
                isUnsupported: false,
                statusMessage: combinedStatus);
        }
    }
}

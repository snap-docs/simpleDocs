using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public class ElectronStrategy : ICaptureStrategy
    {
        private readonly ClipboardCompatibilityMode _compatibilityMode;

        public ElectronStrategy(ClipboardCompatibilityMode compatibilityMode)
        {
            _compatibilityMode = compatibilityMode;
        }

        public async Task<CaptureResult> CaptureAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureSelectedTextAsync(
                window,
                _compatibilityMode,
                preferMsaaFirst: false);

            CapturePipelines.BackgroundCaptureOutcome background = await CapturePipelines.CaptureBrowserContainerBackground(
                window,
                maxChars: 3000);

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Unsupported(
                    window,
                    EnvironmentType.Electron,
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
                type: EnvironmentType.Electron,
                selectedMethod: selected.Method,
                backgroundMethod: background.Method,
                isPartial: isPartial,
                isUnsupported: false,
                statusMessage: combinedStatus);
        }
    }
}

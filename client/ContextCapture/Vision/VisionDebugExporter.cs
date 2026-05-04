using System;
using System.IO;

namespace CodeExplainer.ContextCapture.Vision
{
    internal static class VisionDebugExporter
    {
        public static void Export(string rootDirectory, string requestId, VisionCaptureResult result)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || result == null || !result.HasAnyImage)
            {
                return;
            }

            string safeRequestId = requestId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string folder = Path.Combine(rootDirectory, $"{stamp}_{safeRequestId}");
            Directory.CreateDirectory(folder);

            WriteIfPresent(Path.Combine(folder, "layer1-cursor.png"), result.CursorRegionPng);
            WriteIfPresent(Path.Combine(folder, "layer2-panel.png"), result.ActivePanelPng);
            WriteIfPresent(Path.Combine(folder, "layer3-window.png"), result.FullWindowPng);
            File.WriteAllText(
                Path.Combine(folder, "metadata.txt"),
                $"process={result.ProcessName}{Environment.NewLine}" +
                $"window={result.WindowTitle}{Environment.NewLine}" +
                $"ocr_chars={result.ExtractedTextOcr?.Length ?? 0}{Environment.NewLine}" +
                $"cursor_bounds={result.CursorScreenBounds}");
        }

        private static void WriteIfPresent(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            File.WriteAllBytes(path, bytes);
        }
    }
}

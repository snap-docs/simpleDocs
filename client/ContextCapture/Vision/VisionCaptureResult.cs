using System.Windows;

namespace CodeExplainer.ContextCapture.Vision
{
    public sealed class VisionCaptureResult
    {
        public byte[] CursorRegionPng { get; set; } = [];
        public byte[] ActivePanelPng { get; set; } = [];
        public byte[] FullWindowPng { get; set; } = [];
        public string ExtractedTextOcr { get; set; } = string.Empty;
        public string SelectedTextUia { get; set; } = string.Empty;
        public string BackgroundContextUia { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public Rect CursorScreenBounds { get; set; }

        public bool HasAnyImage =>
            CursorRegionPng.Length > 0 ||
            ActivePanelPng.Length > 0 ||
            FullWindowPng.Length > 0;

        public int TotalImageBytes =>
            CursorRegionPng.Length +
            ActivePanelPng.Length +
            FullWindowPng.Length;
    }
}

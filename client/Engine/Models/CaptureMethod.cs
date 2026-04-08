namespace CodeExplainer.Engine.Models
{
    public enum CaptureMethod
    {
        None,
        UiaTextPatternSelection,
        UiaTextPatternDocumentRange,
        UiaTextPatternVisibleRanges,
        UiaTreeContainer,
        MsaaSelection,
        MsaaFocusedText,
        MsaaContainer,
        ClipboardCompatibility,
        ClipboardCompatTerminal,
        ConsoleBuffer,
        WindowMetadata,
        OcrVisualCapture,
        Unsupported
    }

    public static class CaptureMethodExtensions
    {
        public static string ToApiValue(this CaptureMethod method)
        {
            return method switch
            {
                CaptureMethod.None => "none",
                CaptureMethod.UiaTextPatternSelection => "uia_textpattern_selection",
                CaptureMethod.UiaTextPatternDocumentRange => "uia_textpattern_document_range",
                CaptureMethod.UiaTextPatternVisibleRanges => "uia_textpattern_visible_ranges",
                CaptureMethod.UiaTreeContainer => "uia_tree_container",
                CaptureMethod.MsaaSelection => "msaa_selection",
                CaptureMethod.MsaaFocusedText => "msaa_focused_text",
                CaptureMethod.MsaaContainer => "msaa_container",
                CaptureMethod.ClipboardCompatibility => "clipboard_compatibility",
                CaptureMethod.ClipboardCompatTerminal => "clipboard_compat_terminal",
                CaptureMethod.ConsoleBuffer => "console_buffer",
                CaptureMethod.WindowMetadata => "window_metadata",
                CaptureMethod.OcrVisualCapture => "ocr_visual_capture",
                CaptureMethod.Unsupported => "unsupported",
                _ => "unknown"
            };
        }
    }
}

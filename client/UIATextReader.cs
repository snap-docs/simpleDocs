using System;
using System.Windows.Automation;

namespace CodeExplainer
{
    /// <summary>
    /// Reads selected text and full document context from the focused UI element
    /// using UIAutomationClient. Silent — no clipboard interaction, no side effects.
    /// </summary>
    public static class UIATextReader
    {
        private const int MAX_CONTEXT_LENGTH = 10000;

        /// <summary>
        /// Gets the currently selected text from the focused element via UIA TextPattern.
        /// Returns null if UIA is not supported or no text is selected.
        /// </summary>
        public static string? GetSelectedText()
        {
            try
            {
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement == null)
                    return null;

                if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    var textPattern = (TextPattern)patternObj;
                    var selection = textPattern.GetSelection();

                    if (selection.Length > 0)
                    {
                        string text = selection[0].GetText(-1);
                        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIATextReader.GetSelectedText error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the full document content from the focused element via UIA TextPattern.DocumentRange.
        /// Returns null if UIA is not supported. Capped at MAX_CONTEXT_LENGTH characters.
        /// </summary>
        public static string? GetFullContext()
        {
            try
            {
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement == null)
                    return null;

                if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    var textPattern = (TextPattern)patternObj;
                    var documentRange = textPattern.DocumentRange;
                    string text = documentRange.GetText(MAX_CONTEXT_LENGTH);

                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIATextReader.GetFullContext error: {ex.Message}");
                return null;
            }
        }
    }
}

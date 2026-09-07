using System;

namespace CodeExplainer.Engine.Strategies
{
    internal static class ContextTextWindow
    {
        // Preserve the actual selection when fitting surrounding context into the payload budget.
        public static string AroundSelection(string text, string? selection, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return string.Empty;
            if (text.Length <= maxChars) return text;
            int anchor = string.IsNullOrEmpty(selection) ? -1 : text.IndexOf(selection, StringComparison.Ordinal);
            if (anchor < 0) return text.Substring(0, maxChars);
            int selectedLength = Math.Min(selection!.Length, maxChars);
            int start = Math.Clamp(anchor - (maxChars - selectedLength) / 2, 0, text.Length - maxChars);
            return text.Substring(start, maxChars);
        }

        public static bool AddsContext(string text, string? selection)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (string.IsNullOrWhiteSpace(selection)) return true;
            string compactText = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            string compactSelection = string.Join(" ", selection.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compactText.Length > 0 && !compactSelection.Contains(compactText, StringComparison.Ordinal);
        }
    }
}

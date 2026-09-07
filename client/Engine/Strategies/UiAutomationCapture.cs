using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Automation;
using System.Linq;
using System.Windows.Automation.Text;

namespace CodeExplainer.Engine.Strategies
{
    internal static class UiAutomationCapture
    {
        private const int DefaultAncestorDepth = 10;

        public static AutomationElement? GetFocusedElement()
        {
            try
            {
                var scope = CaptureScope.Current;
                return scope == null ? AutomationElement.FocusedElement : scope.IsValid ? scope.Focused : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsTerminalFocusedElement()
        {
            AutomationElement? focused = GetFocusedElement();
            if (focused == null)
            {
                return false;
            }

            foreach (var element in EnumerateFocusedAndAncestors(6))
            {
                try
                {
                    if (element.Current.ControlType == ControlType.Window) break;
                    if (ContainsTerminalHint(element.Current.ClassName)
                        || ContainsTerminalHint(element.Current.AutomationId)
                        || ContainsTerminalHint(element.Current.Name)) return true;
                }
                catch { }
            }
            return false;
        }

        public static bool IsEditorContentFocusedElement()
        {
            AutomationElement? focused = GetFocusedElement();
            if (focused == null)
            {
                return false;
            }

            foreach (AutomationElement element in EnumerateFocusedAndAncestors(8))
            {
                if (LooksLikeNonEditorPanel(element))
                {
                    return false;
                }
            }

            foreach (AutomationElement element in EnumerateFocusedAndAncestors(8))
            {
                if (LooksLikeEditorElement(element))
                {
                    return true;
                }
            }

            // In some Chromium-hosted editors, provider hints are sparse.
            // If no terminal/sidebar hints were found, treat focus as editor-like.
            return true;
        }

        public static bool TryGetSelectedText(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                if (TryGetSelectedTextFromElement(element, maxChars, out string selected))
                {
                    text = selected;
                    return true;
                }
            }

            // Some apps expose selection on a descendant node rather than the focused node.
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(8))
            {
                if (TryGetSelectedTextFromDescendants(element, maxChars, out string descendantSelected))
                {
                    text = descendantSelected;
                    return true;
                }
            }

            return false;
        }

        public static bool HasSelection()
        {
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                if (HasSelectionOnElement(element))
                {
                    return true;
                }
            }

            foreach (AutomationElement element in EnumerateFocusedAndAncestors(8))
            {
                if (HasSelectionOnDescendants(element))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetDocumentRangeText(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                if (TryGetDocumentRangeTextFromElement(element, maxChars, out string document))
                {
                    text = document;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetVisibleRangesText(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                if (TryGetVisibleRangesTextFromElement(element, maxChars, out string visible))
                {
                    text = visible;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetDocumentRangeTextFromDescendants(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(6))
            {
                if (TryGetTextPatternTextFromDescendants(
                    element,
                    maxChars,
                    TryGetDocumentRangeTextFromElement,
                    out string document))
                {
                    text = document;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetVisibleRangesTextFromDescendants(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(6))
            {
                if (TryGetTextPatternTextFromDescendants(
                    element,
                    maxChars,
                    TryGetVisibleRangesTextFromElement,
                    out string visible))
                {
                    text = visible;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetNearestContainerText(int maxChars, out string text)
        {
            text = string.Empty;
            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                string? candidate = TryGetContainerCandidateText(element, maxChars);
                if (!string.IsNullOrWhiteSpace(candidate)
                    && ContextTextWindow.AddsContext(candidate, CaptureScope.Current?.SelectionHint))
                {
                    text = candidate!;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetNeighborLinesViaTextRange(int maxChars, int linesUp, int linesDown, out string text)
        {
            text = string.Empty;

            foreach (AutomationElement element in EnumerateFocusedAndAncestors(DefaultAncestorDepth))
            {
                if (TryGetNeighborLinesViaTextRangeFromElement(element, maxChars, linesUp, linesDown, out string candidate))
                {
                    text = candidate;
                    return true;
                }
            }

            foreach (AutomationElement element in EnumerateFocusedAndAncestors(6))
            {
                if (TryGetNeighborLinesViaTextRangeFromDescendants(element, maxChars, linesUp, linesDown, out string candidate))
                {
                    text = candidate;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<AutomationElement> EnumerateFocusedAndAncestors(int maxDepth)
        {
            AutomationElement? current = GetFocusedElement();
            int depth = 0;

            while (current != null && depth <= maxDepth)
            {
                if (CaptureScope.Current?.IsValid == false) yield break;
                if (Automation.Compare(current, AutomationElement.RootElement)) yield break;
                yield return current;
                if (current.Current.NativeWindowHandle == CaptureScope.Current?.Window.Hwnd.ToInt64()
                    || current.Current.ControlType == ControlType.Window) yield break;
                current = TryGetParent(current);
                depth++;
            }
        }

        internal static bool TryGetSelectedTextFromElement(AutomationElement element, int maxChars, out string text)
        {
            text = string.Empty;
            try
            {
                if (element.Current.IsPassword || element.Current.IsOffscreen) return false;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) &&
                    patternObject is TextPattern textPattern)
                {
                    var selection = textPattern.GetSelection();
                    if (selection.Length == 0)
                    {
                        return false;
                    }

                    var builder = new StringBuilder();
                    foreach (var range in selection)
                    {
                        string? value = Normalize(range.GetText(maxChars), maxChars);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            if (builder.Length > 0)
                            {
                                builder.AppendLine();
                            }

                            builder.Append(value);
                            if (builder.Length >= maxChars)
                            {
                                break;
                            }
                        }
                    }

                    string? normalized = Normalize(builder.ToString(), maxChars);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        text = normalized!;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and keep probing ancestors.
            }

            return false;
        }

        internal static bool TryGetNeighborLinesViaTextRangeFromElement(
            AutomationElement element,
            int maxChars,
            int linesUp,
            int linesDown,
            out string text)
        {
            text = string.Empty;

            try
            {
                if (element.Current.IsPassword || element.Current.IsOffscreen) return false;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) &&
                    patternObject is TextPattern textPattern)
                {
                    var selection = textPattern.GetSelection();
                    if (selection.Length == 0)
                    {
                        return false;
                    }

                    foreach (var selectedRange in selection)
                    {
                        if (string.IsNullOrWhiteSpace(selectedRange.GetText(Math.Min(200, maxChars)))) continue;
                        var expanded = selectedRange.Clone();
                        // Move cloned endpoints independently; enclosing-unit expansion can drop a multiline selection.
                        expanded.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -Math.Max(1, maxChars / 3));
                        expanded.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, Math.Max(1, maxChars / 3));
                        string selectedValue = selectedRange.GetText(maxChars);
                        string candidateText = expanded.GetText(maxChars * 2);
                        text = ContextTextWindow.AroundSelection(candidateText, selectedValue, maxChars);
                        if (ContextTextWindow.AddsContext(text, selectedValue)) return true;

                    }
                }
            }
            catch
            {
                // Ignore and keep probing.
            }

            return false;
        }

        private static bool TryGetNeighborLinesViaTextRangeFromDescendants(
            AutomationElement root,
            int maxChars,
            int linesUp,
            int linesDown,
            out string text)
        {
            text = string.Empty;
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<(AutomationElement element, int depth)>();
            queue.Enqueue((root, 0));

            int inspected = 0;
            const int maxNodes = 500;
            const int maxDepth = 5;

            while (queue.Count > 0 && inspected < maxNodes && CaptureScope.Current?.IsValid != false)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                inspected++;

                if (depth > 0
                    && !LooksLikeNonEditorPanel(element)
                    && TryGetNeighborLinesViaTextRangeFromElement(element, maxChars, linesUp, linesDown, out string candidate))
                {
                    text = candidate;
                    return true;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                try
                {
                    AutomationElement? child = walker.GetFirstChild(element);
                    while (child != null && inspected + queue.Count < maxNodes)
                    {
                        queue.Enqueue((child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
                catch
                {
                    // Ignore traversal failures for individual nodes.
                }
            }

            return false;
        }

        private static bool TryGetSelectedTextFromDescendants(AutomationElement root, int maxChars, out string text)
        {
            text = string.Empty;
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<(AutomationElement element, int depth)>();
            queue.Enqueue((root, 0));

            int inspected = 0;
            const int maxNodes = 1200;
            const int maxDepth = 7;

            while (queue.Count > 0 && inspected < maxNodes && CaptureScope.Current?.IsValid != false)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                inspected++;

                if (depth > 0 && TryGetSelectedTextFromElement(element, maxChars, out string selected))
                {
                    text = selected;
                    return true;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                try
                {
                    AutomationElement? child = walker.GetFirstChild(element);
                    while (child != null && inspected + queue.Count < maxNodes)
                    {
                        queue.Enqueue((child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
                catch
                {
                    // Ignore traversal failures for individual nodes.
                }
            }

            return false;
        }

        private delegate bool TextPatternExtractor(AutomationElement element, int maxChars, out string text);

        private static bool TryGetTextPatternTextFromDescendants(
            AutomationElement root,
            int maxChars,
            TextPatternExtractor extractor,
            out string text)
        {
            text = string.Empty;
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<(AutomationElement element, int depth)>();
            queue.Enqueue((root, 0));

            int inspected = 0;
            const int maxNodes = 500;
            const int maxDepth = 5;

            while (queue.Count > 0 && inspected < maxNodes && CaptureScope.Current?.IsValid != false)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                inspected++;

                if (depth > 0 && !LooksLikeNonEditorPanel(element) && extractor(element, maxChars, out string candidate))
                {
                    text = candidate;
                    return true;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                try
                {
                    AutomationElement? child = walker.GetFirstChild(element);
                    while (child != null && inspected + queue.Count < maxNodes)
                    {
                        queue.Enqueue((child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
                catch
                {
                    // Ignore traversal failures for individual nodes.
                }
            }

            return false;
        }

        private static bool HasSelectionOnElement(AutomationElement element)
        {
            try
            {
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) &&
                    patternObject is TextPattern textPattern)
                {
                    var selection = textPattern.GetSelection();
                    if (selection.Length == 0)
                    {
                        return false;
                    }

                    foreach (var range in selection)
                    {
                        if (!string.IsNullOrWhiteSpace(Normalize(range.GetText(200), 200)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore and keep probing.
            }

            return false;
        }

        private static bool HasSelectionOnDescendants(AutomationElement root)
        {
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<(AutomationElement element, int depth)>();
            queue.Enqueue((root, 0));

            int inspected = 0;
            const int maxNodes = 1000;
            const int maxDepth = 7;

            while (queue.Count > 0 && inspected < maxNodes && CaptureScope.Current?.IsValid != false)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                inspected++;

                if (depth > 0 && HasSelectionOnElement(element))
                {
                    return true;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                try
                {
                    AutomationElement? child = walker.GetFirstChild(element);
                    while (child != null && inspected + queue.Count < maxNodes)
                    {
                        queue.Enqueue((child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
                catch
                {
                    // Ignore traversal failures for individual nodes.
                }
            }

            return false;
        }

        private static bool TryGetDocumentRangeTextFromElement(AutomationElement element, int maxChars, out string text)
        {
            text = string.Empty;
            try
            {
                if (element.Current.IsPassword || element.Current.IsOffscreen) return false;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) &&
                    patternObject is TextPattern textPattern)
                {
                    if (TryGetNeighborLinesViaTextRangeFromElement(element, maxChars, 30, 30, out text))
                        return true;
                    string? value = Normalize(textPattern.DocumentRange.GetText(maxChars), maxChars);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        text = value!;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and keep probing ancestors.
            }

            return false;
        }

        private static bool TryGetVisibleRangesTextFromElement(AutomationElement element, int maxChars, out string text)
        {
            text = string.Empty;
            try
            {
                if (element.Current.IsPassword || element.Current.IsOffscreen) return false;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) &&
                    patternObject is TextPattern textPattern)
                {
                    var ranges = textPattern.GetVisibleRanges();
                    if (ranges.Length == 0)
                    {
                        return false;
                    }

                    var builder = new StringBuilder();
                    foreach (var range in ranges)
                    {
                        string? value = Normalize(range.GetText(maxChars), maxChars);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            if (builder.Length > 0)
                            {
                                builder.AppendLine();
                            }

                            builder.Append(value);
                            if (builder.Length >= maxChars)
                            {
                                break;
                            }
                        }
                    }

                    string? normalized = Normalize(builder.ToString(), maxChars);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        text = normalized!;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and keep probing ancestors.
            }

            return false;
        }

        private static string? TryGetContainerCandidateText(AutomationElement element, int maxChars)
        {
            // Prefer actual text providers first.
            if (TryGetDocumentRangeTextFromElement(element, maxChars, out string documentText))
            {
                return documentText;
            }

            if (TryGetVisibleRangesTextFromElement(element, maxChars, out string visibleText))
            {
                return visibleText;
            }

            try
            {
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject) &&
                    valuePatternObject is ValuePattern valuePattern)
                {
                    string? value = Normalize(valuePattern.Current.Value, maxChars);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // Ignore and continue to name fallback.
            }

            return null;
        }

        private static AutomationElement? TryGetParent(AutomationElement element)
        {
            try
            {
                return TreeWalker.ControlViewWalker.GetParent(element);
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsTerminalHint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("xterm", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("console", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeNonEditorPanel(AutomationElement element)
        {
            string joined =
                $"{element.Current.Name} {element.Current.AutomationId} {element.Current.ClassName} {element.Current.HelpText}";

            if (element.Current.ControlType == ControlType.Window
                || element.Current.ControlType == ControlType.Document
                || element.Current.ControlType == ControlType.Edit) return false;
            if (ContainsTerminalHint(joined)) return true;

            return ContainsAnyHint(joined,
                "explorer",
                "search",
                "source control",
                "run and debug",
                "extensions",
                "outline",
                "panel",
                "problems",
                "debug console",
                "activity bar",
                "status bar",
                "ports");
        }

        private static bool LooksLikeEditorElement(AutomationElement element)
        {
            try
            {
                ControlType controlType = element.Current.ControlType;
                bool editorType =
                    controlType == ControlType.Document ||
                    controlType == ControlType.Edit ||
                    controlType == ControlType.Custom;

                bool hasTextProvider =
                    element.TryGetCurrentPattern(TextPattern.Pattern, out _) ||
                    element.TryGetCurrentPattern(ValuePattern.Pattern, out _);

                string joined =
                    $"{element.Current.Name} {element.Current.AutomationId} {element.Current.ClassName} {element.Current.HelpText}";
                bool classHint = ContainsAnyHint(joined, "editor", "monaco", "text");

                return editorType && (hasTextProvider || classHint) && !LooksLikeNonEditorPanel(element);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsAnyHint(string source, params string[] hints)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            foreach (string hint in hints)
            {
                if (source.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetFocusedContentBounds(out System.Windows.Rect bounds)
        {
            bounds = System.Windows.Rect.Empty;
            foreach (var element in EnumerateFocusedAndAncestors(6))
            {
                try
                {
                    if (element.Current.IsPassword) return false;
                    if (element.Current.ControlType == ControlType.Window) break;
                    var rect = element.Current.BoundingRectangle;
                    if (!element.Current.IsOffscreen && rect.Width >= 150 && rect.Height >= 100
                        && (element.Current.ControlType == ControlType.Document
                            || element.TryGetCurrentPattern(TextPattern.Pattern, out _)))
                    { bounds = rect; return true; }
                }
                catch { }
            }
            return false;
        }

        private static string? Normalize(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string normalized = text.Replace("\r\n", "\n").Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            if (normalized.Length > maxLength)
            {
                normalized = normalized.Substring(0, maxLength);
            }

            return normalized;
        }
    }
}

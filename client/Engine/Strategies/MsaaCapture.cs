using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace CodeExplainer.Engine.Strategies
{
    internal static class MsaaCapture
    {
        private const uint ObjIdClient = 0xFFFFFFFC;
        private const int ChildIdSelf = 0;
        private static readonly Guid IAccessibleGuid = new("618736E0-3C3D-11CF-810C-00AA00389B71");

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd,
            uint dwId,
            ref Guid riid,
            [In, Out, MarshalAs(UnmanagedType.Interface)] ref object ppvObject);

        public static bool TryGetSelectedText(IntPtr hwnd, int maxChars, out string text, out bool fromFocusedNode)
        {
            text = string.Empty;
            fromFocusedNode = false;

            if (TryGetExplicitSelectionText(hwnd, maxChars, out string explicitSelection))
            {
                text = explicitSelection;
                return true;
            }

            if (TryGetFocusedText(hwnd, maxChars, out string focusedText))
            {
                text = focusedText;
                fromFocusedNode = true;
                return true;
            }

            return false;
        }

        public static bool TryGetExplicitSelectionText(IntPtr hwnd, int maxChars, out string text)
        {
            text = string.Empty;

            if (!TryGetRootAccessible(hwnd, out IAccessible? root) || root == null)
            {
                return false;
            }

            if (TryGetTextFromSelection(root, maxChars, out string selectedText))
            {
                text = selectedText;
                return true;
            }

            return false;
        }

        public static bool TryGetFocusedText(IntPtr hwnd, int maxChars, out string text)
        {
            text = string.Empty;

            if (!TryGetRootAccessible(hwnd, out IAccessible? root) || root == null)
            {
                return false;
            }

            if (TryGetTextFromFocus(root, maxChars, out string focusedText))
            {
                text = focusedText;
                return true;
            }

            return false;
        }

        public static bool HasSelection(IntPtr hwnd)
        {
            if (!TryGetRootAccessible(hwnd, out IAccessible? root) || root == null)
            {
                return false;
            }

            try
            {
                object? selection = root.accSelection;
                if (selection == null)
                {
                    return false;
                }

                var builder = new StringBuilder();
                AppendVariantText(root, selection, builder, maxChars: 300, depth: 0);
                return !string.IsNullOrWhiteSpace(Normalize(builder.ToString(), 300));
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetContainerText(IntPtr hwnd, int maxChars, out string text)
        {
            text = string.Empty;

            if (!TryGetRootAccessible(hwnd, out IAccessible? root) || root == null)
            {
                return false;
            }

            // Prefer focus context first.
            if (TryGetFocusedAccessible(root, out IAccessible? focused) && focused != null)
            {
                if (TryExtractContainerTextFromNode(focused, maxChars, out string focusedContext))
                {
                    text = focusedContext;
                    return true;
                }
            }

            // Fall back to root container text.
            if (TryExtractContainerTextFromNode(root, maxChars, out string rootContext))
            {
                text = rootContext;
                return true;
            }

            return false;
        }

        private static bool TryGetRootAccessible(IntPtr hwnd, out IAccessible? root)
        {
            root = null;

            try
            {
                object accessibleObject = new object();
                Guid iid = IAccessibleGuid;
                int hr = AccessibleObjectFromWindow(hwnd, ObjIdClient, ref iid, ref accessibleObject);
                if (hr < 0 || accessibleObject is not IAccessible accessible)
                {
                    return false;
                }

                root = accessible;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetTextFromSelection(IAccessible root, int maxChars, out string text)
        {
            text = string.Empty;
            try
            {
                object? selection = root.accSelection;
                if (selection == null)
                {
                    return false;
                }

                var builder = new StringBuilder();
                AppendVariantText(root, selection, builder, maxChars, depth: 0);
                string normalized = Normalize(builder.ToString(), maxChars);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    text = normalized;
                    return true;
                }
            }
            catch
            {
                // Ignore and fall back.
            }

            return false;
        }

        private static bool TryGetTextFromFocus(IAccessible root, int maxChars, out string text)
        {
            text = string.Empty;
            try
            {
                object? focus = root.accFocus;
                if (focus == null)
                {
                    return false;
                }

                var builder = new StringBuilder();
                AppendVariantText(root, focus, builder, maxChars, depth: 0);
                string normalized = Normalize(builder.ToString(), maxChars);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    text = normalized;
                    return true;
                }
            }
            catch
            {
                // Ignore and fall back.
            }

            return false;
        }

        private static bool TryGetFocusedAccessible(IAccessible root, out IAccessible? focusedAccessible)
        {
            focusedAccessible = null;
            try
            {
                object? focus = root.accFocus;
                if (focus is IAccessible focused)
                {
                    focusedAccessible = focused;
                    return true;
                }
            }
            catch
            {
                // Ignore and fall back.
            }

            return false;
        }

        private static bool TryExtractContainerTextFromNode(IAccessible node, int maxChars, out string text)
        {
            text = string.Empty;

            var builder = new StringBuilder();
            AppendAccessibleText(node, builder, maxChars, depth: 0);
            string normalized = Normalize(builder.ToString(), maxChars);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                text = normalized;
                return true;
            }

            return false;
        }

        private static void AppendVariantText(IAccessible root, object variant, StringBuilder builder, int maxChars, int depth)
        {
            if (builder.Length >= maxChars || depth > 6)
            {
                return;
            }

            if (variant is IAccessible acc)
            {
                AppendAccessibleText(acc, builder, maxChars, depth + 1);
                return;
            }

            if (variant is int childId)
            {
                AppendNodeNameValue(root, childId, builder, maxChars);
                return;
            }

            if (variant is Array array)
            {
                foreach (object? item in array)
                {
                    if (item == null || builder.Length >= maxChars)
                    {
                        continue;
                    }

                    AppendVariantText(root, item, builder, maxChars, depth + 1);
                }

                return;
            }

            if (variant is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item == null || builder.Length >= maxChars)
                    {
                        continue;
                    }

                    AppendVariantText(root, item, builder, maxChars, depth + 1);
                }

                return;
            }

            string normalized = Normalize(variant.ToString(), maxChars);
            AppendLine(builder, normalized, maxChars);
        }

        private static void AppendAccessibleText(IAccessible acc, StringBuilder builder, int maxChars, int depth)
        {
            if (builder.Length >= maxChars || depth > 6)
            {
                return;
            }

            AppendNodeNameValue(acc, ChildIdSelf, builder, maxChars);

            int childCount = 0;
            try
            {
                childCount = acc.accChildCount;
            }
            catch
            {
                return;
            }

            int limit = Math.Min(childCount, 60);
            for (int i = 1; i <= limit && builder.Length < maxChars; i++)
            {
                try
                {
                    object? child = acc.get_accChild(i);
                    if (child is IAccessible childAcc)
                    {
                        AppendAccessibleText(childAcc, builder, maxChars, depth + 1);
                    }
                    else
                    {
                        AppendNodeNameValue(acc, i, builder, maxChars);
                    }
                }
                catch
                {
                    // Ignore individual child failures.
                }
            }
        }

        private static void AppendNodeNameValue(IAccessible acc, object childId, StringBuilder builder, int maxChars)
        {
            try
            {
                string? name = acc.get_accName(childId);
                AppendLine(builder, Normalize(name, maxChars), maxChars);
            }
            catch
            {
                // Ignore.
            }

            try
            {
                string? value = acc.get_accValue(childId);
                AppendLine(builder, Normalize(value, maxChars), maxChars);
            }
            catch
            {
                // Ignore.
            }
        }

        private static void AppendLine(StringBuilder builder, string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value) || builder.Length >= maxChars)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            if (builder.Length + value.Length > maxChars)
            {
                builder.Append(value[..Math.Max(0, maxChars - builder.Length)]);
                return;
            }

            builder.Append(value);
        }

        private static string Normalize(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n").Trim();
            if (normalized.Length > maxChars)
            {
                normalized = normalized[..maxChars];
            }

            return normalized;
        }
    }
}

using System.Globalization;
using System.Text;

namespace CodeExplainer
{
    internal static class TextSanitizer
    {
        public static string SanitizePayloadText(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var builder = new StringBuilder(normalized.Length);
            bool previousWasBlankLine = false;

            foreach (char ch in normalized)
            {
                if (builder.Length >= maxChars)
                {
                    break;
                }

                if (ch == '\uFFFC' || ch == '\uFFFD')
                {
                    continue;
                }

                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.PrivateUse || category == UnicodeCategory.Surrogate)
                {
                    continue;
                }

                if (ch == '\u200B' || ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF')
                {
                    continue;
                }

                if (char.IsControl(ch) && ch != '\n' && ch != '\t')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    bool lastWasLineBreak = builder.Length > 0 && builder[builder.Length - 1] == '\n';
                    if (lastWasLineBreak)
                    {
                        if (previousWasBlankLine)
                        {
                            continue;
                        }

                        previousWasBlankLine = true;
                    }
                    else
                    {
                        previousWasBlankLine = false;
                    }

                    builder.Append(ch);
                    continue;
                }

                previousWasBlankLine = false;
                builder.Append(ch);
            }

            return builder.ToString().Trim();
        }
    }
}

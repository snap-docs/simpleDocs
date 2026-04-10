using System;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Managers
{
    internal static class UsageContextBuilder
    {
        public static string Build(ActiveWindowInfo window, EnvironmentType environmentType)
        {
            return environmentType switch
            {
                EnvironmentType.IDE => $"ide_editor|{NormalizeProcess(window.ProcessName)}",
                EnvironmentType.IDEEmbeddedTerminal => $"ide_terminal|{NormalizeProcess(window.ProcessName)}",
                EnvironmentType.ModernTerminal => $"modern_terminal|{NormalizeProcess(window.ProcessName)}",
                EnvironmentType.ClassicTerminal => $"classic_terminal|{NormalizeProcess(window.ProcessName)}",
                EnvironmentType.BrowserChromium => $"browser|{NormalizeBrowser(window.ProcessName)}|{DetectBrowserSite(window)}",
                EnvironmentType.BrowserFirefox => $"browser|firefox|{DetectBrowserSite(window)}",
                EnvironmentType.Electron => $"electron|{NormalizeProcess(window.ProcessName)}",
                EnvironmentType.External => $"external|{NormalizeProcess(window.ProcessName)}",
                _ => $"unknown|{NormalizeProcess(window.ProcessName)}"
            };
        }

        private static string DetectBrowserSite(ActiveWindowInfo window)
        {
            string? domain = TryExtractDomainFromTitle(window.Title);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                return domain;
            }

            try
            {
                AutomationElement root = AutomationElement.FromHandle(window.Hwnd);
                var edits = root.FindAll(
                    TreeScope.Subtree,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

                foreach (AutomationElement element in edits)
                {
                    string name = (element.Current.Name ?? string.Empty).ToLowerInvariant();
                    string automationId = (element.Current.AutomationId ?? string.Empty).ToLowerInvariant();
                    bool looksLikeAddressBar =
                        name.Contains("address")
                        || name.Contains("search")
                        || automationId.Contains("address")
                        || automationId.Contains("search")
                        || automationId.Contains("url");

                    if (!looksLikeAddressBar)
                    {
                        continue;
                    }

                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObject)
                        && patternObject is ValuePattern valuePattern)
                    {
                        string? extracted = TryExtractDomainFromText(valuePattern.Current.Value);
                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            return extracted;
                        }
                    }
                }
            }
            catch
            {
                // Best effort only. Usage context must never break capture.
            }

            return "unknown";
        }

        private static string? TryExtractDomainFromTitle(string title)
        {
            return TryExtractDomainFromText(title);
        }

        private static string? TryExtractDomainFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            Match match = Regex.Match(
                text,
                @"(?:(?:https?://)?(?:www\.)?)([a-z0-9-]+(?:\.[a-z0-9-]+)+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return null;
            }

            return match.Groups[1].Value.ToLowerInvariant();
        }

        private static string NormalizeProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return "unknown";
            }

            return processName.Trim().ToLowerInvariant();
        }

        private static string NormalizeBrowser(string processName)
        {
            string normalized = NormalizeProcess(processName);
            return normalized switch
            {
                "msedge" => "edge",
                "chrome" => "chrome",
                "brave" => "brave",
                "opera" => "opera",
                _ => normalized
            };
        }
    }
}

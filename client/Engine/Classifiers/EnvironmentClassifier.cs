using System;
using System.Collections.Generic;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Classifiers
{
    public class EnvironmentClassifier
    {
        private static readonly HashSet<string> IdeProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "code", "cursor", "devenv", "idea64", "pycharm64", "webstorm64", "rider64", "sublime_text"
        };

        private static readonly HashSet<string> ChromiumBrowsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "brave", "opera"
        };

        private static readonly HashSet<string> ClassicTerminals = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmd", "powershell", "mintty", "ConEmu64", "ConEmuC64"
        };
        
        public EnvironmentType Classify(ActiveWindowInfo window)
        {
            if (IdeProcesses.Contains(window.ProcessName)) return EnvironmentType.IDE;
            if (window.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentType.BrowserFirefox;
            }

            if (ChromiumBrowsers.Contains(window.ProcessName))
            {
                return EnvironmentType.BrowserChromium;
            }

            // Windows Terminal wraps cmd.exe or pwsh.exe, so the host app is WindowsTerminal
            if (window.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentType.ModernTerminal;
            }

            if (ClassicTerminals.Contains(window.ProcessName) || window.ProcessName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentType.ClassicTerminal;
            }

            if (window.ClassName.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentType.Electron;
            }

            if (window.ProcessName.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentType.Unknown;
            }

            return EnvironmentType.External;
        }
    }
}

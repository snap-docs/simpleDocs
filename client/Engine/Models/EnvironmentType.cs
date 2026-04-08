using System;

namespace CodeExplainer.Engine.Models
{
    public enum EnvironmentType
    {
        IDE,
        IDEEmbeddedTerminal,
        BrowserChromium,
        BrowserFirefox,
        ClassicTerminal,
        ModernTerminal,
        Electron,
        External,
        Unknown
    }

    public static class EnvironmentTypeExtensions
    {
        public static string ToApiValue(this EnvironmentType environmentType)
        {
            return environmentType switch
            {
                EnvironmentType.IDE => "ide_editor",
                EnvironmentType.IDEEmbeddedTerminal => "ide_embedded_terminal",
                EnvironmentType.BrowserChromium => "browser_chromium",
                EnvironmentType.BrowserFirefox => "browser_firefox",
                EnvironmentType.ClassicTerminal => "classic_terminal",
                EnvironmentType.ModernTerminal => "modern_terminal",
                EnvironmentType.Electron => "electron",
                EnvironmentType.External => "external",
                EnvironmentType.Unknown => "unknown",
                _ => "unknown"
            };
        }
    }
}

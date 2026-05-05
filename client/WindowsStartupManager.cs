using System;
using System.IO;
using Microsoft.Win32;

namespace CodeExplainer
{
    internal sealed class WindowsStartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string PreferencesKeyPath = @"Software\simpleDocs";
        private const string RunValueName = "simpleDocs";
        private const string PreferenceValueName = "StartWithWindows";

        public bool IsStartupEnabled { get; private set; }

        public void Initialize()
        {
            bool enabled = ReadPreference();
            Apply(enabled);
        }

        public bool SetStartupEnabled(bool enabled)
        {
            WritePreference(enabled);
            Apply(enabled);
            return IsStartupEnabled;
        }

        private void Apply(bool enabled)
        {
            using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (runKey == null)
            {
                throw new InvalidOperationException("Unable to access the Windows startup registry key.");
            }

            if (enabled)
            {
                runKey.SetValue(RunValueName, BuildStartupCommand(), RegistryValueKind.String);
            }
            else if (runKey.GetValue(RunValueName) != null)
            {
                runKey.DeleteValue(RunValueName, false);
            }

            IsStartupEnabled = enabled;
        }

        private static bool ReadPreference()
        {
            using RegistryKey? preferencesKey = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath);
            object? rawValue = preferencesKey?.GetValue(PreferenceValueName);
            if (rawValue == null)
            {
                return true;
            }

            return rawValue switch
            {
                int intValue => intValue != 0,
                string stringValue => bool.TryParse(stringValue, out bool parsed) ? parsed : true,
                _ => true
            };
        }

        private static void WritePreference(bool enabled)
        {
            using RegistryKey? preferencesKey = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath);
            if (preferencesKey == null)
            {
                throw new InvalidOperationException("Unable to store the Windows startup preference.");
            }

            preferencesKey.SetValue(PreferenceValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        private static string BuildStartupCommand()
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("Unable to determine the application path for Windows startup.");
            }

            if (processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                string assemblyName = typeof(App).Assembly.GetName().Name ?? "CodeExplainer";
                string entryAssemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
                if (!File.Exists(entryAssemblyPath))
                {
                    throw new InvalidOperationException("Unable to determine the application assembly path for Windows startup.");
                }

                return $"\"{processPath}\" \"{entryAssemblyPath}\"";
            }

            return $"\"{processPath}\"";
        }
    }
}

using System;
using System.IO;
using System.Reflection;

namespace CodeExplainer
{
    internal static class RuntimeLog
    {
        private static readonly object Sync = new();
        private static readonly string LogFilePath = ResolveLogFilePath();

        public static string CurrentLogPath => LogFilePath;

        public static string AppVersion =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        public static void Info(string area, string message) => Write("INFO", area, message);

        public static void Warn(string area, string message) => Write("WARN", area, message);

        public static void Error(string area, string message) => Write("ERROR", area, message);

        public static string Preview(string? text, int maxLength = 80)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            string singleLine = text.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            if (singleLine.Length <= maxLength)
            {
                return singleLine;
            }

            return singleLine.Substring(0, maxLength) + "...";
        }

        private static void Write(string level, string area, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {level,-5} [{area}] {message}";

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // WinExe may not have an attached console; file logging remains active.
            }

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > 2 * 1024 * 1024)
                        File.Move(LogFilePath, LogFilePath + ".previous", overwrite: true);
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never crash the app due to diagnostics logging.
            }
        }

        private static string ResolveLogFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodeExplainer", "logs", "client_live.log");
        }
    }
}

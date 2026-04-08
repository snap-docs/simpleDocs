using System;
using System.IO;

namespace CodeExplainer
{
    internal static class RuntimeLog
    {
        private static readonly object Sync = new();
        private static readonly string LogFilePath = ResolveLogFilePath();

        public static string CurrentLogPath => LogFilePath;

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
            string baseDir = AppContext.BaseDirectory;
            string? root = TryFindRepoRoot(baseDir);
            string logDir = root != null
                ? Path.Combine(root, "runlogs")
                : Path.Combine(baseDir, "runlogs");

            Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, "client_live.log");
        }

        private static string? TryFindRepoRoot(string startPath)
        {
            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                string backendPath = Path.Combine(directory.FullName, "backend");
                string clientPath = Path.Combine(directory.FullName, "client");
                if (Directory.Exists(backendPath) && Directory.Exists(clientPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}

using System;

namespace CodeExplainer.Engine.Models
{
    public class ActiveWindowInfo
    {
        public IntPtr Hwnd { get; }
        public uint ProcessId { get; }
        public string ProcessName { get; }
        public string Title { get; }
        public string ClassName { get; }

        public ActiveWindowInfo(IntPtr hwnd, uint processId, string processName, string title, string className)
        {
            Hwnd = hwnd;
            ProcessId = processId;
            ProcessName = processName;
            Title = title;
            ClassName = className;
        }

        public override string ToString()
        {
            return $"[{ProcessName}] Title: '{Title}', Class: '{ClassName}' (PID: {ProcessId}, HWND: {Hwnd})";
        }
    }
}

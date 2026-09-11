using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    internal sealed class CaptureScope : IDisposable
    {
        private static readonly AsyncLocal<CaptureScope?> CurrentScope = new();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly CaptureScope? _previous;
        public static CaptureScope? Current => CurrentScope.Value;
        public ActiveWindowInfo Window { get; }
        private AutomationElement? _focused;
        private bool _focusResolved;
        public AutomationElement? Focused
        {
            get
            {
                if (!_focusResolved && IsValid)
                {
                    _focusResolved = true;
                    try
                    {
                        var element = AutomationElement.FocusedElement;
                        if (BelongsToWindow(element, Window.Hwnd)) _focused = element;
                    }
                    catch { }
                }
                return _focused;
            }
        }
        public string? SelectionHint { get; set; }
        public bool IsValid => _elapsed.Elapsed < TimeSpan.FromSeconds(7)
            && Win32Native.GetForegroundWindow() == Window.Hwnd;

        public CaptureScope(ActiveWindowInfo window)
        {
            Window = window;
            _previous = CurrentScope.Value;
            CurrentScope.Value = this;
        }

        private static bool BelongsToWindow(AutomationElement? element, IntPtr hwnd)
        {
            for (int i = 0; element != null && i < 30; i++)
            {
                if (element.Current.NativeWindowHandle == hwnd.ToInt64()) return true;
                if (Automation.Compare(element, AutomationElement.RootElement)) return false;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
        }

        public void Dispose() => CurrentScope.Value = _previous;
    }
}

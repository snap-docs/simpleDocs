using System;
using System.Windows;
using System.Windows.Input;

namespace CodeExplainer
{
    /// <summary>
    /// Topmost floating overlay that displays streaming AI explanations.
    /// Does not steal focus. Dismiss with Escape or click outside.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;

            // Dismiss on Escape
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    HideOverlay();
            };

            // Dismiss on mouse click
            MouseLeftButtonDown += (s, e) =>
            {
                HideOverlay();
            };

            // Allow dragging the window
            MouseRightButtonDown += (s, e) =>
            {
                DragMove();
            };
        }

        /// <summary>
        /// Shows the overlay in loading state near the current mouse position.
        /// </summary>
        public void ShowLoading(string statusLabel = "")
        {
            Dispatcher.Invoke(() =>
            {
                ResponseText.Text = "";
                CaseLabel.Text = statusLabel;
                LoadingPanel.Visibility = Visibility.Visible;
                ContentScroller.Visibility = Visibility.Collapsed;

                var mousePos = GetMousePosition();
                Left = mousePos.X + 20;
                Top = mousePos.Y + 20;

                var screen = SystemParameters.WorkArea;
                if (Left + Width > screen.Right)
                    Left = screen.Right - Width - 20;
                if (Top + Height > screen.Bottom)
                    Top = screen.Bottom - Height - 20;
                if (Left < screen.Left)
                    Left = screen.Left + 20;
                if (Top < screen.Top)
                    Top = screen.Top + 20;

                Visibility = Visibility.Visible;
                Show();
            });
        }

        /// <summary>
        /// Appends a streaming token to the display. Switches from loading to content on first token.
        /// </summary>
        public void AppendToken(string token)
        {
            Dispatcher.Invoke(() =>
            {
                if (LoadingPanel.Visibility == Visibility.Visible)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    ContentScroller.Visibility = Visibility.Visible;
                }

                ResponseText.Text += token;
                ContentScroller.ScrollToEnd();
            });
        }

        public void SetStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                CaseLabel.Text = status;
            });
        }

        /// <summary>
        /// Called when streaming is complete.
        /// </summary>
        public void OnStreamComplete()
        {
            Dispatcher.Invoke(() =>
            {
                CaseLabel.Text = "✓ Done";
            });
        }

        public void ShowMessage(string message, string statusLabel)
        {
            Dispatcher.Invoke(() =>
            {
                ShowLoading(statusLabel);
                LoadingPanel.Visibility = Visibility.Collapsed;
                ContentScroller.Visibility = Visibility.Visible;
                ResponseText.Text = message;
            });
        }

        private void HideOverlay()
        {
            Visibility = Visibility.Collapsed;
            ResponseText.Text = "";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private static Point GetMousePosition()
        {
            GetCursorPos(out POINT point);
            return new Point(point.X, point.Y);
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeExplainer
{
    /// <summary>
    /// Topmost floating overlay that displays streaming AI explanations.
    /// Does not steal focus. Dismiss with Escape or click outside.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private static readonly Brush NeutralForegroundBrush = CreateBrush("#CCFFFFFF");
        private static readonly Brush PositiveForegroundBrush = CreateBrush("#8FE388");
        private static readonly Brush NegativeForegroundBrush = CreateBrush("#FF9A8A");
        private static readonly Brush TitleForegroundBrush = CreateBrush("#9FC3FF");
        private static readonly Brush StatusNormalBrush = CreateBrush("#80FFFFFF");
        private static readonly Brush StatusWarningBrush = CreateBrush("#F0C987");
        private static readonly Brush StatusErrorBrush = CreateBrush("#FF9A8A");
        private static readonly Brush StatusSuccessBrush = CreateBrush("#8FE388");
        private const double NeutralOpacity = 0.72;
        private const double DimmedOpacity = 0.34;
        private const double SelectedOpacity = 1.0;
        private string? _currentRequestId;
        private bool _feedbackSubmitted;
        private bool _isResponseVisible;
        private string? _selectedReaction;
        public Func<string, string, Task<bool>>? FeedbackHandler { get; set; }

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
                if (IsFeedbackInteraction(e.OriginalSource as DependencyObject))
                {
                    return;
                }

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
        public void ShowLoading(string statusLabel = "", string? requestId = null)
        {
            Dispatcher.Invoke(() =>
            {
                _currentRequestId = requestId;
                _feedbackSubmitted = false;
                _isResponseVisible = false;
                _selectedReaction = null;
                ResponseText.Text = "";
                ResponseText.Inlines.Clear();
                CaseLabel.Text = statusLabel;
                SetStatusLabelColor(statusLabel);
                LoadingPanel.Visibility = Visibility.Visible;
                ContentScroller.Visibility = Visibility.Collapsed;
                HideFeedback();

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
                _isResponseVisible = !string.IsNullOrWhiteSpace(ResponseText.Text);
                if (_isResponseVisible)
                {
                    UpdateFeedbackVisibility();
                }
                ContentScroller.ScrollToEnd();
            });
        }

        public void SetStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                CaseLabel.Text = status;
                SetStatusLabelColor(status);
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
                SetStatusLabelColor("done");
                ApplyCompactColorFormatting();
                UpdateFeedbackVisibility();
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
                ApplyCompactColorFormatting();
                _isResponseVisible = false;
                HideFeedback();
            });
        }

        private void HideOverlay()
        {
            Visibility = Visibility.Collapsed;
            ResponseText.Text = "";
            _isResponseVisible = false;
            HideFeedback();
        }

        private async void ThumbsUpButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitFeedbackAsync("up");
            e.Handled = true;
        }

        private async void ThumbsDownButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitFeedbackAsync("down");
            e.Handled = true;
        }

        private async Task SubmitFeedbackAsync(string reaction)
        {
            if (_feedbackSubmitted || !_isResponseVisible)
            {
                return;
            }

            _feedbackSubmitted = true;
            _selectedReaction = reaction;
            UpdateFeedbackButtons();

            string message =
                $"req={_currentRequestId ?? "-"} reaction={reaction} status=\"{RuntimeLog.Preview(CaseLabel.Text, 80)}\" " +
                $"response_chars={ResponseText.Text.Length} response_preview=\"{RuntimeLog.Preview(ResponseText.Text, 160)}\"";

            _ = Task.Run(() => RuntimeLog.Info("Feedback", message));

            if (string.IsNullOrWhiteSpace(_currentRequestId) || FeedbackHandler == null)
            {
                _feedbackSubmitted = false;
                _selectedReaction = null;
                UpdateFeedbackButtons();
                RuntimeLog.Warn("Feedback", "Feedback was not sent because request_id or handler was unavailable.");
                return;
            }

            try
            {
                bool stored = await FeedbackHandler.Invoke(_currentRequestId, reaction);
                if (!stored)
                {
                    _feedbackSubmitted = false;
                    _selectedReaction = null;
                    UpdateFeedbackButtons();
                }
            }
            catch (Exception ex)
            {
                _feedbackSubmitted = false;
                _selectedReaction = null;
                UpdateFeedbackButtons();
                RuntimeLog.Error("Feedback", $"Failed to store feedback: {ex.Message}");
            }
        }

        private void ApplyCompactColorFormatting()
        {
            string raw = ResponseText.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            ResponseText.Inlines.Clear();
            string[] lines = raw.Replace("\r\n", "\n").Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                AppendFormattedLine(line);
                if (index < lines.Length - 1)
                {
                    ResponseText.Inlines.Add(new LineBreak());
                }
            }
        }

        private void AppendFormattedLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                ResponseText.Inlines.Add(new Run(string.Empty));
                return;
            }

            string trimmed = line.Trim();
            if (TryExtractTitleLabel(trimmed, out string label, out string content))
            {
                Brush labelBrush = GetLabelBrush(label);
                ResponseText.Inlines.Add(new Run(label)
                {
                    Foreground = labelBrush,
                    FontWeight = FontWeights.SemiBold
                });
                ResponseText.Inlines.Add(new Run($" {content}")
                {
                    Foreground = NeutralForegroundBrush
                });
                return;
            }

            ResponseText.Inlines.Add(new Run(line)
            {
                Foreground = NeutralForegroundBrush
            });
        }

        private static bool TryExtractTitleLabel(string line, out string label, out string content)
        {
            label = string.Empty;
            content = string.Empty;

            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0 || colonIndex > 18)
            {
                return false;
            }

            string left = line.Substring(0, colonIndex).Trim();
            if (left.Length < 3)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                char ch = left[i];
                bool valid = char.IsLetter(ch) || ch == ' ';
                if (!valid)
                {
                    return false;
                }
            }

            label = $"{left}:";
            content = line.Substring(colonIndex + 1).TrimStart();
            return true;
        }

        private static Brush GetLabelBrush(string label)
        {
            string normalized = (label ?? string.Empty).Trim().TrimEnd(':').ToLowerInvariant();
            if (normalized.Contains("issue") || normalized.Contains("error") || normalized.Contains("problem"))
            {
                return StatusErrorBrush;
            }

            if (normalized.Contains("hint") || normalized.Contains("check") || normalized.Contains("warning"))
            {
                return StatusWarningBrush;
            }

            if (normalized.Contains("done") || normalized.Contains("success"))
            {
                return StatusSuccessBrush;
            }

            return TitleForegroundBrush;
        }

        private void SetStatusLabelColor(string status)
        {
            string normalized = (status ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("error") || normalized.Contains("unsupported"))
            {
                CaseLabel.Foreground = StatusErrorBrush;
                return;
            }

            if (normalized.Contains("partial") || normalized.Contains("warning"))
            {
                CaseLabel.Foreground = StatusWarningBrush;
                return;
            }

            if (normalized.Contains("done") || normalized.Contains("complete") || normalized.Contains("success"))
            {
                CaseLabel.Foreground = StatusSuccessBrush;
                return;
            }

            CaseLabel.Foreground = StatusNormalBrush;
        }

        private void UpdateFeedbackVisibility()
        {
            FeedbackPanel.Visibility = _isResponseVisible ? Visibility.Visible : Visibility.Collapsed;
            UpdateFeedbackButtons();
        }

        private void HideFeedback()
        {
            FeedbackPanel.Visibility = Visibility.Collapsed;
            UpdateFeedbackButtons();
        }

        private void UpdateFeedbackButtons()
        {
            ThumbsUpButton.IsEnabled = !_feedbackSubmitted;
            ThumbsDownButton.IsEnabled = !_feedbackSubmitted;

            ThumbsUpIcon.Foreground = _selectedReaction == "up" ? PositiveForegroundBrush : NeutralForegroundBrush;
            ThumbsDownIcon.Foreground = _selectedReaction == "down" ? NegativeForegroundBrush : NeutralForegroundBrush;

            ThumbsUpIcon.Opacity = _selectedReaction == "up"
                ? SelectedOpacity
                : _feedbackSubmitted ? DimmedOpacity : NeutralOpacity;

            ThumbsDownIcon.Opacity = _selectedReaction == "down"
                ? SelectedOpacity
                : _feedbackSubmitted ? DimmedOpacity : NeutralOpacity;
        }

        private static bool IsFeedbackInteraction(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.Name == "FeedbackPanel")
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static SolidColorBrush CreateBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
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

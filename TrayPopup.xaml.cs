using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TopBar
{
    public partial class TrayPopup : Window
    {
        private bool _ready;
        private DispatcherTimer? _closeTimer;
        private List<TrayIconInfo> _icons = new();

        public TrayPopup()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                BlurHelper.EnableBlur(this);
                LoadTrayIcons();
            };
            ContentRendered += (_, _) => _ready = true;
            KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        }

        private void LoadTrayIcons()
        {
            try
            {
                _icons = TrayIconReader.GetTrayIcons();
            }
            catch
            {
                _icons = new List<TrayIconInfo>();
            }

            BuildIconGrid();
        }

        private void BuildIconGrid()
        {
            IconsPanel.Items.Clear();

            if (_icons.Count == 0)
            {
                EmptyLabel.Text = "No tray icons found.\n\n" + TrayIconReader.LastDebugInfo;
                EmptyLabel.Visibility = Visibility.Visible;
                return;
            }

            EmptyLabel.Visibility = Visibility.Collapsed;

            var hoverBrush = Application.Current.Resources["PopupHoverBg"] as SolidColorBrush
                ?? new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            var fgBrush = Application.Current.Resources["PopupFgNormal"] as SolidColorBrush
                ?? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

            foreach (var icon in _icons)
            {
                var img = new Image
                {
                    Width = 20,
                    Height = 20,
                    Source = icon.Icon
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                var label = new TextBlock
                {
                    Text = TruncateTooltip(icon.Name, 18),
                    Foreground = fgBrush,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 60,
                    Margin = new Thickness(0, 3, 0, 0)
                };

                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(img);
                stack.Children.Add(label);

                var border = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(3),
                    Width = 72,
                    Cursor = Cursors.Hand,
                    Child = stack,
                    ToolTip = icon.Name,
                    Tag = icon
                };

                border.MouseLeftButtonUp += TrayIcon_LeftClick;
                border.MouseRightButtonUp += TrayIcon_RightClick;
                border.MouseEnter += (s, _) => ((Border)s).Background = hoverBrush;
                border.MouseLeave += (s, _) => ((Border)s).Background = Brushes.Transparent;

                IconsPanel.Items.Add(border);
            }
        }

        private static string TruncateTooltip(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Take first line only
            var firstLine = text.Split('\n')[0].Trim();
            return firstLine.Length > maxLen ? firstLine.Substring(0, maxLen) + "…" : firstLine;
        }

        private void TrayIcon_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is TrayIconInfo info)
            {
                TrayIconReader.SendClick(info);
                e.Handled = true;
            }
        }

        private void TrayIcon_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is TrayIconInfo info)
            {
                TrayIconReader.SendRightClick(info);
                e.Handled = true;
            }
        }

        private void Refresh_Click(object sender, MouseButtonEventArgs e)
        {
            LoadTrayIcons();
        }

        // ── Close-on-deactivate with grace period ───────────────────────────

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            StartCloseTimer();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer?.Stop();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            StartCloseTimer();
        }

        private void StartCloseTimer()
        {
            if (!_ready) return;
            _closeTimer?.Stop();
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _closeTimer.Tick += (_, _) => { _closeTimer!.Stop(); SafeClose(); };
            _closeTimer.Start();
        }

        private void SafeClose()
        {
            _closeTimer?.Stop();
            try { Close(); } catch { }
        }
    }
}

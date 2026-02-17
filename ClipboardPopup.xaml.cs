using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopBar
{
    /// <summary>
    /// Clipboard history popup — stores last 20 clipboard entries and lets you paste them.
    /// </summary>
    public partial class ClipboardPopup : Window
    {
        private static readonly List<string> _history = new();
        private const int MAX_ITEMS = 20;

        private bool _ready, _closing;
        private DispatcherTimer? _leaveTimer;

        public ClipboardPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) => _ready = true;
            RefreshList();
        }

        /// <summary>Call this from MainWindow whenever clipboard changes or periodically.</summary>
        public static void CaptureClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _history.Remove(text); // dedupe
                        _history.Insert(0, text);
                        if (_history.Count > MAX_ITEMS)
                            _history.RemoveAt(_history.Count - 1);
                    }
                }
            }
            catch { }
        }

        private void RefreshList()
        {
            ClipboardList.ItemsSource = null;
            ClipboardList.ItemsSource = _history.Select((t, i) => new ClipboardItem
            {
                Index = i + 1,
                Text = t.Length > 120 ? t[..120] + "…" : t,
                FullText = t
            }).ToList();
        }

        private void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ClipboardItem item)
            {
                try { Clipboard.SetText(item.FullText); } catch { }
                SafeClose();
            }
        }

        private void Item_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        }

        private void Item_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = Brushes.Transparent;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _history.Clear();
            RefreshList();
        }

        // ── Close logic ─────────────────────────────────────────────────────

        private void Window_Deactivated(object sender, EventArgs e) { if (_ready) SafeClose(); }
        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_leaveTimer == null)
            {
                _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); SafeClose(); };
            }
            _leaveTimer.Start();
        }
        private void Window_MouseEnter(object sender, MouseEventArgs e) { _leaveTimer?.Stop(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape) SafeClose(); base.OnKeyDown(e); }
        private void SafeClose() { if (_closing) return; _closing = true; _leaveTimer?.Stop(); Close(); }
    }

    public class ClipboardItem
    {
        public int Index { get; set; }
        public string Text { get; set; } = "";
        public string FullText { get; set; } = "";
    }
}

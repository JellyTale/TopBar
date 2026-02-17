using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopBar
{
    public partial class CalendarPopup : Window
    {
        private bool _ready, _closing;
        private DispatcherTimer? _leaveTimer;

        public CalendarPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) =>
            {
                _ready = true;
                DarkifyVisualTree(Cal);
            };
            Cal.DisplayModeChanged += (_, _) =>
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => DarkifyVisualTree(Cal));

            DateHeader.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
            Cal.SelectedDate = DateTime.Today;
            Cal.DisplayDate = DateTime.Today;
        }

        /// <summary>Recursively fix hardcoded backgrounds and text colors inside the WPF Calendar.</summary>
        private static void DarkifyVisualTree(DependencyObject parent)
        {
            var fgBrush = Application.Current.Resources["PopupFgNormal"] as SolidColorBrush
                ?? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            var fgBright = Application.Current.Resources["PopupFgBright"] as SolidColorBrush
                ?? new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
            bool isDark = fgBrush.Color.R > 128; // white text = dark bg

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var typeName = child.GetType().Name;

                if (child is Border b)
                {
                    if (b.Background is SolidColorBrush bg && ShouldClearBg(bg.Color, isDark))
                        b.Background = Brushes.Transparent;
                    if (b.BorderBrush is SolidColorBrush bb && bb.Color.A > 20)
                        b.BorderBrush = Brushes.Transparent;
                }

                if (child is Panel p && p.Background is SolidColorBrush pbg && ShouldClearBg(pbg.Color, isDark))
                    p.Background = Brushes.Transparent;

                if (child is TextBlock tb && tb.Foreground is SolidColorBrush tfg && ShouldFixText(tfg.Color, isDark))
                    tb.Foreground = fgBrush;

                // Fix header/nav buttons but not CalendarDayButton/CalendarButton (those have our styles)
                if (child is Button btn && typeName != "CalendarDayButton" && typeName != "CalendarButton")
                {
                    btn.Foreground = fgBright;
                    btn.Background = Brushes.Transparent;
                    btn.BorderBrush = Brushes.Transparent;
                }

                DarkifyVisualTree(child);
            }
        }

        /// <summary>Should we replace this background? Clear light bgs in dark mode, dark bgs in light mode.</summary>
        private static bool ShouldClearBg(Color c, bool isDark)
        {
            if (c.A <= 20) return false;
            int sum = c.R + c.G + c.B;
            return isDark ? sum > 500 : sum < 200;
        }

        /// <summary>Should we replace this text color? Fix dark text on dark, or light text on light.</summary>
        private static bool ShouldFixText(Color c, bool isDark)
        {
            if (c.A <= 20) return false;
            int sum = c.R + c.G + c.B;
            return isDark ? sum < 200 : sum > 500;
        }

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
}

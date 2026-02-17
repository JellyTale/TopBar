using System;
using System.Management;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopBar
{
    public partial class BrightnessPopup : Window
    {
        private bool _ready, _closing, _suppress;
        private DispatcherTimer? _leaveTimer;

        public BrightnessPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) => _ready = true;

            _suppress = true;
            int current = GetBrightness();
            BrightnessSlider.Value = current;
            BrightnessLabel.Text = $"{current}%";
            _suppress = false;
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress) return;
            int val = (int)e.NewValue;
            BrightnessLabel.Text = $"{val}%";
            SetBrightness(val);
        }

        private static int GetBrightness()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI",
                    "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return 50;
        }

        private static void SetBrightness(int value)
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI",
                    "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in s.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { 1, value });
                    break;
                }
            }
            catch { }
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
}

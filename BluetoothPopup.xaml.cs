using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace TopBar
{
    public partial class BluetoothPopup : Window
    {
        private bool _ready, _closing;
        private DispatcherTimer? _leaveTimer;
        private Radio? _radio;
        private bool _btEnabled;

        public BluetoothPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) => _ready = true;

            InitBluetoothAsync();
        }

        private async void InitBluetoothAsync()
        {
            try
            {
                var radios = await Radio.GetRadiosAsync();
                _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                _btEnabled = _radio?.State == RadioState.On;
            }
            catch
            {
                _btEnabled = false;
            }
            UpdateUI();
        }

        private void UpdateUI()
        {
            StatusText.Text = _btEnabled ? "On" : "Off";
            ToggleText.Text = _btEnabled ? "Turn Off" : "Turn On";
        }

        private async void Toggle_Click(object sender, MouseButtonEventArgs e)
        {
            if (_radio == null) return;
            try
            {
                var newState = _btEnabled ? RadioState.Off : RadioState.On;
                var result = await _radio.SetStateAsync(newState);
                if (result == RadioAccessStatus.Allowed)
                {
                    _btEnabled = !_btEnabled;
                    UpdateUI();
                }
            }
            catch { }
        }

        private void OpenSettings_Click(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            SafeClose();
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

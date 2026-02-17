using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace TopBar
{
    public partial class SoundPopup : Window
    {
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _defaultDevice;
        private bool _suppressSliderEvent;
        private DispatcherTimer? _soundDebounce;

        public SoundPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) => _ready = true;

            try
            {
                _enumerator = new MMDeviceEnumerator();
                LoadDefaultDevice();
                LoadDevices();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Audio init error: {ex.Message}", "TopBar");
            }
        }

        // ── Volume ──────────────────────────────────────────────────────────

        private void LoadDefaultDevice()
        {
            try
            {
                _defaultDevice = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch
            {
                _defaultDevice = null;
            }

            if (_defaultDevice != null)
            {
                _suppressSliderEvent = true;
                VolumeSlider.Value = Math.Round(_defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                _suppressSliderEvent = false;
                UpdateVolumeLabel();
                UpdateMuteIcon();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvent || _defaultDevice == null) return;

            float vol = (float)(e.NewValue / 100.0);
            _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = vol;

            // Unmute if user drags slider.
            if (_defaultDevice.AudioEndpointVolume.Mute && vol > 0)
                _defaultDevice.AudioEndpointVolume.Mute = false;

            UpdateVolumeLabel();
            UpdateMuteIcon();

            // Play the system volume-change sound (debounced).
            PlayVolumeSound();
        }

        private void UpdateVolumeLabel()
        {
            VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
        }

        // ── Mute toggle ─────────────────────────────────────────────────────

        private const string ICON_UNMUTED =
            "M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0014 8.3v7.4a4.47 4.47 0 002.5-3.7zM14 3.2v2.1a7 7 0 010 13.4v2.1A9 9 0 0014 3.2z";
        private const string ICON_MUTED =
            "M3 9v6h4l5 5V4L7 9H3zm13 .8v4.4L19.2 12 16 8.8zm3.5-3.3L21 8l-3 3 3 3-1.5 1.5L16.5 12l-3 3L12 13.5l3-3-3-3L13.5 6l3 3 3-3z";

        private void MuteToggle_Click(object sender, MouseButtonEventArgs e)
        {
            if (_defaultDevice == null) return;

            _defaultDevice.AudioEndpointVolume.Mute = !_defaultDevice.AudioEndpointVolume.Mute;
            UpdateMuteIcon();
        }

        private void UpdateMuteIcon()
        {
            if (_defaultDevice == null) return;

            bool muted = _defaultDevice.AudioEndpointVolume.Mute;
            SpeakerIcon.Data = Geometry.Parse(muted ? ICON_MUTED : ICON_UNMUTED);
        }

        private void MuteToggle_MouseEnter(object sender, MouseEventArgs e)
        {
            MuteButton.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }

        private void MuteToggle_MouseLeave(object sender, MouseEventArgs e)
        {
            MuteButton.Background = Brushes.Transparent;
        }

        // ── Device list ─────────────────────────────────────────────────────

        private void LoadDevices()
        {
            if (_enumerator == null) return;
            var items = new List<AudioDeviceItem>();

            string? defaultId = null;
            try { defaultId = _defaultDevice?.ID; } catch { }

            foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                items.Add(new AudioDeviceItem
                {
                    Name = dev.FriendlyName,
                    DeviceId = dev.ID,
                    IsDefault = dev.ID == defaultId
                });
            }

            DeviceList.ItemsSource = items;
        }

        private void DeviceItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AudioDeviceItem item)
            {
                // Use the PolicyConfig COM interface to set the default device.
                try
                {
                    var policy = new PolicyConfigClient();
                    policy.SetDefaultEndpoint(item.DeviceId, Role.Multimedia);
                    policy.SetDefaultEndpoint(item.DeviceId, Role.Communications);
                }
                catch { }

                // Refresh.
                LoadDefaultDevice();
                LoadDevices();
            }
        }

        private void DeviceItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b && b.DataContext is AudioDeviceItem item && !item.IsDefault)
                b.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        }

        private void DeviceItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b && b.DataContext is AudioDeviceItem item)
                b.Background = item.IsDefault
                    ? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF))
                    : Brushes.Transparent;
        }

        // ── Close on deactivate / mouse-leave ──────────────────────────────

        private bool _ready;
        private bool _closing;
        private DispatcherTimer? _leaveTimer;

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_ready) SafeClose();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_leaveTimer == null)
            {
                _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); SafeClose(); };
            }
            _leaveTimer.Start();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _leaveTimer?.Stop();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape) SafeClose();
            base.OnKeyDown(e);
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            _leaveTimer?.Stop();
            Close();
        }

        // ── Volume change sound ─────────────────────────────────────────────

        private void PlayVolumeSound()
        {
            // Debounce: only play after the user stops dragging for 200ms.
            if (_soundDebounce == null)
            {
                _soundDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _soundDebounce.Tick += (_, _) =>
                {
                    _soundDebounce.Stop();
                    SystemSounds.Beep.Play();
                };
            }

            _soundDebounce.Stop();
            _soundDebounce.Start();
        }
    }

    // ── View model for device list items ────────────────────────────────────

    public class AudioDeviceItem
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        public Visibility CheckVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;
        public SolidColorBrush BackgroundBrush => IsDefault
            ? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF))
            : Brushes.Transparent;
    }
}

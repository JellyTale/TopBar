using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;
using static TopBar.NativeMethods;

namespace TopBar
{
    public partial class MediaPopup : Window
    {
        private bool _ready, _closing;
        private DispatcherTimer? _leaveTimer;
        private DispatcherTimer? _mediaTimer;

        // Virtual key codes for media keys
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;

        public MediaPopup()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                BlurHelper.EnableBlur(this);
                await UpdateMediaInfoAsync();
            };
            ContentRendered += (_, _) => _ready = true;

            _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mediaTimer.Tick += async (_, _) => await UpdateMediaInfoAsync();
            _mediaTimer.Start();
        }

        private async System.Threading.Tasks.Task UpdateMediaInfoAsync()
        {
            try
            {
                var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = sessionManager.GetCurrentSession();
                if (session != null)
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    TrackTitle.Text = string.IsNullOrWhiteSpace(props.Title) ? "No media playing" : props.Title;
                    TrackArtist.Text = props.Artist ?? "";
                }
                else
                {
                    TrackTitle.Text = "No media playing";
                    TrackArtist.Text = "";
                }
            }
            catch
            {
                TrackTitle.Text = "No media playing";
                TrackArtist.Text = "";
            }
        }

        private void PlayPause_Click(object sender, MouseButtonEventArgs e)
        {
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void Next_Click(object sender, MouseButtonEventArgs e)
        {
            keybd_event(VK_MEDIA_NEXT_TRACK, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void Prev_Click(object sender, MouseButtonEventArgs e)
        {
            keybd_event(VK_MEDIA_PREV_TRACK, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MEDIA_PREV_TRACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void Btn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }

        private void Btn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = Brushes.Transparent;
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
        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            _mediaTimer?.Stop();
            _leaveTimer?.Stop();
            Close();
        }
    }
}

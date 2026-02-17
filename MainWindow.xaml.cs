using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;
using static TopBar.NativeMethods;

namespace TopBar
{
    public partial class MainWindow : Window
    {
        private const int BAR_HEIGHT = 36;
        private const int ANIM_MS = 200;

        private readonly AppBarManager _appBar;
        private readonly MouseEdgeDetector _edgeDetector;
        private readonly DispatcherTimer _clock;
        private readonly DispatcherTimer _clipboardTimer;
        private readonly ActiveWindowTracker _windowTracker;
        private readonly GlobalHotkey _hotkey;
        private readonly GlobalHotkey _clipboardHotkey;

        private BarConfig _config;
        private bool _isVisible;
        private Window? _activePopup;

        // Media play/pause icon tracking
        private System.Windows.Shapes.Path? _playPausePath;
        private DispatcherTimer? _mediaStateTimer;
        private static readonly string PlayIcon = "M8 5v14l11-7z";
        private static readonly string PauseIcon = "M6 19h4V5H6v14zm8-14v14h4V5h-4z";

        // Contrast-aware foreground (white on dark bar, black on light bar)
        private Color _fgBase = Colors.White;
        private Color FgNormal => Color.FromArgb(0xCC, _fgBase.R, _fgBase.G, _fgBase.B);
        private Color FgDim => Color.FromArgb(0x88, _fgBase.R, _fgBase.G, _fgBase.B);
        private Color FgFull => Color.FromArgb(0xFF, _fgBase.R, _fgBase.G, _fgBase.B);
        private Color HoverBg => Color.FromArgb(0x30, _fgBase.R, _fgBase.G, _fgBase.B);

        public MainWindow()
        {
            InitializeComponent();

            _config = BarConfig.Load();

            var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            Width = screenW / dpiScale;
            Height = BAR_HEIGHT;
            Left = 0;
            Top = -BAR_HEIGHT;

            int barHeightPx = (int)(BAR_HEIGHT * dpiScale);
            _appBar = new AppBarManager(this) { BarHeight = barHeightPx };

            _edgeDetector = new MouseEdgeDetector
            {
                BarHeight = BAR_HEIGHT,
                EdgeThreshold = 2,
                LeaveMargin = 30,
                RevealDelayMs = _config.RevealDelayMs
            };
            _edgeDetector.MouseEnteredTopEdge += OnReveal;
            _edgeDetector.MouseLeftTopEdge += OnHide;

            // Clock timer
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => UpdateClock();
            _clock.Start();

            // Clipboard monitor
            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _clipboardTimer.Tick += (_, _) => ClipboardPopup.CaptureClipboard();
            _clipboardTimer.Start();

            // Active window title
            _windowTracker = new ActiveWindowTracker();
            _windowTracker.TitleChanged += title =>
                Dispatcher.Invoke(() => WindowTitleText.Text = title);

            // Global hotkey
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += () => Dispatcher.Invoke(ToggleBar);

            // Clipboard hotkey
            _clipboardHotkey = new GlobalHotkey(9001);
            _clipboardHotkey.Pressed += () => Dispatcher.Invoke(() =>
            {
                if (!_config.ShowClipboardHistory) return;
                if (!_isVisible) OnReveal();
                if (_activePopup is ClipboardPopup) { CloseActivePopup(); return; }
                OpenPopup(new ClipboardPopup());
            });

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Hide from Alt+Tab
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            ApplyConfig();
            EnableBlur();
            _edgeDetector.Install();
            UpdateClock();

            try { _hotkey.Register(this, _config.ToggleShortcut); }
            catch { }

            try
            {
                if (!string.IsNullOrWhiteSpace(_config.ClipboardShortcut))
                    _clipboardHotkey.Register(this, _config.ClipboardShortcut);
            }
            catch { }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _edgeDetector.Dispose();
            _appBar.Dispose();
            _clock.Stop();
            _clipboardTimer.Stop();
            _mediaStateTimer?.Stop();
            _windowTracker.Dispose();
            _hotkey.Dispose();
            _clipboardHotkey.Dispose();
        }

        // ── Config application ──────────────────────────────────────────────

        private string _lastPanelHash = "";

        private void ComputeForegroundColor()
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_config.BarColor);
                double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                double eff = (c.A / 255.0) * lum + (1.0 - c.A / 255.0) * 128.0;
                _fgBase = eff > 150 ? Colors.Black : Colors.White;
            }
            catch { _fgBase = Colors.White; }
        }

        private void UpdatePopupResources()
        {
            var app = Application.Current;
            bool isDark = _fgBase == Colors.White;
            app.Resources["PopupFgBright"] = new SolidColorBrush(Color.FromArgb(0xEE, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupFgNormal"] = new SolidColorBrush(Color.FromArgb(0xCC, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupFgDim"] = new SolidColorBrush(Color.FromArgb(0x88, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupFgLabel"] = new SolidColorBrush(Color.FromArgb(0x99, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupAccent"] = new SolidColorBrush(Color.FromArgb(0x40, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupSeparator"] = new SolidColorBrush(Color.FromArgb(0x30, _fgBase.R, _fgBase.G, _fgBase.B));
            app.Resources["PopupThumb"] = new SolidColorBrush(Color.FromArgb(0xDD, _fgBase.R, _fgBase.G, _fgBase.B));
            // Input/button colors adapt to dark vs light background
            app.Resources["PopupInputBg"] = new SolidColorBrush(isDark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xF0, 0xF0, 0xF0));
            app.Resources["PopupButtonBg"] = new SolidColorBrush(isDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE0, 0xE0, 0xE0));
            app.Resources["PopupInputBorder"] = new SolidColorBrush(isDark ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0xBB, 0xBB, 0xBB));
            app.Resources["PopupInputFg"] = new SolidColorBrush(isDark ? Colors.White : Colors.Black);
            app.Resources["PopupHoverBg"] = new SolidColorBrush(Color.FromArgb(0x20, _fgBase.R, _fgBase.G, _fgBase.B));
        }

        private void ApplyConfig()
        {
            ComputeForegroundColor();
            UpdatePopupResources();

            AppNameText.Text = _config.BarName;
            AppNameText.Foreground = new SolidColorBrush(FgNormal);
            WindowTitleText.Foreground = new SolidColorBrush(FgDim);
            _edgeDetector.RevealDelayMs = _config.RevealDelayMs;

            // Window title visibility
            WindowTitleText.Visibility = _config.ShowWindowTitle ? Visibility.Visible : Visibility.Collapsed;
            if (_config.ShowWindowTitle) _windowTracker.Start(); else _windowTracker.Stop();

            // Startup
            SetStartup(_config.RunAtStartup);

            // Always visible
            if (_config.AlwaysVisible && !_isVisible)
                OnReveal();

            // Rebuild icons if modules, order, or bar color changed
            var panelHash = string.Join(",", _config.RightItemOrder) + "|" +
                $"{_config.ShowMediaControls},{_config.ShowClipboardHistory},{_config.ShowBluetooth}," +
                $"{_config.ShowBattery},{_config.ShowBrightness},{_config.ShowWifi},{_config.ShowSound}," +
                $"{_config.ShowClock},{_config.ShowDesktopCorner},{_config.ShowTrayIcons}|{_config.BarColor}";

            if (panelHash != _lastPanelHash)
            {
                _lastPanelHash = panelHash;
                BuildRightPanel();
                UpdateClock();
            }
        }

        private void BuildRightPanel()
        {
            RightPanel.Children.Clear();

            foreach (var itemKey in _config.RightItemOrder)
            {
                UIElement? element = itemKey switch
                {
                    "MediaControls" when _config.ShowMediaControls => MakeMediaControls(),
                    "ClipboardHistory" when _config.ShowClipboardHistory => MakeIcon("ClipboardBorder",
                        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z",
                        "Clipboard", Clipboard_Click),
                    "Bluetooth" when _config.ShowBluetooth => MakeIcon("BtBorder",
                        "M17.71 7.71L12 2h-1v7.59L6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 11 14.41V22h1l5.71-5.71-4.3-4.29 4.3-4.29zM13 5.83l1.88 1.88L13 9.59V5.83zm1.88 10.46L13 18.17v-3.76l1.88 1.88z",
                        "Bluetooth", Bluetooth_Click),
                    "Battery" when _config.ShowBattery => MakeBatteryIndicator(),
                    "Brightness" when _config.ShowBrightness => MakeIcon("BrightBorder",
                        "M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1z",
                        "Brightness", Brightness_Click),
                    "Wifi" when _config.ShowWifi => MakeIcon("WifiBorder",
                        "M12 3C7.8 3 3.9 4.6 1 7.4L3.2 9.6C5.5 7.4 8.6 6 12 6s6.5 1.4 8.8 3.6L23 7.4C20.1 4.6 16.2 3 12 3zM12 9c-2.7 0-5.2 1.1-7 2.9L7.2 14c1.3-1.2 3-2 4.8-2s3.5.8 4.8 2l2.2-2.1C17.2 10.1 14.7 9 12 9zm0 6c-1.4 0-2.6.5-3.5 1.5L12 20l3.5-3.5C14.6 15.5 13.4 15 12 15z",
                        "Wi-Fi", Wifi_Click),
                    "Sound" when _config.ShowSound => MakeIcon("SoundBorder",
                        "M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0014 8.3v7.4a4.47 4.47 0 002.5-3.7zM14 3.2v2.1a7 7 0 010 13.4v2.1A9 9 0 0014 3.2z",
                        "Sound", Sound_Click),
                    "TrayIcons" when _config.ShowTrayIcons => MakeIcon("TrayBorder",
                        "M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z",
                        "System Tray", Tray_Click),
                    "Clock" when _config.ShowClock => MakeClockElement(),
                    "ShowDesktop" when _config.ShowDesktopCorner => MakeShowDesktopCorner(),
                    _ => null
                };

                if (element != null)
                    RightPanel.Children.Add(element);
            }
        }

        // ── Icon factory ────────────────────────────────────────────────────

        private UIElement MakeMediaControls()
        {
            var wrapper = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            wrapper.Children.Add(MakeSmallMediaBtn("M6 6h2v12H6zm3.5 6l8.5 6V6z", VK_MEDIA_PREV_TRACK));

            var playPauseBtn = MakeSmallMediaBtn(PlayIcon, VK_MEDIA_PLAY_PAUSE);
            // The Path is inside: Border > Viewbox > Canvas > Path
            var vb = (Viewbox)playPauseBtn.Child;
            var cv = (Canvas)vb.Child;
            _playPausePath = (System.Windows.Shapes.Path)cv.Children[0];
            wrapper.Children.Add(playPauseBtn);

            wrapper.Children.Add(MakeSmallMediaBtn("M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z", VK_MEDIA_NEXT_TRACK));

            // Poll media state to toggle play/pause icon
            _mediaStateTimer?.Stop();
            _mediaStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mediaStateTimer.Tick += async (_, _) => await UpdatePlayPauseIconAsync();
            _mediaStateTimer.Start();
            // Initial check
            _ = UpdatePlayPauseIconAsync();

            var outer = new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(2, 0, 2, 0),
                Child = wrapper,
                ToolTip = "Media Controls",
                VerticalAlignment = VerticalAlignment.Center
            };

            // Hover for now-playing popup
            DispatcherTimer? hoverTimer = null;
            outer.MouseEnter += (_, _) =>
            {
                if (hoverTimer == null)
                {
                    hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    hoverTimer.Tick += (_, _) =>
                    {
                        hoverTimer!.Stop();
                        if (_activePopup is MediaPopup) return;
                        OpenPopup(new MediaPopup());
                    };
                }
                hoverTimer.Start();
            };
            outer.MouseLeave += (_, _) => hoverTimer?.Stop();

            return outer;
        }

        private async System.Threading.Tasks.Task UpdatePlayPauseIconAsync()
        {
            if (_playPausePath == null) return;
            try
            {
                var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = mgr.GetCurrentSession();
                if (session != null)
                {
                    var info = session.GetPlaybackInfo();
                    var isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var target = isPlaying ? PauseIcon : PlayIcon;
                    var current = _playPausePath.Data?.ToString() ?? "";
                    if (current != Geometry.Parse(target).ToString())
                        _playPausePath.Data = Geometry.Parse(target);
                }
            }
            catch { }
        }

        private Border MakeSmallMediaBtn(string pathData, byte vk)
        {
            var path = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(FgNormal),
                Data = Geometry.Parse(pathData)
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            var viewbox = new Viewbox { Width = 10, Height = 10, Child = canvas };

            var border = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(3, 4, 3, 4),
                CornerRadius = new CornerRadius(4),
                Child = viewbox
            };

            border.MouseLeftButtonDown += (_, e) =>
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                e.Handled = true;
            };
            border.MouseEnter += (s, _) => ((Border)s).Background = new SolidColorBrush(HoverBg);
            border.MouseLeave += (s, _) => ((Border)s).Background = Brushes.Transparent;

            return border;
        }

        private Border MakeIcon(string name, string pathData, string tooltip, MouseButtonEventHandler click)
        {
            var path = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(FgNormal),
                Data = Geometry.Parse(pathData)
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            var viewbox = new Viewbox { Width = 14, Height = 14, Child = canvas };

            var border = new Border
            {
                Name = name,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = tooltip,
                Child = viewbox,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.MouseLeftButtonDown += click;
            border.MouseEnter += (s, _) => ((Border)s).Background = new SolidColorBrush(HoverBg);
            border.MouseLeave += (s, _) => ((Border)s).Background = Brushes.Transparent;

            return border;
        }

        private UIElement MakeBatteryIndicator()
        {
            var pct = GetBatteryPercent();
            var text = new TextBlock
            {
                Text = pct >= 0 ? $"🔋 {pct}%" : "⚡ AC",
                Foreground = new SolidColorBrush(FgNormal),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            var border = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = "Battery",
                Child = text,
                Cursor = Cursors.Arrow,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Update every 30s
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (_, _) =>
            {
                var p = GetBatteryPercent();
                text.Text = p >= 0 ? $"🔋 {p}%" : "⚡ AC";
            };
            timer.Start();

            return border;
        }

        private UIElement MakeClockElement()
        {
            var tb = new TextBlock
            {
                Name = "ClockTextBlock",
                Foreground = new SolidColorBrush(FgNormal),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            tb.MouseLeftButtonDown += Clock_Click;
            tb.MouseEnter += (_, _) => tb.Foreground = new SolidColorBrush(FgFull);
            tb.MouseLeave += (_, _) => tb.Foreground = new SolidColorBrush(FgNormal);
            return tb;
        }

        private UIElement MakeShowDesktopCorner()
        {
            var inner = new Border
            {
                Background = new SolidColorBrush(HoverBg),
                Width = 2,
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var border = new Border
            {
                Background = Brushes.Transparent,
                Width = 8,
                Margin = new Thickness(4, 0, -12, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Show Desktop",
                Child = inner,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            border.MouseLeftButtonUp += ShowDesktop_Click;
            border.MouseEnter += (s, _) => ((Border)s).Background = new SolidColorBrush(HoverBg);
            border.MouseLeave += (s, _) => ((Border)s).Background = Brushes.Transparent;
            return border;
        }

        // ── Battery helper ──────────────────────────────────────────────────

        private static int GetBatteryPercent()
        {
            try
            {
                var status = System.Windows.Forms.SystemInformation.PowerStatus;
                if (status.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                    return -1;
                return (int)(status.BatteryLifePercent * 100);
            }
            catch
            {
                // Fallback: use WMI
                return GetBatteryWmi();
            }
        }

        private static int GetBatteryWmi()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT EstimatedChargeRemaining FROM Win32_Battery");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                    return Convert.ToInt32(obj["EstimatedChargeRemaining"]);
            }
            catch { }
            return -1;
        }

        // ── Show / hide ─────────────────────────────────────────────────────

        private void ToggleBar()
        {
            if (_isVisible) OnHide(); else OnReveal();
        }

        private void OnReveal()
        {
            if (_isVisible) return;
            _isVisible = true;

            Dispatcher.Invoke(() =>
            {
                if (_config.PushWindows) _appBar.RegisterBar();

                var anim = new DoubleAnimation
                {
                    From = -BAR_HEIGHT, To = 0,
                    Duration = TimeSpan.FromMilliseconds(ANIM_MS),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(TopProperty, anim);
            });
        }

        private void OnHide()
        {
            if (!_isVisible || _activePopup != null) return;
            if (_config.AlwaysVisible) return;
            _isVisible = false;

            Dispatcher.Invoke(() =>
            {
                var anim = new DoubleAnimation
                {
                    From = 0, To = -BAR_HEIGHT,
                    Duration = TimeSpan.FromMilliseconds(ANIM_MS),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                anim.Completed += (_, _) =>
                {
                    if (_config.PushWindows) _appBar.UnregisterBar();
                };
                BeginAnimation(TopProperty, anim);
            });
        }

        // ── Blur ────────────────────────────────────────────────────────────

        private void EnableBlur()
        {
            uint color = 0xBF000000;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_config.BarColor);
                // Convert ARGB to AABBGGRR for SetWindowCompositionAttribute
                color = ((uint)c.A << 24) | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R;
            }
            catch { }

            BlurHelper.CurrentColor = color;
            BlurHelper.EnableBlur(this, color);
        }

        // ── Popup helpers ───────────────────────────────────────────────────

        private void OpenPopup(Window popup, double rightOffset = 12)
        {
            CloseActivePopup();

            popup.Left = Left + Width - popup.Width - rightOffset;
            popup.Top = Top + BAR_HEIGHT + 4;
            popup.Topmost = true;
            _activePopup = popup;

            if (_config.AnimatePopups)
            {
                popup.Opacity = 0;
                popup.Loaded += (_, _) =>
                {
                    var slide = new DoubleAnimation
                    {
                        From = popup.Top - 10, To = popup.Top,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var fade = new DoubleAnimation
                    {
                        From = 0, To = 1,
                        Duration = TimeSpan.FromMilliseconds(150)
                    };
                    popup.BeginAnimation(TopProperty, slide);
                    popup.BeginAnimation(OpacityProperty, fade);
                };
            }

            popup.Closed += (_, _) =>
            {
                _activePopup = null;
                HideIfMouseOutside();
            };

            popup.Show();
        }

        private void OpenPopupLeft(Window popup)
        {
            CloseActivePopup();

            popup.Left = Left + 12;
            popup.Top = Top + BAR_HEIGHT + 4;
            popup.Topmost = true;
            _activePopup = popup;

            if (_config.AnimatePopups)
            {
                popup.Opacity = 0;
                popup.Loaded += (_, _) =>
                {
                    var slide = new DoubleAnimation
                    {
                        From = popup.Top - 10, To = popup.Top,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var fade = new DoubleAnimation
                    {
                        From = 0, To = 1,
                        Duration = TimeSpan.FromMilliseconds(150)
                    };
                    popup.BeginAnimation(TopProperty, slide);
                    popup.BeginAnimation(OpacityProperty, fade);
                };
            }

            popup.Closed += (_, _) =>
            {
                _activePopup = null;
                HideIfMouseOutside();
            };

            popup.Show();
        }

        private void CloseActivePopup()
        {
            if (_activePopup != null)
            {
                var p = _activePopup;
                _activePopup = null;
                p.Close();
            }
        }

        private void HideIfMouseOutside()
        {
            GetCursorPos(out var pos);
            var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            int barHeightPx = (int)(BAR_HEIGHT * dpiScale);
            if (pos.Y > barHeightPx)
                OnHide();
        }

        // ── Click handlers ──────────────────────────────────────────────────

        private void SettingsButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activePopup is SettingsWindow) { CloseActivePopup(); return; }
            CloseActivePopup();

            var settings = new SettingsWindow(_config);
            settings.Left = Left + 12;
            settings.Top = Top + BAR_HEIGHT + 4;
            settings.Topmost = true;
            _activePopup = settings;

            settings.Closed += (_, _) =>
            {
                _activePopup = null;
                HideIfMouseOutside();
            };

            // Live-apply every settings change
            settings.ConfigChanged += () =>
            {
                ApplyConfig();
                EnableBlur();

                // Update active popup blur color live
                if (_activePopup != null)
                    BlurHelper.EnableBlur(_activePopup);

                try
                {
                    _hotkey.Unregister();
                    _hotkey.Register(this, _config.ToggleShortcut);
                }
                catch { }
try
                {
                    _clipboardHotkey.Unregister();
                    if (!string.IsNullOrWhiteSpace(_config.ClipboardShortcut))
                        _clipboardHotkey.Register(this, _config.ClipboardShortcut);
                }
                catch { }

                
                if (!_config.PushWindows && _isVisible)
                    _appBar.UnregisterBar();
                else if (_config.PushWindows && _isVisible)
                    _appBar.RegisterBar();
            };

            if (_config.AnimatePopups)
            {
                settings.Opacity = 0;
                settings.Loaded += (_, _) =>
                {
                    var slide = new DoubleAnimation
                    {
                        From = settings.Top - 10, To = settings.Top,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(150) };
                    settings.BeginAnimation(TopProperty, slide);
                    settings.BeginAnimation(OpacityProperty, fade);
                };
            }

            settings.Show();
        }

        private void SettingsButton_MouseEnter(object sender, MouseEventArgs e)
        {
            SettingsButton.Background = new SolidColorBrush(HoverBg);
        }

        private void SettingsButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SettingsButton.Background = Brushes.Transparent;
        }

        private void Wifi_Click(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-wifi") { UseShellExecute = true });
            e.Handled = true;
        }

        private void Sound_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is SoundPopup) { CloseActivePopup(); return; }
            OpenPopup(new SoundPopup());
        }

        private void Bluetooth_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is BluetoothPopup) { CloseActivePopup(); return; }
            OpenPopup(new BluetoothPopup());
        }

        private void Brightness_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is BrightnessPopup) { CloseActivePopup(); return; }
            OpenPopup(new BrightnessPopup());
        }

        private void Clipboard_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is ClipboardPopup) { CloseActivePopup(); return; }
            OpenPopup(new ClipboardPopup());
        }

        private void Tray_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is TrayPopup) { CloseActivePopup(); return; }
            OpenPopup(new TrayPopup());
        }

        private void Clock_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_activePopup is CalendarPopup) { CloseActivePopup(); return; }

            var cal = new CalendarPopup();

            // Position near the clock element that was clicked
            CloseActivePopup();
            var clickedElement = sender as FrameworkElement;
            if (clickedElement != null)
            {
                var pos = clickedElement.PointToScreen(new Point(clickedElement.ActualWidth / 2, 0));
                var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                // We'll position it after layout so SizeToContent has resolved
                cal.Loaded += (_, _) =>
                {
                    cal.Left = (pos.X / dpiScale) - (cal.ActualWidth / 2);
                    cal.Top = Top + BAR_HEIGHT + 4;
                    // Clamp to screen right edge
                    var screenW = GetSystemMetrics(SM_CXSCREEN) / dpiScale;
                    if (cal.Left + cal.ActualWidth > screenW)
                        cal.Left = screenW - cal.ActualWidth - 8;
                    if (cal.Left < 0) cal.Left = 8;
                };
            }

            cal.Top = Top + BAR_HEIGHT + 4;
            cal.Left = Left + Width - 300; // initial rough position
            cal.Topmost = true;
            _activePopup = cal;

            if (_config.AnimatePopups)
            {
                cal.Opacity = 0;
                cal.Loaded += (_, _) =>
                {
                    var slide = new DoubleAnimation
                    {
                        From = cal.Top - 10, To = cal.Top,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(150) };
                    cal.BeginAnimation(TopProperty, slide);
                    cal.BeginAnimation(OpacityProperty, fade);
                };
            }

            cal.Closed += (_, _) =>
            {
                _activePopup = null;
                HideIfMouseOutside();
            };

            cal.Show();
        }

        private void ShowDesktop_Click(object sender, MouseButtonEventArgs e)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ── Startup ─────────────────────────────────────────────────────────

        private static void SetStartup(bool enable)
        {
            try
            {
                const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                        key.SetValue("TopBar", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("TopBar", false);
                }
            }
            catch { }
        }

        // ── Clock ───────────────────────────────────────────────────────────

        private void UpdateClock()
        {
            // Find the clock TextBlock in the right panel
            foreach (UIElement child in RightPanel.Children)
            {
                if (child is TextBlock tb && tb.Name == "ClockTextBlock")
                {
                    tb.Text = DateTime.Now.ToString("ddd MMM d   h:mm tt");
                    return;
                }
            }
        }
    }
}

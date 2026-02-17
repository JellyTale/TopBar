using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopBar
{
    public partial class SettingsWindow : Window
    {
        public BarConfig Config { get; private set; }
        public event Action? ConfigChanged;

        private bool _ready, _closing, _isDragging, _isDialogOpen, _suppressEvents;
        private DispatcherTimer? _leaveTimer;
        private Point _dragStart;
        private ObservableCollection<string> _layoutItems = new();

        // Maps layout key → friendly name
        private static readonly Dictionary<string, string> DisplayNames = new()
        {
            ["MediaControls"] = "Media Controls",
            ["ClipboardHistory"] = "Clipboard History",
            ["Bluetooth"] = "Bluetooth",
            ["Battery"] = "Battery",
            ["Brightness"] = "Brightness",
            ["Wifi"] = "Wi-Fi",
            ["Sound"] = "Sound",
            ["TrayIcons"] = "System Tray",
            ["Clock"] = "Clock",
            ["ShowDesktop"] = "Show Desktop"
        };

        // Maps layout key → Config property name for the Show* booleans
        private static readonly Dictionary<string, string> ShowPropNames = new()
        {
            ["MediaControls"] = nameof(BarConfig.ShowMediaControls),
            ["ClipboardHistory"] = nameof(BarConfig.ShowClipboardHistory),
            ["Bluetooth"] = nameof(BarConfig.ShowBluetooth),
            ["Battery"] = nameof(BarConfig.ShowBattery),
            ["Brightness"] = nameof(BarConfig.ShowBrightness),
            ["Wifi"] = nameof(BarConfig.ShowWifi),
            ["Sound"] = nameof(BarConfig.ShowSound),
            ["TrayIcons"] = nameof(BarConfig.ShowTrayIcons),
            ["Clock"] = nameof(BarConfig.ShowClock),
            ["ShowDesktop"] = nameof(BarConfig.ShowDesktopCorner),
        };

        public SettingsWindow(BarConfig config)
        {
            InitializeComponent();
            Loaded += (_, _) => BlurHelper.EnableBlur(this);
            ContentRendered += (_, _) => { _ready = true; _suppressEvents = false; };

            Config = config;
            _suppressEvents = true;

            // ── General ──
            NameBox.Text = Config.BarName;
            AlwaysVisibleCheckBox.IsChecked = Config.AlwaysVisible;
            PushCheckBox.IsChecked = Config.PushWindows;
            DelaySlider.Value = Config.RevealDelayMs;
            DelayLabel.Text = Config.RevealDelayMs.ToString();
            StartupCheckBox.IsChecked = Config.RunAtStartup;
            ShortcutBox.Text = Config.ToggleShortcut;
            ClipShortcutBox.Text = Config.ClipboardShortcut;

            // ── Appearance ──
            AnimateCheckBox.IsChecked = Config.AnimatePopups;
            ColorBox.Text = Config.BarColor;

            // ── Modules (unified) ──
            ModWindowTitle.IsChecked = Config.ShowWindowTitle;
            _layoutItems = new ObservableCollection<string>(Config.RightItemOrder);
            RebuildLayoutList();

            // ── Auto-save handlers ──
            NameBox.LostFocus += (_, _) => PushConfigFromUI();
            AlwaysVisibleCheckBox.Checked += (_, _) => PushConfigFromUI();
            AlwaysVisibleCheckBox.Unchecked += (_, _) => PushConfigFromUI();
            PushCheckBox.Checked += (_, _) => PushConfigFromUI();
            PushCheckBox.Unchecked += (_, _) => PushConfigFromUI();
            DelaySlider.ValueChanged += (_, _) => PushConfigFromUI();
            StartupCheckBox.Checked += (_, _) => PushConfigFromUI();
            StartupCheckBox.Unchecked += (_, _) => PushConfigFromUI();
            ShortcutBox.LostFocus += (_, _) => PushConfigFromUI();
            ClipShortcutBox.LostFocus += (_, _) => PushConfigFromUI();
            AnimateCheckBox.Checked += (_, _) => PushConfigFromUI();
            AnimateCheckBox.Unchecked += (_, _) => PushConfigFromUI();
            ColorBox.LostFocus += (_, _) => PushConfigFromUI();
            ModWindowTitle.Checked += (_, _) => PushConfigFromUI();
            ModWindowTitle.Unchecked += (_, _) => PushConfigFromUI();

            NameBox.SelectAll();
            NameBox.Focus();
        }

        // ── Build the unified layout list (grip + name + toggle) ───────────

        private void RebuildLayoutList()
        {
            var items = new List<object>();
            foreach (var key in _layoutItems)
            {
                var display = DisplayNames.TryGetValue(key, out var v) ? v : key;
                bool isOn = GetShowFlag(key);

                var gripBrush = Application.Current.Resources["PopupFgDim"] as SolidColorBrush
                    ?? new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                var labelBrush = Application.Current.Resources["PopupFgBright"] as SolidColorBrush
                    ?? new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));

                // Build row: ☰  Name  [checkbox]
                var grip = new TextBlock
                {
                    Text = "☰",
                    Foreground = gripBrush,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var label = new TextBlock
                {
                    Text = display,
                    Foreground = labelBrush,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var cb = new CheckBox
                {
                    IsChecked = isOn,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = key
                };
                cb.Checked += ModuleToggle_Changed;
                cb.Unchecked += ModuleToggle_Changed;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(grip, 0);
                Grid.SetColumn(label, 1);
                Grid.SetColumn(cb, 2);
                grid.Children.Add(grip);
                grid.Children.Add(label);
                grid.Children.Add(cb);

                items.Add(grid);
            }
            LayoutList.ItemsSource = items;
        }

        private void ModuleToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string key)
            {
                SetShowFlag(key, cb.IsChecked == true);
                PushConfigFromUI();
            }
        }

        private bool GetShowFlag(string key) => key switch
        {
            "MediaControls" => Config.ShowMediaControls,
            "ClipboardHistory" => Config.ShowClipboardHistory,
            "Bluetooth" => Config.ShowBluetooth,
            "Battery" => Config.ShowBattery,
            "Brightness" => Config.ShowBrightness,
            "Wifi" => Config.ShowWifi,
            "Sound" => Config.ShowSound,
            "TrayIcons" => Config.ShowTrayIcons,
            "Clock" => Config.ShowClock,
            "ShowDesktop" => Config.ShowDesktopCorner,
            _ => true
        };

        private void SetShowFlag(string key, bool value)
        {
            switch (key)
            {
                case "MediaControls": Config.ShowMediaControls = value; break;
                case "ClipboardHistory": Config.ShowClipboardHistory = value; break;
                case "Bluetooth": Config.ShowBluetooth = value; break;
                case "Battery": Config.ShowBattery = value; break;
                case "Brightness": Config.ShowBrightness = value; break;
                case "Wifi": Config.ShowWifi = value; break;
                case "Sound": Config.ShowSound = value; break;
                case "TrayIcons": Config.ShowTrayIcons = value; break;
                case "Clock": Config.ShowClock = value; break;
                case "ShowDesktop": Config.ShowDesktopCorner = value; break;
            }
        }

        // ── Push config (auto-save) ─────────────────────────────────────────

        private void PushConfigFromUI()
        {
            if (_suppressEvents) return;

            var trimmed = NameBox.Text.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                Config.BarName = trimmed;

            Config.AlwaysVisible = AlwaysVisibleCheckBox.IsChecked == true;
            Config.PushWindows = PushCheckBox.IsChecked == true;
            Config.RevealDelayMs = (int)DelaySlider.Value;
            Config.RunAtStartup = StartupCheckBox.IsChecked == true;
            Config.ToggleShortcut = ShortcutBox.Text.Trim();
            Config.ClipboardShortcut = ClipShortcutBox.Text.Trim();

            Config.AnimatePopups = AnimateCheckBox.IsChecked == true;
            Config.BarColor = ColorBox.Text.Trim();

            Config.ShowWindowTitle = ModWindowTitle.IsChecked == true;

            // Show* flags for right-panel items are set directly via SetShowFlag
            Config.RightItemOrder = _layoutItems.ToList();

            Config.Save();
            ConfigChanged?.Invoke();
        }

        // ── Tabs ────────────────────────────────────────────────────────────

        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
                SwitchTab(tag);
        }

        private void SwitchTab(string name)
        {
            PanelGeneral.Visibility = name == "General" ? Visibility.Visible : Visibility.Collapsed;
            PanelAppearance.Visibility = name == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
            PanelModules.Visibility = name == "Modules" ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tab in new[] { TabGeneral, TabAppearance, TabModules })
            {
                bool active = (string)tab.Tag == name;
                var accentBrush = Application.Current.Resources["PopupAccent"] as SolidColorBrush;
                var brightBrush = Application.Current.Resources["PopupFgBright"] as SolidColorBrush;
                var labelBrush = Application.Current.Resources["PopupFgLabel"] as SolidColorBrush;
                tab.Background = active
                    ? accentBrush ?? Brushes.Transparent
                    : Brushes.Transparent;
                if (tab.Child is TextBlock tb)
                    tb.Foreground = active
                        ? brightBrush ?? Brushes.White
                        : labelBrush ?? Brushes.Gray;
            }
        }

        // ── Appearance helpers ──────────────────────────────────────────────

        private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(ColorBox.Text);
                ColorPreview.Background = new SolidColorBrush(color);
            }
            catch { }
        }

        private void Preset_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string hex)
            {
                ColorBox.Text = hex;
                PushConfigFromUI();
            }
        }

        private void ColorPreview_Click(object sender, MouseButtonEventArgs e)
        {
            _isDialogOpen = true;
            try
            {
                var dlg = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    AnyColor = true
                };

                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(ColorBox.Text);
                    dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                catch { }

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var sc = dlg.Color;
                    ColorBox.Text = $"#{sc.A:X2}{sc.R:X2}{sc.G:X2}{sc.B:X2}";
                    PushConfigFromUI();
                }
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        // ── Layout drag-to-reorder ──────────────────────────────────────────

        private void LayoutList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
        }

        private void LayoutList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            var diff = _dragStart - pos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var listBox = (ListBox)sender;
                int idx = listBox.SelectedIndex;
                if (idx >= 0)
                {
                    _isDragging = true;
                    DragDrop.DoDragDrop(listBox, idx.ToString(), DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void LayoutList_Drop(object sender, DragEventArgs e)
        {
            var data = e.Data.GetData(typeof(string)) as string;
            if (data == null || !int.TryParse(data, out int sourceIdx)) return;
            if (sourceIdx < 0 || sourceIdx >= _layoutItems.Count) return;

            var listBox = (ListBox)sender;
            var dropPos = e.GetPosition(listBox);
            int targetIdx = _layoutItems.Count - 1;

            for (int i = 0; i < listBox.Items.Count; i++)
            {
                var container = (ListBoxItem)listBox.ItemContainerGenerator.ContainerFromIndex(i);
                if (container != null)
                {
                    var itemPos = container.TranslatePoint(new Point(0, container.ActualHeight / 2), listBox);
                    if (dropPos.Y < itemPos.Y)
                    {
                        targetIdx = i;
                        break;
                    }
                }
            }

            if (sourceIdx != targetIdx)
            {
                var key = _layoutItems[sourceIdx];
                _layoutItems.RemoveAt(sourceIdx);
                if (targetIdx > sourceIdx) targetIdx--;
                _layoutItems.Insert(targetIdx, key);

                RebuildLayoutList();
                PushConfigFromUI();
            }
        }

        // ── Delay slider label ──────────────────────────────────────────────

        private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DelayLabel != null)
                DelayLabel.Text = ((int)e.NewValue).ToString();
        }

        // ── Close logic ─────────────────────────────────────────────────────

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_ready && !_isDragging && !_isDialogOpen) SafeClose();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging || _isDialogOpen) return;
            if (_leaveTimer == null)
            {
                _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); SafeClose(); };
            }
            _leaveTimer.Start();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e) { _leaveTimer?.Stop(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) SafeClose();
            base.OnKeyDown(e);
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            _leaveTimer?.Stop();
            Close();
        }
    }
}

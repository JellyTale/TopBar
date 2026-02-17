using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TopBar
{
    /// <summary>
    /// Registers a system-wide hotkey (e.g. Ctrl+Shift+T) to toggle the bar.
    /// </summary>
    internal sealed class GlobalHotkey : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly int _hotkeyId;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private IntPtr _hwnd;
        private HwndSource? _source;
        public event Action? Pressed;

        public GlobalHotkey(int id = 9000) { _hotkeyId = id; }

        public void Register(Window window, string shortcut)
        {
            Unregister();

            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            _hwnd = helper.Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);

            ParseShortcut(shortcut, out uint mods, out uint vk);
            RegisterHotKey(_hwnd, _hotkeyId, mods, vk);
        }

        public void Unregister()
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, _hotkeyId);
                _source?.RemoveHook(WndProc);
                _hwnd = IntPtr.Zero;
                _source = null;
            }
        }

        public void Dispose() => Unregister();

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
            {
                Pressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static void ParseShortcut(string shortcut, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            var parts = shortcut.Split('+');
            foreach (var part in parts)
            {
                var p = part.Trim().ToLowerInvariant();
                switch (p)
                {
                    case "ctrl": case "control": mods |= MOD_CTRL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": case "windows": mods |= MOD_WIN; break;
                    default:
                        if (p.Length == 1 && char.IsLetterOrDigit(p[0]))
                            vk = (uint)char.ToUpper(p[0]);
                        else if (Enum.TryParse<Key>(part.Trim(), true, out var key))
                            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        break;
                }
            }
        }
    }
}

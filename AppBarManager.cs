using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static TopBar.NativeMethods;

namespace TopBar
{
    /// <summary>
    /// Registers / unregisters the window as a Windows AppBar docked at the top
    /// of the primary monitor.  When registered, other windows are pushed down
    /// to make room.
    /// </summary>
    internal sealed class AppBarManager : IDisposable
    {
        private readonly Window _window;
        private IntPtr _hwnd;
        private bool _isRegistered;
        private uint _callbackMessageId;
        private HwndSource? _hwndSource;

        public int BarHeight { get; set; } = 36;

        public AppBarManager(Window window)
        {
            _window = window;
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>Reserve screen space at the top (pushes other windows down).</summary>
        public void RegisterBar()
        {
            if (_isRegistered) return;

            EnsureHwnd();

            _callbackMessageId = RegisterWindowMessage("TopBarAppBarMessage");

            var abd = NewData();
            abd.uCallbackMessage = _callbackMessageId;
            SHAppBarMessage(ABM_NEW, ref abd);

            // Hook the message loop so we can respond to ABN_POSCHANGED etc.
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            _isRegistered = true;

            SetPosition();
        }

        /// <summary>Release the reserved screen space.</summary>
        public void UnregisterBar()
        {
            if (!_isRegistered) return;

            var abd = NewData();
            SHAppBarMessage(ABM_REMOVE, ref abd);

            _hwndSource?.RemoveHook(WndProc);
            _isRegistered = false;
        }

        /// <summary>Tell Windows exactly where the bar sits.</summary>
        public void SetPosition()
        {
            if (!_isRegistered) return;

            var abd = NewData();
            abd.uEdge = ABE_TOP;
            abd.rc.Left = 0;
            abd.rc.Top = 0;
            abd.rc.Right = GetSystemMetrics(SM_CXSCREEN);
            abd.rc.Bottom = BarHeight;

            SHAppBarMessage(ABM_QUERYPOS, ref abd);
            SHAppBarMessage(ABM_SETPOS, ref abd);

            // Do NOT call MoveWindow here — the WPF animation controls
            // the window's Top property.  We only need SHAppBarMessage
            // to reserve the screen space so other windows stay clear.
        }

        public void Dispose()
        {
            UnregisterBar();
        }

        // ── Internals ───────────────────────────────────────────────────────

        private void EnsureHwnd()
        {
            if (_hwnd != IntPtr.Zero) return;

            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _hwnd = helper.Handle;

            // Hide from Alt-Tab / taskbar.
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
        }

        private APPBARDATA NewData()
        {
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = _hwnd;
            return abd;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)_callbackMessageId)
            {
                switch (wParam.ToInt32())
                {
                    case ABN_POSCHANGED:
                        SetPosition();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static TopBar.NativeMethods;

namespace TopBar
{
    /// <summary>
    /// Detects the mouse at the top edge of the screen and manages the
    /// show / hide animation of the bar.  Uses a global low-level mouse
    /// hook so it works even when the bar window does not have focus.
    /// </summary>
    internal sealed class MouseEdgeDetector : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelMouseProc _proc;

        /// <summary>How many pixels from the top of the screen trigger the reveal.</summary>
        public int EdgeThreshold { get; set; } = 2;

        public event Action? MouseEnteredTopEdge;
        public event Action? MouseLeftTopEdge;

        /// <summary>Height of the bar (px).  Mouse below this = "left".</summary>
        public int BarHeight { get; set; } = 36;

        /// <summary>Extra margin (px) below the bar before we consider the mouse "away".</summary>
        public int LeaveMargin { get; set; } = 20;

        /// <summary>How long (ms) the mouse must stay at the top edge before firing.</summary>
        public int RevealDelayMs { get; set; } = 200;

        private bool _isInside;
        private bool _waitingToReveal;
        private DispatcherTimer? _delayTimer;

        public MouseEdgeDetector()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc,
                                        GetModuleHandle(curModule.ModuleName), 0);
        }

        public void Uninstall()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        public void Dispose()
        {
            _delayTimer?.Stop();
            Uninstall();
        }

        private void StartRevealTimer()
        {
            if (_delayTimer == null)
            {
                _delayTimer = new DispatcherTimer();
                _delayTimer.Tick += (_, _) =>
                {
                    _delayTimer.Stop();
                    if (_waitingToReveal)
                    {
                        _waitingToReveal = false;
                        _isInside = true;
                        MouseEnteredTopEdge?.Invoke();
                    }
                };
            }
            _delayTimer.Interval = TimeSpan.FromMilliseconds(RevealDelayMs);
            _waitingToReveal = true;
            _delayTimer.Start();
        }

        private void CancelRevealTimer()
        {
            _waitingToReveal = false;
            _delayTimer?.Stop();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int y = info.pt.Y;

                if (!_isInside && !_waitingToReveal && y <= EdgeThreshold)
                {
                    // Start the delay timer instead of firing immediately.
                    StartRevealTimer();
                }
                else if (_waitingToReveal && y > EdgeThreshold)
                {
                    // Mouse left the edge before delay elapsed — cancel.
                    CancelRevealTimer();
                }
                else if (_isInside && y > BarHeight + LeaveMargin)
                {
                    _isInside = false;
                    MouseLeftTopEdge?.Invoke();
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}

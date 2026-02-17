using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using static TopBar.NativeMethods;

namespace TopBar
{
    /// <summary>
    /// Polls the foreground window title every 500ms.
    /// </summary>
    internal sealed class ActiveWindowTracker : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);

        private readonly DispatcherTimer _timer;
        private string _lastTitle = string.Empty;

        public event Action<string>? TitleChanged;

        public ActiveWindowTracker()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => Poll();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
        }

        private void Poll()
        {
            var hwnd = GetForegroundWindow();
            int len = GetWindowTextLength(hwnd);
            if (len <= 0)
            {
                if (_lastTitle != string.Empty)
                {
                    _lastTitle = string.Empty;
                    TitleChanged?.Invoke(string.Empty);
                }
                return;
            }
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title != _lastTitle)
            {
                _lastTitle = title;
                TitleChanged?.Invoke(title);
            }
        }
    }
}

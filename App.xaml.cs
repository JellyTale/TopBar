using System.Windows;
using static TopBar.NativeMethods;

namespace TopBar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Ensure correct pixel coordinates on high-DPI displays.
            SetProcessDPIAware();
            base.OnStartup(e);
        }
    }
}

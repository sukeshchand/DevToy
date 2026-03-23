using System.Runtime.InteropServices;

namespace DevToy;

static class NativeMethods
{
    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

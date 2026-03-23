using System.Runtime.InteropServices;

namespace DevToy;

static class NativeMethods
{
    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool WriteConsole(IntPtr hConsoleOutput, string lpBuffer, int nNumberOfCharsToWrite, out int lpNumberOfCharsWritten, IntPtr lpReserved);

    internal const int STD_OUTPUT_HANDLE = -11;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetConsoleTitle(System.Text.StringBuilder lpConsoleTitle, int nSize);
}

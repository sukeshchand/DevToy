using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ProdToy;

/// <summary>
/// Detects triple Ctrl key taps (3 press+release within a time window)
/// using a low-level keyboard hook. Fires TripleTapped when detected.
/// </summary>
class TripleCtrlDetector : IDisposable
{
    private IntPtr _hookId;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly long[] _tapTimes = new long[3];
    private int _tapCount;
    private const long MaxWindowTicks = 600 * TimeSpan.TicksPerMillisecond; // 600ms for all 3 taps

    public event Action? TripleTapped;

    public TripleCtrlDetector()
    {
        // Must keep a reference to prevent GC of the delegate
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
    }

    private static IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        return NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            proc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYUP)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_LCONTROL || vkCode == NativeMethods.VK_RCONTROL)
            {
                long now = DateTime.UtcNow.Ticks;

                // Shift previous taps and add new one
                _tapTimes[0] = _tapTimes[1];
                _tapTimes[1] = _tapTimes[2];
                _tapTimes[2] = now;
                _tapCount = Math.Min(_tapCount + 1, 3);

                if (_tapCount >= 3 && (now - _tapTimes[0]) <= MaxWindowTicks)
                {
                    _tapCount = 0;
                    _tapTimes[0] = _tapTimes[1] = _tapTimes[2] = 0;
                    TripleTapped?.Invoke();
                }
            }
            else
            {
                // Any other key resets the sequence
                _tapCount = 0;
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}

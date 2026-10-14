using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace AmbiMote.Core.Engine;

/// <summary>
/// A timer to wait on that fires when asked, to well under a millisecond. Ordinary waits wake on the Windows clock tick
/// of about 15.6 ms, so 30 or 60 frames a second come out uneven, which shows as stutter in slow fades.
/// </summary>
internal sealed class HighResolutionTimer : WaitHandle
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;

    public HighResolutionTimer()
    {
        // High resolution timers need Windows 10 1803; fall back to an ordinary one before that.
        var handle = CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
        }

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException($"Could not create a timer (error {Marshal.GetLastWin32Error()}).");
        }

        SafeWaitHandle = handle;
    }

    /// <summary>Signals the timer once, after <paramref name="delay"/>.</summary>
    public void Start(TimeSpan delay)
    {
        // A negative due time is relative, in 100 ns units, which are TimeSpan ticks.
        long due = -Math.Max(1, delay.Ticks);
        SetWaitableTimerEx(SafeWaitHandle, in due, 0, 0, 0, 0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(
        SafeWaitHandle timer, in long dueTime, int period, nint completionRoutine, nint argument, nint wakeContext, uint tolerableDelay);
}

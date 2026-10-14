using System.Diagnostics;
using System.Runtime.InteropServices;

using AmbiMote.Core.Engine;

namespace AmbiMote.App;

/// <summary>Finds out which program is in front and whether its window fills the screen.</summary>
internal static unsafe partial class ForegroundWindows
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    /// <summary>The desktop and the taskbar: in front when clicked, but never a full-screen app.</summary>
    private static readonly HashSet<string> s_shellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>The window in front, or null if there is none or it is AmbiMote's own.</summary>
    public static ForegroundWindow? Read()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return null;
        }

        uint processId;
        GetWindowThreadProcessId(hwnd, &processId);
        if (processId == Environment.ProcessId)
        {
            return null;
        }

        return new ForegroundWindow(ProcessFileName(processId), !IsShell(hwnd) && IsFullScreen(hwnd));
    }

    /// <summary>The file names of programs with a window open, such as <c>game.exe</c>, for choosing one to add a rule for.</summary>
    public static IReadOnlyList<string> RunningApps()
    {
        var apps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && process.MainWindowHandle != 0)
                    {
                        apps.Add(process.ProcessName + ".exe");
                    }
                }
                catch (InvalidOperationException)
                {
                    // It exited while we looked.
                }
            }
        }

        return [.. apps];
    }

    private static string? ProcessFileName(uint processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            char* path = stackalloc char[1024];
            uint length = 1024;
            return QueryFullProcessImageNameW(process, 0, path, &length) ? Path.GetFileName(new string(path, 0, (int)length)) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsShell(nint hwnd)
    {
        char* name = stackalloc char[64];
        int length = GetClassNameW(hwnd, name, 64);
        return length > 0 && s_shellClasses.Contains(new string(name, 0, length));
    }

    /// <summary>
    /// Whether the window covers its whole monitor without a title bar, as games and full-screen video do. A maximised
    /// window keeps its title bar, so it does not count even when the taskbar hides itself.
    /// </summary>
    private static bool IsFullScreen(nint hwnd)
    {
        if (SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN)
        {
            return true;
        }

        RECT window;
        var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!GetWindowRect(hwnd, &window) || !GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), &monitor))
        {
            return false;
        }

        var screen = monitor.rcMonitor;
        bool covers = window.left <= screen.left && window.top <= screen.top && window.right >= screen.right && window.bottom >= screen.bottom;
        return covers && (GetWindowLongPtrW(hwnd, GWL_STYLE) & WS_CAPTION) != WS_CAPTION;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassNameW(nint hwnd, char* className, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* path, uint* length);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}

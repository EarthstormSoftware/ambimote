using System.Runtime.InteropServices;

namespace AmbiMote.App;

/// <summary>
/// A hidden window on the UI thread, for Windows messages that need a window to arrive at, such as lock and screen
/// notifications and hotkeys. Messages the handler does not answer (null) get the default handling.
/// </summary>
internal sealed unsafe partial class MessageWindow : IDisposable
{
    private readonly WndProc _wndProc;
    private readonly Func<uint, nint, nint, nint?> _handler;
    private readonly string _className;
    private bool _disposed;

    /// <param name="name">Part of the window class name, to tell AmbiMote's windows apart in tools like Spy++.</param>
    /// <param name="handler">Given each message, wParam and lParam; returns the result, or null for the default.</param>
    public MessageWindow(string name, Func<uint, nint, nint, nint?> handler)
    {
        _handler = handler;
        _wndProc = OnMessage;
        _className = $"AmbiMote.{name}.{Guid.NewGuid():N}";
        fixed (char* className = _className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandleW(null),
                lpszClassName = className,
            };
            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new InvalidOperationException($"Could not register the {name} window class (error {Marshal.GetLastPInvokeError()}).");
            }

            Handle = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, 0, 0, windowClass.hInstance, 0);
        }

        if (Handle == 0)
        {
            throw new InvalidOperationException($"Could not create the {name} window (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    public nint Handle { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyWindow(Handle);
        fixed (char* className = _className)
        {
            UnregisterClassW(className, GetModuleHandleW(null));
        }
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam) =>
        _handler(message, wParam, lParam) ?? DefWindowProcW(hwnd, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(char* className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(
        uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
}

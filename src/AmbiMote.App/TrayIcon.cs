using System.Runtime.InteropServices;

namespace AmbiMote.App;

/// <summary>One entry in the tray icon's menu. A <see cref="Text"/> of "-" is a separator.</summary>
internal sealed record TrayMenuItem(
    string Text,
    Action? Click = null,
    bool Checked = false,
    bool Enabled = true,
    bool IsDefault = false,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new("-");
}

/// <summary>
/// An icon in the notification area, with a native menu that follows the Windows light or dark mode. WinUI has no tray
/// icon of its own, so this talks to the Shell directly through a hidden window on the UI thread.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_WARNING = 0x02;
    private const uint NotifyIconVersion4 = 4;
    private const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_CHECKED = 0x0008, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020;
    private const int SM_CXSMICON = 49;

    private readonly WndProc _wndProc;
    private readonly string _className = $"AmbiMote.Tray.{Guid.NewGuid():N}";
    private readonly nint _hwnd;
    private readonly uint _taskbarCreated;
    private readonly Dictionary<string, nint> _icons = [];
    private string? _iconPath;
    private string _tooltip = "";
    private bool _added;
    private bool _disposed;

    public TrayIcon()
    {
        _wndProc = OnMessage;
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
                throw new InvalidOperationException($"Could not register the tray window class (error {Marshal.GetLastPInvokeError()}).");
            }

            // A hidden top-level window rather than a message-only one, which would miss TaskbarCreated.
            _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, 0, 0, windowClass.hInstance, 0);
        }

        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"Could not create the tray window (error {Marshal.GetLastPInvokeError()}).");
        }

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        DarkMenus.Allow(_hwnd);
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>The icon was clicked, or chosen with the keyboard.</summary>
    public event EventHandler? Clicked;

    /// <summary>Builds the menu each time it opens, so it always shows the current state.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuRequested { get; set; }

    /// <summary>Shows the icon from an .ico file, at the size Windows uses for tray icons.</summary>
    public void SetIcon(string path)
    {
        _iconPath = path;
        Update();
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        Update();
    }

    public void ShowWarning(string title, string message)
    {
        var data = NewData(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(message, data.szInfo, 256);
        data.dwInfoFlags = NIIF_WARNING;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, &data);
        }

        DestroyWindow(_hwnd);
        fixed (char* className = _className)
        {
            UnregisterClassW(className, GetModuleHandleW(null));
        }

        foreach (nint icon in _icons.Values)
        {
            DestroyIcon(icon);
        }
    }

    private static void Copy(string text, char* buffer, int length)
    {
        int count = Math.Min(text.Length, length - 1);
        text.AsSpan(0, count).CopyTo(new Span<char>(buffer, length));
        buffer[count] = '\0';
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
    };

    private void Update()
    {
        if (_disposed || _iconPath is null)
        {
            return;
        }

        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = LoadIcon(_iconPath);
        Copy(_tooltip, data.szTip, 128);
        if (_added && Shell_NotifyIconW(NIM_MODIFY, &data))
        {
            return;
        }

        // Not added yet, or Explorer restarted and forgot it.
        _added = Shell_NotifyIconW(NIM_ADD, &data);
        if (_added)
        {
            data.uVersion = NotifyIconVersion4;
            Shell_NotifyIconW(NIM_SETVERSION, &data);
        }
    }

    private nint LoadIcon(string path)
    {
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        string key = $"{path}|{size}";
        if (!_icons.TryGetValue(key, out nint icon))
        {
            fixed (char* file = path)
            {
                icon = LoadImageW(0, file, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
            }

            _icons[key] = icon;
        }

        return icon;
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT or NIN_KEYSELECT:
                    Clicked?.Invoke(this, EventArgs.Empty);
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    break;
            }

            return 0;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            _added = false;
            Update();
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        if (MenuRequested?.Invoke() is not { } items)
        {
            return;
        }

        var actions = new List<Action?>();
        nint menu = BuildMenu(items, actions);
        try
        {
            // Without this the menu does not close when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            int chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, x, y, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
            if (chosen > 0)
            {
                actions[chosen - 1]?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static nint BuildMenu(IReadOnlyList<TrayMenuItem> items, List<Action?> actions)
    {
        nint menu = CreatePopupMenu();
        foreach (var item in items)
        {
            fixed (char* text = item.Text)
            {
                if (item.Text == "-")
                {
                    AppendMenuW(menu, MF_SEPARATOR, 0, null);
                }
                else if (item.Children is { } children)
                {
                    AppendMenuW(menu, MF_POPUP | (item.Enabled ? 0 : MF_GRAYED), (nuint)BuildMenu(children, actions), text);
                }
                else
                {
                    actions.Add(item.Click);
                    uint id = (uint)actions.Count;
                    AppendMenuW(menu, MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED), id, text);
                    if (item.IsDefault)
                    {
                        SetMenuDefaultItem(menu, id, 0);
                    }
                }
            }
        }

        return menu;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
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

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint instance, char* name, uint type, int width, int height, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, char* text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    /// <summary>
    /// Lets native menus follow the Windows dark mode. uxtheme exports this only by ordinal; it is undocumented but used by
    /// Explorer, Notepad and many others, and if it is missing the menus are simply light.
    /// </summary>
    private static partial class DarkMenus
    {
        private const int AllowDarkModeForWindowOrdinal = 133;
        private const int SetPreferredAppModeOrdinal = 135;
        private const int FlushMenuThemesOrdinal = 136;
        private const int AllowDark = 1;

        public static void Allow(nint hwnd)
        {
            if (!NativeLibrary.TryLoad("uxtheme.dll", out nint uxtheme))
            {
                return;
            }

            nint setPreferredAppMode = GetProcAddress(uxtheme, SetPreferredAppModeOrdinal);
            nint allowDarkModeForWindow = GetProcAddress(uxtheme, AllowDarkModeForWindowOrdinal);
            nint flushMenuThemes = GetProcAddress(uxtheme, FlushMenuThemesOrdinal);
            if (setPreferredAppMode == 0 || allowDarkModeForWindow == 0 || flushMenuThemes == 0)
            {
                return;
            }

            ((delegate* unmanaged<int, int>)setPreferredAppMode)(AllowDark);
            ((delegate* unmanaged<nint, int, int>)allowDarkModeForWindow)(hwnd, 1);
            ((delegate* unmanaged<void>)flushMenuThemes)();
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint GetProcAddress(nint module, nint ordinal);
    }
}

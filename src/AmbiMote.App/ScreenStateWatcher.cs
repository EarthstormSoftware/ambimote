using System.Runtime.InteropServices;

namespace AmbiMote.App;

/// <summary>
/// Watches for the PC being locked and the screens turning off, through a hidden window on the UI thread that Windows
/// sends both notifications to. <see cref="Changed"/> is raised on the UI thread.
/// </summary>
internal sealed unsafe partial class ScreenStateWatcher : IDisposable
{
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_POWERBROADCAST = 0x0218;
    private const nint WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
    private const nint PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint NOTIFY_FOR_THIS_SESSION = 0;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    /// <summary>Whether the screens are on, off or dimmed; Windows sends it once straight after registering.</summary>
    private static readonly Guid s_consoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private readonly MessageWindow _window;
    private readonly nint _powerNotification;
    private readonly bool _sessionNotification;
    private bool _disposed;

    public ScreenStateWatcher()
    {
        _window = new MessageWindow("Screen", OnMessage);

        // If either fails, that change is simply never seen and the lights stay on, as they did before.
        _sessionNotification = WTSRegisterSessionNotification(_window.Handle, NOTIFY_FOR_THIS_SESSION);
        var guid = s_consoleDisplayState;
        _powerNotification = RegisterPowerSettingNotification(_window.Handle, &guid, DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    /// <summary><see cref="IsLocked"/> or <see cref="AreScreensOff"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Whether the PC is locked. Assumed not when AmbiMote starts, since it starts when the user signs in.
    /// </summary>
    public bool IsLocked { get; private set; }

    /// <summary>Whether Windows has turned the screens off. Dimmed counts as on.</summary>
    public bool AreScreensOff { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_powerNotification != 0)
        {
            UnregisterPowerSettingNotification(_powerNotification);
        }

        if (_sessionNotification)
        {
            WTSUnRegisterSessionNotification(_window.Handle);
        }

        _window.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WM_WTSSESSION_CHANGE && wParam is WTS_SESSION_LOCK or WTS_SESSION_UNLOCK)
        {
            Set(locked: wParam == WTS_SESSION_LOCK, screensOff: AreScreensOff);
            return 0;
        }

        if (message == WM_POWERBROADCAST && wParam == PBT_POWERSETTINGCHANGE && lParam != 0)
        {
            var setting = (POWERBROADCAST_SETTING*)lParam;
            if (setting->PowerSetting == s_consoleDisplayState && setting->DataLength >= sizeof(uint))
            {
                // 0 is off, 1 on and 2 dimmed.
                Set(locked: IsLocked, screensOff: *(uint*)&setting->Data == 0);
            }

            return 1;
        }

        return null;
    }

    private void Set(bool locked, bool screensOff)
    {
        if (locked == IsLocked && screensOff == AreScreensOff)
        {
            return;
        }

        IsLocked = locked;
        AreScreensOff = screensOff;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSUnRegisterSessionNotification(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint RegisterPowerSettingNotification(nint recipient, Guid* powerSettingGuid, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterPowerSettingNotification(nint handle);
}

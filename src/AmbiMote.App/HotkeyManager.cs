using System.Runtime.InteropServices;

using AmbiMote.Core.Settings;

namespace AmbiMote.App;

/// <summary>
/// Registers the keyboard shortcuts with Windows, so they work whichever program is in front. <see cref="Pressed"/>
/// is raised on the UI thread.
/// </summary>
internal sealed partial class HotkeyManager : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly MessageWindow _window;
    private readonly List<int> _registeredIds = [];
    private HotkeySettings _settings = new() { Enabled = false };
    private bool _suspended;

    public HotkeyManager() => _window = new MessageWindow("Hotkeys", OnMessage);

    public event EventHandler<HotkeyAction>? Pressed;

    /// <summary><see cref="Problems"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Shortcuts that could not be registered, usually because another program already uses them.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>
    /// Releases the shortcuts while true, so a shortcut can be recorded without setting it off. Windows would otherwise
    /// swallow the keys before the window sees them.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended != value)
            {
                _suspended = value;
                Register();
            }
        }
    }

    /// <summary>Registers the shortcuts in <paramref name="settings"/>, if they have changed.</summary>
    public void Apply(HotkeySettings settings)
    {
        if (settings == _settings)
        {
            return;
        }

        _settings = settings;
        Register();
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private void Register()
    {
        Unregister();
        var problems = new List<string>();
        if (!_suspended)
        {
            var used = new HashSet<Hotkey>();
            foreach (var (action, hotkey) in _settings.Bindings())
            {
                if (!used.Add(hotkey))
                {
                    problems.Add($"{hotkey} is set for more than one shortcut.");
                    continue;
                }

                uint modifiers = (uint)hotkey.Modifiers | (HotkeySettings.Repeats(action) ? 0 : MOD_NOREPEAT);
                if (RegisterHotKey(_window.Handle, (int)action, modifiers, (uint)hotkey.Key))
                {
                    _registeredIds.Add((int)action);
                }
                else
                {
                    problems.Add($"{hotkey} is already used by another program.");
                }
            }
        }

        // While suspended the shortcuts are only being recorded, so keep showing what was wrong before.
        if (!_suspended && !problems.SequenceEqual(Problems))
        {
            Problems = problems;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Unregister()
    {
        foreach (int id in _registeredIds)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _registeredIds.Clear();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WM_HOTKEY)
        {
            return null;
        }

        var action = (HotkeyAction)(int)wParam;
        if (Enum.IsDefined(action))
        {
            Pressed?.Invoke(this, action);
        }

        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}

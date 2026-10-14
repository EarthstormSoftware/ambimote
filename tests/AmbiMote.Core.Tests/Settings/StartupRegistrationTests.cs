using AmbiMote.Core.Settings;

using Microsoft.Win32;

namespace AmbiMote.Core.Tests.Settings;

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = $@"Software\AmbiMote.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _baseKey;

    public StartupRegistrationTests() => _baseKey = Registry.CurrentUser.CreateSubKey(_keyPath);

    public void Dispose()
    {
        _baseKey.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void EnableAndDisable()
    {
        var startup = new StartupRegistration(_baseKey);
        string exe = @"C:\Apps\AmbiMote\AmbiMote.exe";

        Assert.False(startup.IsEnabled);

        startup.Enable(exe);

        Assert.True(startup.IsEnabled);
        Assert.Equal($"\"{exe}\"", startup.Command);
        Assert.True(startup.Starts(exe));
        Assert.False(startup.Starts(@"D:\Elsewhere\AmbiMote.exe"));

        startup.Disable();
        startup.Disable();

        Assert.False(startup.IsEnabled);
    }
}

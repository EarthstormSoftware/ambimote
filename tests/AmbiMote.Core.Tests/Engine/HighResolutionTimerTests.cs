using System.Diagnostics;

using AmbiMote.Core.Engine;

namespace AmbiMote.Core.Tests.Engine;

public class HighResolutionTimerTests
{
    [Fact]
    public void Start_SignalsAfterTheDelay()
    {
        using var timer = new HighResolutionTimer();
        var clock = Stopwatch.StartNew();

        timer.Start(TimeSpan.FromMilliseconds(20));
        bool signalled = timer.WaitOne(TimeSpan.FromSeconds(2));

        Assert.True(signalled);
        Assert.InRange(clock.Elapsed.TotalMilliseconds, 15, 1000);
    }
}

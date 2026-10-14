using AmbiMote.Core.Control;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AmbiMote.App;

/// <summary>Starts the app, unless a copy is already running, in which case that copy shows its window.</summary>
public static class Program
{
    private const string ShowName = @"Local\AmbiMote.Show";

    [STAThread]
    public static void Main()
    {
        // Only one copy may drive the sticks.
        using var running = new Mutex(initiallyOwned: true, ControlPipe.RunningMutexName, out bool first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(ShowName, out var show))
            {
                using (show)
                {
                    show.Set();
                }
            }

            return;
        }

        using var showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(showRequests);
        });
    }
}

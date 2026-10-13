namespace AmbiMote.Core.Capture;

/// <summary>Captures one monitor as a small image. Desktop Duplication in practice; a fake in tests.</summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the screen to change. Returns false if it did not,
    /// so callers can skip work. The image stays valid until the next call.
    /// </summary>
    bool TryCapture(TimeSpan timeout, out BgraImage? image);
}

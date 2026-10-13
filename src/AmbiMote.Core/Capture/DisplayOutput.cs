using Vortice.DXGI;

namespace AmbiMote.Core.Capture;

/// <summary>A monitor attached to the desktop, as DXGI sees it.</summary>
/// <param name="DeviceName">The GDI device name, such as <c>\\.\DISPLAY1</c>.</param>
/// <param name="Left">Left edge in desktop coordinates.</param>
/// <param name="Top">Top edge in desktop coordinates.</param>
/// <param name="Width">Width in pixels, as the desktop is laid out (after rotation).</param>
/// <param name="Height">Height in pixels, as the desktop is laid out (after rotation).</param>
/// <param name="Rotated">True if the monitor is turned to portrait or upside down.</param>
/// <param name="Adapter">The graphics adapter's name.</param>
public sealed record DisplayOutput(string DeviceName, int Left, int Top, int Width, int Height, bool Rotated, string Adapter)
{
    /// <summary>The primary monitor has its top-left corner at the desktop origin.</summary>
    public bool IsPrimary => Left == 0 && Top == 0;

    /// <summary>Lists the monitors attached to the desktop, across all adapters.</summary>
    public static IReadOnlyList<DisplayOutput> List()
    {
        var outputs = new List<DisplayOutput>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                string adapterName = adapter.Description1.Description;
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var desc = output.Description;
                        if (!desc.AttachedToDesktop)
                        {
                            continue;
                        }

                        var bounds = desc.DesktopCoordinates;
                        outputs.Add(new DisplayOutput(
                            desc.DeviceName,
                            bounds.Left,
                            bounds.Top,
                            bounds.Right - bounds.Left,
                            bounds.Bottom - bounds.Top,
                            desc.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified),
                            adapterName));
                    }
                }
            }
        }

        return outputs;
    }
}

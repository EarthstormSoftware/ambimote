using System.Runtime.InteropServices;

using SharpGen.Runtime;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AmbiMote.Core.Capture;

/// <summary>
/// Captures one monitor with DXGI Desktop Duplication. Each new frame is copied into a mipmapped texture
/// and downscaled on the GPU; only one small mip level is copied back to the CPU.
/// Frames are always 8-bit BGRA: on an HDR desktop Windows converts them to SDR first.
/// </summary>
public sealed class DesktopDuplicationCapture : IScreenCapture
{
    /// <summary>The default largest side of the image read back from the GPU, in pixels.</summary>
    public const int DefaultMaxSize = 128;

    private readonly string _deviceName;
    private readonly int _maxSize;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutput1 _output;

    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _mipTexture;
    private ID3D11ShaderResourceView? _mipView;
    private ID3D11Texture2D? _staging;
    private uint _level;
    private BgraImage? _image;

    private DesktopDuplicationCapture(string deviceName, int maxSize, ID3D11Device device, ID3D11DeviceContext context, IDXGIOutput1 output)
    {
        _deviceName = deviceName;
        _maxSize = maxSize;
        _device = device;
        _context = context;
        _output = output;
    }

    /// <summary>Opens the monitor with this device name, such as <c>\\.\DISPLAY1</c>.</summary>
    /// <exception cref="ArgumentException">No monitor has that name.</exception>
    /// <exception cref="NotSupportedException">The monitor is rotated; that is not supported yet.</exception>
    public static DesktopDuplicationCapture Open(string deviceName, int maxSize = DefaultMaxSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var desc = output.Description;
                        if (!desc.AttachedToDesktop || !string.Equals(desc.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (desc.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
                        {
                            throw new NotSupportedException($"{deviceName} is rotated. Rotated monitors are not supported yet.");
                        }

                        D3D11.D3D11CreateDevice(
                            adapter,
                            DriverType.Unknown,
                            DeviceCreationFlags.BgraSupport,
                            [FeatureLevel.Level_11_0],
                            out ID3D11Device? device,
                            out ID3D11DeviceContext? context).CheckError();
                        return new DesktopDuplicationCapture(desc.DeviceName, maxSize, device!, context!, output.QueryInterface<IDXGIOutput1>());
                    }
                }
            }
        }

        throw new ArgumentException($"No monitor named {deviceName}.", nameof(deviceName));
    }

    /// <summary>
    /// Picks the first mip level whose larger side is at most <paramref name="maxSize"/>,
    /// and returns it with that level's size.
    /// </summary>
    internal static (uint Level, int Width, int Height) ChooseMipLevel(int width, int height, int maxSize)
    {
        uint level = 0;
        while (Math.Max(width, height) > maxSize && (width > 1 || height > 1))
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            level++;
        }

        return (level, width, height);
    }

    public bool TryCapture(TimeSpan timeout, out BgraImage? image)
    {
        image = null;
        if (_duplication is null && !TryStartDuplication())
        {
            return false;
        }

        var result = _duplication!.AcquireNextFrame((uint)timeout.TotalMilliseconds, out var info, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return false;
        }

        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            // The display mode changed, a full-screen app took over, or the secure desktop is showing.
            StopDuplication();
            return false;
        }

        result.CheckError();
        try
        {
            // A frame with no present time only moved the mouse pointer.
            if (info.LastPresentTime == 0)
            {
                return false;
            }

            using var frame = resource.QueryInterface<ID3D11Texture2D>();
            EnsureTextures(frame.Description);
            _context.CopySubresourceRegion(_mipTexture!, 0, 0, 0, 0, frame, 0);
            _context.GenerateMips(_mipView!);
            _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _mipTexture!, _level);
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }

        image = ReadStaging();
        return true;
    }

    public void Dispose()
    {
        StopDuplication();
        _output.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private bool TryStartDuplication()
    {
        try
        {
            _duplication = _output.DuplicateOutput(_device);
            return true;
        }
        catch (SharpGenException ex) when (
            ex.ResultCode == Vortice.DXGI.ResultCode.NotCurrentlyAvailable
            || ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost
            || ex.ResultCode == Vortice.DXGI.ResultCode.Unsupported
            || ex.ResultCode == Vortice.DXGI.ResultCode.SessionDisconnected
            || ex.ResultCode == Result.AccessDenied)
        {
            // Not available right now, for example while the secure desktop (UAC, lock screen) is showing. Try again later.
            return false;
        }
    }

    /// <summary>Creates the mipmapped and staging textures for frames like this one, unless they already fit.</summary>
    private void EnsureTextures(Texture2DDescription frame)
    {
        if (frame.Format != Format.B8G8R8A8_UNorm)
        {
            throw new NotSupportedException($"{_deviceName} gives frames in the {frame.Format} format, which is not supported.");
        }

        if (_mipTexture is { } existing && existing.Description.Width == frame.Width && existing.Description.Height == frame.Height)
        {
            return;
        }

        ReleaseTextures();
        var (level, width, height) = ChooseMipLevel((int)frame.Width, (int)frame.Height, _maxSize);
        _mipTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = frame.Width,
            Height = frame.Height,
            MipLevels = level + 1,
            ArraySize = 1,
            Format = frame.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _mipView = _device.CreateShaderResourceView(_mipTexture);
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = frame.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _level = level;
        _image = new BgraImage(width, height);
    }

    private void ReleaseTextures()
    {
        _staging?.Dispose();
        _staging = null;
        _mipView?.Dispose();
        _mipView = null;
        _mipTexture?.Dispose();
        _mipTexture = null;
    }

    private void StopDuplication()
    {
        ReleaseTextures();
        _duplication?.Dispose();
        _duplication = null;
    }

    private BgraImage ReadStaging()
    {
        var image = _image!;
        var mapped = _context.Map(_staging!, 0, MapMode.Read);
        try
        {
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), image.Pixels, y * image.Stride, image.Stride);
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }

        return image;
    }
}

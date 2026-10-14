using AmbiMote.Core;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Sampling;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.Foundation;

namespace AmbiMote.App;

/// <summary>
/// Draws the monitors as the desktop lays them out, with each stick's LEDs along the edge it sits behind, in the
/// colours they are showing. The shapes are made once per layout; each frame only changes their colours.
/// </summary>
internal sealed class StickPreview(Canvas canvas)
{
    /// <summary>How far outside a monitor's edge its sticks are drawn.</summary>
    private const double StickOffset = 7;

    /// <summary>Room around the monitors for the sticks.</summary>
    private const double Margin = 14;

    private readonly List<(Shape Led, int Stick, int Index)> _leds = [];
    private string? _layoutKey;
    private LightingPreview? _shown;

    /// <summary>Shows <paramref name="preview"/>, or a placeholder when there is nothing to show yet.</summary>
    public void Show(LightingPreview? preview)
    {
        if (ReferenceEquals(preview, _shown) && _layoutKey is not null)
        {
            return;
        }

        _shown = preview;
        string key = LayoutKey(preview);
        if (key != _layoutKey)
        {
            _layoutKey = key;
            Build(preview);
        }

        if (preview is null)
        {
            return;
        }

        for (int i = 0; i < _leds.Count; i++)
        {
            var (led, stick, index) = _leds[i];
            var current = preview.Sticks[stick];
            var colour = index < current.Colours.Length ? current.Colours[index] : Rgb.Black;
            ((SolidColorBrush)led.Fill).Color = LedColour(colour);
        }
    }

    /// <summary>Lays the shapes out again, for example after the canvas changed size.</summary>
    public void Relayout()
    {
        _layoutKey = null;
        var preview = _shown;
        _shown = null;
        Show(preview);
    }

    /// <summary>Unlit LEDs are drawn faintly, so the sticks still show.</summary>
    private static Windows.UI.Color LedColour(Rgb colour) =>
        colour.R + colour.G + colour.B < 24
            ? ColorHelper.FromArgb(70, 128, 128, 128)
            : ColorHelper.FromArgb(255, colour.R, colour.G, colour.B);

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private string LayoutKey(LightingPreview? preview) =>
        preview is null
            ? $"none|{canvas.ActualWidth}"
            : $"{canvas.ActualWidth}|{string.Join(';', preview.Monitors.Select(static m => $"{m.DeviceName},{m.Left},{m.Top},{m.Width},{m.Height}"))}|"
                + string.Join(';', preview.Sticks.Select(static s => $"{s.HostId},{s.Channel},{s.Monitor?.DeviceName},{s.Edge},{s.From},{s.To},{s.Colours.Length}"));

    private void Build(LightingPreview? preview)
    {
        canvas.Children.Clear();
        _leds.Clear();
        double width = canvas.ActualWidth;
        double height = canvas.Height;
        if (width <= 0)
        {
            return;
        }

        if (preview is null || preview.Sticks.Count == 0)
        {
            var text = new TextBlock
            {
                Text = "The sticks show here once a Mote host is connected.",
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                Width = width,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetTop(text, (height / 2) - 10);
            canvas.Children.Add(text);
            return;
        }

        if (preview.Monitors.Count == 0)
        {
            BuildRows(preview, width, height);
            return;
        }

        // Fit the desktop's monitor layout into the canvas, leaving room around it for the sticks.
        double left = preview.Monitors.Min(static m => m.Left);
        double top = preview.Monitors.Min(static m => m.Top);
        double right = preview.Monitors.Max(static m => m.Left + m.Width);
        double bottom = preview.Monitors.Max(static m => m.Top + m.Height);
        double scale = Math.Min((width - (2 * Margin)) / (right - left), (height - (2 * Margin)) / (bottom - top));
        double originX = (width - ((right - left) * scale)) / 2;
        double originY = (height - ((bottom - top) * scale)) / 2;
        Rect Place(Core.Capture.DisplayOutput m) => new(
            originX + ((m.Left - left) * scale), originY + ((m.Top - top) * scale), m.Width * scale, m.Height * scale);

        foreach (var monitor in preview.Monitors)
        {
            var bounds = Place(monitor);
            var screen = new Rectangle
            {
                Width = Math.Max(0, bounds.Width - 4),
                Height = Math.Max(0, bounds.Height - 4),
                RadiusX = 3,
                RadiusY = 3,
                Fill = ThemeBrush("SubtleFillColorSecondaryBrush"),
                Stroke = ThemeBrush("ControlStrongStrokeColorDefaultBrush"),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(screen, bounds.X + 2);
            Canvas.SetTop(screen, bounds.Y + 2);
            canvas.Children.Add(screen);
        }

        for (int s = 0; s < preview.Sticks.Count; s++)
        {
            var stick = preview.Sticks[s];
            if (stick.Monitor is null)
            {
                continue;
            }

            var bounds = Place(stick.Monitor);
            bool horizontal = stick.Edge is ScreenEdge.Top or ScreenEdge.Bottom;
            double edgeLength = horizontal ? bounds.Width : bounds.Height;
            int count = stick.Colours.Length;
            double spacing = Math.Abs(stick.To - stick.From) * edgeLength / Math.Max(1, count);
            double size = Math.Clamp(spacing * 0.75, 2, 7);
            for (int i = 0; i < count; i++)
            {
                double along = stick.From + ((stick.To - stick.From) * (i + 0.5) / count);
                var (x, y) = stick.Edge switch
                {
                    ScreenEdge.Top => (bounds.X + (along * bounds.Width), bounds.Y - StickOffset),
                    ScreenEdge.Bottom => (bounds.X + (along * bounds.Width), bounds.Bottom + StickOffset),
                    ScreenEdge.Left => (bounds.X - StickOffset, bounds.Y + (along * bounds.Height)),
                    _ => (bounds.Right + StickOffset, bounds.Y + (along * bounds.Height)),
                };
                AddLed(s, i, x, y, size);
            }
        }
    }

    /// <summary>Without a stick mapping, each host's sticks in a row, in channel order.</summary>
    private void BuildRows(LightingPreview preview, double width, double height)
    {
        var rows = preview.Sticks.Select(static (s, i) => (Stick: s, Index: i)).GroupBy(static s => s.Stick.HostId).ToList();
        int perRow = rows.Max(static r => r.Sum(static s => s.Stick.Colours.Length + 2));
        double spacing = Math.Min(10, (width - (2 * Margin)) / perRow);
        double rowHeight = Math.Min(28, (height - (2 * Margin)) / rows.Count);
        double y = (height - (rowHeight * (rows.Count - 1))) / 2;
        foreach (var row in rows)
        {
            double x = (width - (perRow * spacing)) / 2;
            foreach (var (stick, index) in row)
            {
                for (int i = 0; i < stick.Colours.Length; i++)
                {
                    AddLed(index, i, x + (spacing / 2), y, spacing * 0.75);
                    x += spacing;
                }

                x += spacing * 2;
            }

            y += rowHeight;
        }
    }

    private void AddLed(int stick, int index, double centreX, double centreY, double size)
    {
        var led = new Rectangle
        {
            Width = size,
            Height = size,
            RadiusX = size / 4,
            RadiusY = size / 4,
            Fill = new SolidColorBrush(LedColour(Rgb.Black)),
        };
        Canvas.SetLeft(led, centreX - (size / 2));
        Canvas.SetTop(led, centreY - (size / 2));
        canvas.Children.Add(led);
        _leds.Add((led, stick, index));
    }
}

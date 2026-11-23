using System.Windows;
using System.Windows.Media;

namespace Mizu.Widget.Controls;

internal sealed class GlassMaterial
{
    private (ThemePalette Palette, double Opacity)? current;
    private Brush glass = Brushes.Transparent, glow = Brushes.Transparent;
    private Pen? rim;
    private Brush[]? frost;
    private readonly SolidColorBrush text = new(), secondary = new(), quiet = new(), accent = new(), line = new();

    public Brush Text => text;
    public Brush Secondary => secondary;
    public Brush Quiet => quiet;
    public Brush Accent => accent;
    public Brush Line => line;

    public void Update(ThemePalette palette, double opacity)
    {
        var key = (palette, opacity);
        if (current == key) return;
        current = key;
        var tint = Art.Brush(palette.Base).Color;
        byte topAlpha = (byte)(opacity * 255);
        byte bottomAlpha = (byte)Math.Max(16, topAlpha - 18);

        // Only the interior receives the extra tint. It falls away across the
        // outer margins so the opacity slider still exposes the background there.
        double glassFloor = bottomAlpha / 255d;
        double contentFloor = palette.Dark ? .74 : .64;
        double addedOpacity = Math.Max(0, (contentFloor - glassFloor) / (1 - glassFloor));
        byte frostAlpha = (byte)Math.Round(addedOpacity * 255);
        frost = frostAlpha == 0 ? null : MakeFrost(tint, frostAlpha);
        double edgeSoftness = Math.Min(frostAlpha / 160d, 1);
        byte glowAlpha = BlendAlpha(palette.Dark ? 18 : 68, palette.Dark ? 12 : 43, edgeSoftness);
        glass = new LinearGradientBrush(WithAlpha(tint, topAlpha), WithAlpha(tint, bottomAlpha), 95);
        glow = new RadialGradientBrush(WithAlpha(Colors.White, glowAlpha), WithAlpha(Colors.White, 0))
        { GradientOrigin = new(.1, .0), Center = new(.1, .0), RadiusX = 1.1, RadiusY = .7 };
        int outline = palette.Dark ? 56 : 200;
        var rimBrush = new LinearGradientBrush(
            WithAlpha(Colors.White, BlendAlpha(outline, palette.Dark ? 41 : 97, edgeSoftness)),
            WithAlpha(Colors.White, BlendAlpha(outline, palette.Dark ? 15 : 28, edgeSoftness)),
            90);
        rim = Art.Pen(rimBrush, 1.1 - .25 * edgeSoftness);

        var worstBackdrop = Mix(palette.Dark ? Colors.White : Colors.Black, tint, glassFloor);
        if (palette.Dark) worstBackdrop = Mix(worstBackdrop, Colors.White, glowAlpha / 255d);
        worstBackdrop = Mix(worstBackdrop, tint, frostAlpha / 255d);
        var ink = palette.Dark ? Colors.White : Colors.Black;
        text.Color = ReadableInk(Art.Brush(palette.Text).Color, ink, worstBackdrop, 5.7);
        secondary.Color = ReadableInk(Art.Brush(palette.Secondary).Color, ink, worstBackdrop, 4.9);
        quiet.Color = ReadableInk(Art.Brush(palette.Quiet).Color, ink, worstBackdrop, 4.5);
        accent.Color = ReadableInk(Art.Brush(palette.Accent).Color, ink, worstBackdrop, 3.2);
        line.Color = Art.Brush(palette.Line).Color;
    }

    public void Draw(DrawingContext drawing, Rect bounds, double radius)
    {
        drawing.DrawRectangle(glass, null, bounds);
        drawing.DrawRectangle(glow, null, bounds);
        if (frost is { } brushes)
        {
            double w = bounds.Width, h = bounds.Height;
            double x = Math.Min(28, w / 4), y = Math.Min(26, h / 4);
            double right = w - x, bottom = h - y, centerWidth = right - x, centerHeight = bottom - y;
            drawing.PushGuidelineSet(new GuidelineSet([x, right], [y, bottom]));
            drawing.DrawRectangle(brushes[0], null, new(x, y, centerWidth, centerHeight));
            drawing.DrawRectangle(brushes[1], null, new(0, y, x, centerHeight));
            drawing.DrawRectangle(brushes[2], null, new(x, 0, centerWidth, y));
            drawing.DrawRectangle(brushes[3], null, new(right, y, x, centerHeight));
            drawing.DrawRectangle(brushes[4], null, new(x, bottom, centerWidth, y));
            drawing.DrawRectangle(brushes[5], null, new(0, 0, x, y));
            drawing.DrawRectangle(brushes[6], null, new(right, 0, x, y));
            drawing.DrawRectangle(brushes[7], null, new(right, bottom, x, y));
            drawing.DrawRectangle(brushes[8], null, new(0, bottom, x, y));
            drawing.Pop();
        }
        drawing.DrawRoundedRectangle(null, rim, new(.7, .7, bounds.Width - 1.4, bounds.Height - 1.4),
            Math.Max(0, radius - .7), Math.Max(0, radius - .7));
    }

    private static Brush[] MakeFrost(Color tint, byte alpha)
    {
        const int steps = 12;
        var ramp = new GradientStopCollection(steps + 1);
        int edgeAlpha = (int)Math.Round(alpha * .30);
        // Share the same eased profile at straight edges and rounded corners.
        // The shallow ends eliminate the visible step into the flat center.
        for (int i = 0; i <= steps; i++)
        {
            double offset = (double)i / steps;
            double eased = offset * offset * (3 - 2 * offset);
            ramp.Add(new GradientStop(WithAlpha(tint, BlendAlpha(alpha, edgeAlpha, eased)), offset));
        }
        ramp.Freeze();
        Brush Edge(Point inner, Point outer) => new LinearGradientBrush(ramp, inner, outer);
        Brush Corner(Point inner) => new RadialGradientBrush(ramp)
        { Center = inner, GradientOrigin = inner, RadiusX = 1, RadiusY = 1 };
        Brush[] brushes =
        [
            new SolidColorBrush(WithAlpha(tint, alpha)),
            Edge(new(1, .5), new(0, .5)), Edge(new(.5, 1), new(.5, 0)),
            Edge(new(0, .5), new(1, .5)), Edge(new(.5, 0), new(.5, 1)),
            Corner(new(1, 1)), Corner(new(0, 1)), Corner(new(0, 0)), Corner(new(1, 0))
        ];
        foreach (var brush in brushes) brush.Freeze();
        return brushes;
    }

    private static Color ReadableInk(Color tone, Color ink, Color backdrop, double target)
    {
        double backgroundLuminance = Luminance(backdrop);
        double Contrast(Color color)
        {
            double luminance = Luminance(color);
            return (Math.Max(luminance, backgroundLuminance) + .05) / (Math.Min(luminance, backgroundLuminance) + .05);
        }
        if (Contrast(tone) >= target) return tone;
        double low = 0, high = 1;
        for (int i = 0; i < 8; i++)
        {
            double middle = (low + high) / 2;
            if (Contrast(Mix(tone, ink, middle)) >= target) high = middle;
            else low = middle;
        }
        return Mix(tone, ink, high);
    }

    private static double Luminance(Color color)
    {
        static double Linear(double channel) => channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        return .2126 * Linear(color.R / 255d) + .7152 * Linear(color.G / 255d) + .0722 * Linear(color.B / 255d);
    }

    private static Color Mix(Color from, Color to, double progress)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * progress);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    private static byte BlendAlpha(int from, int to, double progress) => (byte)Math.Round(from + (to - from) * progress);
}

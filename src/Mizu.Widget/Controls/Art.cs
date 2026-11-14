using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Mizu.Widget.Controls;

public static class Art
{
    public static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    public static Pen Pen(Brush brush, double width = 1.3) => new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    public static void Line(DrawingContext d, Pen p, double x, double y, double x2, double y2) => d.DrawLine(p, new(x, y), new(x2, y2));
    public static void Text(DrawingContext d, string value, double x, double y, double size, Brush brush, double pixelsPerDip = 1, bool medium = false, double? width = null, int? weight = null, bool fit = false, TextAlignment alignment = TextAlignment.Left)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, weight is { } wgt ? FontWeight.FromOpenTypeWeight(wgt) : medium ? FontWeights.Medium : FontWeights.Normal, FontStretches.Normal), size, brush, pixelsPerDip);
        if (width is { } w)
        {
            if (fit && text.WidthIncludingTrailingWhitespace > w) text.SetFontSize(size * w / text.WidthIncludingTrailingWhitespace);
            text.MaxTextWidth = w; text.TextAlignment = alignment; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis;
        }
        d.DrawText(text, new(x, y));
    }
    public static void Icon(DrawingContext d, string kind, double x, double y, double size, Brush brush, double stroke = 1.35)
    {
        d.PushTransform(new TranslateTransform(x, y)); d.PushTransform(new ScaleTransform(size / 24, size / 24));
        var p = Pen(brush, stroke * 24 / size);
        void L(double a, double b, double c, double e) => Line(d, p, a, b, c, e);
        switch (kind)
        {
            case "music": d.DrawGeometry(null, p, Geometry.Parse("M 10,17 L 10,4 20,2 20,15 M 10,8 L 20,6")); d.DrawEllipse(null, p, new(6,18), 4, 3); d.DrawEllipse(null, p, new(16,16), 4, 3); break;
            case "previous": L(5,5,5,19); d.DrawGeometry(brush, null, Geometry.Parse("M 18,5 L 7,12 18,19 Z")); break;
            case "next": L(19,5,19,19); d.DrawGeometry(brush, null, Geometry.Parse("M 6,5 L 17,12 6,19 Z")); break;
            case "play": d.DrawGeometry(brush, null, Geometry.Parse("M 7,4 L 20,12 7,20 Z")); break;
            case "pause": L(8,5,8,19); L(16,5,16,19); break;
            case "plus": L(12, 5, 12, 19); L(5, 12, 19, 12); break;
            case "settings":
                d.DrawEllipse(null, p, new(12, 12), 4, 4);
                for (int i = 0; i < 8; i++) { var a = i * Math.PI / 4; L(12 + 8 * Math.Cos(a), 12 + 8 * Math.Sin(a), 12 + 10 * Math.Cos(a), 12 + 10 * Math.Sin(a)); }
                d.DrawEllipse(null, p, new(12, 12), 8, 8); break;
            case "close": L(6, 6, 18, 18); L(18, 6, 6, 18); break;
            case "refresh":
                d.DrawGeometry(null, p, Geometry.Parse("M 19,8 A 8,8 0 1 0 19,17 M 19,3 L 19,8 14,8")); break;
            case "pin": d.DrawGeometry(null, p, Geometry.Parse("M 8,3 L 16,3 M 9,3 L 9,10 6,14 18,14 15,10 15,3 M 12,14 L 12,22")); break;
            case "cpu":
                d.DrawRoundedRectangle(null, p, new(6, 6, 12, 12), 2, 2); d.DrawRectangle(null, p, new(9, 9, 6, 6));
                for (int i = 8; i <= 16; i += 4) { L(i, 2, i, 5); L(i, 19, i, 22); L(2, i, 5, i); L(19, i, 22, i); } break;
            case "memory":
                d.DrawRoundedRectangle(null, p, new(3, 6, 18, 11), 2, 2);
                for (int i = 6; i <= 18; i += 4) { L(i, 10, i, 13); L(i, 17, i, 20); } break;
            case "temperature":
                d.DrawGeometry(null, p, Geometry.Parse("M 9,15 L 9,5 A 3,3 0 0 1 15,5 L 15,15 A 5,5 0 1 1 9,15 M 12,8 L 12,18"));
                d.DrawEllipse(brush, null, new(12, 18), 1.8, 1.8); break;
            case "bluetooth": d.DrawGeometry(null, p, Geometry.Parse("M 7,6 L 17,16 12,21 12,3 17,8 7,18")); break;
            case "receiver":
                d.DrawRoundedRectangle(null, p, new(9, 12, 6, 9), 1, 1);
                d.DrawGeometry(null, p, Geometry.Parse("M 8,9 Q 12,5 16,9 M 5,6 Q 12,0 19,6")); break;
            case "nearlink": d.DrawGeometry(null, p, Geometry.Parse("M 6,19 L 6,5 18,19 18,5 M 2,12 L 9,12 M 15,12 L 22,12")); break;
            case "wired": d.DrawGeometry(null, p, Geometry.Parse("M 8,3 L 8,8 M 16,3 L 16,8 M 6,8 L 18,8 18,11 A 6,6 0 0 1 6,11 Z M 12,17 L 12,22")); break;
            case "bolt": d.DrawGeometry(brush, null, Geometry.Parse("M 13,1 L 5,13 11,13 10,23 20,9 13,9 Z")); break;
            case "wind": d.DrawGeometry(null, p, Geometry.Parse("M 2,8 L 15,8 C 21,8 21,2 17,2 M 2,12 L 20,12 M 2,16 L 13,16 C 18,16 18,22 14,22")); break;
            case "drop": d.DrawGeometry(null, p, Geometry.Parse("M 12,2 C 10,7 5,11 5,15 A 7,7 0 0 0 19,15 C 19,11 14,7 12,2 Z")); break;
            case "moon": d.DrawGeometry(null, p, Geometry.Parse("M 18,17 A 9,9 0 1 1 8,3 A 8,8 0 0 0 18,17 Z")); break;
            case "unknown": d.DrawRoundedRectangle(null, p, new(5, 4, 14, 16), 4, 4); d.DrawEllipse(brush, null, new(12, 12), 1, 1); break;
        }
        d.Pop(); d.Pop();
    }
    public static void Keyboard(DrawingContext d, double x, double y, Brush line, Brush fill)
    {
        var p = Pen(line, 1.05);
        d.DrawRoundedRectangle(fill, p, new(x, y, 74, 37), 6, 6);
        for (int row = 0; row < 3; row++) for (int col = 0; col < 10; col++)
            d.DrawRoundedRectangle(null, Pen(line, .7), new(x + 5 + col * 6.5, y + 5 + row * 7, 4.2, 4), .8, .8);
        d.DrawRoundedRectangle(null, Pen(line, .8), new(x + 18, y + 27, 35, 4), 1, 1);
        for (int i = 0; i < 2; i++) { d.DrawRoundedRectangle(null, Pen(line, .7), new(x + 5 + i * 6.5, y + 27, 4.2, 4), .8, .8); d.DrawRoundedRectangle(null, Pen(line, .7), new(x + 57 + i * 6.5, y + 27, 4.2, 4), .8, .8); }
    }
    public static void Mouse(DrawingContext d, double x, double y, Brush line, Brush fill)
    {
        var p = Pen(line, 1.1);
        d.DrawRoundedRectangle(fill, p, new(x + 21, y - 4, 32, 46), 16, 16);
        Line(d, Pen(line, .7), x + 37, y - 3, x + 37, y + 14);
        d.DrawRoundedRectangle(line, null, new(x + 35.5, y + 3, 3, 9), 1.5, 1.5);
    }
    public static void Weather(DrawingContext d, int code, bool day, double x, double y, double size, Brush foreground, bool dark)
    {
        if (code < 0) { Icon(d, "unknown", x + size * .25, y + size * .2, size * .5, foreground); return; }
        d.PushTransform(new TranslateTransform(x, y)); d.PushTransform(new ScaleTransform(size / 80, size / 80));
        var gold = Brush(dark ? "#D9C391" : "#B9A16E"); var p = Pen(gold, 1.65);
        if (code <= 2 && day)
        {
            var radial = new RadialGradientBrush(Color.FromArgb(65, 220, 194, 137), Colors.Transparent);
            d.DrawEllipse(radial, null, new(36, 33), 31, 31);
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 226, 202, 149)), p, new(36, 33), 13, 13);
            for (int i = 0; i < 8; i++) { var a = i * Math.PI / 4; Line(d, p, 36 + Math.Cos(a) * 19, 33 + Math.Sin(a) * 19, 36 + Math.Cos(a) * 24, 33 + Math.Sin(a) * 24); }
        }
        else if (code <= 2) Icon(d, "moon", 15, 6, 42, foreground, 1.3);
        if (code > 0)
        {
            var g = Geometry.Parse("M 20,58 C 5,58 5,40 18,38 C 15,19 43,16 49,34 C 66,27 78,54 61,58 Z");
            var cloud = new LinearGradientBrush(Brush(dark ? "#68706E" : "#FBFBF7").Color, Brush(dark ? "#3D4545" : "#DADDD4").Color, 90);
            d.DrawGeometry(cloud, Pen(foreground, .9), g);
            if (code is >= 51 and < 71 || code >= 80)
            { for (int i = 0; i < 3; i++) Line(d, Pen(Brush("#8FA9B0"), 2), 25 + i * 13, 65, 22 + i * 13, 71); }
            if (code is >= 71 and < 80) for (int i = 0; i < 3; i++) d.DrawEllipse(foreground, null, new(23 + i * 15, 69), 1.5, 1.5);
            if (code >= 95) Icon(d, "bolt", 32, 48, 19, gold);
        }
        d.Pop(); d.Pop();
    }
}

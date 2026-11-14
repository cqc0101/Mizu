using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Data;
using System.ComponentModel;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

// Mutable brushes keep existing controls and open popup templates in sync,
// including pages rebuilt after device scans and todo edits.
public sealed class SettingsTheme(Window window)
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private readonly Dictionary<string, SolidColorBrush> brushes = new();
    private readonly Dictionary<string, Tone> tones = new();
    private sealed class Tone(Color initial) : INotifyPropertyChanged
    {
        private Color color = initial;
        public Color Color { get => color; set { color = value; PropertyChanged?.Invoke(this, new(nameof(Color))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private ThemePalette palette = ThemePalette.All[0];
    private string? appliedId;
    private IntPtr appliedHwnd;
    public Brush Brush(string original)
    {
        if (!brushes.TryGetValue(original, out var brush))
        {
            var tone = new Tone(Map(original)); tones.Add(original, tone);
            brush = new();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(Tone.Color)) { Source = tone });
            brushes.Add(original, brush);
            window.Resources["Settings" + original.TrimStart('#')] = brush;
        }
        return brush;
    }
    private Color Blend(string a, string b, double ratio)
    {
        var x = Art.Brush(a).Color; var y = Art.Brush(b).Color;
        return Color.FromRgb((byte)(x.R + (y.R - x.R) * ratio), (byte)(x.G + (y.G - x.G) * ratio), (byte)(x.B + (y.B - x.B) * ratio));
    }
    private Color Map(string color) => color.ToUpperInvariant() switch
    {
        "#F0F1EC" => Art.Brush(palette.Base).Color,
        "#FCFCF8" or "#FCFDF8" => Blend(palette.Base, "#FFFFFF", palette.Dark ? .045 : .48),
        "#FAFAF5" or "#FAFAF6" or "#F2F4ED" or "#F1F4EB" or "#F0F3EA" => Blend(palette.Base, "#FFFFFF", palette.Dark ? .09 : .3),
        "#41483E" or "#434B3E" or "#4A5945" or "#4B5843" or "#40573D" => Art.Brush(palette.Text).Color,
        "#8B7758" or "#8D9487" or "#8A9183" or "#87917C" or "#7B8674" => Art.Brush(palette.Secondary).Color,
        "#8A9F7B" or "#8B9F7F" or "#8EA37F" or "#5C7650" or "#90A37E" or "#9AAC8C" or "#8B9980" => Art.Brush(palette.Accent).Color,
        "#DEE6D7" or "#D9E2CF" or "#E1E8D8" => Blend(palette.Base, palette.Accent, palette.Dark ? .3 : .24),
        "#E7EBE1" or "#E8EDDF" or "#E5E8DC" => Blend(palette.Base, palette.Accent, .15),
        _ => Blend(palette.Base, palette.Secondary, .25)
    };
    public void Apply(string id)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (appliedId == id && appliedHwnd == hwnd) return;
        appliedId = id; appliedHwnd = hwnd;
        palette = ThemePalette.Get(id);
        foreach (var entry in tones) entry.Value.Color = Map(entry.Key);
        HoverHints.SetPalette(window, palette); HoverHints.SetIsDark(window, palette.Dark);
        if (hwnd != IntPtr.Zero)
        {
            int dark = palette.Dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var c = Art.Brush(palette.Base).Color; int caption = c.R | c.G << 8 | c.B << 16;
                c = Art.Brush(palette.Text).Color; int text = c.R | c.G << 8 | c.B << 16;
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4); DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            }
        }
    }
}

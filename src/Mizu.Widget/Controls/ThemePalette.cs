using System.Windows.Media;

namespace Mizu.Widget.Controls;

public sealed record ThemePalette(string Id, string Name, bool Dark, string Base, string Text, string Secondary, string Quiet, string Accent, string Line)
{
    public static readonly ThemePalette[] All = [
        new("light", "雾白", false, "#F3F3EB", "#40483F", "#7B8276", "#A3AA9C", "#7B967E", "#22616E5C"),
        new("dark", "墨绿", true, "#232A27", "#E9EAE2", "#B2B9AE", "#69766C", "#ACC1AC", "#26E2E8DF"),
        new("navy", "深蓝", true, "#18283F", "#E4EDF7", "#ACBED5", "#7188A4", "#93B5DC", "#2AABC5E6"),
        new("pink", "浅粉", false, "#F8EAF0", "#654D5B", "#987887", "#B69AA8", "#BD8CA3", "#287F596D"),
        new("lavender", "浅紫", false, "#EEE9F8", "#564D6B", "#8A7D9E", "#AAA0BC", "#A18ABF", "#28705B8C")
    ];
    public static ThemePalette Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
    public Brush HintBackground { get { var color = Art.Brush(Base).Color; color.A = 248; return new SolidColorBrush(color); } }
    public Brush HintForeground => Art.Brush(Text);
    public Brush HintBorder => Art.Brush(Line);
}

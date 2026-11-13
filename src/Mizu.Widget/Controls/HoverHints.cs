using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Mizu.Widget.Controls;

/// <summary>One popup appearance for widget controls and settings, including dark themes.</summary>
public static class HoverHints
{
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.RegisterAttached("Palette", typeof(ThemePalette), typeof(HoverHints), new FrameworkPropertyMetadata(ThemePalette.All[0], FrameworkPropertyMetadataOptions.Inherits));
    public static void SetPalette(DependencyObject target, ThemePalette value) => target.SetValue(PaletteProperty, value);
    public static ThemePalette GetPalette(DependencyObject target) => (ThemePalette)target.GetValue(PaletteProperty);
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.RegisterAttached(
        "IsDark", typeof(bool), typeof(HoverHints), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsDark(DependencyObject element) => (bool)element.GetValue(IsDarkProperty);
    public static void SetIsDark(DependencyObject element, bool value) => element.SetValue(IsDarkProperty, value);

    private static readonly Lazy<Style> HintStyle = new(() => (Style)new ResourceDictionary
    { Source = new Uri("/Mizu;component/Controls/HoverStyles.xaml", UriKind.Relative) }["MizuHint"]);

    public static void Set(FrameworkElement target, string? text, PlacementMode placement = PlacementMode.Bottom)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (target.ToolTip is ToolTip old) old.IsOpen = false;
            target.ToolTip = null;
            return;
        }
        if (target.ToolTip is ToolTip current)
        {
            if (!Equals(current.Content, text)) current.Content = text;
            return;
        }
        // Bind directly to the owner. Popup content lives in a separate visual
        // tree and cannot rely on inheriting the window palette.
        var tip = new ToolTip
        {
            Style = HintStyle.Value, Content = text, PlacementTarget = target,
            Placement = placement, HorizontalOffset = 0, VerticalOffset = 5
        };
        BindingOperations.SetBinding(tip, IsDarkProperty, new Binding
        { Source = target, Path = new PropertyPath(IsDarkProperty), Mode = BindingMode.OneWay });
        foreach (var pair in new[] { (Control.BackgroundProperty, "HintBackground"), (Control.ForegroundProperty, "HintForeground"), (Control.BorderBrushProperty, "HintBorder") })
            BindingOperations.SetBinding(tip, pair.Item1, new Binding { Source = target, Path = new PropertyPath("(0)." + pair.Item2, PaletteProperty) });
        target.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(target, 800);
        ToolTipService.SetBetweenShowDelay(target, 0);
        ToolTipService.SetShowDuration(target, 6000);
    }

    public static void Close(FrameworkElement target)
    { if (target.ToolTip is ToolTip tip) tip.IsOpen = false; }

    public static void CloseTree(DependencyObject root)
    {
        if (root is FrameworkElement element) Close(element);
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            CloseTree(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
    }
}

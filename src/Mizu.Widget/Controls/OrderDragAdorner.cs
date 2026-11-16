using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mizu.Widget.Controls;

internal sealed class OrderDragAdorner : Adorner
{
    private readonly RenderTargetBitmap snapshot;
    private readonly double width, height;
    private readonly double grabY;
    private Point pointer;
    public OrderDragAdorner(FrameworkElement list, FrameworkElement item, double grabOffset) : base(list)
    {
        IsHitTestVisible = false; width = Math.Max(1, item.ActualWidth); height = Math.Max(1, item.ActualHeight);
        grabY = Math.Clamp(grabOffset, 0, height);
        var dpi = VisualTreeHelper.GetDpi(item);
        snapshot = new((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        snapshot.Render(item); snapshot.Freeze();
    }
    public void Follow(Point point) { pointer = point; InvalidateVisual(); }
    protected override void OnRender(DrawingContext d)
    {
        var rect = new Rect(5, pointer.Y - grabY, width, height);
        d.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(24, 0, 0, 0)), null, new(rect.X + 2, rect.Y + 4, rect.Width, rect.Height), 8, 8);
        d.PushOpacity(.96); d.DrawImage(snapshot, rect); d.Pop();
    }
}

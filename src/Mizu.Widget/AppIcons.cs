using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mizu.Widget;

// Keep window, taskbar and tray icons on the same embedded asset, including
// development/test hosts whose process executable does not belong to Mizu.
public static class AppIcons
{
    private static readonly Uri Asset = new("pack://application:,,,/Mizu;component/Assets/mizu.ico");
    private static ImageSource? windowIcon;
    public static ImageSource WindowIcon
    {
        get
        {
            if (windowIcon is not null) return windowIcon;
            var decoder = new IconBitmapDecoder(Asset, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return windowIcon = frame;
        }
    }

    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(Asset)!.Stream;
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}

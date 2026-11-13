namespace Mizu.Core;

public sealed class DevicePager
{
    private int remainder;
    private long lastEvent = long.MinValue / 2, lastTurn = long.MinValue / 2;
    public int Advance(int page, int delta, int deviceCount, long milliseconds)
    {
        int lastPage = Math.Max(0, (deviceCount - 1) / 2);
        page = Math.Clamp(page, 0, lastPage);
        if (delta == 0 || lastPage == 0) return page;
        if (milliseconds - lastEvent > 250 || Math.Sign(delta) != Math.Sign(remainder)) remainder = 0;
        lastEvent = milliseconds;
        if (milliseconds - lastTurn < 180) { remainder = 0; return page; }
        remainder += delta;
        if (Math.Abs(remainder) < 120) return page;
        int next = Math.Clamp(page + (remainder < 0 ? 1 : -1), 0, lastPage);
        remainder = 0; lastTurn = milliseconds;
        return next;
    }
}

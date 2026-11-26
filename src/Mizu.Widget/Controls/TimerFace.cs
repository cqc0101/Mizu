using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

internal sealed class TimerFace : Grid
{
    private readonly TextBlock digits = new() { FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TimerDial dial;
    private bool? hasDial;

    public TimerFace(Brush accent, Brush line)
    {
        dial = new(accent, line) { Width = 148, Height = 148, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Typography.SetNumeralAlignment(digits, FontNumeralAlignment.Tabular);
        Children.Add(dial);
        Children.Add(digits);
    }

    public void Update(DesktopTimer timer, DateTimeOffset now, bool showDial)
    {
        if (hasDial != showDial)
        {
            hasDial = showDial;
            Height = showDial ? 152 : 66;
            dial.Visibility = showDial ? Visibility.Visible : Visibility.Collapsed;
        }
        var remaining = timer.Remaining(now);
        var time = remaining >= 3_600 ? $"{remaining / 3_600}:{remaining / 60 % 60:D2}:{remaining % 60:D2}"
            : $"{remaining / 60:D2}:{remaining % 60:D2}";
        if (digits.Text != time) digits.Text = time;
        digits.FontSize = showDial ? (remaining >= 36_000 ? 26 : remaining >= 3_600 ? 28 : 33) : 48;
        dial.Update(timer.RemainingFraction(now), timer.DueAt is not null);
    }

    private sealed class TimerDial(Brush accent, Brush track) : FrameworkElement
    {
        private static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
            "Fraction", typeof(double), typeof(TimerDial), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly Pen accentStroke = Art.Pen(accent, 5);
        private readonly Pen trackStroke = Art.Pen(track, 5);
        private double target = 1;

        public void Update(double next, bool running)
        {
            if (Math.Abs(target - next) < .000001 && (running || !HasAnimatedProperties)) return;
            var current = (double)GetValue(FractionProperty);
            target = next;
            SetValue(FractionProperty, next);
            if (IsVisible && running && next < current && current - next < .15 && SystemParameters.ClientAreaAnimation)
                BeginAnimation(FractionProperty, new DoubleAnimation(current, next, TimeSpan.FromMilliseconds(650)) { FillBehavior = FillBehavior.Stop });
            else BeginAnimation(FractionProperty, null);
        }

        protected override void OnRender(DrawingContext drawing)
        {
            double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
            if (radius <= 0) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            drawing.DrawEllipse(null, trackStroke, center, radius, radius);
            var fraction = (double)GetValue(FractionProperty);
            if (fraction >= .99999)
            {
                drawing.DrawEllipse(null, accentStroke, center, radius, radius);
                return;
            }
            if (fraction <= 0) return;
            var angle = fraction * Math.Tau;
            var arc = new StreamGeometry();
            using (var path = arc.Open())
            {
                path.BeginFigure(new(center.X, center.Y - radius), false, false);
                path.ArcTo(new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                    new(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, accentStroke, arc);
        }
    }
}

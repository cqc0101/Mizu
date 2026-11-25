using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

internal sealed class WordLookupPopup : Popup
{
    private const string ScrollThumbResource = "WordLookupScrollThumb";
    private readonly AppSettings settings;
    private readonly Border card;
    private readonly Border searchField;
    private readonly TextBox query;
    private readonly TextBlock placeholder;
    private readonly TextBlock message;
    private readonly StackPanel rows = new();
    private readonly ScrollViewer results;
    private readonly SolidColorBrush secondary = new();
    private ThemePalette? theme;
    private IReadOnlyList<LocalWordMatch> matches = [];
    private string? previousQuery;

    public WordLookupPopup(AppSettings settings, FrameworkElement anchor)
    {
        this.settings = settings;
        PlacementTarget = anchor;
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = (popup, target, _) =>
        [
            new(new Point(target.Width - popup.Width, target.Height + 7), PopupPrimaryAxis.Horizontal),
            new(new Point(target.Width - popup.Width, -popup.Height - 7), PopupPrimaryAxis.Horizontal)
        ];
        AllowsTransparency = true;
        PopupAnimation = PopupAnimation.Fade;
        StaysOpen = false;

        query = new TextBox
        {
            MaxLength = 200, Background = Brushes.Transparent, BorderThickness = new(0), FontSize = 14,
            Padding = new(0, 8, 0, 8), VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(query, "输入英文或中文释义");
        placeholder = new TextBlock
        {
            Text = "英文或中文释义", FontSize = 13, Foreground = secondary, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center
        };
        var field = new Grid();
        field.ColumnDefinitions.Add(new() { Width = new GridLength(26) });
        field.ColumnDefinitions.Add(new());
        Grid.SetColumn(query, 1);
        Grid.SetColumn(placeholder, 1);
        field.Children.Add(new IconView("search", secondary) { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false });
        field.Children.Add(query);
        field.Children.Add(placeholder);
        searchField = new Border { Child = field, CornerRadius = new(11), BorderThickness = new(1), Padding = new(10, 0, 10, 0) };

        results = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 242, Focusable = false };
        results.Resources.Add(typeof(ScrollBar), ScrollBarStyle());
        message = new TextBlock { FontSize = 11, Foreground = secondary, Margin = new(5, 9, 4, 1) };
        var content = new StackPanel { Margin = new(12) };
        content.Children.Add(searchField);
        content.Children.Add(results);
        content.Children.Add(message);
        card = new Border { Width = 310, CornerRadius = new(16), BorderThickness = new(1), Child = content, UseLayoutRounding = true };
        TextElement.SetFontFamily(card, new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"));
        Child = card;

        query.TextChanged += (_, _) => Refresh();
        query.GotKeyboardFocus += (_, _) => UpdateFieldBorder();
        query.LostKeyboardFocus += (_, _) => UpdateFieldBorder();
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            IsOpen = false;
            e.Handled = true;
        };
        Opened += (_, _) =>
        {
            Refresh();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (IsOpen) query.Focus();
            }));
        };
        Closed += (_, _) => query.Clear();
    }

    public void Refresh()
    {
        if (!IsOpen) return;
        var palette = ThemePalette.Get(settings.Theme);
        bool paletteChanged = theme != palette;
        if (paletteChanged)
        {
            theme = palette;
            card.Background = palette.HintBackground;
            card.BorderBrush = Art.Brush(palette.Line);
            query.Foreground = query.CaretBrush = Art.Brush(palette.Text);
            query.SelectionBrush = Art.Brush(palette.Accent);
            secondary.Color = Art.Brush(palette.Secondary).Color;
            var thumbColor = Art.Brush(palette.Accent).Color;
            thumbColor.A = 140;
            results.Resources[ScrollThumbResource] = new SolidColorBrush(thumbColor);
            searchField.Background = new SolidColorBrush(palette.Dark ? Color.FromArgb(15, 255, 255, 255) : Color.FromArgb(102, 255, 255, 255));
            UpdateFieldBorder();
        }

        placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string text = query.Text.Trim();
        var found = LocalWordSearch.Find(settings.Vocabulary, text);
        bool queryChanged = previousQuery != text;
        if (!queryChanged && !paletteChanged && matches.SequenceEqual(found)) return;
        previousQuery = text;
        matches = found;
        rows.Children.Clear();
        results.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        results.Margin = new(0, matches.Count == 0 ? 0 : 7, 0, 0);
        foreach (var match in matches)
            rows.Children.Add(ResultRow(match, palette, rows.Children.Count > 0));

        message.Text = text.Length == 0 ? "" : matches.Count == 0 ? "未找到本地词条"
            : matches.Any(match => !match.Saved) ? "内置词库仅提供拼写" : "";
        message.Visibility = message.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (queryChanged) results.ScrollToTop();
    }

    private void UpdateFieldBorder()
    {
        if (theme is { } palette) searchField.BorderBrush = Art.Brush(query.IsKeyboardFocused ? palette.Accent : palette.Line);
    }

    private static Style ScrollBarStyle()
    {
        var style = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="9"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Track x:Name="PART_Track" IsDirectionReversed="True">
                      <Track.Thumb>
                        <Thumb Background="{TemplateBinding Foreground}">
                          <Thumb.Template>
                            <ControlTemplate TargetType="Thumb">
                              <Grid Background="Transparent">
                                <Border Background="{TemplateBinding Background}" CornerRadius="2"
                                        HorizontalAlignment="Right" Width="4" MinHeight="16"/>
                              </Grid>
                            </ControlTemplate>
                          </Thumb.Template>
                        </Thumb>
                      </Track.Thumb>
                    </Track>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ScrollThumbResource)));
        return style;
    }

    private static Border ResultRow(LocalWordMatch match, ThemePalette palette, bool separator)
    {
        var heading = new DockPanel();
        if (!match.Saved)
        {
            var source = new TextBlock { Text = "内置词库", FontSize = 10, Foreground = Art.Brush(palette.Secondary),
                VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
            DockPanel.SetDock(source, Dock.Right);
            heading.Children.Add(source);
        }
        heading.Children.Add(new TextBlock { Text = match.Word, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = Art.Brush(palette.Text), TextWrapping = TextWrapping.Wrap });
        var body = new StackPanel();
        body.Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(match.Meaning))
            body.Children.Add(new TextBlock { Text = match.Meaning, FontSize = 12, Foreground = Art.Brush(palette.Text),
                TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) });
        return new Border { Child = body, Padding = new(7, 8, 7, 8), BorderBrush = Art.Brush(palette.Line),
            BorderThickness = new(0, separator ? 1 : 0, 0, 0) };
    }
}

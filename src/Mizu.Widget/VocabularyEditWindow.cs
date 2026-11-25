using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Mizu.Core.Services;

namespace Mizu.Widget;

internal sealed class VocabularyEditWindow : Window
{
    public VocabularyEditWindow(MainWindow owner, VocabularyEntry entry)
    {
        Owner = owner;
        Title = "编辑单词";
        Icon = AppIcons.WindowIcon;
        Width = 344;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        var theme = new SettingsTheme(this);
        Background = theme.Brush("#F0F1EC");
        Foreground = theme.Brush("#41483E");
        theme.Brush("#D9E2CF");
        theme.Brush("#8EA37F");
        var content = new StackPanel { Margin = new(20, 16, 20, 16) };
        content.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mizu;component/SettingsStyles.xaml", UriKind.Relative) });
        TextElement.SetForeground(content, Foreground);
        Content = content;

        TextBox Field(string label, string value, int limit)
        {
            content.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 5) });
            var input = new TextBox
            {
                Text = value, MaxLength = limit, Padding = new(9, 6, 9, 6), Margin = new(0, 0, 0, 11),
                Foreground = Foreground, Background = theme.Brush("#FCFCF8"), BorderBrush = theme.Brush("#E7EBE1")
            };
            AutomationProperties.SetName(input, label);
            content.Children.Add(input);
            return input;
        }

        var word = Field("单词", entry.Word, 80);
        var meaning = Field("释义", entry.Meaning, 200);
        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        content.Children.Add(error);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 3, 0, 0)
        };
        content.Children.Add(actions);
        Button ActionButton(string label, bool primary)
        {
            var button = new Button
            {
                Content = label, Padding = new(15, 7, 15, 7), MinWidth = 68,
                Cursor = Cursors.Hand, Foreground = Foreground,
                BorderBrush = theme.Brush(primary ? "#8B9F7F" : "#E7EBE1"),
                Background = theme.Brush(primary ? "#DEE6D7" : "#FCFCF8")
            };
            actions.Children.Add(button);
            return button;
        }

        ActionButton("取消", false).Click += (_, _) => Close();
        var save = ActionButton("保存", true);
        save.Margin = new(8, 0, 0, 0);
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            if (owner.TryEditVocabulary(entry, word.Text, meaning.Text, out var message))
            {
                Close();
                return;
            }
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        word.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            meaning.Focus();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        Loaded += (_, _) => { word.Focus(); word.SelectAll(); };

        void RefreshTheme() => theme.Apply(owner.Settings.Theme);
        RefreshTheme();
        SourceInitialized += (_, _) => RefreshTheme();
        owner.SettingsApplied += RefreshTheme;
        Closed += (_, _) => owner.SettingsApplied -= RefreshTheme;
    }
}

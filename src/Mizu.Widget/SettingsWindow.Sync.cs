using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed partial class SettingsWindow
{
    private TextBlock? syncStatus;

    private UIElement FirebaseSyncTab()
    {
        var root = new StackPanel { Margin = new(1, 4, 12, 0) };
        root.Children.Add(Label("云端备份"));
        root.Children.Add(Label("备份待办、单词本和显示偏好。使用自己的 Firebase 项目和账号。", true));
        syncStatus = Label("", true);
        root.Children.Add(syncStatus);
        if (!widget.SyncAvailable)
        {
            syncStatus.Text = "调试与界面预览不联网同步。";
            return root;
        }

        var backups = widget.Backups;
        var signInPanel = new StackPanel();
        var signedInPanel = new StackPanel();
        root.Children.Add(signInPanel);
        root.Children.Add(signedInPanel);
        FirebaseConfig? selectedConfig = null;
        var configSummary = Label("可以粘贴网页应用的 Firebase 配置，也可以选择 TXT、JSON 或 JS 文件。", true);
        var config = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxLength = FirebaseConfig.MaximumConfigLength, Height = 88, Padding = new(9),
            Background = theme.Brush("#F2F4ED"), BorderBrush = theme.Brush("#DDE2D6")
        };
        System.Windows.Automation.AutomationProperties.SetName(config, "Firebase 网页配置");
        FirebaseConfig ReadConfig() => !string.IsNullOrWhiteSpace(config.Text) ? FirebaseConfig.Parse(config.Text)
            : selectedConfig ?? throw new FormatException("请先导入 Firebase 网页配置。");
        void ExportConfig(object sender, RoutedEventArgs e)
        {
            try
            {
                var project = backups.Connection?.Config ?? ReadConfig();
                var content = project.ExportWebConfig();
                var dialog = new SaveFileDialog
                {
                    Title = "导出 Firebase 网页配置", Filter = "JSON|*.json", DefaultExt = ".json",
                    FileName = $"mizu-firebase-{project.ProjectId}.json"
                };
                if (dialog.ShowDialog(this) != true) return;
                File.WriteAllText(dialog.FileName, content);
                syncStatus.Text = "配置已导出。可在网页版导入，再用同一 Firebase 账号登录。";
            }
            catch (FormatException ex) { syncStatus.Text = ex.Message; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { syncStatus.Text = "无法保存配置文件。"; }
        }
        signInPanel.Children.Add(configSummary);
        signInPanel.Children.Add(config);
        var configActions = new WrapPanel();
        signInPanel.Children.Add(configActions);
        configActions.Children.Add(ActionButton("选择配置文件", (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Firebase 网页配置", Filter = "Firebase 网页配置|*.txt;*.json;*.js"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > FirebaseConfig.MaximumConfigLength)
                    throw new FormatException("Firebase 配置最多 64 KB。");
                selectedConfig = FirebaseConfig.Parse(File.ReadAllText(dialog.FileName));
                config.Clear();
                configSummary.Text = "网页配置已读取。";
                syncStatus.Text = "请输入这个项目里的 Firebase Auth 邮箱和密码。";
            }
            catch (FormatException ex) { syncStatus.Text = ex.Message; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { syncStatus.Text = "无法读取配置文件。"; }
        }));
        configActions.Children.Add(ActionButton("导出网页配置", ExportConfig));

        var email = new TextBox
        {
            MaxLength = 254, Padding = new(9), Margin = new(0, 0, 0, 10),
            Background = theme.Brush("#F2F4ED"), BorderBrush = theme.Brush("#DDE2D6")
        };
        var password = new PasswordBox
        {
            MaxLength = 512, Padding = new(9), Margin = new(0, 0, 0, 4),
            Background = theme.Brush("#F2F4ED"), BorderBrush = theme.Brush("#DDE2D6")
        };
        System.Windows.Automation.AutomationProperties.SetName(email, "Firebase Auth 邮箱");
        System.Windows.Automation.AutomationProperties.SetName(password, "Firebase Auth 密码");
        signInPanel.Children.Add(Label("Firebase Auth 邮箱"));
        signInPanel.Children.Add(email);
        signInPanel.Children.Add(Label("密码"));
        signInPanel.Children.Add(password);
        var help = Label("首次使用需创建默认 Firestore 数据库、添加邮箱用户和发布访问规则。", true);
        signInPanel.Children.Add(help);
        signInPanel.Children.Add(ActionButton("查看配置指南", (_, _) =>
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://github.com/cqc0101/Mizu/blob/main/docs/firebase-sync.md") { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception) { syncStatus.Text = "无法打开配置指南，请在 Mizu 仓库查看 docs/firebase-sync.md。"; }
        }));

        var account = Label("", true);
        signedInPanel.Children.Add(account);
        var actions = new WrapPanel();
        signedInPanel.Children.Add(actions);
        var schedule = Label("", true);
        var autoBackup = Toggle("自动备份至云端", settings.AutoCloudBackup, enabled =>
        {
            if (settings.AutoCloudBackup == enabled) return;
            settings.AutoCloudBackup = enabled;
            widget.SaveSettings();
            backups.Schedule();
        });
        signedInPanel.Children.Add(autoBackup);
        signedInPanel.Children.Add(SliderRow("备份间隔", settings.CloudBackupIntervalMinutes, 1, 720, 1, "0' 分钟'", value =>
        {
            settings.CloudBackupIntervalMinutes = (int)value;
            Save();
            backups.Schedule();
        }));
        signedInPanel.Children.Add(schedule);

        var cleanup = BuildLocalCleanup();
        root.Children.Add(cleanup);
        var localActions = new WrapPanel();
        var undoDownload = ActionButton("恢复下载前的本机数据", async (_, _) => await RestoreLocalAsync(MainWindow.SyncBackupPath));
        var undoCleanup = ActionButton("恢复清理前的本机数据", async (_, _) => await RestoreLocalAsync(MainWindow.CleanupBackupPath));
        localActions.Children.Add(undoDownload);
        localActions.Children.Add(undoCleanup);
        root.Children.Add(localActions);

        void Refresh()
        {
            root.IsEnabled = !backups.Busy;
            syncStatus.Text = backups.Message;
            autoBackup.IsChecked = settings.AutoCloudBackup;
            bool connected = backups.Connection is not null;
            signInPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            signedInPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            account.Text = backups.Connection is { } session
                ? $"项目 {session.Config.ProjectId}\n账号 {session.Email}\n" +
                  (session.LastSyncAt is { } at ? $"上次确认云端版本 {at.ToLocalTime():yyyy-MM-dd HH:mm}" : "尚未备份")
                : "";
            schedule.Text = backups.NextBackupAt is { } next
                ? $"Mizu 运行时自动上传。下次检查约在 {next.ToLocalTime():MM-dd HH:mm}。"
                : "仅在 Mizu 运行时自动检查，恢复云端数据始终由你手动发起。";
            undoDownload.Visibility = File.Exists(MainWindow.SyncBackupPath) ? Visibility.Visible : Visibility.Collapsed;
            undoCleanup.Visibility = File.Exists(MainWindow.CleanupBackupPath) ? Visibility.Visible : Visibility.Collapsed;
        }
        backups.Changed += Refresh;
        Closed += (_, _) => backups.Changed -= Refresh;

        signInPanel.Children.Add(ActionButton("登录 Firebase", async (_, _) =>
        {
            FirebaseConfig imported;
            try
            {
                imported = ReadConfig();
                if (string.IsNullOrWhiteSpace(email.Text) || password.Password.Length == 0)
                    throw new FormatException("请填写 Firebase Auth 邮箱和密码。");
            }
            catch (FormatException ex) { syncStatus.Text = ex.Message; return; }
            var login = backups.SignInAsync(imported, email.Text, password.Password, lifetime.Token);
            password.Clear();
            if (!await login) return;
            selectedConfig = null; config.Clear(); email.Clear();
            configSummary.Text = "可以粘贴网页应用的 Firebase 配置，也可以选择 TXT、JSON 或 JS 文件。";
        }));
        actions.Children.Add(ActionButton("上传备份至云端", async (_, _) =>
        {
            FlushPendingSave();
            await backups.UploadAsync(false, cloud =>
                ConfirmCloud(cloud, "覆盖云端备份", "云端有不同版本。要用当前本机数据更新云端备份吗？"), lifetime.Token);
        }));
        actions.Children.Add(ActionButton("恢复云端备份", async (_, _) =>
        {
            FlushPendingSave();
            var token = lifetime.Token;
            if (await backups.RestoreCloudAsync(cloud =>
                ConfirmCloud(cloud, "恢复云端备份", "云端内容将替换本机的单词本、待办和显示设置。本机当前数据会另存备份。"), token)
                && !token.IsCancellationRequested)
                ReloadSyncPage(backups.Message);
        }));
        actions.Children.Add(ActionButton("退出登录", async (_, _) => await backups.SignOutAsync(lifetime.Token)));
        actions.Children.Add(ActionButton("导出网页配置", ExportConfig));
        Refresh();
        return root;
    }

    private bool ConfirmCloud(FirestoreSnapshot cloud, string title, string detail)
    {
        string count = $"云端包含 {cloud.Snapshot.Words.Length} 个单词、{cloud.Snapshot.Todos.Length} 项待办。";
        return System.Windows.MessageBox.Show(this, $"{count}\n\n{detail}", title,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private UIElement BuildLocalCleanup()
    {
        var panel = new StackPanel { Margin = new(0, 8, 0, 0) };
        panel.Children.Add(Label("勾选想清空的项目。清理单词、待办或显示设置时会备份当前本机数据并暂停自动上传。", true));
        var choices = new List<(LocalCleanup Flag, CheckBox Box)>();
        var clear = ActionButton("清空所选内容", async (_, _) =>
        {
            var selection = choices.Where(choice => choice.Box.IsChecked == true).ToArray();
            if (selection.Length == 0) return;
            string names = string.Join("、", selection.Select(choice => choice.Box.Content as string));
            FlushPendingSave();
            var token = lifetime.Token;
            var mask = selection.Aggregate(LocalCleanup.None, (flags, choice) => flags | choice.Flag);
            if (await widget.Backups.CleanupAsync(mask, () =>
                System.Windows.MessageBox.Show(this, $"确认清空所选内容？\n\n{names}",
                    "清空本机内容", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                token) && !token.IsCancellationRequested)
                ReloadSyncPage(widget.Backups.Message);
        });
        clear.IsEnabled = false;
        void Item(LocalCleanup flag, string title)
        {
            var check = Toggle(title, false, value =>
                clear.IsEnabled = value || choices.Any(choice => choice.Box.IsChecked == true));
            choices.Add((flag, check));
            panel.Children.Add(check);
        }
        Item(LocalCleanup.Weather, "天气缓存");
        Item(LocalCleanup.Poetry, "诗词缓存");
        Item(LocalCleanup.Battery, "外设读数缓存");
        Item(LocalCleanup.Vocabulary, "单词本");
        Item(LocalCleanup.Todos, "待办清单数据");
        Item(LocalCleanup.Display, "显示设置（云端可恢复）");
        panel.Children.Add(clear);
        return new Expander { Header = "清空本地缓存或数据", Content = panel, Margin = new(0, 12, 0, 8) };
    }

    private async Task RestoreLocalAsync(string path)
    {
        FlushPendingSave();
        var token = lifetime.Token;
        if (await widget.Backups.RestoreLocalAsync(path, () =>
            System.Windows.MessageBox.Show(this, "恢复这份本机备份？当前内容会留作备份，自动上传将暂停。",
                "恢复本机备份", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
            token) && !token.IsCancellationRequested)
            ReloadSyncPage(widget.Backups.Message);
    }

    private void ReloadSyncPage(string message)
    {
        Close();
        widget.ReopenSyncSettings(message);
    }
    internal void ShowSync(string message)
    {
        SelectTab("同步");
        if (syncStatus is not null) syncStatus.Text = message;
    }
}

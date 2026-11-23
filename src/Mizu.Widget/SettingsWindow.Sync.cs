using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed partial class SettingsWindow
{
    private readonly FirebaseSyncClient firebase = new();
    private FirebaseConnection? firebaseConnection;
    private TextBlock? syncStatus;

    private UIElement FirebaseSyncTab()
    {
        var root = new StackPanel { Margin = new(1, 4, 12, 0) };
        root.Children.Add(Label("使用自己的 Firebase 项目"));
        root.Children.Add(Label("手动同步待办、单词本和显示偏好。项目、账号与写入权限由你管理。", true));
        syncStatus = Label("", true);
        root.Children.Add(syncStatus);
        if (!widget.SyncAvailable)
        {
            syncStatus.Text = "调试与界面预览不联网同步。";
            return root;
        }

        firebaseConnection = FirebaseConnection.Restore(settings.ProtectedFirebaseSync);
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
        signInPanel.Children.Add(configSummary);
        signInPanel.Children.Add(config);
        signInPanel.Children.Add(ActionButton("选择配置文件", (_, _) =>
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
        var recovery = new StackPanel();
        var restore = ActionButton("恢复上次下载前的本机数据", (_, _) => RestoreSyncBackup());
        recovery.Children.Add(restore);
        root.Children.Add(recovery);
        void Refresh()
        {
            bool connected = firebaseConnection is not null;
            signInPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            signedInPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            account.Text = firebaseConnection is { } session
                ? $"项目 {session.Config.ProjectId}\n账号 {session.Email}\n" +
                  (session.LastSyncAt is { } at ? $"上次同步 {at.ToLocalTime():yyyy-MM-dd HH:mm}" : "尚未同步")
                : "";
            restore.Visibility = File.Exists(MainWindow.SyncBackupPath) ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task Run(string progress, Func<CancellationToken, Task> operation)
        {
            if (!root.IsEnabled) return;
            root.IsEnabled = false;
            syncStatus.Text = progress;
            var lifetimeToken = lifetime.Token;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(55));
            try { await operation(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!lifetimeToken.IsCancellationRequested) syncStatus.Text = "同步请求超时，请重试。";
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException)
            {
                syncStatus.Text = ex is InvalidDataException ? ex.Message : "数据格式无效，请检查配置后重试。";
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                syncStatus.Text = "Firebase 响应异常，请检查网络和项目设置后重试。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                syncStatus.Text = ex is FirebaseRequestException
                    ? ex.Message : "无法保存同步数据，请检查本机存储权限和可用空间。";
            }
            finally { if (!lifetimeToken.IsCancellationRequested) { root.IsEnabled = true; Refresh(); } }
        }

        signInPanel.Children.Add(ActionButton("登录 Firebase", async (_, _) =>
        {
            FirebaseConfig imported;
            try
            {
                imported = !string.IsNullOrWhiteSpace(config.Text) ? FirebaseConfig.Parse(config.Text)
                    : selectedConfig ?? throw new FormatException("请先导入 Firebase 网页配置。");
                if (string.IsNullOrWhiteSpace(email.Text) || password.Password.Length == 0)
                    throw new FormatException("请填写 Firebase Auth 邮箱和密码。");
            }
            catch (FormatException ex) { syncStatus.Text = ex.Message; return; }
            await Run("正在登录 Firebase…", async token =>
            {
                var login = firebase.SignInAsync(imported, email.Text, password.Password, token);
                password.Clear();
                var session = await login;
                token.ThrowIfCancellationRequested();
                widget.SaveFirebaseSync(session);
                firebaseConnection = session;
                selectedConfig = null; config.Clear(); email.Clear();
                configSummary.Text = "可以粘贴网页应用的 Firebase 配置，也可以选择 TXT、JSON 或 JS 文件。";
                syncStatus.Text = "登录成功。点击「立即同步」连接 Firestore。";
            });
        }));
        actions.Children.Add(ActionButton("立即同步", async (_, _) => await Run("正在检查云端版本…", SyncNowAsync)));
        actions.Children.Add(ActionButton("退出登录", async (_, _) => await Run("正在退出…", _ =>
        {
            widget.SaveFirebaseSync(null);
            firebaseConnection = null;
            syncStatus.Text = "已退出。云端数据保留。";
            return Task.CompletedTask;
        })));
        Refresh();
        if (settings.ProtectedFirebaseSync is not null && firebaseConnection is null)
            syncStatus.Text = "本机无法读取已保存的登录，请重新登录。";
        return root;
    }

    private async Task SyncNowAsync(CancellationToken token)
    {
        if (firebaseConnection is not { } login) return;
        FlushPendingSave();
        var access = await firebase.RefreshAsync(login, token);
        token.ThrowIfCancellationRequested();
        if (access.Connection.RefreshToken != login.RefreshToken)
        {
            widget.SaveFirebaseSync(access.Connection);
            firebaseConnection = access.Connection;
        }
        var cloud = await firebase.DownloadAsync(access, token);
        token.ThrowIfCancellationRequested();
        var local = SyncSnapshot.Capture(settings);
        var localBytes = local.Encode();
        string localHash = SyncSnapshot.Hash(localBytes);
        string? cloudHash = cloud is null ? null : SyncSnapshot.Hash(cloud.Snapshot.Encode());
        var action = SyncSnapshot.Choose(localHash, cloudHash, login.LastSyncHash);
        if (action == SyncAction.Conflict)
        {
            var choice = System.Windows.MessageBox.Show(this,
                $"本机与云端都有独立版本。\n\n本机 {local.Todos.Length} 项待办、{local.Words.Length} 个单词\n" +
                $"云端 {cloud!.Snapshot.Todos.Length} 项待办、{cloud.Snapshot.Words.Length} 个单词\n\n" +
                "是：用本机版本更新云端\n否：用云端版本更新本机，并保留本机备份\n取消：保留两边",
                "选择同步版本", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (choice == MessageBoxResult.Cancel) { syncStatus!.Text = "已取消同步。"; return; }
            token.ThrowIfCancellationRequested();
            if (localHash != SyncSnapshot.Hash(SyncSnapshot.Capture(settings).Encode()))
            { syncStatus!.Text = "本机数据在确认期间发生了变化，请重新同步。"; return; }
            action = choice == MessageBoxResult.Yes ? SyncAction.Upload : SyncAction.Download;
        }

        if (action == SyncAction.Upload)
        {
            syncStatus!.Text = "正在上传到 Firestore…";
            await firebase.UploadAsync(access, localBytes, cloud?.Revision, token);
            token.ThrowIfCancellationRequested();
        }
        var next = access.Connection with
        {
            LastSyncHash = action == SyncAction.Download ? cloudHash : localHash,
            LastSyncAt = DateTimeOffset.UtcNow
        };
        widget.SaveFirebaseSync(next, action == SyncAction.Download ? cloud!.Snapshot : null);
        firebaseConnection = next;
        if (action == SyncAction.Download) ReloadSyncPage("已下载云端版本。下载前的数据已在本机备份。");
        else syncStatus!.Text = action == SyncAction.Upload ? "已将本机版本同步到 Firestore。" : "本机和云端已一致。";
    }

    private void RestoreSyncBackup()
    {
        if (System.Windows.MessageBox.Show(this, "恢复上次下载前的待办、单词本与显示偏好？这会把当前版本留作本机备份。",
            "恢复本机备份", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            if (new FileInfo(MainWindow.SyncBackupPath).Length > SyncSnapshot.MaximumBytes) throw new InvalidDataException();
            var snapshot = SyncSnapshot.Decode(File.ReadAllBytes(MainWindow.SyncBackupPath));
            widget.SaveFirebaseSync(firebaseConnection, snapshot);
            ReloadSyncPage("已恢复本机备份。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        { syncStatus!.Text = "无法恢复本机备份。"; }
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

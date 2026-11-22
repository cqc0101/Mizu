using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Mizu.Core.Services;

namespace Mizu.Widget;

public static class UpdateStartup
{
    private static string? Root
    {
        get {
            string app = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Path.GetFileName(app).Equals("app", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(app) : null;
        }
    }
    private static string? ArgumentSession(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length && Guid.TryParseExact(args[index + 1], "N", out var id) ? id.ToString("N") : null;
    }
    private static UpdatePlan ReadPlan(string session)
    {
        string path = Path.Combine(UpdateService.JobsDirectory, session, "plan.json");
        Mizu.Updating.PackageArchive.CheckTree(path);
        if (new FileInfo(path).Length > 65536) throw new IOException("更新状态无效。");
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(path)) ?? throw new IOException("更新计划缺失。");
        if (plan.Format != 1 || plan.Session != session || Root is not { } root || !UpdateDeployment.SamePath(plan.TargetRoot, root))
            throw new IOException("更新目录不匹配。");
        return plan;
    }
    public static bool ResumeInterrupted(string[] args)
    {
        if (Root is not { } root) return false;
        string marker = Path.Combine(root, ".mizu-update.pending");
        if (!File.Exists(marker)) return false;
        string session = File.ReadAllText(marker).Trim();
        if (!Guid.TryParseExact(session, "N", out _)) throw new IOException("更新恢复标记无效。");
        var plan = ReadPlan(session);
        if (ArgumentSession(args, "--update-session") == session) {
            if (plan.TargetVersion != typeof(App).Assembly.GetName().Version!.ToString(3)) throw new IOException("新版启动版本不匹配。");
            using var self = Process.GetCurrentProcess();
            string path = Path.Combine(UpdateService.JobsDirectory, session, "starting.json"), temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { Session = session, Root = plan.TargetRoot, Pid = self.Id, StartedUtcTicks = self.StartTime.ToUniversalTime().Ticks }));
            File.Move(temp, path, true);
            return false;
        }
        string helper = Path.Combine(UpdateService.JobsDirectory, session, "Mizu.Updater.exe");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "--recover", session, "--caller", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动更新恢复助手。");
        return true;
    }
    public static void Attach(MainWindow window, string[] args)
    {
        string? session = ArgumentSession(args, "--update-session");
        if (session is not null) {
            var plan = ReadPlan(session);
            if (plan.TargetVersion != typeof(App).Assembly.GetName().Version!.ToString(3)) throw new IOException("新版启动版本不匹配。");
            window.IsEnabled = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => {
                timer.Stop();
                try {
                    string path = Path.Combine(UpdateService.JobsDirectory, session, "healthy.json"), temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(new { Session = session, Version = plan.TargetVersion, Root = plan.TargetRoot, Pid = Environment.ProcessId }));
                    File.Move(temporary, path, true);
                    window.IsEnabled = true; window.Updates.SetStartupMessage("已启动新版 " + plan.TargetVersion + "。");
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("update.health", ex.GetType().Name); }
            };
            EventHandler? rendered = null;
            rendered = (_, _) => { window.ContentRendered -= rendered; timer.Start(); };
            window.ContentRendered += rendered;
            window.Closed += (_, _) => timer.Stop();
        }
        string? resultSession = ArgumentSession(args, "--update-result");
        if (resultSession is not null) {
            ReadPlan(resultSession);
            string result = Path.Combine(UpdateService.JobsDirectory, resultSession, "result.json");
            if (File.Exists(result) && new FileInfo(result).Length <= 65536) {
                using var document = JsonDocument.Parse(File.ReadAllText(result));
                window.Updates.SetStartupMessage(document.RootElement.GetProperty("Message").GetString() ?? "上次更新未完成，当前已恢复。");
            }
        }
    }
}

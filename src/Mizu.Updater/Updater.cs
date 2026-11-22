using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Mizu.Updating
{
    public sealed class Plan
    {
        public int Format { get; set; }
        public string Session { get; set; }
        public string TargetRoot { get; set; }
        public string Runtime { get; set; }
        public string Mode { get; set; }
        public string TargetVersion { get; set; }
        public string PackageSha256 { get; set; }
        public int ParentPid { get; set; }
        public long ParentStartedUtcTicks { get; set; }
    }
    public sealed class Journal
    {
        public string Phase { get; set; }
        public bool DataExisted { get; set; }
        public string[] OldEntries { get; set; }
        public int CandidatePid { get; set; }
        public long CandidateStartedUtcTicks { get; set; }
    }
    public static class JsonFile
    {
        public static T Read<T>(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 1024 * 1024) throw new IOException("更新状态文件过大。");
            return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }
        public static void Write(string path, object value)
        {
            string temp = path + ".tmp";
            WriteDurable(temp, new JavaScriptSerializer().Serialize(value));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static void WriteDurable(string path, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
    }
    public class Transaction
    {
        public readonly Plan Plan;
        public readonly string Job, DataRoot;
        public Journal State;
        public Action<string> Checkpoint;
        public string Marker { get { return Path.Combine(Plan.TargetRoot, ".mizu-update.pending"); } }
        private string Backup { get { return Path.Combine(Job, "backup"); } }
        private string Next { get { return Path.Combine(Plan.TargetRoot, ".mizu-next-" + Plan.Session); } }
        private string Old { get { return Path.Combine(Plan.TargetRoot, ".mizu-old-" + Plan.Session); } }
        private string StatePath { get { return Path.Combine(Job, "journal.json"); } }
        public Transaction(Plan plan, string job, string dataRoot)
        {
            Plan = plan; Job = Path.GetFullPath(job); DataRoot = Path.GetFullPath(dataRoot);
            Validate();
            State = File.Exists(StatePath) ? JsonFile.Read<Journal>(StatePath) : new Journal { Phase = "Prepared", OldEntries = new string[0] };
        }
        public void Validate()
        {
            Guid session;
            if (Plan == null || Plan.Format != 1 || !Guid.TryParseExact(Plan.Session, "N", out session) ||
                Path.GetFileName(Job) != Plan.Session || (Plan.Mode != "portable" && Plan.Mode != "installed") ||
                (Plan.Runtime != "win-x64" && Plan.Runtime != "win-x86" && Plan.Runtime != "win-arm64") ||
                !System.Text.RegularExpressions.Regex.IsMatch(Plan.TargetVersion ?? "", @"^\d+\.\d+\.\d+$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(Plan.PackageSha256 ?? "", "^[a-fA-F0-9]{64}$"))
                throw new IOException("更新计划无效。");
            string root = Path.GetFullPath(Plan.TargetRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(root, Plan.TargetRoot, StringComparison.OrdinalIgnoreCase) || root == Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar) ||
                DataRoot.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(DataRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(Job + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Job.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(root, DataRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root, "Mizu.exe")))
                throw new IOException("更新目标不是独立的 Mizu 程序目录。");
            PackageArchive.CheckTree(root); PackageArchive.CheckTree(Job); PackageArchive.CheckTree(DataRoot);
            if (File.Exists(Marker) && File.ReadAllText(Marker).Trim() != Plan.Session) throw new IOException("另一个更新尚未完成。");
        }
        public static void CopyTree(string source, string destination)
        {
            PackageArchive.CheckTree(source); PackageArchive.CheckTree(destination);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) { PackageArchive.CheckTree(file); File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false); }
            foreach (string folder in Directory.GetDirectories(source)) CopyTree(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
        public static void DeleteTree(string path, string boundary)
        {
            path = Path.GetFullPath(path); boundary = Path.GetFullPath(boundary).TrimEnd(Path.DirectorySeparatorChar);
            if (boundary.Length < 4 || path == Path.GetPathRoot(path) ||
                (!String.Equals(path, boundary, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("拒绝清理越界目录。");
            PackageArchive.CheckTree(path);
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.GetFiles(path)) { PackageArchive.CheckTree(file); File.Delete(file); }
            foreach (string folder in Directory.GetDirectories(path)) DeleteTree(folder, boundary);
            Directory.Delete(path);
        }
        private static bool Owned(string name)
        {
            return new[] { "app", "Mizu.exe", "说明与许可", "使用说明.txt", "mizu-package.json" }.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(name, @"^unins\d{3}\.(exe|dat|msg)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private string[] Entries() { return Directory.GetFileSystemEntries(Plan.TargetRoot).Select(Path.GetFileName).Where(Owned).ToArray(); }
        private void Save(string phase)
        {
            State.Phase = phase; JsonFile.Write(StatePath, State);
            if (Checkpoint != null) Checkpoint(phase);
        }
        public void VerifyPackage()
        {
            string package = Path.Combine(Job, Plan.Mode == "portable" ? "package.zip" : "package.exe");
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(package))
                if (!String.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), Plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("更新包校验失败。");
            if (Plan.Mode == "portable") PackageArchive.Inspect(package, Plan.Runtime);
            else PackageArchive.CheckExecutable(package, "");
        }
        public virtual void BackupRegistration() { }
        public virtual void RestoreRegistration() { }
        public void BackupBeforeApply()
        {
            VerifyPackage();
            if (!File.Exists(Path.Combine(Plan.TargetRoot, "app", "Mizu.exe"))) throw new IOException("原程序不完整，无法安全更新。");
            if (Directory.Exists(Backup)) throw new IOException("备份目录已存在，请恢复上次更新后重试。");
            Directory.CreateDirectory(Backup);
            State.OldEntries = Entries(); State.DataExisted = Directory.Exists(DataRoot);
            foreach (string entry in State.OldEntries)
            {
                string source = Path.Combine(Plan.TargetRoot, entry), target = Path.Combine(Backup, "program", entry);
                if (Directory.Exists(source)) CopyTree(source, target);
                else { PackageArchive.CheckTree(source); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, false); }
            }
            if (State.DataExisted) CopyTree(DataRoot, Path.Combine(Backup, "data"));
            BackupRegistration(); Save("BackedUp");
            string markerTemp = Marker + "." + Plan.Session;
            JsonFile.WriteDurable(markerTemp, Plan.Session);
            File.Move(markerTemp, Marker);
        }
        public virtual int RunInstaller(string setup, string arguments)
        {
            return InstallerJob.Run(setup, arguments, Job, Plan.Session);
        }
        public static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in value) {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') { result.Append('\\', slashes * 2 + 1); result.Append(ch); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(ch);
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
        private void AtomicCopy(string source, string destination)
        {
            PackageArchive.CheckTree(destination);
            string temporary = destination + ".mizu-copy-" + Plan.Session;
            File.Copy(source, temporary, true);
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        public void Apply()
        {
            if (State.Phase != "BackedUp" || !File.Exists(Marker)) throw new IOException("备份未完成。");
            Save("Applying");
            if (Plan.Mode == "portable")
            {
                string extracted = PackageArchive.Extract(Path.Combine(Job, "package.zip"), Plan.Runtime, Path.Combine(Job, "candidate"));
                CopyTree(Path.Combine(extracted, "app"), Next);
                Directory.Move(Path.Combine(Plan.TargetRoot, "app"), Old);
                Save("OldAppMoved");
                Directory.Move(Next, Path.Combine(Plan.TargetRoot, "app"));
                Save("NewAppMoved");
                foreach (string entry in new[] { "Mizu.exe", "mizu-package.json", "使用说明.txt" })
                    if (File.Exists(Path.Combine(extracted, entry))) AtomicCopy(Path.Combine(extracted, entry), Path.Combine(Plan.TargetRoot, entry));
                string docs = Path.Combine(extracted, "说明与许可");
                if (Directory.Exists(docs))
                {
                    string target = Path.Combine(Plan.TargetRoot, "说明与许可");
                    DeleteTree(target, Plan.TargetRoot); CopyTree(docs, target);
                }
            }
            else
            {
                string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NORESTARTAPPLICATIONS /RESTARTEXITCODE=3010 /DIR=" +
                    Quote(Plan.TargetRoot) + " /LOG=" + Quote(Path.Combine(Job, "installer.log"));
                int result = RunInstaller(Path.Combine(Job, "package.exe"), arguments);
                if (result != 0) throw new IOException("安装程序未能完成更新，退出码 " + result + "。");
            }
            PackageArchive.CheckExecutable(Path.Combine(Plan.TargetRoot, "app", "Mizu.exe"), Plan.Runtime);
            string actual = FileVersionInfo.GetVersionInfo(Path.Combine(Plan.TargetRoot, "app", "Mizu.exe")).FileVersion;
            Version version;
            if (!Version.TryParse(actual, out version) || version.ToString(3) != Plan.TargetVersion) throw new IOException("安装后的程序版本不匹配。");
            Save("Installed");
        }
        public virtual Process StartCandidate()
        {
            var start = new ProcessStartInfo(Path.Combine(Plan.TargetRoot, "app", "Mizu.exe"), "--update-session " + Plan.Session)
                { UseShellExecute = false, WorkingDirectory = Path.Combine(Plan.TargetRoot, "app") };
            return Process.Start(start);
        }
        public void WaitForHealth(int timeoutMilliseconds)
        {
            using (var candidate = StartCandidate())
            {
                if (candidate == null) throw new IOException("无法启动新版程序。");
                State.CandidatePid = candidate.Id; State.CandidateStartedUtcTicks = candidate.StartTime.ToUniversalTime().Ticks; Save("Starting");
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    if (candidate.HasExited) throw new IOException("新版程序启动后意外退出。");
                    if (Healthy()) { Save("Healthy"); return; }
                    Thread.Sleep(150);
                }
                throw new IOException("新版程序启动确认超时。");
            }
        }
        public bool Healthy()
        {
            LoadCandidate();
            string path = Path.Combine(Job, "healthy.json");
            if (!File.Exists(path)) return false;
            try {
                var value = JsonFile.Read<Dictionary<string, object>>(path);
                if ((string)value["Session"] != Plan.Session || (string)value["Version"] != Plan.TargetVersion ||
                    (string)value["Root"] != Plan.TargetRoot || Convert.ToInt32(value["Pid"]) != State.CandidatePid) return false;
                using (var process = Process.GetProcessById(State.CandidatePid))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == State.CandidateStartedUtcTicks &&
                        String.Equals(Path.GetFullPath(process.MainModule.FileName), Path.Combine(Plan.TargetRoot,"app","Mizu.exe"), StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }
        public void StopCandidate()
        {
            LoadCandidate();
            if (State.CandidatePid <= 0) return;
            try {
                using (var process = Process.GetProcessById(State.CandidatePid)) {
                    if (process.StartTime.ToUniversalTime().Ticks != State.CandidateStartedUtcTicks ||
                        !String.Equals(Path.GetFullPath(process.MainModule.FileName), Path.Combine(Plan.TargetRoot, "app", "Mizu.exe"), StringComparison.OrdinalIgnoreCase)) return;
                    process.Kill(); if (!process.WaitForExit(15000)) throw new IOException("新版程序尚未退出，暂不恢复文件。");
                }
            } catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        private void LoadCandidate()
        {
            if (State.CandidatePid > 0) return;
            string path = Path.Combine(Job, "starting.json");
            if (!File.Exists(path)) return;
            var value = JsonFile.Read<Dictionary<string,object>>(path);
            if ((string)value["Session"] != Plan.Session || (string)value["Root"] != Plan.TargetRoot) throw new IOException("新版启动会话不匹配。");
            State.CandidatePid = Convert.ToInt32(value["Pid"]); State.CandidateStartedUtcTicks = Convert.ToInt64(value["StartedUtcTicks"]);
        }
        public void Commit()
        {
            Save("Completed");
            try { if (File.Exists(Marker)) File.Delete(Marker); } catch (IOException) { }
            // The backup stays available for interrupted-update recovery. Old
            // runtime/staging copies are safe to remove only after health succeeds.
            try { DeleteTree(Old, Plan.TargetRoot); DeleteTree(Next, Plan.TargetRoot); } catch (IOException) { }
            try { WriteResult("Succeeded", "更新已完成。"); } catch (IOException) { }
        }
        public void Rollback()
        {
            if (State.Phase == "Completed") return;
            if (!File.Exists(Marker)) return; // Nothing was changed before backup finished.
            if (File.ReadAllText(Marker).Trim() != Plan.Session) throw new IOException("更新会话不匹配。");
            ValidateRecoveryBackup();
            if (Plan.Mode == "installed") InstallerJob.StopExisting(Plan.Session);
            StopCandidate();
            RestoreData();
            var oldEntries = new HashSet<string>(State.OldEntries, StringComparer.OrdinalIgnoreCase);
            foreach (string current in Entries())
                if (!oldEntries.Contains(current)) RemoveOwned(current);
            foreach (string entry in State.OldEntries)
            {
                string source = Path.Combine(Backup, "program", entry), destination = Path.Combine(Plan.TargetRoot, entry);
                if (Directory.Exists(source)) { DeleteTree(destination, Plan.TargetRoot); CopyTree(source, destination); }
                else AtomicCopy(source, destination);
            }
            RestoreRegistration();
            DeleteTree(Old, Plan.TargetRoot); DeleteTree(Next, Plan.TargetRoot);
            Save("RolledBack"); File.Delete(Marker);
            WriteResult("RolledBack", "更新未完成，已恢复旧版程序和原来的数据。");
        }
        private void ValidateRecoveryBackup()
        {
            if (State.OldEntries == null || !State.OldEntries.Contains("app", StringComparer.OrdinalIgnoreCase) ||
                !State.OldEntries.Contains("Mizu.exe", StringComparer.OrdinalIgnoreCase) || State.OldEntries.Any(entry => !Owned(entry)))
                throw new IOException("恢复记录不完整，当前文件已保留。");
            foreach (string entry in State.OldEntries)
            {
                string path = Path.Combine(Backup, "program", entry);
                PackageArchive.CheckTree(path);
                if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("程序备份不完整，当前文件已保留。");
            }
            if (!File.Exists(Path.Combine(Backup, "program", "app", "Mizu.exe")) ||
                (State.DataExisted && !Directory.Exists(Path.Combine(Backup, "data"))))
                throw new IOException("更新备份不完整，当前文件已保留。");
        }
        private void RestoreData()
        {
            string stage = DataRoot + ".mizu-restore-" + Plan.Session;
            string previous = DataRoot + ".mizu-pre-restore-" + Plan.Session;
            string dataParent = Path.GetDirectoryName(DataRoot);
            PackageArchive.CheckTree(stage);
            PackageArchive.CheckTree(previous);
            // Copy on the data volume first. The existing data stays intact
            // if copying fails, and directory moves can be resumed after interruption.
            if (!Directory.Exists(previous) || !Directory.Exists(DataRoot))
            {
                if (State.DataExisted)
                {
                    DeleteTree(stage, dataParent);
                    CopyTree(Path.Combine(Backup, "data"), stage);
                }
                if (Directory.Exists(DataRoot)) Directory.Move(DataRoot, previous);
                if (State.DataExisted) Directory.Move(stage, DataRoot);
            }
            Save("DataRestored");
            try { DeleteTree(previous, dataParent); } catch (IOException) { }
        }
        private void RemoveOwned(string name)
        {
            if (!Owned(name)) throw new IOException("拒绝移除非程序文件。");
            string path = Path.Combine(Plan.TargetRoot, name);
            if (Directory.Exists(path)) DeleteTree(path, Plan.TargetRoot); else { PackageArchive.CheckTree(path); File.Delete(path); }
        }
        public void WriteResult(string status, string message)
        {
            JsonFile.Write(Path.Combine(Job, "result.json"), new { Session = Plan.Session, Status = status, Message = message });
        }
        public void RestartOld()
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Plan.TargetRoot, "app", "Mizu.exe"), "--update-result " + Plan.Session)
                { UseShellExecute = false, WorkingDirectory = Path.Combine(Plan.TargetRoot, "app") });
        }
    }
    public sealed class WindowsTransaction : Transaction
    {
        private const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7481F09E-89BC-44F4-9A51-66F31CDB4869}_is1";
        public WindowsTransaction(Plan plan, string job, string dataRoot) : base(plan, job, dataRoot) { }
        public sealed class RegistryValue { public string Name { get; set; } public string Kind { get; set; } public object Value { get; set; } }
        public sealed class RegistrySnapshot { public bool Present { get; set; } public List<RegistryValue> Values { get; set; } }
        public override void BackupRegistration()
        {
            if (Plan.Mode != "installed") return;
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            using (var key = root.OpenSubKey(Uninstall))
            {
                var snapshot = new RegistrySnapshot { Present = key != null, Values = new List<RegistryValue>() };
                if (key != null) foreach (string name in key.GetValueNames())
                    snapshot.Values.Add(new RegistryValue { Name = name, Kind = key.GetValueKind(name).ToString(), Value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) });
                JsonFile.Write(Path.Combine(Job, "registry-" + view + ".json"), snapshot);
            }
        }
        public override void RestoreRegistration()
        {
            if (Plan.Mode != "installed") return;
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            {
                var snapshot = JsonFile.Read<RegistrySnapshot>(Path.Combine(Job, "registry-" + view + ".json"));
                if (!snapshot.Present) { root.DeleteSubKeyTree(Uninstall, false); continue; }
                using (var key = root.CreateSubKey(Uninstall))
                {
                    foreach (string name in key.GetValueNames()) key.DeleteValue(name, false);
                    foreach (var value in snapshot.Values)
                    {
                        var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), value.Kind);
                        object data = value.Value;
                        if (kind == RegistryValueKind.DWord) data = Convert.ToInt32(data);
                        else if (kind == RegistryValueKind.QWord) data = Convert.ToInt64(data);
                        else if (kind == RegistryValueKind.MultiString) data = ((System.Collections.IEnumerable)data).Cast<object>().Select(Convert.ToString).ToArray();
                        else if (kind == RegistryValueKind.Binary) data = ((System.Collections.IEnumerable)data).Cast<object>().Select(Convert.ToByte).ToArray();
                        key.SetValue(value.Name, data, kind);
                    }
                }
            }
        }
    }
    public static class Program
    {
        private static void WaitForCaller(int pid, string root, long started)
        {
            if (pid <= 0) return;
            try {
                using (var process = Process.GetProcessById(pid)) {
                    string image = Path.GetFullPath(process.MainModule.FileName);
                    if ((started > 0 && process.StartTime.ToUniversalTime().Ticks != started) ||
                        (!String.Equals(image, Path.Combine(root, "app", "Mizu.exe"), StringComparison.OrdinalIgnoreCase) &&
                         !String.Equals(image, Path.Combine(root, "Mizu.exe"), StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("更新发起程序不匹配。");
                    if (!process.WaitForExit(45000)) throw new IOException("旧版程序尚未退出，请稍后重试。");
                }
            } catch (ArgumentException) { }
        }
        [STAThread] public static int Main(string[] args)
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return Run(args, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizu-Updates"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizu"), identity.User.Value);
        }
        public static int Run(string[] args, string jobsRoot, string dataRoot, string mutexIdentity)
        {
            Transaction transaction = null;
            try
            {
                Guid session;
                if (args.Length < 2 || (args[0] != "--apply" && args[0] != "--recover") || !Guid.TryParseExact(args[1], "N", out session)) return 2;
                string job = Path.Combine(jobsRoot, session.ToString("N"));
                if (!String.Equals(Path.GetFullPath(System.Reflection.Assembly.GetExecutingAssembly().Location), Path.Combine(job, "Mizu.Updater.exe"), StringComparison.OrdinalIgnoreCase)) return 2;
                var plan = JsonFile.Read<Plan>(Path.Combine(job, "plan.json"));
                using (var mutex = new Mutex(false, "Local\\Mizu.Update." + mutexIdentity))
                using (var appMutex = new Mutex(false, "Local\\Mizu." + mutexIdentity))
                {
                    bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return 3;
                    try
                    {
                        int caller = plan.ParentPid;
                        long started = plan.ParentStartedUtcTicks;
                        if (args[0] == "--recover" && args.Length == 4 && args[2] == "--caller" && Int32.TryParse(args[3], out caller)) started = 0;
                        transaction = new WindowsTransaction(plan, job, dataRoot);
                        if (args[0] == "--apply") {
                            if (plan.ParentPid <= 0) throw new IOException("更新发起程序无效。");
                            transaction.VerifyPackage();
                            JsonFile.Write(Path.Combine(job, "ready.json"), new { Session = plan.Session, HelperPid = Process.GetCurrentProcess().Id });
                        }
                        WaitForCaller(caller, plan.TargetRoot, started);
                        return Execute(transaction, args[0] == "--recover", appMutex);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("无法开始更新，原来的程序保持不变。\n" + error.Message, "Mizu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
        private static void AcquireApp(Mutex mutex)
        {
            bool acquired; try { acquired = mutex.WaitOne(15000); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("另一个 Mizu 实例仍在运行，请退出后再恢复更新。");
        }
        private static int Execute(Transaction transaction, bool recover, Mutex appMutex)
        {
            // Called while the per-user updater mutex is held, including rollback.
            bool appOwned = false;
            try {
                if (recover) {
                    if (transaction.State.Phase == "Completed" || transaction.Healthy()) transaction.Commit();
                    else {
                        transaction.StopCandidate(); AcquireApp(appMutex); appOwned = true;
                        transaction.Rollback(); appMutex.ReleaseMutex(); appOwned = false;
                    }
                    transaction.RestartOld(); return 0;
                }
                AcquireApp(appMutex); appOwned = true;
                transaction.BackupBeforeApply(); transaction.Apply();
                appMutex.ReleaseMutex(); appOwned = false;
                transaction.WaitForHealth(45000); transaction.Commit(); return 0;
            } catch (Exception error) {
                try { File.WriteAllText(Path.Combine(transaction.Job, "error.log"), error.ToString()); } catch { }
                if (transaction.State.Phase == "Completed") return 0;
                try {
                    bool changed = File.Exists(transaction.Marker);
                    transaction.StopCandidate();
                    if (!appOwned) { AcquireApp(appMutex); appOwned = true; }
                    transaction.Rollback();
                    transaction.WriteResult(changed ? "RolledBack" : "FailedBeforeChange", changed ? "更新未完成，已恢复原来的程序和数据。" : "更新未完成，旧版保持不变。");
                    appMutex.ReleaseMutex(); appOwned = false;
                    transaction.RestartOld();
                    MessageBox.Show("更新未完成，原来的程序和数据已保留。请稍后在设置中重试。", "Mizu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {
                    MessageBox.Show("更新恢复未能完成。请保留备份目录并重新运行启动器重试恢复：\n" + transaction.Job, "Mizu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
            finally { if (appOwned) appMutex.ReleaseMutex(); }
        }
    }
}

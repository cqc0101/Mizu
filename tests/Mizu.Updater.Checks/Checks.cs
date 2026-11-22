using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Mizu.Updating;

class UpdaterChecks
{
    static int checks;
    static string OldExe, NewExe, Helper, Folder;
    static void Check(bool condition,string name) { if(!condition)throw new Exception(name);checks++; }
    static void Fails(Action operation,string name) { bool failed=false;try{operation();}catch(IOException){failed=true;}Check(failed,name); }
    static string Hash(string path) { using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
    sealed class TestTransaction : Transaction
    {
        public string Payload, Mode="healthy", InstallerArguments="";
        public bool InstallerFails;
        public bool ActualInstaller;
        public TestTransaction(Plan plan,string job,string data) : base(plan,job,data) { }
        public override Process StartCandidate()
        {
            var start=new ProcessStartInfo(Path.Combine(Plan.TargetRoot,"app","Mizu.exe"),"--update-session "+Plan.Session){UseShellExecute=false};
            start.EnvironmentVariables["MIZU_FIXTURE_JOB"]=Job;
            start.EnvironmentVariables["MIZU_FIXTURE_DATA"]=DataRoot;
            start.EnvironmentVariables["MIZU_FIXTURE_MODE"]=Mode;
            return Process.Start(start);
        }
        public override int RunInstaller(string executable,string arguments)
        {
            InstallerArguments=arguments;
            if(ActualInstaller)return base.RunInstaller(executable,arguments);
            File.Copy(Path.Combine(Payload,"app","Mizu.exe"),Path.Combine(Plan.TargetRoot,"app","Mizu.exe"),true);
            File.WriteAllText(Path.Combine(Plan.TargetRoot,"app","new-file.dll"),"new file");
            File.WriteAllText(Path.Combine(Plan.TargetRoot,"unins000.dat"),"new uninstall record");
            if(InstallerFails) { File.WriteAllText(Path.Combine(DataRoot,"settings.json"),"changed-during-failed-install"); return 1; }
            return 0;
        }
    }
    static TestTransaction Create(string name,bool installed)
    {
        string scope=Path.Combine(Folder,name),root=Path.Combine(scope,"Mizu with spaces"),data=Path.Combine(scope,"user-data");
        string session=Guid.NewGuid().ToString("N"),job=Path.Combine(scope,session),payload=Path.Combine(scope,"payload","Mizu-win-x64");
        Directory.CreateDirectory(Path.Combine(root,"app"));Directory.CreateDirectory(data);Directory.CreateDirectory(job);Directory.CreateDirectory(Path.Combine(payload,"app"));
        File.Copy(OldExe,Path.Combine(root,"Mizu.exe"));File.Copy(OldExe,Path.Combine(root,"app","Mizu.exe"));
        File.WriteAllText(Path.Combine(root,"app","old-only.dll"),"old dependency");
        File.WriteAllText(Path.Combine(root,"keep-user-file.txt"),"untouched external user file");
        File.WriteAllText(Path.Combine(root,"mizu-package.json"),installed?"installed":"portable");
        File.WriteAllText(Path.Combine(root,"unins000.dat"),"old uninstall record");
        File.WriteAllText(Path.Combine(data,"settings.json"),"original-settings-todo-vocabulary");
        Directory.CreateDirectory(Path.Combine(data,"nested")); File.WriteAllText(Path.Combine(data,"nested","token.dat"),"protected-token-sentinel");
        File.Copy(NewExe,Path.Combine(payload,"Mizu.exe")); File.Copy(NewExe,Path.Combine(payload,"app","Mizu.exe"));
        File.Copy(Helper,Path.Combine(payload,"app","Mizu.Updater.exe"));
        File.WriteAllText(Path.Combine(payload,"app","Mizu.dll"),"fixture assembly");
        File.WriteAllText(Path.Combine(payload,"app","Mizu.runtimeconfig.json"),"{}");
        File.WriteAllText(Path.Combine(payload,"app","mizu-package.json"),"portable");
        string package=Path.Combine(job,installed?"package.exe":"package.zip");
        if(installed)File.Copy(NewExe,package);
        else using(var output=File.Create(package))using(var zip=new ZipArchive(output,ZipArchiveMode.Create)) {
            string parent=Path.GetDirectoryName(payload);
            foreach(string file in Directory.GetFiles(payload,"*",SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file,file.Substring(parent.Length+1).Replace('\\','/'));
        }
        var plan=new Plan {Format=1,Session=session,TargetRoot=root,Runtime="win-x64",Mode=installed?"installed":"portable",TargetVersion="0.7.12",PackageSha256=Hash(package)};
        JsonFile.Write(Path.Combine(job,"plan.json"),plan);
        return new TestTransaction(plan,job,data){Payload=payload};
    }
    static void Restored(Transaction tx,string label)
    {
        Check(Hash(Path.Combine(tx.Plan.TargetRoot,"app","Mizu.exe"))==Hash(OldExe),label+": original executable restored");
        Check(File.Exists(Path.Combine(tx.Plan.TargetRoot,"app","old-only.dll")) && !File.Exists(Path.Combine(tx.Plan.TargetRoot,"app","new-file.dll")),label+": old dependencies restored and new files removed");
        Check(File.ReadAllText(Path.Combine(tx.DataRoot,"settings.json"))=="original-settings-todo-vocabulary",label+": settings, todo and vocabulary restored");
        Check(File.ReadAllText(Path.Combine(tx.DataRoot,"nested","token.dat"))=="protected-token-sentinel",label+": nested protected data preserved");
        Check(File.ReadAllText(Path.Combine(tx.Plan.TargetRoot,"unins000.dat"))=="old uninstall record",label+": uninstall data restored");
        Check(File.ReadAllText(Path.Combine(tx.Plan.TargetRoot,"keep-user-file.txt"))=="untouched external user file",label+": unrelated root files preserved");
        Check(!File.Exists(tx.Marker),label+": recovery marker removed");
    }
    static int Main(string[] args)
    {
        try {
            OldExe=args[0];NewExe=args[1];Helper=args[2];Folder=Path.GetFullPath(args[3]);
            if(!Path.GetFileName(Folder).StartsWith("updater-integration-"))throw new Exception("Unsafe fixture directory");
            Directory.CreateDirectory(Folder);
            var success=Create("portable-success",false);
            success.BackupBeforeApply();success.Apply();success.WaitForHealth(10000);success.Commit();
            Check(Hash(Path.Combine(success.Plan.TargetRoot,"app","Mizu.exe"))==Hash(NewExe),"Portable success installs the new executable");
            Check(File.ReadAllText(Path.Combine(success.DataRoot,"settings.json"))=="original-settings-todo-vocabulary","Successful update preserves original user data");
            Check(!File.Exists(success.Marker) && success.State.Phase=="Completed","Successful health handshake commits the transaction");
            success.StopCandidate();
            var installer=Create("installer-failure",true);installer.InstallerFails=true;
            installer.BackupBeforeApply();Fails(installer.Apply,"Installer nonzero exit aborts");installer.Rollback();Restored(installer,"Failed installer");
            Check(installer.InstallerArguments.Contains("/NORESTART ") && installer.InstallerArguments.Contains("/DIR="+Transaction.Quote(installer.Plan.TargetRoot)),"Installer command preserves the exact target and prevents OS restart");
            var installedSuccess=Create("installer-success",true);
            installedSuccess.BackupBeforeApply();installedSuccess.Apply();installedSuccess.WaitForHealth(10000);installedSuccess.Commit();
            Check(Hash(Path.Combine(installedSuccess.Plan.TargetRoot,"app","Mizu.exe"))==Hash(NewExe),"Installed deployment completes a successful transaction");installedSuccess.StopCandidate();
            var real=Create("actual-inno-installer",true);real.ActualInstaller=true;
            File.Copy(args[4],Path.Combine(real.Job,"package.exe"),true);real.Plan.PackageSha256=Hash(Path.Combine(real.Job,"package.exe"));
            JsonFile.Write(Path.Combine(real.Job,"plan.json"),real.Plan);
            real.BackupBeforeApply();real.Apply();real.WaitForHealth(10000);real.Commit();
            Check(Hash(Path.Combine(real.Plan.TargetRoot,"app","Mizu.exe"))==Hash(NewExe) && File.ReadAllText(Path.Combine(real.DataRoot,"settings.json"))=="original-settings-todo-vocabulary","Real silent Inno installer updates an isolated target and preserves data");real.StopCandidate();
            var crash=Create("startup-crash",false);crash.Mode="crash";
            crash.BackupBeforeApply();crash.Apply();Fails(()=>crash.WaitForHealth(10000),"Crashing candidate does not pass health validation");crash.Rollback();Restored(crash,"Crashing candidate");
            var timeout=Create("startup-timeout",false);timeout.Mode="hang";
            timeout.BackupBeforeApply();timeout.Apply();Fails(()=>timeout.WaitForHealth(350),"Unresponsive candidate times out");timeout.Rollback();Restored(timeout,"Unresponsive candidate");
            var interrupted=Create("interrupted",false);
            interrupted.BackupBeforeApply();interrupted.Checkpoint=phase=>{if(phase=="OldAppMoved")throw new IOException("simulated helper interruption");};
            Fails(interrupted.Apply,"Interruption happens after the original app directory moved");
            Check(!Directory.Exists(Path.Combine(interrupted.Plan.TargetRoot,"app")) && File.Exists(interrupted.Marker),"Interrupted state remains recoverable without an app directory");
            var recovery=new Transaction(interrupted.Plan,interrupted.Job,interrupted.DataRoot);recovery.Rollback();Restored(recovery,"Interrupted update");
            var missingBackup=Create("missing-backup",false);
            missingBackup.BackupBeforeApply();missingBackup.Apply();
            File.Move(Path.Combine(missingBackup.Job,"backup","program","app","Mizu.exe"),Path.Combine(missingBackup.Job,"missing-app.exe"));
            Fails(missingBackup.Rollback,"Incomplete backup prevents destructive recovery");
            Check(Hash(Path.Combine(missingBackup.Plan.TargetRoot,"app","Mizu.exe"))==Hash(NewExe) &&
                File.ReadAllText(Path.Combine(missingBackup.DataRoot,"settings.json"))=="original-settings-todo-vocabulary" &&
                File.Exists(missingBackup.Marker),"Incomplete recovery preserves live files and leaves a recovery marker");
            var reentrant=Create("interrupted-data-restore",false);
            reentrant.BackupBeforeApply();reentrant.Apply();
            File.WriteAllText(Path.Combine(reentrant.DataRoot,"settings.json"),"damaged-by-new-version");
            Directory.Move(reentrant.DataRoot,reentrant.DataRoot+".mizu-pre-restore-"+reentrant.Plan.Session);
            var resumedData=new Transaction(reentrant.Plan,reentrant.Job,reentrant.DataRoot);
            resumedData.Rollback();Restored(resumedData,"Interrupted data restore");
            var tamper=Create("tampered",false);File.AppendAllText(Path.Combine(tamper.Job,"package.zip"),"tampering");
            Fails(tamper.BackupBeforeApply,"A post-download checksum change is caught by the helper");
            Check(!File.Exists(tamper.Marker) && Hash(Path.Combine(tamper.Plan.TargetRoot,"app","Mizu.exe"))==Hash(OldExe),"Tampering never changes the running version");
            var orphan=Create("orphan-candidate",false);orphan.Mode="hang";
            orphan.BackupBeforeApply();orphan.Apply();
            using(var process=orphan.StartCandidate()) {
                var budget=Stopwatch.StartNew();
                while(!File.Exists(Path.Combine(orphan.Job,"starting.json")) && budget.ElapsedMilliseconds<5000)System.Threading.Thread.Sleep(25);
                var recovered=new Transaction(orphan.Plan,orphan.Job,orphan.DataRoot);recovered.Rollback();
                Check(process.WaitForExit(5000),"Recovery finds and stops a candidate when the helper died before saving its PID");
                Restored(recovered,"Orphan candidate");
            }
            var invalid=Create("invalid-root",false);invalid.Plan.TargetRoot=Path.GetPathRoot(invalid.Plan.TargetRoot).TrimEnd(Path.DirectorySeparatorChar);
            Fails(()=>new Transaction(invalid.Plan,invalid.Job,invalid.DataRoot),"Drive-root replacement is rejected");
            var nested=Create("nested-program-data",false);
            string nestedRoot=Path.Combine(nested.DataRoot,"program");Directory.CreateDirectory(nestedRoot);File.Copy(OldExe,Path.Combine(nestedRoot,"Mizu.exe"));
            nested.Plan.TargetRoot=nestedRoot;
            Fails(()=>new Transaction(nested.Plan,nested.Job,nested.DataRoot),"A program nested inside its data directory is rejected before any rollback can delete it");
            var protocol=Create("standalone-protocol",false);protocol.Mode="parent";
            File.Copy(args[5],Path.Combine(protocol.Job,"Mizu.Updater.exe"),true);
            using(var parent=protocol.StartCandidate()) {
                protocol.Plan.ParentPid=parent.Id;protocol.Plan.ParentStartedUtcTicks=parent.StartTime.ToUniversalTime().Ticks;
                JsonFile.Write(Path.Combine(protocol.Job,"plan.json"),protocol.Plan);
                var start=new ProcessStartInfo(Path.Combine(protocol.Job,"Mizu.Updater.exe"),"--apply "+protocol.Plan.Session){UseShellExecute=false};
                start.EnvironmentVariables["MIZU_TEST_JOBS"]=Path.GetDirectoryName(protocol.Job);
                start.EnvironmentVariables["MIZU_TEST_DATA"]=protocol.DataRoot;
                start.EnvironmentVariables["MIZU_TEST_MUTEX"]=Guid.NewGuid().ToString("N");
                start.EnvironmentVariables["MIZU_FIXTURE_JOB"]=protocol.Job;
                start.EnvironmentVariables["MIZU_FIXTURE_DATA"]=protocol.DataRoot;
                start.EnvironmentVariables["MIZU_FIXTURE_MODE"]="healthy";
                using(var helper=Process.Start(start)) {
                    var budget=Stopwatch.StartNew();
                    while(!File.Exists(Path.Combine(protocol.Job,"ready.json")) && budget.ElapsedMilliseconds<10000 && !helper.HasExited)System.Threading.Thread.Sleep(25);
                    Check(File.Exists(Path.Combine(protocol.Job,"ready.json")) && !parent.HasExited,"Standalone helper acknowledges readiness before the parent exits");
                    Check(Hash(Path.Combine(protocol.Plan.TargetRoot,"app","Mizu.exe"))==Hash(OldExe),"Readiness does not replace files while the old app is running");
                    parent.Kill();parent.WaitForExit();
                    if(!helper.WaitForExit(15000)){helper.Kill();throw new Exception("Protocol helper did not complete");}
                    Check(helper.ExitCode==0,"Standalone helper applies, checks health and exits successfully");
                    var done=new Transaction(protocol.Plan,protocol.Job,protocol.DataRoot);
                    Check(done.State.Phase=="Completed" && !File.Exists(done.Marker),"Standalone helper commits its journal and clears recovery state");
                    done.StopCandidate();
                }
            }
            Check(Transaction.Quote(@"D:\Mizu folder\")=="\"D:\\Mizu folder\\\\\"","Windows command quoting preserves trailing backslashes");
            string jobTree=Path.Combine(Folder,"installer-child-tree");Directory.CreateDirectory(jobTree);
            using(var owner=Process.Start(new ProcessStartInfo(args[6],Transaction.Quote(args[7])+" "+Transaction.Quote(jobTree)+" "+Guid.NewGuid().ToString("N")){UseShellExecute=false})) {
                var budget=Stopwatch.StartNew();
                while(!File.Exists(Path.Combine(jobTree,"child.started")) && budget.ElapsedMilliseconds<10000 && !owner.HasExited)System.Threading.Thread.Sleep(25);
                Check(File.Exists(Path.Combine(jobTree,"child.started")),"Installer child tree starts inside the Windows job");
                string[] pids=File.ReadAllLines(Path.Combine(jobTree,"pids.txt"));
                using(var installerProcess=Process.GetProcessById(Int32.Parse(pids[0])))
                using(var childProcess=Process.GetProcessById(Int32.Parse(pids[1]))) {
                    owner.Kill();owner.WaitForExit();
                    Check(installerProcess.WaitForExit(5000) && childProcess.WaitForExit(5000),"Interrupting the helper terminates both installer and child before recovery");
                }
            }
            Console.WriteLine("PASS "+checks+" updater transaction checks. Isolated fixtures only; no real user settings or registration changed.");
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        finally {
            // Every candidate is explicitly stopped before fixture cleanup. Retain
            // failed test folders for diagnosis instead of deleting open binaries.
        }
    }
}

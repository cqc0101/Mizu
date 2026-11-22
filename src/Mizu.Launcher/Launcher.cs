using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;
internal static class Launcher {
    static string Quote(string value) {
        var text=new StringBuilder("\"");int slashes=0;
        foreach(char c in value) {
            if(c=='\\'){slashes++;continue;}
            if(c=='"'){text.Append('\\',slashes*2+1);text.Append(c);slashes=0;continue;}
            text.Append('\\',slashes);slashes=0;text.Append(c);
        }
        text.Append('\\',slashes*2);return text.Append('"').ToString();
    }
    [STAThread] static int Main(string[] args) {
        try {
            string folder=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string executable=Path.Combine(folder,"app","Mizu.exe");
            string pending=Path.Combine(folder,".mizu-update.pending");
            if(File.Exists(pending)) {
                Guid session;
                if(!Guid.TryParseExact(File.ReadAllText(pending).Trim(),"N",out session))throw new IOException("更新恢复标记无效。");
                string job=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mizu-Updates",session.ToString("N"));
                string planFile=Path.Combine(job,"plan.json");
                if(new FileInfo(planFile).Length>65536)throw new IOException("更新恢复计划无效。");
                var plan=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(planFile));
                if(!String.Equals(Path.GetFullPath((string)plan["TargetRoot"]).TrimEnd(Path.DirectorySeparatorChar),folder.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("更新恢复目录不匹配。");
                string helper=Path.Combine(job,"Mizu.Updater.exe");
                Process.Start(new ProcessStartInfo(helper,"--recover "+session.ToString("N")+" --caller "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});
                return 0;
            }
            if(!File.Exists(executable)){MessageBox.Show("请完整解压便携包，并保留 app 文件夹。","Mizu");return 1;}
            var forwarded=new StringBuilder();foreach(string arg in args){if(forwarded.Length>0)forwarded.Append(' ');forwarded.Append(Quote(arg));}
            using(var child=Process.Start(new ProcessStartInfo(executable,forwarded.ToString()){UseShellExecute=false,CreateNoWindow=true})) {
                if(child==null)return 1;
                if(args.Length==0 || Array.Exists(args, arg => arg=="--startup" || arg=="--restart"))return 0;
                child.WaitForExit();return child.ExitCode;
            }
        } catch(Exception ex) {MessageBox.Show("无法启动 Mizu："+ex.Message,"Mizu");return 1;}
    }
}

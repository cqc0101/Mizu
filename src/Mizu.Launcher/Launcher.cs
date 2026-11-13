using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
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
            if(!File.Exists(executable)){MessageBox.Show("请完整解压便携包，并保留 app 文件夹。","Mizu");return 1;}
            var forwarded=new StringBuilder();foreach(string arg in args){if(forwarded.Length>0)forwarded.Append(' ');forwarded.Append(Quote(arg));}
            using(var child=Process.Start(new ProcessStartInfo(executable,forwarded.ToString()){UseShellExecute=false,CreateNoWindow=true})) {
                if(child==null)return 1;
                if(args.Length==0)return 0;
                child.WaitForExit();return child.ExitCode;
            }
        } catch(Exception ex) {MessageBox.Show("无法启动 Mizu："+ex.Message,"Mizu");return 1;}
    }
}

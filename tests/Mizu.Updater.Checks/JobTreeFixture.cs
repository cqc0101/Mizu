using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
class JobTreeFixture
{
    static int Main(string[] args)
    {
        string directory=args[args.Length-1];
        if(args[0]=="child") {
            File.WriteAllText(Path.Combine(directory,"child.started"),"started");
            Thread.Sleep(30000);File.WriteAllText(Path.Combine(directory,"child.finished"),"finished");return 0;
        }
        var child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"child \""+directory+"\""){UseShellExecute=false});
        File.WriteAllText(Path.Combine(directory,"pids.txt"),Process.GetCurrentProcess().Id+"\n"+child.Id);
        Thread.Sleep(30000);return 0;
    }
}

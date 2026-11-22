using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
class FixtureApp
{
    static int Main(string[] args)
    {
        string mode = Environment.GetEnvironmentVariable("MIZU_FIXTURE_MODE") ?? "healthy";
        string job = Environment.GetEnvironmentVariable("MIZU_FIXTURE_JOB");
        string data = Environment.GetEnvironmentVariable("MIZU_FIXTURE_DATA");
        var plan = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(job,"plan.json")));
        using(var self=Process.GetCurrentProcess())
            File.WriteAllText(Path.Combine(job,"starting.json"),new JavaScriptSerializer().Serialize(new {
                Session=(string)plan["Session"],Root=(string)plan["TargetRoot"],Pid=self.Id,StartedUtcTicks=self.StartTime.ToUniversalTime().Ticks }));
        if (mode == "crash") { File.WriteAllText(Path.Combine(data,"settings.json"),"damaged-by-new-version"); Thread.Sleep(250); return 1; }
        if (mode == "healthy")
        {
            File.WriteAllText(Path.Combine(job,"healthy.json"),new JavaScriptSerializer().Serialize(new {
                Session=(string)plan["Session"], Version=(string)plan["TargetVersion"], Root=(string)plan["TargetRoot"], Pid=Process.GetCurrentProcess().Id }));
        }
        Thread.Sleep(30000); return 0;
    }
}

using System;
class HelperEntry
{
    [STAThread] static int Main(string[] args)
    {
        return Mizu.Updating.Program.Run(args,Environment.GetEnvironmentVariable("MIZU_TEST_JOBS"),Environment.GetEnvironmentVariable("MIZU_TEST_DATA"),Environment.GetEnvironmentVariable("MIZU_TEST_MUTEX"));
    }
}
class JobEntry
{
    static int Main(string[] args)
    {
        return Mizu.Updating.InstallerJob.Run(args[0],Mizu.Updating.Transaction.Quote(args[1]),args[1],args[2]);
    }
}

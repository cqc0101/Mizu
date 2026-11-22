using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Mizu.Updating
{
    // Windows owns the installer tree. If the helper is interrupted, closing its
    // job handle terminates the installer and its children before file recovery.
    public static class InstallerJob
    {
        [StructLayout(LayoutKind.Sequential)] struct Limits { public long ProcessTime,JobTime; public uint Flags; public UIntPtr MinWorking,MaxWorking; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct Extended { public Limits Basic; public Io Counters; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
        [StructLayout(LayoutKind.Sequential)] struct Accounting { public long User,Kernel,PeriodUser,PeriodKernel; public uint Faults,Total,Active,Terminated; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
            public int Size; public string Reserved,Desktop,Title; public uint X,Y,Width,Height,CharsX,CharsY,Fill,Flags; public short Show,ReservedBytes; public IntPtr ReservedData,Input,Output,Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process,Thread; public uint Pid,Tid; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenJobObject(uint access,bool inherit,string name);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,ref Extended value,int length);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int info,out Accounting value,int length,IntPtr returned);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string image,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
        [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        private static string Name(string session) { Guid id;if(!Guid.TryParseExact(session,"N",out id))throw new IOException("无效安装会话。");return "Local\\Mizu.Install."+session; }
        private static void Stop(IntPtr job)
        {
            Accounting value;
            if (!QueryInformationJobObject(job,1,out value,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero)) throw new Win32Exception();
            if(value.Active==0)return;
            if(!TerminateJobObject(job,1))throw new Win32Exception();
            var clock=Stopwatch.StartNew();
            do {
                if(!QueryInformationJobObject(job,1,out value,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero))throw new Win32Exception();
                if(value.Active==0)return;
                Thread.Sleep(50);
            } while(clock.ElapsedMilliseconds<15000);
            throw new IOException("安装程序尚未停止，已保留备份，请稍后重新运行启动器恢复。");
        }
        public static void StopExisting(string session)
        {
            IntPtr job=OpenJobObject(12,false,Name(session));
            if(job==IntPtr.Zero) { if(Marshal.GetLastWin32Error()==2)return; throw new Win32Exception(); }
            try { Stop(job); } finally { CloseHandle(job); }
        }
        public static int Run(string setup,string arguments,string directory,string session)
        {
            IntPtr job=CreateJobObject(IntPtr.Zero,Name(session));
            if(job==IntPtr.Zero)throw new Win32Exception();
            ProcessInfo process=new ProcessInfo();
            try {
                var limits=new Extended();limits.Basic.Flags=0x2000;
                if(!SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(Extended))))throw new Win32Exception();
                var startup=new Startup();startup.Size=Marshal.SizeOf(typeof(Startup));startup.Flags=1;startup.Show=0;
                if(!CreateProcess(setup,new StringBuilder(Transaction.Quote(setup)+" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000004,IntPtr.Zero,directory,ref startup,out process))throw new Win32Exception();
                if(!AssignProcessToJobObject(job,process.Process)) { TerminateProcess(process.Process,1);WaitForSingleObject(process.Process,15000);throw new Win32Exception(); }
                if(ResumeThread(process.Thread)==UInt32.MaxValue)throw new Win32Exception();
                if(WaitForSingleObject(process.Process,600000)!=0)throw new IOException("安装程序未能在限定时间内完成。");
                uint code;if(!GetExitCodeProcess(process.Process,out code))throw new Win32Exception();
                return unchecked((int)code);
            } finally {
                try { Stop(job); }
                finally { if(process.Thread!=IntPtr.Zero)CloseHandle(process.Thread);if(process.Process!=IntPtr.Zero)CloseHandle(process.Process);CloseHandle(job); }
            }
        }
    }
}

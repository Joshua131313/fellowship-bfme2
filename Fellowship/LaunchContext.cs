using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

public static class LaunchContext
{
    public static ProcessStartInfo Game(string path)=>new(Path.GetFullPath(path))
    {
        UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(path))!
    };
    public static ProcessStartInfo Worker(string path,bool elevated)=>new(path)
    {
        UseShellExecute=true,Verb=elevated?"runas":"open",WorkingDirectory=AppContext.BaseDirectory
    };
    public static bool IsElevated(int pid)
    {
        using var process=OpenProcess(0x1000,false,pid); // Query limited information only.
        if(process.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        if(!OpenProcessToken(process,8,out var token))throw new Win32Exception(Marshal.GetLastWin32Error());
        using(token)
        {
            if(!GetTokenInformation(token,20,out int elevated,4,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
            return elevated!=0;
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(SafeAccessTokenHandle token,int kind,out int value,int size,out int returned);
}

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Fellowship;

public static class Installation
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Fellowship", "Fellowship.exe");
    public static bool IsInstalled => string.Equals(Path.GetFullPath(Environment.ProcessPath!), Executable, StringComparison.OrdinalIgnoreCase);
    public static bool StartsWithWindows
    {
        get { using var key=Registry.CurrentUser.OpenSubKey(RunKey); return string.Equals(key?.GetValue("Fellowship") as string, StartupCommand, StringComparison.OrdinalIgnoreCase); }
    }
    static string StartupCommand => "\""+Executable+"\" --startup";
    public static void SetStartup(bool enabled)
    {
        if(!IsInstalled)throw new InvalidOperationException("Install Fellowship before enabling launch at sign-in.");
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled)key.SetValue("Fellowship",StartupCommand);else key.DeleteValue("Fellowship",false);
        using var preference=Registry.CurrentUser.CreateSubKey(@"Software\Fellowship\Preferences");
        preference.SetValue("StartWithWindows",enabled?"1":"0");
    }
    public static void StartUpdate(string package)
    {
        var worker=Path.Combine(Path.GetDirectoryName(package)!,"installer-worker.exe");
        File.Copy(Environment.ProcessPath!,worker,true);
        var start=new ProcessStartInfo(NativePlacement.PhysicalPath(worker)){UseShellExecute=true};
        start.ArgumentList.Add("--install-update");start.ArgumentList.Add(NativePlacement.PhysicalPath(package));start.ArgumentList.Add(Environment.ProcessId.ToString());
        _ = Process.Start(start) ?? throw new IOException("Could not start the installer helper.");
    }
    public const long UpdateSpaceReserve=1024L*1024*1024;
    public static void ValidateFreeSpace(string drive,long available)
    {
        if(available<UpdateSpaceReserve)throw new IOException($"Not enough free space on {drive}. Free at least 1 GB for the update, then retry. Currently {available/1048576.0:0} MB is available.");
    }
    public static void EnsureUpdateSpace(params string[] paths)
    {
        foreach(var root in paths.Select(p=>Path.GetPathRoot(Path.GetFullPath(p))!).Distinct(StringComparer.OrdinalIgnoreCase))
        {var drive=new DriveInfo(root);ValidateFreeSpace(root,drive.AvailableFreeSpace);}
    }
    public static string InstallerError(int code)=>code switch
    {
        112=>"The disk is full. Free space on the Windows, installation and temporary-file drives, then retry. Aim for at least 1 GB free on each required drive.",
        1618=>"Another installation is running. Let it finish, then retry.",
        1603=>"Windows Installer could not complete the update. Close other Fellowship copies and retry. The log contains the detailed cause.",
        1619 or 1620=>"Windows could not open the update package. Download the update again.",
        1638=>"Windows reports another installed version. Check Installed apps and the installer log before retrying.",
        1602=>"Installation was cancelled. Your preferences are preserved.",
        _=>$"Windows Installer returned error {code}. Your preferences are preserved; review the log and retry."
    };
    public static async Task<int> RunUpdate(string package,int previousPid)
    {
        var window=new UpdateProgressWindow("UPDATE");window.Show();
        while(true)
        {
            string log=Path.Combine(Path.GetDirectoryName(package)!,"install-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".log");
            try
            {
                window.Report("Closing Fellowship…","Waiting for the previous version to finish safely.");
                try { using var previous=Process.GetProcessById(previousPid);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));await previous.WaitForExitAsync(timeout.Token); }
                catch(ArgumentException) { }
                catch(OperationCanceledException){throw new IOException("The previous Fellowship is still running. Quit its tray icon, then retry.");}
                // Give the former room's native worker time to observe its parent exit.
                await Task.Delay(1500);
                EnsureUpdateSpace(package,Executable,Path.GetTempPath(),Environment.SystemDirectory);
                int code;var busyTimer=Stopwatch.StartNew();
                do
                {
                    window.Report("Installing update…","Keep this window open. Fellowship will reopen when installation finishes.");
                    var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false,CreateNoWindow=true};
                    foreach(var arg in new[]{"/i",package,"/qn","/norestart","/L*v",log})start.ArgumentList.Add(arg);
                    using var installer=Process.Start(start)??throw new IOException("Could not start Windows Installer.");
                    await installer.WaitForExitAsync();code=installer.ExitCode;
                    if(code==1618&&busyTimer.Elapsed<TimeSpan.FromMinutes(2))
                    {window.Report("Waiting for Windows Installer…","Another installation is running. Retrying automatically…");await Task.Delay(5000);}
                    else break;
                }while(true);
                if(code is not (0 or 3010))throw new IOException(InstallerError(code)+$" (Code {code})");
                window.Report("Opening Fellowship…",code==3010?"Update installed. Windows may require a restart.":"Update installed successfully.",100);
                if(!File.Exists(Executable))throw new IOException("Installation finished but Fellowship.exe was not found. Open the log for details.");
                // The new app cleans again once this worker exits, releasing its file lock.
                if(Version.TryParse(FileVersionInfo.GetVersionInfo(Executable).FileVersion,out var installedVersion))
                    UpdateCache.Clean(Path.Combine(Settings.Folder,"versions"),installedVersion,Environment.ProcessPath!);
                Process.Start(new ProcessStartInfo(Executable){UseShellExecute=true});window.Finish();return 0;
            }
            catch(Exception error)
            {
                try{if(!File.Exists(log))File.WriteAllText(log,error.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}
                var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler closed=(_,_)=>decision.TrySetResult(false);window.Closed+=closed;
                window.Failure(error.Message,()=>decision.TrySetResult(true),File.Exists(log)?log:null);
                bool retry=await decision.Task;window.Closed-=closed;if(!retry)return 1;
            }
        }
    }
}

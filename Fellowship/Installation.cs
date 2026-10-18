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
    public static async Task<int> RunUpdate(string package,int previousPid)
    {
        try
        {
            try { using var previous=Process.GetProcessById(previousPid);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await previous.WaitForExitAsync(timeout.Token); }
            catch(ArgumentException) { }
            var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false};
            start.ArgumentList.Add("/i");start.ArgumentList.Add(package);start.ArgumentList.Add("/passive");start.ArgumentList.Add("/norestart");
            using var installer=Process.Start(start)??throw new IOException("Could not start Windows Installer.");
            await installer.WaitForExitAsync();
            if(installer.ExitCode is not (0 or 3010))throw new IOException($"Windows Installer returned {installer.ExitCode}. Your preferences are preserved; run the downloaded installer to retry.");
            Process.Start(new ProcessStartInfo(Executable){UseShellExecute=true});return 0;
        }
        catch(Exception error){MessageBox.Show("Fellowship update: "+error.Message);return 1;}
    }
}

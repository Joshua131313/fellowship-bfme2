using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

// Ecth 7.27's installed CLI was inspected without executing it. It accepts
// -switch with -1.00, -1.06 or -latest, plus -force. No arbitrary patch-name
// argument exists. In particular, -latest must never stand in for an older beta.
public static class PatchSwitcher
{
    const string OpenRequired="Ecth's Patch Switcher must be open to switch patches from Fellowship.";
    const string Unsupported="Ecth's Patch Switcher cannot switch directly to this exact patch. Select it in the switcher, then return to Fellowship.";
    const string UnverifiedLatest="The switcher's latest patch could not be verified. Select the required patch in the switcher, then return to Fellowship.";
    static readonly SemaphoreSlim gate=new(1,1);
    static Process? pending;
    sealed record Switcher(string Executable,string Root);
    public static bool IsSwitching
    {
        get
        {
            if(gate.CurrentCount==0)return true;
            var worker=Volatile.Read(ref pending);
            if(worker==null)return false;
            try{return !worker.HasExited;}
            catch(InvalidOperationException){return false;}
            catch(Win32Exception){return true;}
        }
    }

    public static async Task Switch(string executable,string patch)
    {
        if(!await gate.WaitAsync(0))throw new InvalidOperationException("A patch switch is already in progress.");
        try
        {
            if(pending!=null)
            {
                if(!pending.HasExited)throw new InvalidOperationException("A patch switch is already in progress.");
                pending.Dispose();pending=null;
            }
            if(GamesRunning())throw new InvalidOperationException("Close BFME2 before switching patches.");
            if(EditorsRunning())throw new InvalidOperationException("Close Worldbuilder and FinalBIG before switching patches.");
            var switcher=FindRunning(executable)??throw new InvalidOperationException(OpenRequired);
            if(!SameExecutable(executable,GameTarget(switcher.Root,GameInstallation.Find())??""))
                throw new InvalidOperationException("Fellowship and Ecth's Patch Switcher must use the same BFME2 installation.");
            patch=GamePatches.Normalize(patch);
            if(!GamePatches.IsKnown(patch))throw new InvalidOperationException(Unsupported);
            if(await Task.Run(()=>GamePatches.Active(executable))==patch)return;
            string[] arguments=Arguments(patch,GamePatches.LatestInstalled(executable,switcher.Root),GamePatches.LatestSwitcherTarget(executable,switcher.Root));
            // Recheck immediately before creating the native CLI worker. Do not
            // allow its -force behavior to close a newly opened game or editor.
            if(GamesRunning())throw new InvalidOperationException("Close BFME2 before switching patches.");
            if(EditorsRunning())throw new InvalidOperationException("Close Worldbuilder and FinalBIG before switching patches.");
            if(!SameExecutable(executable,GameTarget(switcher.Root,GameInstallation.Find())??""))
                throw new InvalidOperationException("Fellowship and Ecth's Patch Switcher must use the same BFME2 installation.");
            var start=new ProcessStartInfo(switcher.Executable)
            {
                WorkingDirectory=Path.GetDirectoryName(switcher.Executable)!,UseShellExecute=true,
                Verb="runas",WindowStyle=ProcessWindowStyle.Hidden
            };
            foreach(string argument in arguments)start.ArgumentList.Add(argument);
            try{pending=Process.Start(start)??throw new InvalidOperationException("Ecth's Patch Switcher could not be started.");}
            catch(Win32Exception error) when(error.NativeErrorCode==1223){throw new InvalidOperationException("Windows cancelled the patch switch.",error);}
            catch(Exception error) when(error is Win32Exception or IOException){throw new InvalidOperationException("Ecth's Patch Switcher could not be started.",error);}
            try{await pending.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));}
            catch(TimeoutException)
            {
                // Never terminate a worker while it may be replacing archives.
                // Retain its handle so a retry cannot overlap the unfinished job.
                throw new InvalidOperationException("Patch switching is still running. Wait for Ecth's Patch Switcher to finish before retrying.");
            }
            int exit=pending.ExitCode;pending.Dispose();pending=null;
            if(exit!=0)throw new InvalidOperationException($"Ecth's Patch Switcher returned error {exit}. Check the switcher and retry.");
            var deadline=DateTimeOffset.UtcNow.AddSeconds(20);
            do
            {
                await Task.Delay(1100);
                if(await Task.Run(()=>GamePatches.Active(executable))==patch)return;
            }while(DateTimeOffset.UtcNow<deadline);
            throw new InvalidOperationException("The required patch could not be verified after switching. Check Ecth's Patch Switcher before playing.");
        }
        finally{gate.Release();}
    }
    static string[] Arguments(string required,string? latestInstalled,string? latestSwitcherTarget)
    {
        required=GamePatches.Normalize(required);
        if(!GamePatches.IsKnown(required))throw new InvalidOperationException(Unsupported);
        if(required is "1.00" or "1.06")return ["-switch","-"+required,"-force"];
        if(latestInstalled==null||latestSwitcherTarget==null||GamePatches.Normalize(latestInstalled)!=GamePatches.Normalize(latestSwitcherTarget))
            throw new InvalidOperationException(UnverifiedLatest);
        if(required!=GamePatches.Normalize(latestInstalled))throw new InvalidOperationException(Unsupported);
        return ["-switch","-latest","-force"];
    }
    static bool EditorsRunning()
    {
        foreach(string name in new[]{"worldbuilder","finalbig"})
        {
            var processes=Process.GetProcessesByName(name);
            try{if(processes.Length>0)return true;}
            finally{foreach(var process in processes)process.Dispose();}
        }
        return false;
    }
    static bool GamesRunning()
    {
        if(GameLauncher.IsRunning())return true;
        foreach(string name in new[]{"lotrbfme2ep1","lotrbfme2ep1v9"})
        {
            var processes=Process.GetProcessesByName(name);
            try{if(processes.Length>0)return true;}
            finally{foreach(var process in processes)process.Dispose();}
        }
        return false;
    }
    static Switcher? FindRunning(string executable)
    {
        if(!OperatingSystem.IsWindows())return null;
        var candidates=Roots(executable).SelectMany(root=>new[]{new Switcher(Path.Combine(root,"data","BFME2Switcher.exe"),root),new Switcher(Path.Combine(root,"BFME2Switcher.exe"),root)})
            .Where(s=>File.Exists(s.Executable)&&File.Exists(Path.Combine(Path.GetDirectoryName(s.Executable)!,"noisrev.ini"))).ToArray();
        foreach(var process in Process.GetProcessesByName("BFME2Switcher"))
        using(process)
        {
            try
            {
                if(process.MainWindowHandle==0)continue;
                string? image=ImagePath(process.Id);
                if(image!=null&&candidates.FirstOrDefault(s=>SameExecutable(s.Executable,image)) is {} found)return found;
            }
            catch(Exception error) when(error is Win32Exception or InvalidOperationException){ }
        }
        return null;
    }
    static string? ImagePath(int pid)
    {
        using var handle=OpenProcess(0x1000,false,pid);
        if(handle.IsInvalid)return null;
        var path=new StringBuilder(32768);int length=path.Capacity;
        return QueryFullProcessImageName(handle,0,path,ref length)?path.ToString():null;
    }
    static bool SameExecutable(string first,string second)
    {
        try{return !string.IsNullOrWhiteSpace(first)&&!string.IsNullOrWhiteSpace(second)&&NativePlacement.PhysicalPath(first).Equals(NativePlacement.PhysicalPath(second),StringComparison.OrdinalIgnoreCase);}
        catch(Exception error) when(error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException){return false;}
    }
    static string? GameTarget(string switcherRoot,string registeredExecutable)
    {
        try
        {
            // The native switcher honors this override before its registry path.
            // Only a genuinely absent file permits registry fallback; unreadable
            // or malformed overrides must not redirect a switch to another game.
            using var input=File.OpenRead(Path.Combine(switcherRoot,"custom_bfme2_path.ini"));
            if(input.Length is <1 or >32768)return null;
            var bytes=new byte[input.Length];input.ReadExactly(bytes);
            string folder=new UTF8Encoding(false,true).GetString(bytes);
            // Python's Path(read_text()) does not strip quotes/newlines. Reject
            // ambiguous input rather than interpreting a different destination.
            if(folder!=folder.Trim()||!Path.IsPathFullyQualified(folder)||folder.IndexOfAny(Path.GetInvalidPathChars())>=0||!Directory.Exists(folder))return null;
            string target=Path.Combine(folder,"lotrbfme2.exe");
            return File.Exists(target)?target:null;
        }
        catch(FileNotFoundException){return registeredExecutable;}
        catch(DirectoryNotFoundException){return registeredExecutable;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return null;}
    }
    static IEnumerable<string> Roots(string executable)
    {
        var roots=new List<string>();
        try
        {
            if(!string.IsNullOrWhiteSpace(executable)&&Path.GetDirectoryName(Path.GetFullPath(executable)) is {} game)
            {
                roots.Add(Path.Combine(game,"BFME2 Ecth's Patch Switcher"));
                if(Path.GetDirectoryName(game) is {} parent)roots.Add(Path.Combine(parent,"BFME2 Ecth's Patch Switcher"));
            }
        }
        catch(Exception error) when(error is ArgumentException or IOException or NotSupportedException){ }
        foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
            if(folder.Length>0)roots.Add(Path.Combine(folder,"BFME2 Ecth's Patch Switcher"));
        if(OperatingSystem.IsWindows())
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
        try
        {
            using var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var uninstall=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if(uninstall==null)continue;
            foreach(string key in uninstall.GetSubKeyNames())
            {
                using var app=uninstall.OpenSubKey(key);string name=app?.GetValue("DisplayName") as string??"";
                if(!name.Contains("BFME2",StringComparison.OrdinalIgnoreCase)||!name.Contains("Patch",StringComparison.OrdinalIgnoreCase))continue;
                if(app?.GetValue("InstallLocation") is string location)roots.Add(location);
                if(app?.GetValue("UninstallString") is string command)
                {
                    var quoted=Regex.Match(command,"^\"([^\"]+)\"");string path=quoted.Success?quoted.Groups[1].Value:command;
                    if(Path.GetDirectoryName(path) is {} directory)roots.Add(directory);
                }
                if(app?.GetValue("DisplayIcon") is string icon)
                {
                    icon=Regex.Replace(icon.Trim(),@",\s*-?\d+\s*$","").Trim('"');
                    if(Path.GetDirectoryName(icon) is {} directory)roots.Add(Path.GetFileName(directory).Equals("data",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(directory)!:directory);
                }
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException){ }
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(SafeProcessHandle process,int flags,StringBuilder path,ref int length);
}

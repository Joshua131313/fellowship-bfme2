using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

// This adapter changes default-choice indexes and hero flags in a freshly launched process.
// It never patches executable files or an existing match. Unsupported binaries fail closed.
public static class HostPreset
{
    public const string SupportedHash = "D79265812638D141C4F02E5924B03100D3F69EACA10A9379CFE14B074FBAF716";
    public static string LogPath => Path.Combine(Settings.Folder,"host-preset.log");
    public static bool Matches(Room? room) => room is {Patch:"1.00"} && MatchRules.ResourceValues.Contains(room.Match.Resources) && MatchRules.CommandValues.Contains(room.Match.CommandPoints);

    public static void ValidateBinary(string path)
    {
        GameLauncher.Validate(path);
        using var stream=File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!,"game.dat"));
        if(Convert.ToHexString(SHA256.HashData(stream))!=SupportedHash)
            throw new InvalidOperationException("Automatic setup supports the verified BFME2 1.00 executable only. This game.dat differs; no game files were changed.");
    }

    // Run in an isolated elevated instance: the network-facing UI remains unelevated.
    public static async Task<int> Run(string path,string? encoded=null,bool navigate=false,bool host=true)
    {
        Directory.CreateDirectory(Settings.Folder);
        try
        {
            File.WriteAllText(LogPath,$"Preparing {(host?"host":"guest")} launch. Navigation: {(navigate?"enabled":"disabled")}.\r\n");
            ValidateBinary(path);
            var room=encoded==null?new Room():JsonSerializer.Deserialize<Room>(Convert.FromBase64String(encoded))??throw new InvalidOperationException("Invalid preset.");
            room.Match.Validate();
            if(!GameInstallation.Maps(path).Any(m=>m.Path==room.MapPath))throw new InvalidOperationException("Selected map was not found in this installation.");
            if(GameLauncher.IsRunning())throw new InvalidOperationException("Exit BFME2 before applying the host preset.");
            var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"My Battle for Middle-earth(tm) II Files");
            if(!Directory.Exists(profile))throw new InvalidOperationException("Run BFME2 once to create its player profile first.");
            var pref=Path.Combine(profile,"NetworkPref.ini");
            var original=File.Exists(pref)?File.ReadAllText(pref):"";
            File.WriteAllText(Path.Combine(Settings.Folder,"NetworkPref-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".bak"),original);
            if(host)File.WriteAllText(pref,ProfilePreset.MapPreferences(original,room.MapPath),new UTF8Encoding(false));
            var started=DateTime.Now;
            using var launcher=Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)!});
            var deadline=DateTime.UtcNow.AddSeconds(60);
            while(DateTime.UtcNow<deadline)
            {
                foreach(var process in Process.GetProcesses())
                {
                    using(process)
                    {
                        if(process.ProcessName is not ("game" or "game.dat"))continue;
                        if(process.StartTime<started.AddSeconds(-1))continue;
                        var module=process.MainModule;
                        if(module==null||!string.Equals(module.FileName,Path.Combine(Path.GetDirectoryName(path)!,"game.dat"),StringComparison.OrdinalIgnoreCase))continue;
                        if(host)ApplyDefaults(process.Id,module.BaseAddress,room.Match);
                        File.AppendAllText(LogPath,host?$"Applied: {room.Map}; {room.Match.Summary}. Process {process.Id}.\r\n":"Guest game launched.\r\n");
                        if(navigate)await GameNavigation.Run(process.Id,host);
                        else File.AppendAllText(LogPath,"Open Multiplayer → Network → Create Game. Assign teams and starting positions in the lobby.\r\n");
                        return 0;
                    }
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException("The launcher did not create a BFME2 process within 60 seconds.");
        }
        catch(Exception e){File.AppendAllText(LogPath,"FAILED: "+e.Message+"\r\n");return 1;}
    }

    static void ApplyDefaults(int pid,nint baseAddress,MatchRules rules)
    {
        using var handle=OpenProcess(0x438,false,pid);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        nint Address(int rva)=>baseAddress+rva;
        uint Read(nint address)
        {
            var bytes=new byte[4];
            if(!ReadProcessMemory(handle,address,bytes,4,out var count)||count!=4)throw new Win32Exception(Marshal.GetLastWin32Error());
            return BitConverter.ToUInt32(bytes);
        }
        void Expect(int rva,uint value)
        {
            if(Read(Address(rva))!=value)throw new InvalidOperationException("BFME2 rule data differs from the verified 1.00 layout. Preset was not applied.");
        }
        // Validate both descriptors and chosen values before changing either default.
        Expect(0x867c88,3);Expect(0x867c8c,(uint)Address(0x867c50));Expect(0x867c90,7);Expect(0x867c94,2);
        Expect(0x867cf0,4);Expect(0x867cf4,(uint)Address(0x867c98));Expect(0x867cf8,11);Expect(0x867cfc,2);
        Expect(0x867c6c,200);Expect(0x867cec,4000);
        Expect(0x867c20,0);Expect(0x867c24,1); // AllowCustomHeroes
        Expect(0x867c28,2);Expect(0x867c2c,1); // AllowRingHeroes
        uint cpIndex=(uint)Array.IndexOf(MatchRules.CommandValues,rules.CommandPoints);
        uint resourceIndex=(uint)Array.IndexOf(MatchRules.ResourceValues,rules.Resources);
        Expect(0x867c54+(int)cpIndex*8,(uint)rules.CommandPoints);
        Expect(0x867c9c+(int)resourceIndex*8,(uint)rules.Resources);
        void Write(int rva,uint value)
        {
            var address=Address(rva);
            if(!VirtualProtectEx(handle,address,4,0x04,out uint protection))throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if(!WriteProcessMemory(handle,address,BitConverter.GetBytes(value),4,out var count)||count!=4)throw new Win32Exception(Marshal.GetLastWin32Error());
                if(Read(address)!=value)throw new IOException("BFME2 preset readback failed.");
            }
            finally { VirtualProtectEx(handle,address,4,protection,out _); }
        }
        try{Write(0x867c94,cpIndex);Write(0x867cfc,resourceIndex);Write(0x867c24,rules.CustomHeroes?1u:0u);Write(0x867c2c,rules.RingHeroes?1u:0u);}
        catch
        {
            foreach(var (rva,value) in new[]{(0x867c94,2u),(0x867cfc,2u),(0x867c24,1u),(0x867c2c,1u)})
                try{Write(rva,value);}catch{}
            throw;
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint size,out nuint read);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint size,out nuint written);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(SafeProcessHandle process,nint address,nuint size,uint protection,out uint oldProtection);
}


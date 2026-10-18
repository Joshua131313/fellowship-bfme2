using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Fellowship;

public static class NativePlacement
{
    public static string LogPath=>Path.Combine(Settings.Folder,"native-placement.log");
    public static byte[] RequestBytes(int pid,NativeLobby.Snapshot snapshot,int team,int spot,bool readOnly=false)
    {
        if(team is <0 or >4||spot is <0 or >8)throw new InvalidDataException("Invalid team or starting spot.");
        var map=Encoding.ASCII.GetBytes(snapshot.Map);if(map.Length>=512)throw new InvalidDataException("Map path too long.");
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(0x46424d32u);writer.Write(2u);writer.Write(pid);writer.Write(snapshot.Identity);
        writer.Write(snapshot.Local.Row);writer.Write(team-1);writer.Write(spot-1);writer.Write(readOnly?1:0);
        for(int i=0;i<6;i++)writer.Write(0);
        writer.Write(map);writer.Write(new byte[512-map.Length]);writer.Write(new byte[532]);return stream.ToArray();
    }
    public static byte[] SetupRequestBytes(int pid,NativeLobby.Snapshot snapshot,NativeSetup setup)
    {
        setup.Rules.Validate();var target=Encoding.ASCII.GetBytes(setup.Map);
        if(target.Length>=512||!setup.Map.StartsWith("maps\\",StringComparison.Ordinal)||!setup.Map.EndsWith(".map",StringComparison.Ordinal))throw new InvalidDataException("Invalid native map.");
        var bytes=RequestBytes(pid,snapshot,0,0);using var stream=new MemoryStream(bytes);using var writer=new BinaryWriter(stream);
        stream.Position=568;writer.Write(1);writer.Write(target);stream.Position=1084;
        writer.Write(setup.Resources);writer.Write(setup.CommandPoints);writer.Write(setup.CustomHeroes?1:0);writer.Write(setup.RingHeroes?1:0);return bytes;
    }
    internal static string Extract()
    {
        var folder=Path.Combine(Settings.Folder,"native",AppUpdates.VersionText);Directory.CreateDirectory(folder);
        foreach(var name in new[]{"Fellowship.NativeHost.exe","Fellowship.Native.v017a.dll"})
        {
            using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("Fellowship.Native."+name)??throw new IOException("Native bridge is missing from this build.");
            using var bytes=new MemoryStream();resource.CopyTo(bytes);var payload=bytes.ToArray();var path=Path.Combine(folder,name);
            if(File.Exists(path)&&SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(payload)))continue;
            File.WriteAllBytes(path+".tmp",payload);File.Move(path+".tmp",path,true);
        }
        return PhysicalPath(Path.Combine(folder,"Fellowship.NativeHost.exe"));
    }
    // Resolve MSIX file virtualization before passing paths to the non-packaged game.
    public static string PhysicalPath(string path)
    {
        using var file=File.OpenRead(path);var buffer=new StringBuilder(32768);
        uint count=GetFinalPathNameByHandleW(file.SafeFileHandle,buffer,(uint)buffer.Capacity,0);
        if(count==0||count>=buffer.Capacity)throw new IOException("Cannot resolve native helper path.");
        string resolved=buffer.ToString();return resolved.StartsWith(@"\\?\")?resolved[4..]:resolved;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
    public static async Task Apply(string gamePath,Room room,string localId,bool wait,Func<bool> stillValid)
    {
        var operations=Path.Combine(Settings.Folder,"native");Directory.CreateDirectory(operations);
        var marker=Path.Combine(operations,"operation-"+Guid.NewGuid().ToString("N")+".active");File.WriteAllText(marker,"");
        try
        {
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};
            start.ArgumentList.Add(wait?"--native-wait":"--native-apply");start.ArgumentList.Add(gamePath);
            start.ArgumentList.Add(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(room)));start.ArgumentList.Add(localId);start.ArgumentList.Add(Environment.ProcessId.ToString());start.ArgumentList.Add(PhysicalPath(marker));
            using var helper=Process.Start(start)??throw new IOException("Could not start the direct placement helper.");
            while(!helper.HasExited)
            {
                if(!stillValid())
                {
                    File.Delete(marker);await Task.WhenAny(helper.WaitForExitAsync(),Task.Delay(7000));
                    throw new IOException("Room or choices changed; apply the current selections again.");
                }
                await Task.Delay(200);
            }
            if(helper.ExitCode!=0)throw new IOException(File.Exists(LogPath)?File.ReadAllText(LogPath):"Native placement failed. The BFME2 lobby remains available for manual setup.");
        }
        finally{File.Delete(marker);}
    }

    public static async Task<int> Run(string gamePath,string encoded,string localId,bool wait,int parentPid,string? marker=null)
    {
        Directory.CreateDirectory(Settings.Folder);
        try
        {
            using var parent=Process.GetProcessById(parentPid);
            HostPreset.ValidateBinary(gamePath);
            var room=JsonSerializer.Deserialize<Room>(Convert.FromBase64String(encoded))??throw new InvalidDataException("Missing room.");
            var player=room.Players.Single(p=>p.Id==localId);
            if(player.Team is <0 or >4||player.Spot is <0 or >8||player.Spot>0&&!room.Spots.Any(s=>s.Number==player.Spot))throw new InvalidDataException("Choose a valid team and map spot in Fellowship.");
            string host=Extract();var deadline=DateTime.UtcNow.AddSeconds(wait?300:2);
            File.WriteAllText(LogPath,"Waiting for BFME2's LAN lobby. Open/create or join it manually.\n");
            while(DateTime.UtcNow<deadline)
            {
                if(parent.HasExited||marker!=null&&!File.Exists(marker))throw new IOException("Fellowship closed or its room changed; native assignment cancelled.");
                foreach(var process in Process.GetProcessesByName("game.dat").Concat(Process.GetProcessesByName("game")))
                using(process)
                {
                    if(!string.Equals(process.MainModule?.FileName,Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat"),StringComparison.OrdinalIgnoreCase))continue;
                    using var reader=new NativeLobby(process.Id);var snapshot=reader.Current();if(snapshot==null)continue;
                    if(!snapshot.Map.Equals(room.MapPath,StringComparison.OrdinalIgnoreCase))throw new IOException("BFME2 has a different map selected. Select the Fellowship map before applying.");
                    if(snapshot.HostAddress!=room.HostAddress)throw new IOException("BFME2 is connected to a different host. Join the Fellowship host's LAN game first.");
                    var request=Path.Combine(Settings.Folder,"native","placement-"+Guid.NewGuid().ToString("N")+".bin");
                    try
                    {
                        if(parent.HasExited||marker!=null&&!File.Exists(marker))throw new IOException("Native assignment cancelled.");
                        File.WriteAllBytes(request,RequestBytes(process.Id,snapshot,player.Team,player.Spot));
                        var launch=new ProcessStartInfo(host){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(host)!};launch.ArgumentList.Add(PhysicalPath(request));
                        using var bridge=Process.Start(launch)??throw new IOException("Could not start the native bridge.");
                        await bridge.WaitForExitAsync();var result=File.ReadAllBytes(request);int status=BitConverter.ToInt32(result,32);
                        if(bridge.ExitCode!=0||status!=2)throw new IOException(Describe(status)+$" (bridge status {status})");
                        int stable=0;for(int attempt=0;attempt<20;attempt++)
                        {
                            var current=reader.Current();
                            if(current?.Identity==snapshot.Identity&&current.Local.Team==player.Team-1&&current.Local.Spot==player.Spot-1){if(++stable==3){File.WriteAllText(LogPath,$"Verified in BFME2: Team {player.Team}, spot {player.Spot}. Native LAN setters; no navigation.\n");return 0;}}
                            else stable=0;
                            await Task.Delay(250);
                        }
                        throw new IOException("BFME2 did not retain the selected team and spot. Review its native lobby manually.");
                    }
                    finally{File.Delete(request);}
                }
                if(!wait)break;await Task.Delay(500);
            }
            throw new IOException("No matching BFME2 LAN lobby became available. Open/create or join the matching lobby.");
        }
        catch(Exception error){File.WriteAllText(LogPath,error.Message+"\nNo navigation was used. The native lobby remains open for manual correction.");return 1;}
    }
    public static string Describe(int code)=>code switch
    {
        -3 or -4 or -6=>"BFME2 is not in an editable LAN setup lobby.",
        -7=>"The native map changed. Select the matching map and retry.",
        -8=>"Could not identify your local BFME2 player.",
        -9=>"That starting spot is occupied in BFME2. Choose another spot.",
        -10=>"That starting spot is not valid on the native map.",
        -14=>"Only the native BFME2 host can change map and rules.",
        -15=>"The selected map is missing from BFME2's native map catalog.",
        -16=>"BFME2 did not retain the map or rules.",
        -20=>"This BFME2 executable is not the verified 1.00 build.",
        -21=>"BFME2's engine window was not found.",
        -24 or -25=>"BFME2 did not accept the native bridge request. Leave its lobby open and retry.",
        _=>$"Direct assignment could not be verified (code {code}). Review BFME2's lobby manually."
    };
}

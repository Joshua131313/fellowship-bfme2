using Fellowship;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

static class LiveProbe
{
    public static async Task Setup(string exe,int pid,string output)
    {
        var log=new List<string>();
        try
        {
            using var reader=new NativeLobby(pid);var initial=reader.Current()??throw new Exception("Open a LAN setup lobby first.");
            if(initial.Local.Row!=0||initial.Slots.Count(s=>s.State==6)!=1)throw new Exception("Setup probe requires an otherwise empty host lobby.");
            var original=initial.Setup??throw new Exception("Native rules unavailable");
            var choice=new LivePlacement.Choice(initial.Local.Team+1,initial.Local.Spot+1);
            string gamePath=GameInstallation.Find();
            var map=GameInstallation.Maps(gamePath).First(m=>m.Path!=initial.Map&&GameMapData.Spots(gamePath,m.Path).Count==6);
            var edited=new NativeSetup(map.Path,original.Resources==2000?4000:2000,original.CommandPoints==400?200:400,!original.CustomHeroes,!original.RingHeroes);
            string name="Fellowship.Sync."+Guid.NewGuid().ToString("N");
            using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            var start=new ProcessStartInfo(exe){UseShellExecute=false};
            foreach(string arg in new[]{"--native-sync",gamePath,pid.ToString(),Environment.ProcessId.ToString(),name})start.ArgumentList.Add(arg);
            using var helper=Process.Start(start)!;using var timeout=new CancellationTokenSource(60000);
            await pipe.WaitForConnectionAsync(timeout.Token);
            async Task<LivePlacement.Reply> Tick(LivePlacement.Command command)
            {await LivePlacement.Send(pipe,command,timeout.Token);return await LivePlacement.Receive<LivePlacement.Reply>(pipe,timeout.Token);}
            async Task Native(byte[] bytes)
            {
                string path=Path.GetFullPath("research/setup-probe.bin");File.WriteAllBytes(path,bytes);
                var launch=new ProcessStartInfo(Path.GetFullPath("native/bin/Fellowship.NativeHost.exe")){UseShellExecute=false};launch.ArgumentList.Add(NativePlacement.PhysicalPath(path));
                using var bridge=Process.Start(launch)!;await bridge.WaitForExitAsync(timeout.Token);
                if(bridge.ExitCode!=0)throw new Exception("Native setup restore failed: "+BitConverter.ToInt32(File.ReadAllBytes(path),32));
                await Task.Delay(400);
            }
            var command=new LivePlacement.Command("setup-probe",initial.Map,initial.HostAddress,choice,original);
            try
            {
                for(int settle=0;settle<3;settle++){await Tick(command);await Task.Delay(400);}
                var changed=command with{Map=edited.Map,Setup=edited,Choice=new(choice.Team,0)};
                await Tick(changed);
                if(reader.Current()?.Setup!=edited)throw new Exception("Live native map/rule write mismatch");
                log.Add("PASS live host map change to "+map.Name+", resources, command points and both hero flags with independent native readback");
                await Native(NativePlacement.SetupRequestBytes(pid,reader.Current()!,original));
                var imported=await Tick(changed);
                if(!imported.ImportSetup||imported.Setup!=original)throw new Exception("Game-side map/rules not imported");
                log.Add("PASS separate native map/rule edit observed and returned to Fellowship");
                var acknowledged=await Tick(command);
                if(acknowledged.ImportSetup||reader.Current()?.Setup!=original)throw new Exception("Setup echo loop");
                log.Add("PASS setup import acknowledgement without echo");
            }
            finally
            {
                pipe.Dispose();await helper.WaitForExitAsync(timeout.Token);
                await Native(NativePlacement.SetupRequestBytes(pid,reader.Current()!,original));
                await Native(NativePlacement.RequestBytes(pid,reader.Current()!,choice.Team,choice.Spot));
                log.Add("RESTORED original map, all four rules, team and spot");
            }
        }
        catch(Exception error){log.Add("FAIL "+error);}
        File.WriteAllLines(output,log);
    }
    public static async Task Connection(string exe,int pid,string snapshotFile,string output)
    {
        try
        {
            var initial=JsonSerializer.Deserialize<NativeLobby.Snapshot>(File.ReadAllText(snapshotFile))!;
            string name="Fellowship.Sync."+Guid.NewGuid().ToString("N");
            using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            var start=new ProcessStartInfo(exe){UseShellExecute=true,Verb="runas"};
            foreach(string arg in new[]{"--native-sync",GameInstallation.Find(),pid.ToString(),Environment.ProcessId.ToString(),name})start.ArgumentList.Add(arg);
            using var helper=Process.Start(start)!;using var timeout=new CancellationTokenSource(30000);
            var connected=pipe.WaitForConnectionAsync(timeout.Token);
            if(await Task.WhenAny(connected,helper.WaitForExitAsync(timeout.Token))!=connected)throw new Exception(File.ReadAllText(NativePlacement.LogPath));
            await connected;
            var command=new LivePlacement.Command("connection-probe",initial.Map,initial.HostAddress,new(initial.Local.Team+1,initial.Local.Spot+1));
            LivePlacement.Reply reply=new("Waiting");
            for(int settle=0;settle<3;settle++){await LivePlacement.Send(pipe,command,timeout.Token);reply=await LivePlacement.Receive<LivePlacement.Reply>(pipe,timeout.Token);await Task.Delay(400);}
            if(reply.Observed!=command.Choice)throw new Exception("Connection did not return matching native state");
            pipe.Dispose();await helper.WaitForExitAsync(timeout.Token);
            File.WriteAllText(output,"PASS normal client to elevated helper connection and native state reply");
        }
        catch(Exception error){File.WriteAllText(output,"FAIL "+error);}
    }
    public static async Task Run(string exe,int pid,string output)
    {
        var log=new List<string>();
        try
        {
            using var reader=new NativeLobby(pid);
            var initial=reader.Current()??throw new Exception("Open a native LAN setup lobby first.");
            var original=new LivePlacement.Choice(initial.Local.Team+1,initial.Local.Spot+1);
            int free=Enumerable.Range(0,6).First(n=>n!=initial.Local.Spot&&!initial.Slots.Any(s=>s.State>1&&s.Spot==n));
            var edited=new LivePlacement.Choice(original.Team==4?3:4,free+1);
            string pipeName="Fellowship.Sync."+Guid.NewGuid().ToString("N");
            using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(50));
            var start=new ProcessStartInfo(exe){UseShellExecute=false};
            foreach(string arg in new[]{"--native-sync",GameInstallation.Find(),pid.ToString(),Environment.ProcessId.ToString(),pipeName})start.ArgumentList.Add(arg);
            using var helper=Process.Start(start)!;
            var connected=pipe.WaitForConnectionAsync(deadline.Token);
            if(await Task.WhenAny(connected,helper.WaitForExitAsync(deadline.Token))!=connected)throw new Exception(File.ReadAllText(NativePlacement.LogPath));
            await connected;
            var command=new LivePlacement.Command("live-probe",initial.Map,initial.HostAddress,original);
            async Task<LivePlacement.Reply> Tick(LivePlacement.Command request)
            {await LivePlacement.Send(pipe,request,deadline.Token);return await LivePlacement.Receive<LivePlacement.Reply>(pipe,deadline.Token);}
            async Task Restore()
            {
                var snapshot=reader.Current()??throw new Exception("Lobby closed during probe");
                string request=Path.GetFullPath("research/live-probe-restore.bin");
                File.WriteAllBytes(request,NativePlacement.RequestBytes(pid,snapshot,original.Team,original.Spot));
                var restore=new ProcessStartInfo(Path.GetFullPath("native/bin/Fellowship.NativeHost.exe")){UseShellExecute=false};restore.ArgumentList.Add(NativePlacement.PhysicalPath(request));
                using var process=Process.Start(restore)!;await process.WaitForExitAsync();
                if(process.ExitCode!=0)throw new Exception("Restore failed");
            }
            try
            {
                await Tick(command);await Task.Delay(400);await Tick(command);await Task.Delay(400);
                var baseline=await Tick(command);if(baseline.Observed!=original)throw new Exception("Initial read mismatch");
                var applied=await Tick(command with{Choice=edited});var actual=reader.Current()!;
                if(applied.Observed!=edited||actual.Local.Team+1!=edited.Team||actual.Local.Spot+1!=edited.Spot)throw new Exception("Outbound apply failed");
                log.Add("PASS Fellowship->native live team and spot change with independent readback");
                // A separate native setter invocation emulates a game-side edit, without UI input.
                await Restore();
                var incoming=await Tick(command with{Choice=edited});
                if(!incoming.Import||incoming.Observed!=original)throw new Exception("Native edit not imported");
                log.Add("PASS native-side team/spot edit detected and returned to client");
                var ack=await Tick(command);if(ack.Import||ack.Observed!=original)throw new Exception("Echo loop");
                log.Add("PASS acknowledged import does not echo a write");
                var wrong=await Tick(command with{Map="maps\\wrong.map",Choice=edited});
                if(wrong.Observed!=null||reader.Current()!.Local.Team!=initial.Local.Team)throw new Exception("Wrong-map write");
                log.Add("PASS mismatched map pauses sync without mutation");
            }
            finally{await Restore();pipe.Dispose();}
            await helper.WaitForExitAsync(deadline.Token);
            log.Add("PASS pipe closure stops worker; original Team "+original.Team+" / spot "+original.Spot+" restored");
        }
        catch(Exception error){log.Add("FAIL "+error);}
        File.WriteAllLines(output,log);
    }
}

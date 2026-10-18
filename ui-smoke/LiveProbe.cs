using Fellowship;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

static class LiveProbe
{
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

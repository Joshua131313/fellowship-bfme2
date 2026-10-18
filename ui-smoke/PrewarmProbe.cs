using Fellowship;
using System.Diagnostics;
using System.IO.Pipes;

static class PrewarmProbe
{
    public static async Task Run(string exe)
    {
        string name="Fellowship.Sync."+Guid.NewGuid().ToString("N");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"--native-sync",GameInstallation.Find(),"0",Environment.ProcessId.ToString(),name})start.ArgumentList.Add(arg);
        using var worker=Process.Start(start)??throw new Exception("Worker did not start");
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            // No subsequent request is sent: the probe never reaches native writes.
            await LivePlacement.Send(pipe,new LivePlacement.Command("prewarm-test",GameMap.Rhun.Path,"203.0.113.254",new(0,0)),timeout.Token);
            var reply=await LivePlacement.Receive<LivePlacement.Reply>(pipe,timeout.Token);
            if(reply.Done||reply.Import||reply.ImportSetup||reply.Observed!=null||reply.Setup!=null||
               !(reply.Status.StartsWith("Sync ready.")||reply.Status.StartsWith("BFME2 detected.")))throw new Exception("Invalid prelaunch response: "+reply.Status);
            Console.WriteLine("PASS real worker authenticates its parent and acknowledges preparation without game writes");
            pipe.Dispose();await worker.WaitForExitAsync(timeout.Token);
            if(worker.ExitCode!=0)throw new Exception("Prepared worker failed on disconnect");
            Console.WriteLine("PASS prepared worker exits when its room pipe closes");
        }
        finally{if(!worker.HasExited)worker.Kill();}
    }
}

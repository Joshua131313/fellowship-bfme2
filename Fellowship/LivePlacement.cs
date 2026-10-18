using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

// One request at a time. Only local-player team/spot values cross this pipe.
public static class LivePlacement
{
    public sealed record Choice(int Team,int Spot);
    public sealed record Command(string Room,string Map,string Host,Choice Choice);
    public sealed record Reply(string Status,Choice? Observed=null,bool Import=false);
    public sealed class Reconciler
    {
        Choice? desired, observed;
        public bool ShouldWrite(Choice next)=>desired==null||desired!=next;
        public bool Changed(Choice next)=>observed!=null&&observed!=next;
        public void Remember(Choice next,Choice actual){desired=next;observed=actual;}
        public void Imported(Choice actual){desired=actual;observed=actual;}
    }
    public static async Task Send<T>(Stream stream,T value,CancellationToken token)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        if(bytes.Length>4096)throw new IOException("Sync message too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length),token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
    public static async Task<T> Receive<T>(Stream stream,CancellationToken token)
    {
        byte[] size=new byte[4];await stream.ReadExactlyAsync(size,token);int length=BitConverter.ToInt32(size);
        if(length is <1 or >4096)throw new IOException("Invalid sync message.");
        byte[] bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token);
        return JsonSerializer.Deserialize<T>(bytes)??throw new IOException("Missing sync message.");
    }
    public static async Task Listen(string gamePath,int gamePid,Func<Command?> current,Func<Choice,Task> import,Action<string> status,CancellationToken token)
    {
        string name="Fellowship.Sync."+Guid.NewGuid().ToString("N");
        using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};
        foreach(string arg in new[]{"--native-sync",gamePath,gamePid.ToString(),Environment.ProcessId.ToString(),name})start.ArgumentList.Add(arg);
        status("Approve Windows permission for live team and spot sync.");
        using var helper=Process.Start(start)??throw new IOException("Could not start live sync.");
        using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(45));var connected=pipe.WaitForConnectionAsync(connect.Token);
            if(await Task.WhenAny(connected,helper.WaitForExitAsync(connect.Token))!=connected)
                throw new IOException(File.Exists(NativePlacement.LogPath)?File.ReadAllText(NativePlacement.LogPath):"Live sync could not start.");
            await connected;
        }
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint client)||client!=helper.Id)throw new IOException("Unexpected sync client.");
        while(!token.IsCancellationRequested&&current() is {} command)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
            Reply reply;
            try{await Send(pipe,command,timeout.Token);reply=await Receive<Reply>(pipe,timeout.Token);}
            catch(IOException){throw new IOException(File.Exists(NativePlacement.LogPath)?File.ReadAllText(NativePlacement.LogPath):"The game sync connection ended.");}
            // A user edit or host/map transition wins over an older incoming observation.
            if(current()==command)
            {
                status(reply.Status);
                if(reply.Import&&reply.Observed is {} choice)await import(choice);
            }
            await Task.Delay(400,token);
        }
    }
    public static async Task<int> Run(string gamePath,int gamePid,int parentPid,string pipeName)
    {
        try
        {
            if(!pipeName.StartsWith("Fellowship.Sync.",StringComparison.Ordinal)||!Guid.TryParseExact(pipeName["Fellowship.Sync.".Length..],"N",out _))throw new IOException("Invalid sync pipe name.");
            using var parent=Process.GetProcessById(parentPid);using var game=Process.GetProcessById(gamePid);
            if(!string.Equals(NativePlacement.PhysicalPath(game.MainModule!.FileName),NativePlacement.PhysicalPath(Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat")),StringComparison.OrdinalIgnoreCase))throw new IOException("The running BFME2 does not match the configured installation.");
            using var reader=new NativeLobby(gamePid);string host=NativePlacement.Extract();
            using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(15000);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server)||server!=parentPid)return 1;
            string? room=null,context=null;var reconcile=new Reconciler();
            while(!parent.HasExited&&!game.HasExited)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var command=await Receive<Command>(pipe,timeout.Token);
                room??=command.Room;if(room!=command.Room)break;
                if(command.Choice.Team is <0 or >4||command.Choice.Spot is <0 or >8)throw new IOException("Invalid team or spot.");
                NativeLobby.Snapshot? snapshot;
                try{snapshot=reader.Current();}catch(InvalidDataException){snapshot=null;}
                Reply reply;
                if(snapshot==null||!snapshot.Map.Equals(command.Map,StringComparison.OrdinalIgnoreCase)||snapshot.HostAddress!=command.Host)
                {
                    context=null;reconcile=new();reply=new("Waiting for the matching BFME2 LAN setup lobby…");
                }
                else
                {
                    string nextContext=$"{snapshot.Identity}|{snapshot.Map}|{snapshot.HostAddress}";
                    if(context!=nextContext){context=nextContext;reconcile=new();}
                    var actual=new Choice(snapshot.Local.Team+1,snapshot.Local.Spot+1);
                    if(reconcile.ShouldWrite(command.Choice))
                    {
                        if(actual!=command.Choice)
                        {
                            string request=Path.Combine(Settings.Folder,"native","live-"+Guid.NewGuid().ToString("N")+".bin");
                            try
                            {
                                File.WriteAllBytes(request,NativePlacement.RequestBytes(gamePid,snapshot,command.Choice.Team,command.Choice.Spot));
                                var launch=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true};launch.ArgumentList.Add(NativePlacement.PhysicalPath(request));
                                using var bridge=Process.Start(launch)??throw new IOException("Could not start native bridge.");
                                await bridge.WaitForExitAsync(timeout.Token);
                                int code=BitConverter.ToInt32(File.ReadAllBytes(request),32);
                                if(bridge.ExitCode!=0||code!=2)throw new IOException(NativePlacement.Describe(code));
                                await Task.Delay(300,timeout.Token);
                                var confirmed=reader.Current();
                                if(confirmed==null||confirmed.Identity!=snapshot.Identity)throw new IOException("Native lobby changed during sync.");
                                actual=new(confirmed.Local.Team+1,confirmed.Local.Spot+1);
                            }
                            finally{File.Delete(request);}
                        }
                        reconcile.Remember(command.Choice,actual);
                        bool correction=actual!=command.Choice;if(correction)reconcile.Imported(actual);
                        reply=new("Live team & spot sync connected",actual,correction);
                    }
                    else
                    {
                        bool changed=reconcile.Changed(actual);
                        if(changed)reconcile.Imported(actual);else reconcile.Remember(command.Choice,actual);
                        reply=new("Live team & spot sync connected",actual,changed);
                    }
                }
                await Send(pipe,reply,timeout.Token);
            }
            return 0;
        }
        catch(Exception error)
        {
            File.WriteAllText(NativePlacement.LogPath,"Live sync stopped: "+error.Message);return 1;
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}

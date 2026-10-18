using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

// One request at a time; the host may also synchronize the shared setup.
public static class LivePlacement
{
    public sealed record Choice(int Team,int Spot);
    public sealed record Command(string Room,string Map,string Host,Choice Choice,NativeSetup? Setup=null);
    public sealed record Reply(string Status,Choice? Observed=null,bool Import=false,NativeSetup? Setup=null,bool ImportSetup=false,bool Done=false);
    // A countdown or missing snapshot suspends writes. Only a confirmed destroyed
    // or inactive room followed by a stable new lobby can resume writes.
    public sealed class SessionGuard
    {
        uint? identity,candidate; int stable,absentSamples; bool departed;
        public bool Stopped { get; private set; }
        public int Generation { get; private set; }
        public bool Observe(uint? next,bool starting,bool setupClosed=false)
        {
            if(starting||(identity.HasValue&&next!=identity))Stopped=true;
            if(Stopped)
            {
                if(setupClosed)
                {
                    if(++absentSamples>=3)departed=true;
                    candidate=null;stable=0;return false;
                }
                absentSamples=0;
                if(!departed||starting||next==null){candidate=null;stable=0;return false;}
            }
            else if(identity.HasValue)return true;
            if(next==null){candidate=null;stable=0;return false;}
            if(candidate!=next){candidate=next;stable=0;}
            if(++stable<5)return false;
            identity=next;Stopped=false;departed=false;absentSamples=0;Generation++;
            return true;
        }
    }
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
    static string SessionLog(string name)=>Path.Combine(Settings.Folder,"native-sync-"+Guid.ParseExact(name["Fellowship.Sync.".Length..],"N").ToString("N")+".log");
    public static async Task Listen(string gamePath,int gamePid,Func<Command?> current,Func<Choice,string,Task> import,Func<NativeSetup,Task> importSetup,Action<string> status,CancellationToken token,Action? prepared=null)
    {
        string name="Fellowship.Sync."+Guid.NewGuid().ToString("N");
        using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        bool elevated=gamePid==0||LaunchContext.IsElevated(gamePid);
        var start=LaunchContext.Worker(Environment.ProcessPath!,elevated);
        foreach(string arg in new[]{"--native-sync",gamePath,gamePid.ToString(),Environment.ProcessId.ToString(),name})start.ArgumentList.Add(arg);
        status(gamePid==0?"Approve sync permission before BFME2 starts. Waiting for the game afterward…":elevated?"BFME2 is running as administrator. Approve Windows permission for live sync.":"Connecting live game sync…");
        using var helper=Process.Start(start)??throw new IOException("Could not start live sync.");
        using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(45));var connected=pipe.WaitForConnectionAsync(connect.Token);
            if(await Task.WhenAny(connected,helper.WaitForExitAsync(connect.Token))!=connected)
                throw new IOException(File.Exists(SessionLog(name))?File.ReadAllText(SessionLog(name)):"Live sync could not start.");
            await connected;
        }
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint client)||client!=helper.Id)throw new IOException("Unexpected sync client.");
        while(!token.IsCancellationRequested&&current() is {} command)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
            Reply reply;
            try{await Send(pipe,command,timeout.Token);reply=await Receive<Reply>(pipe,timeout.Token);}
            catch(EndOfStreamException){status("BFME2 sync disconnected. No further changes will be sent.");return;}
            catch(IOException){throw new IOException("The game sync connection ended. No further changes will be sent.");}
            if(reply.Done){status(reply.Status);return;}
            prepared?.Invoke();prepared=null;
            // A user edit or host/map transition wins over an older incoming observation.
            if(current()==command)
            {
                status(reply.Status);
                if(reply.ImportSetup&&reply.Setup is {} setup)await importSetup(setup);
                if(reply.Import&&reply.Observed is {} choice)
                {
                    try{await import(choice,command.Map);}
                    catch(InvalidOperationException error){status("Selection conflict: "+error.Message+" Choose a free spot; live sync remains connected.");}
                }
            }
            await Task.Delay(400,token);
        }
    }
    public static async Task<int> Run(string gamePath,int gamePid,int parentPid,string pipeName)
    {
        string? log=null;
        try
        {
            if(!pipeName.StartsWith("Fellowship.Sync.",StringComparison.Ordinal)||!Guid.TryParseExact(pipeName["Fellowship.Sync.".Length..],"N",out _))throw new IOException("Invalid sync pipe name.");
            log=SessionLog(pipeName);
            using var parent=Process.GetProcessById(parentPid);
            HostPreset.ValidateBinary(gamePath);
            // The server restricts its ACL to the user. Authenticate it by PID below;
            // CurrentUserOnly on an elevated client rejects a medium-integrity owner.
            using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(15000);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server)||server!=parentPid)return 1;
            if(gamePid==0)
            {
                // Authorize the worker before the renderer starts. Never launch the
                // game here: it must retain the interactive user's launch context.
                string? waitingRoom=null;
                while(!parent.HasExited)
                {
                    using var waitTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var waiting=await Receive<Command>(pipe,waitTimeout.Token);
                    waitingRoom??=waiting.Room;if(waitingRoom!=waiting.Room)return 0;
                    gamePid=FindConfiguredGame(gamePath);
                    await Send(pipe,new Reply(gamePid==0?"Sync ready. Waiting for BFME2…":"BFME2 detected. Waiting for the LAN lobby…"),waitTimeout.Token);
                    if(gamePid!=0)break;
                }
                if(gamePid==0)return 0;
            }
            using var game=Process.GetProcessById(gamePid);
            if(!string.Equals(NativePlacement.PhysicalPath(game.MainModule!.FileName),NativePlacement.PhysicalPath(Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat")),StringComparison.OrdinalIgnoreCase))throw new IOException("The running BFME2 does not match the configured installation.");
            using var reader=new NativeLobby(gamePid);string host=NativePlacement.Extract();
            int generation=0;string? lastTrace=null;string? room=null,context=null,placementMap=null,placementNotice=null;var reconcile=new Reconciler();var session=new SessionGuard();NativeSetup? desiredSetup=null,observedSetup=null;
            var maps=GameInstallation.Maps(gamePath).Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while(!parent.HasExited&&!game.HasExited)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var command=await Receive<Command>(pipe,timeout.Token);
                room??=command.Room;if(room!=command.Room)break;
                if(command.Choice.Team is <0 or >4||command.Choice.Spot is <0 or >8)throw new IOException("Invalid team or spot.");
                NativeLobby.Snapshot? snapshot=null;
                bool starting=false,closed=false;
                try
                {
                    closed=reader.SetupClosed();starting=!closed&&reader.Starting();
                    snapshot=starting||closed?null:reader.Current();
                }
                catch(IOException){ /* A raced read is not proof that the setup was destroyed. */ }
                bool editable=session.Observe(snapshot?.Identity,starting,closed);
                string trace=$"closed={closed} starting={starting} lobby={snapshot?.Identity} paused={session.Stopped} generation={session.Generation} desiredTeam={command.Choice.Team} desiredSpot={command.Choice.Spot}";
                if(trace!=lastTrace)
                {
                    if(new FileInfo(log).Exists&&new FileInfo(log).Length>262144)File.WriteAllText(log,"");
                    File.AppendAllText(log,$"{DateTime.UtcNow:O} {trace}\n");lastTrace=trace;
                }
                if(session.Stopped)
                {
                    await Send(pipe,new Reply("Live sync paused during the match. Waiting for a new LAN lobby…"),timeout.Token);
                    continue;
                }
                if(editable&&generation!=session.Generation)
                {
                    generation=session.Generation;context=null;reconcile=new();desiredSetup=observedSetup=null;
                }
                try
                {
                Reply reply;
                if(!editable||snapshot==null||!MatchesLobby(command,snapshot))
                {
                    reply=new("Waiting for the matching BFME2 LAN setup lobby…");
                }
                else
                {
                    if(HasDuplicateSpots(snapshot))
                    {
                        await Send(pipe,new Reply("BFME2 has conflicting starting spots. Resolve them in the native lobby; sync is paused."),timeout.Token);
                        continue;
                    }
                    string nextContext=$"{snapshot.Identity}|{snapshot.HostAddress}|{snapshot.Local.Row}";
                    if(context!=nextContext){context=nextContext;reconcile=new();desiredSetup=observedSetup=null;placementNotice=null;}
                    if(placementMap!=snapshot.Map){placementMap=snapshot.Map;reconcile=new();placementNotice=null;}
                    if(command.Setup is {} requestedSetup)
                    {
                        if(snapshot.Local.Row!=0)throw new IOException("Fellowship's host must also host the BFME2 lobby to sync map and rules.");
                        requestedSetup.Rules.Validate();if(!maps.Contains(requestedSetup.Map))throw new IOException("Selected map is not installed.");
                        if(desiredSetup==null||desiredSetup!=requestedSetup)
                        {
                            if(snapshot.Setup!=requestedSetup)
                            {
                                await Execute(host,gamePid,snapshot,NativePlacement.SetupRequestBytes(gamePid,snapshot,requestedSetup),3,timeout.Token);
                                var updated=ReadAfterWrite(reader);
                                if(updated==null||updated.Identity!=snapshot.Identity)throw new NativeTransitionException();
                                if(updated.Setup!=requestedSetup)throw new IOException("BFME2 did not retain the map and rules.");
                                if(updated.Map!=snapshot.Map)reconcile=new();snapshot=updated;
                            }
                            desiredSetup=requestedSetup;observedSetup=snapshot.Setup;
                        }
                        else if(snapshot.Setup!=observedSetup)
                        {
                            var actualSetup=snapshot.Setup??throw new IOException("Native setup unavailable.");
                            if(!maps.Contains(actualSetup.Map))throw new IOException("Native map is not in Fellowship's installed-map catalog.");
                            if(observedSetup?.Map!=actualSetup.Map)reconcile=new();
                            desiredSetup=observedSetup=actualSetup;
                            await Send(pipe,new Reply("Live map, rules, team & spot sync connected",Setup:actualSetup,ImportSetup:true),timeout.Token);
                            continue;
                        }
                    }
                    var actual=new Choice(snapshot.Local.Team+1,snapshot.Local.Spot+1);
                    if(reconcile.ShouldWrite(command.Choice))
                    {
                        placementNotice=null;
                        var desired=ResolveOccupiedSpot(snapshot,command.Choice);
                        if(desired!=command.Choice)placementNotice="That spot is occupied in BFME2. Your native spot was kept; choose another spot.";
                        if(actual!=desired)
                        {
                                try{await Execute(host,gamePid,snapshot,NativePlacement.RequestBytes(gamePid,snapshot,desired.Team,desired.Spot),2,timeout.Token);}
                                catch(PlacementRejectedException error){placementNotice=error.Message;}
                                var confirmed=ReadAfterWrite(reader);
                                if(confirmed==null||confirmed.Identity!=snapshot.Identity)throw new NativeTransitionException();
                                if(!confirmed.Map.Equals(snapshot.Map,StringComparison.OrdinalIgnoreCase)||confirmed.Local.Row!=snapshot.Local.Row||HasDuplicateSpots(confirmed))throw new NativeRefreshException();
                                actual=new(confirmed.Local.Team+1,confirmed.Local.Spot+1);
                        }
                        reconcile.Remember(command.Choice,actual);
                        bool correction=actual!=command.Choice;
                        reply=new(placementNotice??(command.Setup==null?"Live team & spot sync connected":"Live map, rules, team & spot sync connected"),actual,correction);
                    }
                    else
                    {
                        bool changed=reconcile.Changed(actual);
                        if(changed)reconcile.Imported(actual);else reconcile.Remember(command.Choice,actual);
                        reply=new(placementNotice??(command.Setup==null?"Live team & spot sync connected":"Live map, rules, team & spot sync connected"),actual,changed);
                    }
                }
                await Send(pipe,reply,timeout.Token);
                }
                catch(NativeRefreshException)
                {
                    await Send(pipe,new Reply("Native map or player changed. Waiting for a matching lobby…"),timeout.Token);
                }
                catch(NativeTransitionException)
                {
                    session.Observe(null,true);
                    await Send(pipe,new Reply("Live sync paused during the match. Waiting for a new LAN lobby…"),timeout.Token);
                }
            }
            return 0;
        }
        catch(EndOfStreamException){return 0;}
        catch(Exception error)
        {
            if(log!=null)File.WriteAllText(log,error.Message);return 1;
        }
    }
    static int FindConfiguredGame(string gamePath)
    {
        string expected=NativePlacement.PhysicalPath(Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat"));
        foreach(var candidate in Process.GetProcessesByName("game.dat").Concat(Process.GetProcessesByName("game")))
        using(candidate)
        {
            try
            {
                if(!candidate.HasExited&&candidate.MainModule is {} module&&
                   string.Equals(NativePlacement.PhysicalPath(module.FileName),expected,StringComparison.OrdinalIgnoreCase))return candidate.Id;
            }
            catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException){ }
        }
        return 0;
    }
    public static bool MatchesLobby(Command command,NativeLobby.Snapshot snapshot)=>
        snapshot.HostAddress==command.Host&&(command.Setup!=null&&snapshot.Local.Row==0||
        command.Setup==null&&snapshot.Map.Equals(command.Map,StringComparison.OrdinalIgnoreCase));
    public static bool HasDuplicateSpots(NativeLobby.Snapshot snapshot)=>snapshot.Slots
        .Where(s=>s.State>1&&s.Spot>=0).GroupBy(s=>s.Spot).Any(g=>g.Count()>1);
    public static Choice ResolveOccupiedSpot(NativeLobby.Snapshot snapshot,Choice desired)=>
        desired.Spot>0&&snapshot.Slots.Any(s=>!s.Local&&s.State>1&&s.Spot==desired.Spot-1)
            ?desired with{Spot=snapshot.Local.Spot+1}:desired;
    sealed class PlacementRejectedException(string message) : IOException(message) { }
    sealed class NativeRefreshException : IOException { }
    sealed class NativeTransitionException : IOException { }
    static NativeLobby.Snapshot? ReadAfterWrite(NativeLobby reader)
    {
        try{return reader.Current();}catch(IOException){throw new NativeTransitionException();}
    }
    static async Task Execute(string host,int pid,NativeLobby.Snapshot snapshot,byte[] bytes,int expected,CancellationToken token)
    {
        string request=Path.Combine(Settings.Folder,"native","live-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            File.WriteAllBytes(request,bytes);var launch=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true};launch.ArgumentList.Add(NativePlacement.PhysicalPath(request));
            using var bridge=Process.Start(launch)??throw new IOException("Could not start native bridge.");
            await bridge.WaitForExitAsync(token);int code=BitConverter.ToInt32(File.ReadAllBytes(request),32);
            if(code is -3 or -4 or -6)throw new NativeTransitionException();
            if(code is -7 or -8)throw new NativeRefreshException();
            if(code is -9 or -10)throw new PlacementRejectedException(NativePlacement.Describe(code));
            if(bridge.ExitCode!=0||code!=expected)throw new IOException(NativePlacement.Describe(code));
            await Task.Delay(300,token);
        }
        finally{File.Delete(request);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}

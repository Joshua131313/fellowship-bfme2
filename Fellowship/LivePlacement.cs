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
    public sealed record Choice(int Team,int Spot,int Color=0);
    public sealed record Command(string Room,string Map,string Host,Choice Choice,NativeSetup? Setup=null,string? Members=null,bool Abort=false,bool Rhun3v3=false,SidePlan? Sides=null,bool AllowExternalHost=false);
    public sealed record Reply(string Status,Choice? Observed=null,bool Import=false,NativeSetup? Setup=null,bool ImportSetup=false,bool Done=false,bool Aborted=false,LobbyReport? Lobby=null);
    public sealed class Control
    {
        bool connected;
        TaskCompletionSource<bool>? pending;
        public Task<bool> Abort()
        {
            if(!connected)return Task.FromResult(false);
            pending??=new(TaskCreationOptions.RunContinuationsAsynchronously);return pending.Task;
        }
        internal TaskCompletionSource<bool>? Pending=>pending;
        internal IDisposable Connect(){connected=true;return new Connection(this);}
        internal void Complete(TaskCompletionSource<bool> request,Reply reply)
        {
            if(pending==request)pending=null;
            if(reply.Aborted)request.TrySetResult(true);
            else request.TrySetException(new IOException("The live-sync helper could not stop BFME2: "+reply.Status));
        }
        sealed class Connection(Control owner):IDisposable
        {
            public void Dispose(){owner.connected=false;owner.pending?.TrySetException(new IOException("Live sync disconnected before Abort was confirmed."));owner.pending=null;}
        }
    }
    // A countdown or missing snapshot suspends writes. Only a confirmed destroyed
    // or inactive room followed by a stable new lobby can resume writes.
    public sealed class SessionGuard
    {
        uint? identity,candidate; int stable; bool departed;
        public bool Stopped { get; private set; }
        public int Generation { get; private set; }
        public bool Observe(uint? next,bool starting,bool setupClosed=false)
        {
            if(starting||(identity.HasValue&&next!=identity))Stopped=true;
            if(Stopped)
            {
                if(setupClosed)
                {
                    // SetupClosed is positive native lifecycle evidence, not a failed read.
                    // A quick Network -> Create cycle may be visible for only one poll.
                    departed=true;
                    candidate=null;stable=0;return false;
                }
                if(!departed||starting||next==null){candidate=null;stable=0;return false;}
            }
            else if(identity.HasValue)return true;
            if(next==null){candidate=null;stable=0;return false;}
            if(candidate!=next){candidate=next;stable=0;}
            if(++stable<5)return false;
            identity=next;Stopped=false;departed=false;Generation++;
            return true;
        }
    }
    public sealed class Reconciler
    {
        Choice? desired, observed;
        public bool ShouldWrite(Choice next)=>desired==null||desired!=next;
        public bool Changed(Choice next)=>observed!=null&&observed!=next;
        public void Remember(Choice next,Choice actual,bool retry=false){desired=retry?null:next;observed=actual;}
        public void Imported(Choice actual){desired=actual;observed=actual;}
    }
    // A mixed lobby cannot participate in the atomic Rhûn clear/claim protocol.
    // On returning to managed play, require a new host session rather than
    // applying a plan captured before players made their own native selections.
    public sealed class CoordinationGuard
    {
        bool? coordinated;
        string? previousSession;
        public bool Observe(bool next,SidePlan? plan)
        {
            bool changed=coordinated.HasValue&&coordinated!=next;
            if(!next)
                previousSession=plan?.Session??previousSession;
            coordinated=next;
            return changed;
        }
        public bool IsFenced(SidePlan? plan,NativeLobby.Snapshot snapshot)
        {
            if(previousSession==null||plan!=null&&plan.Session!=previousSession)return false;
            if(plan==null||plan.Started)return true;
            // A host may already have the new member list while another helper
            // briefly sees a mixed roster. Do not wait forever in that case:
            // cancelled plans only import, and an already satisfied move writes
            // nothing. Otherwise keep personal sync until a fresh plan arrives.
            if(plan.Cancelled)return false;
            return plan.Host!=snapshot.HostAddress||!plan.Moves.TryGetValue(snapshot.Local.Ip,out var move)||
                snapshot.Local.Team+1!=move.Team||snapshot.Local.Spot+1!=move.Spot;
        }
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
    public static async Task Listen(string gamePath,int gamePid,Func<Command?> current,Func<Choice,string,Task> import,Func<NativeSetup,Task> importSetup,Action<string> status,CancellationToken token,Action? prepared=null,Control? control=null,Func<LobbyReport,Task>? importLobby=null)
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
        using var controlConnection=control?.Connect();
        DateTimeOffset? missingSince=null;
        while(!token.IsCancellationRequested)
        {
            var command=current();
            if(command==null)
            {
                missingSince??=DateTimeOffset.UtcNow;
                if(DateTimeOffset.UtcNow-missingSince>=TimeSpan.FromSeconds(8))return;
                status("Waiting for the room connection…");
                await Task.Delay(200,token);continue;
            }
            missingSince=null;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var abortRequest=control?.Pending;
            var outgoing=abortRequest==null?command:command with{Abort=true};
            Reply reply;
            try{await Send(pipe,outgoing,timeout.Token);reply=await Receive<Reply>(pipe,timeout.Token);}
            catch(EndOfStreamException){if(helper.HasExited&&GameLauncher.IsRunning())throw new IOException("The sync helper disconnected while BFME2 is still running.");status("BFME2 sync disconnected. Waiting for the next game.");return;}
            catch(IOException){if(!GameLauncher.IsRunning()){status("BFME2 closed. Waiting for the next game.");return;}throw new IOException("The game sync connection ended. No further changes will be sent.");}
            if(abortRequest!=null)control!.Complete(abortRequest,reply);
            if(reply.Done){status(reply.Status);return;}
            prepared?.Invoke();prepared=null;
            // A user edit or host/map transition wins over an older incoming observation.
            if(current()==command)
            {
                status(reply.Status);
                if(reply.Lobby is {} lobby&&importLobby!=null)await importLobby(lobby);
                if(reply.ImportSetup&&reply.Setup is {} setup)
                {
                    try{await importSetup(setup);}
                    catch(InvalidOperationException error){status("Map/rules could not sync: "+error.Message);}
                }
                if(reply.Import&&reply.Observed is {} choice)
                {
                    try{await import(choice,command.Map);}
                    catch(InvalidOperationException error){status("Selection conflict: "+error.Message+" Choose a free spot or color; live sync remains connected.");}
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
            var rotation=new LobbyRotation();var personalSides=new PersonalRhunSides();
            var rhunSpots=GameMapData.Spots(gamePath,GameMap.Rhun.Path,"1.00");
            while(!parent.HasExited)
            {
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
                    gamePid=FindConfiguredGame(gamePath,parent.SessionId);
                    if(waiting.Abort)
                    {
                        GameLauncher.Abort(gamePath,parent.SessionId);
                        gamePid=0;
                        await Send(pipe,new Reply("BFME2 stopped. Sync permission remains ready.",Aborted:true),waitTimeout.Token);continue;
                    }
                    await Send(pipe,new Reply(gamePid==0?"Sync ready. Waiting for BFME2…":"BFME2 detected. Waiting for the LAN lobby…"),waitTimeout.Token);
                    if(gamePid!=0)break;
                }
                if(gamePid==0)return 0;
            }
            using var game=Process.GetProcessById(gamePid);
            if(game.SessionId!=parent.SessionId)throw new IOException("BFME2 belongs to another Windows session.");
            if(!string.Equals(NativePlacement.PhysicalPath(game.MainModule!.FileName),NativePlacement.PhysicalPath(Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat")),StringComparison.OrdinalIgnoreCase))throw new IOException("The running BFME2 does not match the configured installation.");
            using var reader=new NativeLobby(gamePid);string host=NativePlacement.Extract();
            (Choice Requested,Choice Confirmed)? rotationImport=null;
            string nativeSession="",lastReport="",appliedSidePlan="";long lastReportTick=0;NativeLobby.Snapshot? lastLobby=null,lastSideLobby=null;bool reportedStart=false,rememberedStart=false;
            int generation=0;bool? lastPlanMode=null;string? lastTrace=null;string? room=null,context=null,placementMap=null,placementNotice=null;var reconcile=new Reconciler();var session=new SessionGuard();var coordination=new CoordinationGuard();NativeSetup? desiredSetup=null,observedSetup=null;
            var maps=GameInstallation.Maps(gamePath).Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while(!parent.HasExited&&!game.HasExited)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var command=await Receive<Command>(pipe,timeout.Token);
                room??=command.Room;if(room!=command.Room)return 0;
                if(command.Abort)
                {
                    try{if(!game.HasExited)game.Kill();}catch(InvalidOperationException){}
                    await game.WaitForExitAsync(timeout.Token);
                    await Send(pipe,new Reply("BFME2 stopped. Sync permission remains ready.",Aborted:true),timeout.Token);break;
                }
                if(command.Choice.Color is <0 or >10||command.Choice.Team is <0 or >4||command.Choice.Spot is <0 or >8)throw new IOException("Invalid team or spot.");
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
                    rotation.Suspend();
                    if(starting&&!rememberedStart)
                    {
                        personalSides.RememberMatch(command,lastSideLobby);lastSideLobby=null;rememberedStart=true;
                    }
                    if(starting)reportedStart=true;
                    if(reportedStart&&lastLobby?.Local.Row==0&&UsesCoordinatedRhun(command,lastLobby)&&command.Sides?.Started!=true)
                    {
                        reportedStart=true;await Send(pipe,new Reply("Live sync paused during the match. Waiting for a new LAN lobby…",Lobby:new(nativeSession,lastLobby,true)),timeout.Token);continue;
                    }
                    await Send(pipe,new Reply("Live sync paused during the match. Waiting for a new LAN lobby…"),timeout.Token);
                    continue;
                }
                if(editable&&generation!=session.Generation)
                {
                    generation=session.Generation;nativeSession=Guid.NewGuid().ToString("N");lastReport="";lastLobby=lastSideLobby=null;reportedStart=rememberedStart=false;rotation.NewSession();personalSides.NewLobby();rotationImport=null;context=null;reconcile=new();coordination=new();lastPlanMode=null;desiredSetup=observedSetup=null;
                }
                try
                {
                Reply reply;
                if(!editable||snapshot==null||!MatchesLobby(command,snapshot))
                {
                    lastSideLobby=null;
                    rotation.Suspend();
                    reply=new("Waiting for the matching BFME2 LAN setup lobby…");
                }
                else
                {
                    bool coordinatedRhun=UsesCoordinatedRhun(command,snapshot);
                    if(coordination.Observe(coordinatedRhun,command.Sides))
                    {
                        // The native host must publish a new plan session when a
                        // mixed roster becomes fully managed again. A revision
                        // alone may never change for an otherwise ready lineup.
                        nativeSession=Guid.NewGuid().ToString("N");lastReport="";lastLobby=null;reportedStart=false;
                        appliedSidePlan="";rotation=new();personalSides.NewLobby();rotationImport=null;
                    }
                    if(!coordinatedRhun){lastLobby=null;lastReport="";reportedStart=false;}
                    bool useSidePlan=coordinatedRhun&&!coordination.IsFenced(command.Sides,snapshot);
                    if(lastPlanMode.HasValue&&lastPlanMode!=useSidePlan){rotation=new();personalSides.NewLobby();rotationImport=null;}
                    lastPlanMode=useSidePlan;
                    if(HasDuplicateSpots(snapshot))
                    {
                        lastSideLobby=null;
                        await Send(pipe,new Reply("BFME2 has conflicting starting spots. Resolve them in the native lobby; sync is paused."),timeout.Token);
                        continue;
                    }
                    string nextContext=ContextKey(command,snapshot)+"|"+useSidePlan;
                    if(context!=nextContext){context=nextContext;rotationImport=null;reconcile=new();desiredSetup=observedSetup=null;placementNotice=null;}
                    if(placementMap!=snapshot.Map){placementMap=snapshot.Map;rotationImport=null;reconcile=new();placementNotice=null;}
                    if(snapshot.Local.Row==0&&command.Setup is {} requestedSetup)
                    {
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
                            lastSideLobby=null;
                            await Send(pipe,new Reply("Live map, rules, team, spot & color sync connected",Setup:actualSetup,ImportSetup:true),timeout.Token);
                            continue;
                        }
                    }
                    var actual=new Choice(snapshot.Local.Team+1,snapshot.Local.Spot+1,snapshot.Local.Color+1);
                    // Capture the latest native lineup, even while writes wait
                    // for stability, so a last-second team edit is not missed.
                    lastSideLobby=snapshot;
                    if(!rotation.Observe(snapshot,TimeSpan.FromMilliseconds(Environment.TickCount64)))
                    {
                        await Send(pipe,new Reply("Waiting for the lobby selections to settle…"),timeout.Token);continue;
                    }
                    if(coordinatedRhun)
                    {
                        lastLobby=snapshot;
                        if(snapshot.Local.Row==0)
                        {
                            var fingerprint=JsonSerializer.Serialize(snapshot);
                            if(lastReport!=fingerprint||Environment.TickCount64-lastReportTick>2000){lastReport=fingerprint;lastReportTick=Environment.TickCount64;await Send(pipe,new Reply("Coordinating Rhûn team sides…",Lobby:new(nativeSession,snapshot)),timeout.Token);continue;}
                        }
                    }
                    if(useSidePlan)
                    {
                        var plan=command.Sides;
                        if(plan is {Started:true})
                        {await Send(pipe,new Reply("Waiting for the Rhûn host's team selection…"),timeout.Token);continue;}
                        if(plan is {Cancelled:true}&&plan.Key!=appliedSidePlan)
                        {
                            appliedSidePlan=plan.Key;reconcile.Remember(command.Choice,actual);
                            await Send(pipe,new Reply("Native selections kept",actual,actual!=command.Choice),timeout.Token);continue;
                        }
                        if(plan is {Cancelled:false,Started:false}&&plan.Key!=appliedSidePlan&&plan.Host==snapshot.HostAddress&&plan.Moves.TryGetValue(snapshot.Local.Ip,out var move)&&!snapshot.Local.Observer)
                        {
                            var target=new Choice(move.Team,move.Spot,actual.Color);
                            if(actual.Team!=target.Team||actual.Spot!=target.Spot)
                            {
                                if(!plan.Ready&&actual.Team==move.FromTeam&&actual.Spot==move.FromSpot)
                                {
                                    try{await Execute(host,gamePid,snapshot,NativePlacement.SideClearRequestBytes(gamePid,snapshot,move),2,timeout.Token);}catch(PlacementRejectedException){}
                                }
                                else if(plan.Ready&&actual.Team==move.Team&&actual.Spot==0)
                                {
                                    try{await Execute(host,gamePid,snapshot,NativePlacement.RotationRequestBytes(gamePid,snapshot,move.Spot),2,timeout.Token);}catch(PlacementRejectedException){}
                                }
                                await Send(pipe,new Reply("Coordinating Rhûn team sides…"),timeout.Token);continue;
                            }
                            appliedSidePlan=plan.Key;reconcile.Remember(command.Choice,actual);
                        }
                        else if((plan==null||plan.Host!=snapshot.HostAddress)&&snapshot.Local.Team>=0)
                        {await Send(pipe,new Reply("Waiting for the Rhûn host's team selection…"),timeout.Token);continue;}
                        if(plan is {Cancelled:false,Started:false}&&plan.Host==snapshot.HostAddress&&
                           snapshot.Local.Active&&!plan.Moves.ContainsKey(snapshot.Local.Ip))
                        {await Send(pipe,new Reply("Coordinating Rhûn team sides…"),timeout.Token);continue;}
                    }
                    if(snapshot.Local.Observer)
                    {
                        personalSides.NewLobby();
                        if(!rotation.WasActive(snapshot.Local))
                        {
                            await Send(pipe,new Reply("Waiting for the lobby selections to settle…"),timeout.Token);continue;
                        }
                        // Observer is a faction selection, not an empty connection slot.
                        // Import its cleared spot; never write a saved active placement back.
                        reconcile.Remember(command.Choice,actual);
                        await Send(pipe,new Reply("Observing the game. Your active spot is available for replacements.",actual,actual!=command.Choice),timeout.Token);continue;
                    }
                    if(rotationImport is {} pendingImport)
                    {
                        if(command.Choice==pendingImport.Requested&&actual==pendingImport.Confirmed)
                        {
                            // The departing player's cleared spot may reach Fellowship
                            // after our native assignment. Retry the import until accepted.
                            await Send(pipe,new Reply("Team starting spot assigned automatically",actual,true),timeout.Token);continue;
                        }
                        rotationImport=null;
                    }
                    var replacement=useSidePlan?rotation.Replacement(snapshot):null;
                    if(replacement!=null&&(!reconcile.ShouldWrite(command.Choice)||command.Choice.Spot==0))
                    {
                        try
                        {
                            await Execute(host,gamePid,snapshot,NativePlacement.RotationRequestBytes(gamePid,snapshot,replacement.Spot),2,timeout.Token);
                            var confirmed=ReadAfterWrite(reader);
                            if(confirmed==null||confirmed.Identity!=snapshot.Identity)throw new NativeTransitionException();
                            if(confirmed.Map!=snapshot.Map||confirmed.Local.Observer||confirmed.Local.Row!=snapshot.Local.Row)throw new NativeRefreshException();
                            actual=new(confirmed.Local.Team+1,confirmed.Local.Spot+1,confirmed.Local.Color+1);
                            reconcile.Remember(command.Choice,actual);
                            if(actual!=command.Choice)rotationImport=(command.Choice,actual);
                            await Send(pipe,new Reply("Team starting spot assigned automatically",actual,actual!=command.Choice),timeout.Token);continue;
                        }
                        catch(PlacementRejectedException)
                        {
                            // A concurrent native edit or another allocation wins. Re-read.
                            rotation.Suspend();await Send(pipe,new Reply("Waiting for the lobby selections to settle…"),timeout.Token);continue;
                        }
                    }

                    var requested=command.Choice;
                    var personal=personalSides.Resolve(command,snapshot,rhunSpots);
                    if(personal.State!=PersonalRhunSides.State.Disabled)requested=personal.Choice;
                    string? sideNotice=personal.State switch
                    {
                        PersonalRhunSides.State.WaitingForTeams=>"Waiting for the Rhûn host's team selection…",
                        PersonalRhunSides.State.WaitingForSpot=>"Choose a starting spot in Fellowship or BFME2.",
                        PersonalRhunSides.State.Blocked=>"Your mirrored spot is occupied. Waiting for it to become free.",
                        PersonalRhunSides.State.ClearOwn=>"Swapping sides. Waiting for the matching spot to become free.",
                        _=>personal.FromHistory?"Using teams from the last game while lobby teams are unset.":null
                    };
                    if(reconcile.ShouldWrite(requested))
                    {
                        placementNotice=null;
                        var desired=ResolveOccupiedSpot(snapshot,requested);
                        desired=ResolveOccupiedColor(snapshot,desired);
                        bool rejected=false,guardedSide=false;
                        if(desired!=requested)placementNotice="That spot or color is occupied in BFME2. Your native selection was kept; choose another.";
                        if(actual!=desired)
                        {
                                try
                                {
                                    guardedSide=personal.State!=PersonalRhunSides.State.Disabled&&(desired.Team!=actual.Team||desired.Spot!=actual.Spot);
                                    var request=guardedSide?NativePlacement.GuardedSideRequestBytes(gamePid,snapshot,desired.Team,desired.Spot):NativePlacement.RequestBytes(gamePid,snapshot,desired.Team,desired.Spot,color:desired.Color);
                                    await Execute(host,gamePid,snapshot,request,2,timeout.Token);
                                    if(personal.State!=PersonalRhunSides.State.Disabled)personalSides.RecordApplied(guardedSide?desired with{Color=snapshot.Local.Color+1}:desired);
                                }
                                catch(PlacementRejectedException error){placementNotice=error.Message;rejected=true;}
                                var confirmed=ReadAfterWrite(reader);
                                if(confirmed==null||confirmed.Identity!=snapshot.Identity)throw new NativeTransitionException();
                                if(!confirmed.Map.Equals(snapshot.Map,StringComparison.OrdinalIgnoreCase)||confirmed.Local.Row!=snapshot.Local.Row||HasDuplicateSpots(confirmed))throw new NativeRefreshException();
                                actual=new(confirmed.Local.Team+1,confirmed.Local.Spot+1,confirmed.Local.Color+1);
                        }
                        // A raced native edit or allocation may reject the move.
                        // Keep a personal side correction pending until confirmed;
                        // Resolve will first honor any newer manual selection.
                        // Guarded moves preserve the live native color. Apply a
                        // pending explicit color on the next poll, after the side
                        // is confirmed, without importing that intermediate color.
                        bool pendingColor=guardedSide&&desired.Color==requested.Color&&actual.Color!=requested.Color;
                        bool retrySide=personal.State!=PersonalRhunSides.State.Disabled&&(rejected||pendingColor||actual.Team!=requested.Team||actual.Spot!=requested.Spot);
                        reconcile.Remember(requested,actual,retrySide);
                        bool correction=!((rejected||pendingColor)&&personal.State!=PersonalRhunSides.State.Disabled)&&actual!=command.Choice;
                        reply=new(sideNotice??placementNotice??(snapshot.Local.Row!=0?"Live team, spot & color sync connected":"Live map, rules, team, spot & color sync connected"),actual,correction);
                    }
                    else
                    {
                        bool changed=reconcile.Changed(actual);
                        if(changed)reconcile.Imported(actual);else reconcile.Remember(requested,actual);
                        bool pendingSideImport=(personal.State is PersonalRhunSides.State.Ready or PersonalRhunSides.State.ClearOwn)&&actual!=command.Choice;
                        reply=new(sideNotice??placementNotice??(snapshot.Local.Row!=0?"Live team, spot & color sync connected":"Live map, rules, team, spot & color sync connected"),actual,changed||pendingSideImport);
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
            gamePid=0;
            }
            return 0;
        }
        catch(EndOfStreamException){return 0;}
        catch(Exception error)
        {
            if(log!=null)File.WriteAllText(log,error.Message);return 1;
        }
    }
    static int FindConfiguredGame(string gamePath,int session)
    {
        string expected=NativePlacement.PhysicalPath(Path.Combine(Path.GetDirectoryName(gamePath)!,"game.dat"));
        foreach(var candidate in Process.GetProcessesByName("game.dat").Concat(Process.GetProcessesByName("game")))
        using(candidate)
        {
            try
            {
                if(!candidate.HasExited&&candidate.SessionId==session&&candidate.MainModule is {} module&&
                   string.Equals(NativePlacement.PhysicalPath(module.FileName),expected,StringComparison.OrdinalIgnoreCase))return candidate.Id;
            }
            catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException){ }
        }
        return 0;
    }
    public static string ContextKey(Command command,NativeLobby.Snapshot snapshot)=>
        $"{command.Room}|{command.Host}|{command.Setup!=null}|{snapshot.Identity}|{snapshot.HostAddress}|{snapshot.Local.Row}|{command.AllowExternalHost}|{UsesCoordinatedRhun(command,snapshot)}";
    public static bool MatchesLobby(Command command,NativeLobby.Snapshot snapshot)
    {
        if(command.AllowExternalHost)
        {
            // NativeLobby identifies this row using the process's own network
            // address and port; the bridge verifies that identity again. Guests
            // can change their own selections without owning the shared setup.
            if(snapshot.Slots.Count(s=>s.Local)!=1||snapshot.Local.State!=6)return false;
            return command.Setup!=null&&snapshot.Local.Row==0||snapshot.Map.Equals(command.Map,StringComparison.OrdinalIgnoreCase);
        }
        return (command.Members==null?snapshot.HostAddress==command.Host:command.Members.Split('|').Contains(snapshot.HostAddress,StringComparer.Ordinal))&&
            (command.Setup!=null&&snapshot.Local.Row==0||(command.Members!=null||command.Setup==null)&&snapshot.Map.Equals(command.Map,StringComparison.OrdinalIgnoreCase));
    }
    public static bool UsesCoordinatedRhun(Command command,NativeLobby.Snapshot snapshot)
    {
        if(!command.Rhun3v3)return false;
        if(!command.AllowExternalHost)return true;
        var members=(command.Members??command.Host).Split('|',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var humans=snapshot.Slots.Where(s=>s.State==6).ToArray();
        return members.Contains(snapshot.HostAddress)&&humans.Any(s=>s.Row==0&&s.Ip==snapshot.HostAddress)&&
            humans.Select(s=>s.Ip).Distinct(StringComparer.Ordinal).Count()==humans.Length&&humans.All(s=>members.Contains(s.Ip))&&
            !snapshot.Slots.Any(s=>s.State!=6&&s.Active);
    }
    public static bool HasDuplicateSpots(NativeLobby.Snapshot snapshot)=>snapshot.Slots
        .Where(s=>s.Active&&s.Spot>=0).GroupBy(s=>s.Spot).Any(g=>g.Count()>1);
    public static Choice ResolveOccupiedColor(NativeLobby.Snapshot snapshot,Choice desired)=>
        desired.Color>0&&snapshot.Slots.Any(s=>!s.Local&&s.State>1&&s.Color==desired.Color-1)
            ?desired with{Color=snapshot.Local.Color+1}:desired;
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
            if(code is -9 or -10 or -17 or -19)throw new PlacementRejectedException(NativePlacement.Describe(code));
            if(bridge.ExitCode!=0||code!=expected)throw new IOException(NativePlacement.Describe(code));
            await Task.Delay(300,token);
        }
        finally{File.Delete(request);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}

using Fellowship;
using System.IO;
using System.Reflection;
using System.Text.Json;

static class ReconnectionTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed record Choices(int Team,int Spot,int Color,bool Ready,bool GameRunning);
    static Settings SettingsFor(string name){var settings=Settings.InMemory();settings.Name=name;return settings;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Choices Save(Player player)=>new(player.Team,player.Spot,player.Color,player.Ready,player.GameRunning);
    static Player? Player(PeerService peer,string id)=>peer.Current?.Players.FirstOrDefault(p=>p.Id==id);
    static string History(Room room,string id)=>JsonSerializer.Serialize(new{room.SidePlan,room.LastRhunHost,room.LastRhunLineup,Player=room.Players.Where(p=>p.Id==id).Select(p=>new{p.LastMapSpots,p.PreferredMapSpots})});
    static object Gate(PeerService peer)=>typeof(PeerService).GetField("gate",Hidden)!.GetValue(peer)!;
    static bool HasDeadline(PeerService peer,string id){lock(Gate(peer))return ((Dictionary<string,DateTimeOffset>)typeof(PeerService).GetField("reconnectDeadlines",Hidden)!.GetValue(peer)!).ContainsKey(id);}
    static bool HasTicket(PeerService peer,string id){lock(Gate(peer))return ((Dictionary<string,string>)typeof(PeerService).GetField("resumeTickets",Hidden)!.GetValue(peer)!).ContainsKey(id);}
    static object? Upstream(PeerService peer)=>typeof(PeerService).GetField("upstream",Hidden)!.GetValue(peer);
    static void BlockRecovery(PeerService peer,bool blocked)=>typeof(PeerService).GetField("reconnectAllowed",Hidden)!.SetValue(peer,blocked?(Func<string,bool>)(_=>false):null);
    static void DropTransport(PeerService peer)
    {
        var connection=Upstream(peer) as IDisposable??throw new Exception("Reconnect fixture has no upstream to drop");
        connection.Dispose();
    }
    static List<MapSpot> Spots()=>Enumerable.Range(0,6).Select(i=>new MapSpot(i+1,i<3?.2:.8,.15+.35*(i%3))).ToList();
    static async Task Until(Func<bool> condition,string description)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(8);
        do{if(condition()){Console.WriteLine("PASS "+description);return;}await Task.Delay(25);}while(DateTimeOffset.UtcNow<deadline);
        throw new Exception("FAIL "+description);
    }
    static async Task NoAdvertisedHost(PeerService peer,string source)
    {
        Check(!peer.IsHost,"A disconnected guest became a host");
        Check(await PeerService.Query(peer.Address,source)==null,"A disconnected guest advertised its cached room as an owner");
    }
    public static async Task Run()
    {
        await TemporaryGuestPartition();
        await ExpirationAndGracefulLeave();
        await HalfOpenReplacement();
        await BlockedHostCannotResume();
        await ManualRejoinReplacesOwnReservation();
        await LeaveAndDisposeCancel();
        await ReplacedRoomIsNotResumed();
        await LostHostDoesNotElect();
        await ConfirmedHandoffsRemain();
        Console.WriteLine("PASS reconnection-only recovery, reserved identities/selections, no partition election, cancelled recovery and confirmed explicit handoff");
    }
    static async Task TemporaryGuestPartition()
    {
        using var host=new PeerService(SettingsFor("Original owner"));using var guest=new PeerService(SettingsFor("Temporarily offline"));using var bystander=new PeerService(SettingsFor("Still connected"));
        host.Start("127.0.0.181");guest.Start("127.0.0.182");bystander.Start("127.0.0.183");
        host.Host(new(){Title="Partition fixture",Capacity=4,Mode="Custom"},Spots());
        await guest.Join(host.Address);await bystander.Join(host.Address);
        await guest.SelectPlacement(spot:3,team:2,color:4);await guest.SetReady(true);await guest.SetHostGameRunning(true);
        await bystander.SelectPlacement(spot:5,team:1,color:5);await bystander.SetReady(true);
        string id=guest.LocalId,bystanderId=bystander.LocalId,roomId=host.Current!.Id;
        await Until(()=>Player(host,id) is{Ready:true,GameRunning:true,Team:2,Spot:3,Color:4}&&Player(host,bystanderId)?.Ready==true,"partition fixture selections replicated");
        string planSession=Guid.NewGuid().ToString("N");
        lock(Gate(host))
        {
            host.Current!.SidePlan=new(){Session=planSession,Revision=3,Host=host.Address,Ready=true,Moves=new(){[guest.Address]=new(2,3,2,3)},Confirmed=[guest.Address]};
            host.Current.LastRhunHost=guest.Address;host.Current.LastRhunLineup[guest.Address]=new(2,3,4);
            Player(host,id)!.LastMapSpots[@"maps\previous.map"]=2;Player(host,id)!.PreferredMapSpots[@"maps\preferred.map"]=4;
        }
        await host.SetHostGameRunning(false);
        await Until(()=>guest.Current?.SidePlan?.Session==planSession,"side plan and selection history replicated before loss");
        string history=History(host.Current!,id);
        var saved=Save(Player(host,id)!);var otherSaved=Save(Player(host,bystanderId)!);object? otherConnection=Upstream(bystander);
        int launches=0;guest.LaunchRequested+=()=>Interlocked.Increment(ref launches);
        BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true,"transport-only loss retains a reconnecting member reservation");
        Check(guest.Current?.Id==roomId&&guest.LocalId==id&&!guest.IsHost,"Guest loss discarded or changed the cached room identity");
        Check(History(guest.Current!,id)==history&&History(host.Current!,id)==history,"Transport loss discarded the side plan or saved map/lineup history");
        Check(host.IsHost&&host.Current?.Players.Count==3&&bystander.Current?.Players.Count==3,"Guest partition changed the original owner or removed reserved players");
        Check(ReferenceEquals(Upstream(bystander),otherConnection)&&bystander.LocalId==bystanderId,"Guest partition touched the bystander's connection or identity");
        Check(Save(Player(host,bystanderId)!)==otherSaved,"Guest partition changed the bystander's selections");
        Check(!host.Current!.AllGuestsReady,"Room remained launch-ready while a participant was disconnected");
        await NoAdvertisedHost(guest,bystander.Address);
        BlockRecovery(guest,false);
        await Until(()=>!guest.IsReconnecting&&Player(host,id) is{Reconnecting:false}&&guest.Current?.Players.Count==3,"original host resumes the same member after transport recovery");
        Check(guest.LocalId==id&&guest.Current!.Id==roomId&&guest.Current.HostAddress==host.Address&&!guest.IsHost,"Recovery silently joined a different room or changed ownership/identity");
        Check(host.Current!.Players.Count(p=>p.Id==id)==1&&host.Current.Players.Count(p=>p.Address==guest.Address)==1,"Recovery duplicated the member");
        Check(Save(Player(host,id)!)==saved&&Save(Player(guest,id)!)==saved,"Recovery reset team, spot, color, readiness or game-running status");
        Check(History(guest.Current!,id)==history&&History(host.Current!,id)==history,"Recovery reset side-plan coordination or saved map/lineup choices");
        Check(ReferenceEquals(Upstream(bystander),otherConnection)&&Save(Player(host,bystanderId)!)==otherSaved,"Recovery replaced the healthy bystander connection or selections");
        Check(Volatile.Read(ref launches)==0,"Recovery unexpectedly launched or relaunched BFME2");

        BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true,"second partition reserves the same member");
        await host.Kick(id);
        Check(Player(host,id)==null,"Kicking a disconnected member kept its reservation");
        BlockRecovery(guest,false);
        await Until(()=>guest.Current==null&&!guest.IsReconnecting,"revoked reconnect ticket cannot resurrect a kicked member");
        Check(!guest.IsHost&&host.Current!.Players.Count==2,"Kicked member was recreated or promoted");
    }
    static async Task ExpirationAndGracefulLeave()
    {
        using var host=new PeerService(SettingsFor("Reservation owner"));using var guest=new PeerService(SettingsFor("Expiring member"));using var replacement=new PeerService(SettingsFor("Replacement member"));
        host.Start("127.0.0.195");guest.Start("127.0.0.196");replacement.Start("127.0.0.197");host.Host(new(){Capacity=2},Spots());
        await guest.Join(host.Address);await guest.SelectPlacement(spot:1,team:2,color:4);string id=guest.LocalId;
        BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true,"reservation watcher fixture disconnected once");
        BlockRecovery(guest,false);
        await Until(()=>!guest.IsReconnecting&&Player(host,id) is{Reconnecting:false},"reservation watcher survives a successful reconnect");
        // Let the watcher encounter an empty deadline set before the next loss.
        await Task.Delay(1100);BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true&&HasDeadline(host,id),"same room reserves a second disconnected session");
        lock(Gate(host))((Dictionary<string,DateTimeOffset>)typeof(PeerService).GetField("reconnectDeadlines",Hidden)!.GetValue(host)!)[id]=DateTimeOffset.UtcNow.AddSeconds(-1);
        await Until(()=>Player(host,id)==null&&!HasTicket(host,id)&&!HasDeadline(host,id),"expired reservation releases its roster row and resume credentials");
        BlockRecovery(guest,false);
        await Until(()=>guest.Current==null&&!guest.IsReconnecting,"expired resume credentials cannot restore the old member");
        Check(!guest.IsHost,"Expired member became a replacement host");
        await replacement.Join(host.Address);await replacement.SelectPlacement(spot:1,team:2,color:4);string replacementId=replacement.LocalId;
        await Until(()=>Player(host,replacementId) is{Spot:1,Color:4},"expired reservation releases room capacity and occupied choices");
        replacement.Leave();
        await Until(()=>host.Current?.Players.Count==1&&!HasTicket(host,replacementId)&&!HasDeadline(host,replacementId),"graceful leave removes membership immediately without a reconnect reservation");
    }
    static async Task HalfOpenReplacement()
    {
        using var host=new PeerService(SettingsFor("Half-open owner"));using var guest=new PeerService(SettingsFor("Half-open member"));
        host.Start("127.0.0.198");guest.Start("127.0.0.199");host.Host(new(){Capacity=2},Spots());await guest.Join(host.Address);
        await guest.SelectPlacement(spot:4,team:2,color:3);await guest.SetReady(true);string id=guest.LocalId,roomId=host.Current!.Id;
        await Until(()=>Player(host,id) is{Spot:4,Color:3,Ready:true},"half-open fixture selections replicated");
        var saved=Save(Player(host,id)!);object? oldUpstream=Upstream(guest);
        object serverGuests=typeof(PeerService).GetField("guests",Hidden)!.GetValue(host)!;
        object? oldServer=serverGuests.GetType().GetProperty("Item")!.GetValue(serverGuests,[id]);
        string token=(string)typeof(PeerService).GetField("recoveryToken",Hidden)!.GetValue(guest)!;
        int generation=(int)typeof(PeerService).GetField("sessionGeneration",Hidden)!.GetValue(guest)!;
        BlockRecovery(guest,true);
        // Authenticate a replacement without first removing the server's old
        // connection, simulating different TCP failure detection on each side.
        var resume=(Task)typeof(PeerService).GetMethod("ResumeRoom",Hidden)!.Invoke(guest,[host.Address,id,token,roomId,generation,CancellationToken.None])!;
        await resume.WaitAsync(TimeSpan.FromSeconds(8));await Task.Delay(100);
        object? newServer=serverGuests.GetType().GetProperty("Item")!.GetValue(serverGuests,[id]);
        Check(!ReferenceEquals(Upstream(guest),oldUpstream)&&!ReferenceEquals(newServer,oldServer),"Authenticated resume did not replace both half-open connection endpoints");
        Check(!guest.IsHost&&!guest.IsReconnecting&&Player(host,id) is{Reconnecting:false}&&host.Current!.Players.Count==2,"Old connection cleanup reserved or removed its replacement");
        Check(guest.LocalId==id&&Save(Player(host,id)!)==saved&&Save(Player(guest,id)!)==saved,"Half-open replacement reset membership or choices");
    }
    static async Task BlockedHostCannotResume()
    {
        using var host=new PeerService(SettingsFor("Blocked-recovery owner"));using var guest=new PeerService(SettingsFor("Blocking guest"));
        host.Start("127.0.0.204");guest.Start("127.0.0.205");host.Host(new(){Capacity=2},Spots());await guest.Join(host.Address);string id=guest.LocalId;
        BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true,"blocked-host fixture retains a disconnected member");
        await guest.SetBlocked("Blocked-recovery owner",host.Address,true);BlockRecovery(guest,false);
        await Until(()=>guest.Current==null&&!guest.IsReconnecting,"reconnect obeys a newly blocked host instead of silently rejoining");
        Check(!guest.IsHost&&Player(host,id) is{Reconnecting:true},"Blocking the host reconnected or promoted the guest");
    }
    static async Task ManualRejoinReplacesOwnReservation()
    {
        using var host=new PeerService(SettingsFor("Manual-rejoin owner"));using var guest=new PeerService(SettingsFor("Manual-rejoin guest"));
        host.Start("127.0.0.206");guest.Start("127.0.0.207");host.Host(new(){Capacity=2},Spots());await guest.Join(host.Address);string oldId=guest.LocalId;
        BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,oldId)?.Reconnecting==true,"manual-rejoin fixture fills the room with a disconnected reservation");
        guest.Leave();BlockRecovery(guest,false);await guest.Join(host.Address);string newId=guest.LocalId;
        await Until(()=>host.Current?.Players.Count==2&&Player(host,newId) is{Reconnecting:false},"manual join can replace its own disconnected reservation in a full room");
        Check(newId!=oldId&&Player(host,oldId)==null&&!HasTicket(host,oldId)&&!HasDeadline(host,oldId),"Fresh join retained an obsolete identity or resume credentials");
        Check(host.Current!.Players.Count(p=>p.Address==guest.Address)==1&&!guest.IsReconnecting&&!guest.IsHost,"Manual rejoin duplicated the same source or resumed with the wrong role");
        await Task.Delay(1100);
        Check(guest.LocalId==newId&&Player(host,newId) is{Reconnecting:false},"Cancelled automatic retry replaced a successful fresh manual join");
    }
    static async Task LeaveAndDisposeCancel()
    {
        using var host=new PeerService(SettingsFor("Cancellation owner"));using var leaver=new PeerService(SettingsFor("Leave during reconnect"));
        host.Start("127.0.0.184");leaver.Start("127.0.0.185");host.Host(new(){Capacity=4},Spots());
        await leaver.Join(host.Address);string id=leaver.LocalId;
        BlockRecovery(leaver,true);DropTransport(leaver);
        await Until(()=>leaver.IsReconnecting&&Player(host,id)?.Reconnecting==true,"leave-cancellation fixture is reconnecting");
        leaver.Leave();BlockRecovery(leaver,false);
        Check(leaver.Current==null&&!leaver.IsHost&&!leaver.IsReconnecting,"Explicit leave did not cancel disconnected recovery immediately");
        await Task.Delay(1500);
        Check(leaver.Current==null&&!leaver.IsHost&&!leaver.IsReconnecting,"A retry resurrected a room after explicit leave");

        var disposed=new PeerService(SettingsFor("Dispose during reconnect"));
        bool disposedAlready=false;
        try
        {
            disposed.Start("127.0.0.186");await disposed.Join(host.Address);string disposedId=disposed.LocalId;
            BlockRecovery(disposed,true);DropTransport(disposed);
            await Until(()=>disposed.IsReconnecting&&Player(host,disposedId)?.Reconnecting==true,"dispose-cancellation fixture is reconnecting");
            disposed.Dispose();disposedAlready=true;BlockRecovery(disposed,false);
            await Task.Delay(1500);
            Check(disposed.Current==null&&!disposed.IsHost&&!disposed.IsReconnecting,"A disposed peer resumed or promoted its cached room");
        }
        finally{if(!disposedAlready)disposed.Dispose();}
    }
    static async Task ReplacedRoomIsNotResumed()
    {
        using var host=new PeerService(SettingsFor("New-room owner"));using var guest=new PeerService(SettingsFor("Old-room guest"));
        host.Start("127.0.0.187");guest.Start("127.0.0.188");host.Host(new(){Title="Old room"},Spots());await guest.Join(host.Address);
        string oldRoom=host.Current!.Id,id=guest.LocalId;BlockRecovery(guest,true);DropTransport(guest);
        await Until(()=>guest.IsReconnecting&&Player(host,id)?.Reconnecting==true,"old-room retry fixture is disconnected");
        host.Host(new(){Title="Replacement room"},Spots());string replacement=host.Current!.Id;
        Check(replacement!=oldRoom,"Fixture did not replace the room");BlockRecovery(guest,false);
        await Until(()=>guest.Current==null&&!guest.IsReconnecting,"old room token cannot resume into a replacement room at the same address");
        Check(host.Current!.Id==replacement&&host.Current.Players.Count==1&&!guest.IsHost,"Stale recovery joined or mutated the replacement room");
    }
    static async Task LostHostDoesNotElect()
    {
        var host=new PeerService(SettingsFor("Unavailable owner"));using var guest=new PeerService(SettingsFor("First eligible guest"));using var other=new PeerService(SettingsFor("Another guest"));
        bool hostDisposed=false;
        try
        {
            host.Start("127.0.0.189");guest.Start("127.0.0.190");other.Start("127.0.0.191");host.Host(new(){Title="No election"},Spots());
            await guest.Join(host.Address);await other.Join(host.Address);string room=host.Current!.Id;
            host.Dispose();hostDisposed=true;
            await Until(()=>guest.IsReconnecting&&other.IsReconnecting,"host loss makes both guests reconnect without electing a replacement");
            Check(guest.Current?.Id==room&&other.Current?.Id==room&&!guest.IsHost&&!other.IsHost,"Host loss changed the cached room or promoted a guest");
            await NoAdvertisedHost(guest,other.Address);await NoAdvertisedHost(other,guest.Address);
            guest.Leave();other.Leave();
        }
        finally{if(!hostDisposed)host.Dispose();}
    }
    static async Task ConfirmedHandoffsRemain()
    {
        using var host=new PeerService(SettingsFor("Transfer owner"));using var successor=new PeerService(SettingsFor("Confirmed successor"));using var guest=new PeerService(SettingsFor("Handoff participant"));
        host.Start("127.0.0.192");successor.Start("127.0.0.193");guest.Start("127.0.0.194");host.Host(new(){Title="Confirmed handoff",Capacity=4},Spots());
        await successor.Join(host.Address);await guest.Join(host.Address);await guest.SelectPlacement(spot:2,team:2,color:4);await guest.SetReady(true);
        string room=host.Current!.Id,guestId=guest.LocalId;
        await Until(()=>Player(host,guestId) is{Ready:true,Team:2,Spot:2,Color:4},"explicit-handoff fixture selections replicated");
        var saved=Save(Player(host,guestId)!);await host.Promote(successor.LocalId);
        await Until(()=>successor.IsHost&&host.Current?.HostAddress==successor.Address&&guest.Current?.HostAddress==successor.Address,"confirmed Promote still transfers ownership");
        Check(successor.Current!.Id==room&&guest.LocalId==guestId&&Save(Player(successor,guestId)!)==saved,"Explicit handoff lost room, member identity or selections");
        await successor.LeaveRoom();
        await Until(()=>host.IsHost&&successor.Current==null&&guest.Current?.HostAddress==host.Address,"confirmed host LeaveRoom still hands the room to a connected member");
        Check(host.Current!.Id==room&&host.Current.Players.Count==2&&Save(Player(host,guestId)!)==saved,"Confirmed leaving-host handoff reset room state");
    }
}

using Fellowship;
using System.Text.Json;

static class ExternalHostSyncTests
{
    const string Host="26.1.0.1",Local="26.1.0.2",Outside="26.1.0.99";
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static NativeLobby.Slot Human(int row,string address,bool local=false,int spot=0,int team=0,int color=0,int faction=0)=>
        new(row,6,spot,team,local,color,faction,address);
    static NativeLobby.Snapshot Lobby(string host,params NativeLobby.Slot[] slots)=>
        new(123,GameMap.Rhun.Path,host,slots.ToList()){Setup=new(GameMap.Rhun.Path,1000,100,true,true)};
    static LivePlacement.Command Command()=>new("personal-room",GameMap.Rhun.Path,Host,new(2,5,3),
        new(GameMap.Rhun.Path,4000,200,false,false),Host+"|"+Local,Rhun3v3:true,AllowExternalHost:true);

    public static void Run()
    {
        var command=Command();
        var external=Lobby(Outside,Human(0,Outside),Human(3,Local,true,4,1,2));
        Check(LivePlacement.MatchesLobby(command,external),"External native host blocked the local player's matching-map sync");
        Check(!LivePlacement.UsesCoordinatedRhun(command,external),"External host entered the shared Rhûn side protocol");
        Check(!LivePlacement.MatchesLobby(command with{AllowExternalHost=false},external),"Legacy command lost its native-host identity guard");
        Check(!LivePlacement.MatchesLobby(command,external with{Map=@"maps\other\other.map"}),"External guest synchronized on the wrong map");
        Check(LivePlacement.MatchesLobby(command,external with{Map=GameMap.Rhun.Path.ToUpperInvariant()}),"Matching native map required identical path casing");
        Check(LivePlacement.MatchesLobby(command,external with{Setup=new(GameMap.Rhun.Path,750,50,false,true)}),"External host's different custom rules blocked personal selections");
        Check(!LivePlacement.MatchesLobby(command,external with{Slots=[Human(0,Outside)]}),"Missing native local identity was accepted");
        Check(!LivePlacement.MatchesLobby(command,external with{Slots=[Human(0,Outside,true),Human(3,Local,true)]}),"Ambiguous native local identity was accepted");
        Check(!LivePlacement.MatchesLobby(command,external with{Slots=[Human(0,Outside),new(3,3,4,1,true,2,0,Local)]}),"A native AI row was accepted as the local human");

        var managed=Lobby(Host,Human(0,Host),Human(3,Local,true,4,1,2));
        Check(LivePlacement.UsesCoordinatedRhun(command,managed),"Fully managed Rhûn lobby lost coordinated sides");
        Check(!LivePlacement.UsesCoordinatedRhun(command with{Rhun3v3=false},managed),"Non-Rhûn mode entered side coordination");
        var mixed=managed with{Slots=[..managed.Slots,Human(6,Outside,false,2,0,4)]};
        Check(LivePlacement.MatchesLobby(command,mixed)&&!LivePlacement.UsesCoordinatedRhun(command,mixed),"Unknown participant stalled a known-host mixed lobby");
        var observer=mixed with{Slots=mixed.Slots.Select(s=>s.Ip==Outside?s with{Faction=-2,Spot=-1}:s).ToList()};
        Check(!LivePlacement.UsesCoordinatedRhun(command,observer),"Unknown observer entered a report RhunSides would reject");
        var knownObserver=managed with{Slots=[..managed.Slots,Human(6,Outside,false,-1,0,4,-2)]};
        Check(LivePlacement.UsesCoordinatedRhun(command with{Members=command.Members+"|"+Outside},knownObserver),"Known observer unnecessarily disabled managed Rhûn");
        var ai=managed with{Slots=[..managed.Slots,new(5,3,2,0,false,4,0)]};
        Check(!LivePlacement.UsesCoordinatedRhun(command,ai),"Active AI entered human-only side coordination");
        Check(LivePlacement.UsesCoordinatedRhun(command,ai with{Slots=ai.Slots.Select(s=>s.Row==5?s with{State=1}:s).ToList()}),"Unused native slot disabled managed Rhûn");
        var duplicate=managed with{Slots=[..managed.Slots,Human(6,Local,false,2,0,4)]};
        Check(!LivePlacement.UsesCoordinatedRhun(command,duplicate),"Duplicate native identities entered the shared side protocol");
        Check(LivePlacement.UsesCoordinatedRhun(command with{AllowExternalHost=false},mixed),"Opt-in changed legacy Rhûn coordination behavior");

        Check(LivePlacement.ContextKey(command,managed)!=LivePlacement.ContextKey(command,mixed),"Mixed roster did not invalidate reconciliation context");
        Check(LivePlacement.ContextKey(command,external)!=LivePlacement.ContextKey(command,managed),"Actual native host change kept the previous reconciliation context");
        Check(LivePlacement.ContextKey(command,managed)!=LivePlacement.ContextKey(command with{AllowExternalHost=false},managed),"Personal-sync opt-in did not invalidate reconciliation context");

        // A human or AI outside Fellowship keeps its occupied seat and color.
        // Native requests still target the one decoded local row, never row zero.
        string before=JsonSerializer.Serialize(mixed);
        var conflict=LivePlacement.ResolveOccupiedSpot(mixed,new(2,3,5));
        conflict=LivePlacement.ResolveOccupiedColor(mixed,conflict);
        Check(conflict==new LivePlacement.Choice(2,5,3),"Outside player's occupied spot/color was overwritten");
        var aiConflict=LivePlacement.ResolveOccupiedSpot(ai,new(2,3,3));
        Check(aiConflict.Spot==5,"Outside AI's occupied spot was overwritten");
        byte[] request=NativePlacement.RequestBytes(1234,external,command.Choice.Team,command.Choice.Spot,color:command.Choice.Color);
        Check(BitConverter.ToInt32(request,16)==external.Local.Row&&BitConverter.ToInt32(request,568)==0,"Personal request targeted another native row or shared map/rules mode");
        Check(BitConverter.ToInt32(request,20)==command.Choice.Team-1&&BitConverter.ToInt32(request,24)==command.Choice.Spot-1,"Personal native team/spot request was not preserved");
        Check(before==JsonSerializer.Serialize(mixed),"Personal safety checks mutated another native player");
        Check(!LivePlacement.HasDuplicateSpots(mixed)&&LivePlacement.HasDuplicateSpots(mixed with{Slots=mixed.Slots.Select(s=>s.Ip==Outside?s with{Spot=managed.Local.Spot}:s).ToList()}),"Mixed-lobby duplicate spot protection changed");

        Transitions(command,managed);
        string json=JsonSerializer.Serialize(command);
        Check(JsonSerializer.Deserialize<LivePlacement.Command>(json)?.AllowExternalHost==true,"Personal-sync opt-in did not survive helper IPC");
        Check(JsonSerializer.Deserialize<LivePlacement.Command>("{\"Room\":\"legacy\",\"Map\":\"maps\\\\test.map\",\"Host\":\"host\",\"Choice\":{\"Team\":1,\"Spot\":1}}")?.AllowExternalHost==false,"Legacy helper command silently opted into external hosts");
        Console.WriteLine("PASS external/mixed native lobby personal sync, map/local identity guards, managed Rhûn preservation, stale-plan fencing, occupied outsiders, own-row-only requests and legacy IPC defaults");
    }

    static void Transitions(LivePlacement.Command command,NativeLobby.Snapshot managed)
    {
        var old=new SidePlan{Session=Guid.NewGuid().ToString("N"),Host=Host,Ready=true,
            Moves=new(){[Local]=new(2,5,1,2)}};
        var guard=new LivePlacement.CoordinationGuard();
        Check(!guard.Observe(true,old)&&!guard.IsFenced(old,managed),"Initial managed plan was fenced without a mixed lobby");
        Check(guard.Observe(false,old),"Managed-to-personal transition was not detected");
        Check(!guard.Observe(false,old),"Stable personal mode repeatedly reset the helper");
        Check(guard.Observe(true,old)&&guard.IsFenced(old,managed),"Mixed-to-managed transition reused an unsafe old side plan");
        Check(guard.IsFenced(null,managed),"Missing post-mixed plan prematurely enabled coordinated writes");
        Check(guard.IsFenced(new SidePlan{Session=old.Session,Host=Host,Revision=old.Revision+1,Ready=true,Moves=old.Moves},managed),"An old-session revision bypassed stale native-selection protection");
        var reconciler=new LivePlacement.Reconciler();
        bool personal=LivePlacement.UsesCoordinatedRhun(command,managed)&&guard.IsFenced(old,managed);
        Check(personal&&LivePlacement.MatchesLobby(command,managed)&&reconciler.ShouldWrite(command.Choice),"Unsafe stale plan stalled ordinary personal reconciliation");
        reconciler.Remember(command.Choice,command.Choice);
        Check(!reconciler.ShouldWrite(command.Choice)&&reconciler.ShouldWrite(command.Choice with{Spot=6}),"Personal mode did not retain normal new-selection reconciliation");
        var cancelled=new SidePlan{Session=old.Session,Host=Host,Cancelled=true};
        Check(!guard.IsFenced(cancelled,managed),"Cancelled plan unnecessarily stalled native-selection import");
        var satisfied=new SidePlan{Session=old.Session,Host=Host,Ready=true,
            Moves=new(){[Local]=new(2,5,managed.Local.Team+1,managed.Local.Spot+1)}};
        Check(!guard.IsFenced(satisfied,managed),"Already satisfied old plan stalled a helper with delayed membership");
        satisfied.Started=true;
        Check(guard.IsFenced(satisfied,managed),"Started plan from before mixed play paused a new lobby indefinitely");
        var fresh=new SidePlan{Session=Guid.NewGuid().ToString("N"),Host=Host,Ready=true,Moves=old.Moves};
        Check(!guard.IsFenced(fresh,managed),"Fresh native-host session could not resume coordination");
        var simultaneous=new LivePlacement.CoordinationGuard();
        simultaneous.Observe(false,old);
        Check(simultaneous.Observe(true,fresh)&&!simultaneous.IsFenced(fresh,managed),"Fresh plan arriving with managed membership was mistaken for the old personal-mode session");
        var withoutPlan=new LivePlacement.CoordinationGuard();
        withoutPlan.Observe(false,null);
        Check(withoutPlan.Observe(true,fresh)&&!withoutPlan.IsFenced(fresh,managed),"First managed plan was fenced when personal mode had no prior plan");
        Check(guard.Observe(false,fresh)&&guard.Observe(true,fresh)&&guard.IsFenced(fresh,managed),"A second mixed session reused the previous managed plan");
    }
}

namespace Fellowship;

// Personal mirroring for a native lobby which cannot use the shared Rhûn plan.
// Native team numbers remain the native host's choice; only our local seat moves.
public sealed class PersonalRhunSides
{
    public enum State {Disabled,WaitingForTeams,WaitingForSpot,Random,Ready,Blocked,ClearOwn}
    public sealed record Resolution(LivePlacement.Choice Choice,State State,int TargetSpot=0,bool FromHistory=false);

    sealed record MatchHistory(string Room,string Map,string Player,int Spot,Dictionary<string,int> Teams);
    MatchHistory? lastMatch;

    string room="",map="",player="",host="";
    uint? identity;
    int? vertical,previousNativeSpot,previousNativeTeam,pendingTeam,previousNativeColor,previousCommandColor,color;
    LivePlacement.Choice? previousCommand,output;
    bool fresh=true,manualRandom,teamNeedsConfirmation,teamEdited;

    // SessionGuard has positive evidence for recreated lobbies even when BFME2
    // reuses the same native address. Keep top/middle/bottom across a replay.
    public void NewLobby()
    {
        fresh=true;manualRandom=teamEdited=false;previousNativeSpot=previousNativeTeam=previousNativeColor=null;previousCommand=output=null;
        pendingTeam=null;teamNeedsConfirmation=false;
    }
    public void Reset()
    {
        ResetPlacement();lastMatch=null;
    }
    void ResetPlacement()
    {
        NewLobby();vertical=previousCommandColor=color=null;room=map=player=host="";identity=null;
    }
    // Record native success before the next snapshot: a player may undo the
    // applied selection before ReadAfterWrite, including back to the old value.
    public void RecordApplied(LivePlacement.Choice applied)
    {
        previousNativeSpot=applied.Spot;previousNativeTeam=applied.Team;previousNativeColor=applied.Color;
        if(pendingTeam==applied.Team){pendingTeam=null;teamNeedsConfirmation=false;}
    }
    // Called only with the last matching lobby when native match-start flags are
    // observed. Lobby guesses and elapsed game counts never become evidence.
    public void RememberMatch(LivePlacement.Command command,NativeLobby.Snapshot? snapshot)
    {
        lastMatch=null;
        if(snapshot==null)return;
        if(!command.Rhun3v3||!Rhun(command.Map)||!Rhun(snapshot.Map)||string.IsNullOrEmpty(command.Room))return;
        var active=snapshot.Slots.Where(slot=>slot.Active&&!slot.Observer).ToArray();
        if(active.Length!=6||active.Any(slot=>slot.State!=6||slot.Team is not (0 or 1)||!KnownIp(Ip(slot))))return;
        if(active.Select(slot=>Ip(slot)).Distinct(StringComparer.Ordinal).Count()!=6||
           active.Count(slot=>slot.Team==0)!=3||active.Count(slot=>slot.Team==1)!=3)return;
        var locals=active.Where(slot=>slot.Local).ToArray();
        var hosts=active.Where(slot=>slot.Row==0&&Ip(slot)==snapshot.HostAddress).ToArray();
        if(locals.Length!=1||hosts.Length!=1)return;
        lastMatch=new(command.Room,snapshot.Map,Ip(locals[0]),locals[0].Spot+1,
            active.ToDictionary(slot=>Ip(slot),slot=>slot.Team,StringComparer.Ordinal));
    }
    public Resolution Resolve(LivePlacement.Command command,NativeLobby.Snapshot snapshot,IReadOnlyList<MapSpot> spots)
    {
        var requested=command.Choice;
        if(lastMatch!=null&&(lastMatch.Room!=command.Room||!command.Rhun3v3||!command.AllowExternalHost||!Rhun(command.Map)||!Rhun(snapshot.Map)))lastMatch=null;
        if(!command.AllowExternalHost||!command.Rhun3v3||
           !command.Map.Equals(GameMap.Rhun.Path,StringComparison.OrdinalIgnoreCase)||
           !snapshot.Map.Equals(GameMap.Rhun.Path,StringComparison.OrdinalIgnoreCase)||
           LivePlacement.UsesCoordinatedRhun(command,snapshot))
        {ResetPlacement();return new(requested,State.Disabled);}
        var locals=snapshot.Slots.Where(slot=>slot.Local&&slot.State==6).ToArray();
        if(locals.Length!=1){ResetPlacement();return new(requested,State.Disabled);}
        var local=locals[0];string localIp=Ip(local);
        if(lastMatch!=null&&lastMatch.Player!=localIp)lastMatch=null;
        if(room!=command.Room||map!=snapshot.Map||player!=localIp)
        {ResetPlacement();room=command.Room;map=snapshot.Map;player=localIp;}
        if(identity!=snapshot.Identity||host!=snapshot.HostAddress)
        {NewLobby();identity=snapshot.Identity;host=snapshot.HostAddress;}

        var left=spots.Where(spot=>spot.X<.5).OrderBy(spot=>spot.Y).ThenBy(spot=>spot.Number).ToArray();
        var right=spots.Where(spot=>spot.X>=.5).OrderBy(spot=>spot.Y).ThenBy(spot=>spot.Number).ToArray();
        bool valid=spots.Count==6&&left.Length==3&&right.Length==3&&
            spots.All(spot=>spot.Number is >0 and <=8&&double.IsFinite(spot.X)&&double.IsFinite(spot.Y))&&
            spots.Select(spot=>spot.Number).Distinct().Count()==6;
        if(!valid){ResetPlacement();return new(requested,State.Disabled);}
        int Rank(int number)
        {
            int index=Array.FindIndex(left,spot=>spot.Number==number);
            return index>=0?index:Array.FindIndex(right,spot=>spot.Number==number);
        }
        int nativeSpot=local.Spot+1,nativeTeam=local.Team+1,nativeColor=local.Color+1;
        bool commandSpotEdit=previousCommand!=null&&requested.Spot!=previousCommand.Spot&&requested.Spot!=output?.Spot;
        bool commandTeamEdit=previousCommand!=null&&requested.Team!=previousCommand.Team&&requested.Team!=output?.Team;
        bool nativeSpotEdit=previousNativeSpot.HasValue&&nativeSpot!=previousNativeSpot&&nativeSpot!=output?.Spot;
        bool nativeTeamEdit=previousNativeTeam.HasValue&&nativeTeam!=previousNativeTeam&&nativeTeam!=output?.Team;
        bool commandColorEdit=previousCommandColor.HasValue&&requested.Color!=previousCommandColor&&requested.Color!=output?.Color;
        bool nativeColorEdit=previousNativeColor.HasValue&&nativeColor!=previousNativeColor&&nativeColor!=output?.Color;
        if(!color.HasValue||commandColorEdit)color=requested.Color;
        else if(nativeColorEdit)color=nativeColor;
        if(fresh)
        {
            int rank=Rank(nativeSpot);if(rank<0)rank=Rank(requested.Spot);
            if(rank<0&&lastMatch!=null)rank=Rank(lastMatch.Spot);
            if(rank>=0)vertical=rank;
            if(nativeTeam is not (1 or 2)&&requested.Team is 1 or 2)
            {pendingTeam=requested.Team;teamNeedsConfirmation=true;}
        }
        else
        {
            // A native edit wins when both sides changed before the next poll.
            int changed=nativeSpotEdit?nativeSpot:commandSpotEdit?requested.Spot:-1;
            if(changed==0)manualRandom=true;
            else if(Rank(changed) is >=0 and var rank){vertical=rank;manualRandom=false;}
            if(nativeTeamEdit){pendingTeam=null;teamNeedsConfirmation=false;}
            else if(commandTeamEdit){pendingTeam=requested.Team;teamNeedsConfirmation=false;}
            if(commandTeamEdit||nativeTeamEdit)teamEdited=true;
        }
        if(pendingTeam==nativeTeam){pendingTeam=null;teamNeedsConfirmation=false;}
        int team=pendingTeam??nativeTeam;
        bool fromHistory=false;
        Resolution Finish(State state,int spot,int target=0)
        {
            var choice=new LivePlacement.Choice(team,spot,color.Value);
            previousCommand=requested;previousNativeSpot=nativeSpot;previousNativeTeam=nativeTeam;previousNativeColor=nativeColor;previousCommandColor=requested.Color;output=choice;fresh=false;
            return new(choice,state,target,fromHistory);
        }
        var nativeHost=snapshot.Slots.FirstOrDefault(slot=>slot.Row==0&&slot.State==6&&Ip(slot)==snapshot.HostAddress);
        if(local.Observer||!local.Active)return Finish(State.WaitingForTeams,0);
        if(nativeHost==null||!nativeHost.Active||nativeHost.Observer)
            return Finish(State.WaitingForTeams,nativeSpot);
        if(nativeHost.Team is not (-1 or 0 or 1)||nativeTeam is not (0 or 1 or 2)||team is not (0 or 1 or 2))
            return Finish(State.WaitingForTeams,nativeSpot);
        bool hosting;
        if(nativeHost.Team is 0 or 1&&team is 1 or 2&&!teamNeedsConfirmation)
            hosting=team-1==nativeHost.Team;
        else if(!teamEdited&&TryHistory(snapshot,localIp,nativeHost,out hosting))
        {
            fromHistory=true;
            // Saved Fellowship team numbers may belong to the previous lobby.
            // Derive a missing label only from a known current native label;
            // when both are Random, choose the proven side without a label.
            team=nativeTeam is 1 or 2?nativeTeam:nativeHost.Team is 0 or 1?
                (hosting?nativeHost.Team+1:2-nativeHost.Team):0;
            pendingTeam=null;teamNeedsConfirmation=false;
        }
        else return Finish(State.WaitingForTeams,nativeSpot);
        if(manualRandom)return Finish(State.Random,0);
        if(vertical is not >=0 or >2)return Finish(State.WaitingForSpot,nativeSpot);

        int target=(hosting?right:left)[vertical.Value].Number;
        var occupied=snapshot.Slots.Where(slot=>!slot.Local&&slot.State>1&&slot.Spot+1==target).ToArray();
        if(occupied.Length==0)return Finish(State.Ready,target,target);

        // Two known clients exchanging complementary seats may each release
        // their own seat. Outsiders, AI, other verticals and arbitrary occupied
        // spots never authorize a clear, and nobody writes another player's row.
        var members=(command.Members??"").Split('|',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if(occupied.Length==1&&nativeSpot>0&&members.Contains(localIp)&&nativeHost.Team is 0 or 1)
        {
            var other=occupied[0];int otherRank=Rank(other.Spot+1);
            if(other.State==6&&other.Active&&!other.Observer&&other.Team is 0 or 1&&
               members.Contains(Ip(other))&&otherRank>=0)
            {
                int counterpart=(other.Team==nativeHost.Team?right:left)[otherRank].Number;
                if(counterpart==nativeSpot&&counterpart!=target)return Finish(State.ClearOwn,0,target);
            }
        }
        return Finish(State.Blocked,nativeSpot,target);
    }
    bool TryHistory(NativeLobby.Snapshot snapshot,string localIp,NativeLobby.Slot nativeHost,out bool hosting)
    {
        hosting=false;
        if(lastMatch==null||!lastMatch.Teams.TryGetValue(localIp,out int localCohort)||
           !lastMatch.Teams.TryGetValue(Ip(nativeHost),out int hostCohort))return false;
        // The same two cohorts may use swapped numeric labels in a new lobby.
        // Any current known member contradicting that relationship makes the
        // old lineup unsafe evidence, even if host/local labels are unset.
        var labels=new Dictionary<int,int>();
        foreach(var slot in snapshot.Slots.Where(slot=>slot.State==6&&slot.Active&&!slot.Observer&&slot.Team is 0 or 1))
        {
            if(!lastMatch.Teams.TryGetValue(Ip(slot),out int cohort))continue;
            if(labels.TryGetValue(cohort,out int label)&&label!=slot.Team)return false;
            if(labels.Any(pair=>pair.Key!=cohort&&pair.Value==slot.Team))return false;
            labels[cohort]=slot.Team;
        }
        hosting=localCohort==hostCohort;
        return true;
    }
    static bool Rhun(string path)=>path.Equals(GameMap.Rhun.Path,StringComparison.OrdinalIgnoreCase);
    static bool KnownIp(string value)=>System.Net.IPAddress.TryParse(value,out var address)&&
        address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&value!="0.0.0.0"&&value!="255.255.255.255";
    static string Ip(NativeLobby.Slot slot)
    {
        try{return slot.Ip;}
        catch(FormatException){return "";}
    }
}

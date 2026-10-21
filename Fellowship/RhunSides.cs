namespace Fellowship;

public sealed record LobbyReport(string Session,NativeLobby.Snapshot Lobby,bool Started=false);
public sealed record SideMove(int FromTeam,int FromSpot,int Team,int Spot);
public sealed record SideArrival(int NativeTeam,int Team);
public sealed class SidePlan
{
    public string Session {get;set;}="";
    public int Revision {get;set;}
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key=>Session+":"+Revision;
    public string Host {get;set;}="";
    public bool Ready {get;set;}
    public bool Cancelled {get;set;}
    public bool Started {get;set;}
    public Dictionary<string,SideMove> Moves {get;set;}=new();
    public HashSet<string> Confirmed {get;set;}=new();
    public Dictionary<string,SideArrival> Arrivals {get;set;}=new();
}
public static class RhunSides
{
    public static bool Enabled(Room room)=>room.Patch=="1.00"&&room.Mode=="3v3"&&room.MapPath.Equals(GameMap.Rhun.Path,StringComparison.OrdinalIgnoreCase);
    public static bool Apply(Room room,LobbyReport report,string sender)
    {
        var lobby=report.Lobby;
        if(!Enabled(room)||lobby.Map!=room.MapPath||sender!=lobby.HostAddress||
           !Guid.TryParseExact(report.Session,"N",out _)||lobby.Slots.Count>8)return false;
        var humans=lobby.Slots.Where(s=>s.State==6).ToArray();
        if(humans.Select(s=>s.Ip).Distinct().Count()!=humans.Length||
           humans.Any(s=>!room.Players.Any(p=>p.Address==s.Ip)||s.Team is < -1 or >3||s.Spot is < -1 or >7))return false;
        if(humans.Where(s=>s.Active&&s.Spot>=0).GroupBy(s=>s.Spot).Any(g=>g.Count()>1)||
           humans.Any(s=>s.Active&&s.Spot>=0&&!room.Spots.Any(p=>p.Number==s.Spot+1)))return false;
        var host=humans.FirstOrDefault(s=>s.Row==0&&s.Ip==sender);
        if(host==null)return false;
        if(report.Started)
        {
            if(room.SidePlan?.Session!=report.Session||room.SidePlan.Started)return false;
            room.LastRhunHost=sender;
            room.LastRhunLineup=humans.Where(s=>s.Active&&s.Team is 0 or 1)
                .ToDictionary(s=>s.Ip,s=>new LivePlacement.Choice(s.Team+1,s.Spot+1));
            room.SidePlan.Started=true;return true;
        }
        if(room.SidePlan is {} existing&&existing.Session==report.Session)
        {
            if(existing.Started)return false;
            var present=humans.Where(s=>s.Active).Select(s=>s.Ip).ToHashSet();
            if(existing.Moves.Keys.Any(ip=>!present.Contains(ip)))
            {
                if((existing.Ready||existing.Cancelled)&&Plan(room,report,sender,humans,host,existing))return true;
                // Release a departing/observing player's reservation without
                // reseating everyone who is still clearing or claiming a spot.
                var remaining=new SidePlan{Session=existing.Session,Revision=existing.Revision+1,Host=sender,
                    Ready=existing.Ready,Cancelled=existing.Cancelled,
                    Moves=existing.Moves.Where(e=>present.Contains(e.Key)).ToDictionary(),
                    Confirmed=existing.Confirmed.Where(present.Contains).ToHashSet(),
                    Arrivals=existing.Arrivals.Where(e=>present.Contains(e.Key)).ToDictionary()};
                bool cancel=existing.Ready||existing.Cancelled||humans.Any(s=>s.Active&&remaining.Moves.TryGetValue(s.Ip,out var move)&&!Allowed(s,move));
                Publish(room,remaining,humans,existing);
                if(cancel)Cancel(room,remaining,humans);
                return true;
            }
            bool confirmed=false;
            foreach(var slot in humans.Where(s=>s.Active))
                if(existing.Moves.TryGetValue(slot.Ip,out var move)&&slot.Team+1==move.Team&&slot.Spot+1==move.Spot)
                    confirmed|=existing.Confirmed.Add(slot.Ip);
            if(existing.Ready&&!existing.Cancelled&&humans.Any(s=>s.Active&&existing.Moves.TryGetValue(s.Ip,out var move)&&
                (s.Team+1!=move.Team||(s.Spot>=0&&s.Spot+1!=move.Spot))))
            {
                // Ready permits claiming the target from Random, not overwriting
                // a fresh native choice made before a client saw the revision.
                Cancel(room,existing,humans);return true;
            }
            bool added=humans.Any(s=>s.Active&&s.Team is 0 or 1&&!existing.Moves.ContainsKey(s.Ip));
            if(added&&!existing.Ready&&!existing.Cancelled&&host.Team is 0 or 1)
            {
                // Capture which team an arrival chose while native team numbers
                // may still be changing during the shared clear phase.
                foreach(var slot in humans.Where(s=>s.Active&&s.Team is 0 or 1&&!existing.Moves.ContainsKey(s.Ip)))
                    if(!existing.Arrivals.TryGetValue(slot.Ip,out var arrival)||arrival.NativeTeam!=slot.Team+1)
                    {existing.Arrivals[slot.Ip]=new(slot.Team+1,slot.Team==host.Team?2:1);confirmed=true;}
            }
            // Finish the current clear phase first. Its temporary Random values
            // are not new preferences, and new arrivals must not claim its seats.
            if(added&&(existing.Ready||existing.Cancelled))
                return Plan(room,report,sender,humans,host,existing)||confirmed;
            if(existing.Ready||existing.Cancelled)return confirmed;
            // An explicit edit outside the planned old/intermediate/final states wins.
            foreach(var entry in existing.Moves)
            {
                var slot=humans.FirstOrDefault(s=>s.Ip==entry.Key);
                if(slot==null||slot.Observer){Cancel(room,existing,humans);return true;}
                if(!Allowed(slot,entry.Value))
                {Cancel(room,existing,humans);return true;}
            }
            if(existing.Moves.All(e=>humans.Any(s=>s.Ip==e.Key&&s.Team+1==e.Value.Team&&(s.Spot<0||s.Spot+1==e.Value.Spot))))
            {existing.Ready=true;return true;}
            return confirmed;
        }
        return Plan(room,report,sender,humans,host,null);
    }
    static bool Allowed(NativeLobby.Slot slot,SideMove move)=>
        (slot.Team+1==move.FromTeam&&slot.Spot+1==move.FromSpot)||
        (slot.Team+1==move.Team&&(slot.Spot<0||slot.Spot+1==move.Spot));
    static bool Plan(Room room,LobbyReport report,string sender,NativeLobby.Slot[] humans,NativeLobby.Slot host,SidePlan? previous)
    {
        var active=humans.Where(s=>s.Active).ToArray();
        if(host.Team is not (0 or 1)||active.Length==0||active.Any(s=>s.Team is not (0 or 1)))return false;
        var left=room.Spots.Where(s=>s.X<.5).OrderBy(s=>s.Y).ToArray();
        var right=room.Spots.Where(s=>s.X>=.5).OrderBy(s=>s.Y).ToArray();
        if(left.Length!=3||right.Length!=3)return false;
        bool swapped=previous==null&&room.LastRhunHost.Length>0&&room.LastRhunHost!=sender&&room.LastRhunLineup.ContainsKey(sender);
        // A new teammate uses the current native labels. When labels reset, map
        // them to the old cohorts only if the remembered teammates agree.
        var cohorts=active.Append(host).Where(s=>room.LastRhunLineup.ContainsKey(s.Ip))
            .GroupBy(s=>s.Team).ToDictionary(g=>g.Key,g=>g.Select(s=>room.LastRhunLineup[s.Ip].Team).Distinct().ToArray());
        if(swapped&&active.Any(s=>!room.LastRhunLineup.ContainsKey(s.Ip)&&
            (!cohorts.TryGetValue(s.Team,out var groups)||groups.Length!=1)))return false;
        int Group(NativeLobby.Slot s)=>previous is {Cancelled:false}&&previous.Arrivals.TryGetValue(s.Ip,out var arrival)&&arrival.NativeTeam==s.Team+1?arrival.Team:
            !swapped?s.Team+1:
            room.LastRhunLineup.TryGetValue(s.Ip,out var old)?old.Team:cohorts[s.Team][0];
        int hostingGroup=Group(host);
        var groups=active.GroupBy(s=>Group(s)==hostingGroup?2:1).ToArray();
        if(groups.Any(g=>g.Count()>3))return false;
        var plan=new SidePlan{Session=report.Session,Revision=previous==null?0:previous.Revision+1,Host=sender};
        var reserved=new HashSet<int>();
        bool ManualRandom(NativeLobby.Slot s)=>s.Spot<0&&previous?.Moves.ContainsKey(s.Ip)==true&&
            (previous.Cancelled||previous.Confirmed.Contains(s.Ip));
        bool Pin(NativeLobby.Slot s,int team)=>s.Spot>=0&&room.Spots.Any(p=>p.Number==s.Spot+1&&
            ((p.X>=.5)==(team==2)||(previous?.Confirmed.Contains(s.Ip)==true&&s.Team+1==team)));
        foreach(var group in groups)
        foreach(var slot in group.Where(s=>Pin(s,group.Key)||ManualRandom(s)))
        {
            int target=slot.Spot+1;
            if(target>0&&!reserved.Add(target))return false;
            plan.Moves[slot.Ip]=new(slot.Team+1,slot.Spot+1,group.Key,target);
        }
        foreach(var group in groups)
        {
            var side=group.Key==2?right:left;var free=side.ToList();
            free.RemoveAll(s=>reserved.Contains(s.Number));
            int Original(NativeLobby.Slot slot)=>slot.Spot>=0?slot.Spot+1:
                previous is {Cancelled:false}&&previous.Moves.TryGetValue(slot.Ip,out var pending)?pending.Spot:
                room.LastRhunLineup.TryGetValue(slot.Ip,out var old)?old.Spot:
                room.Players.First(p=>p.Address==slot.Ip).LastMapSpots.GetValueOrDefault(room.MapPath);
            // Current selections win; Random falls back to the last match even
            // when the same player hosts a replay. Allocate known positions before
            // new substitutes so an unknown preference cannot take a saved seat.
            foreach(var slot in group.Where(s=>!plan.Moves.ContainsKey(s.Ip)).OrderByDescending(s=>s.Spot>=0)
                .ThenByDescending(s=>room.Spots.Any(p=>p.Number==Original(s)))
                .ThenBy(s=>s.Ip,StringComparer.Ordinal))
            {
                int original=Original(slot);
                var coordinate=room.Spots.FirstOrDefault(s=>s.Number==original);
                if(free.Count==0)return false;
                var target=free.OrderBy(s=>coordinate==null?0:Math.Abs(s.Y-coordinate.Y)).ThenBy(s=>s.Number).First();
                free.Remove(target);
                reserved.Add(target.Number);
                plan.Moves[slot.Ip]=new(slot.Team+1,slot.Spot+1,group.Key,target.Number);
            }
        }
        foreach(var slot in active)
            if(plan.Moves.TryGetValue(slot.Ip,out var move)&&slot.Team+1==move.Team&&slot.Spot+1==move.Spot)
                plan.Confirmed.Add(slot.Ip);
        Publish(room,plan,humans,previous);return true;
    }
    static void Publish(Room room,SidePlan plan,NativeLobby.Slot[] humans,SidePlan? previous)
    {
        // Apply only the shared plan; native clients clear occupied positions before
        // claiming their targets. Fellowship's roster changes atomically, not piecemeal.
        room.SidePlan=plan;
        foreach(var player in room.Players)
        {
            if(player.Spot>0)player.LastMapSpots[room.MapPath]=player.Spot;
            if(plan.Moves.TryGetValue(player.Address,out var move)){player.Team=move.Team;player.Spot=move.Spot;}
            else if(previous?.Moves.ContainsKey(player.Address)==true||humans.Any(s=>s.Ip==player.Address&&s.Observer)||
                plan.Moves.Values.Any(m=>m.Spot>0&&m.Spot==player.Spot))player.Spot=0;
        }
    }
    static void Cancel(Room room,SidePlan plan,NativeLobby.Slot[] humans)
    {
        plan.Cancelled=true;
        // Import the native layout atomically; individual imports could conflict
        // with reservations left over from the cancelled target layout.
        foreach(var player in room.Players)
        {
            var slot=humans.FirstOrDefault(s=>s.Ip==player.Address);
            if(slot!=null){player.Team=slot.Team+1;player.Spot=slot.Observer?0:slot.Spot+1;}
        }
        var occupied=humans.Where(s=>s.Active&&s.Spot>=0).Select(s=>s.Spot+1).ToHashSet();
        foreach(var player in room.Players.Where(p=>humans.All(s=>s.Ip!=p.Address)&&occupied.Contains(p.Spot)))
        {player.LastMapSpots[room.MapPath]=player.Spot;player.Spot=0;}
    }
}

namespace Fellowship;

public sealed record LobbyReport(string Session,NativeLobby.Snapshot Lobby,bool Started=false);
public sealed record SideMove(int FromTeam,int FromSpot,int Team,int Spot);
public sealed class SidePlan
{
    public string Session {get;set;}="";
    public string Host {get;set;}="";
    public bool Ready {get;set;}
    public bool Cancelled {get;set;}
    public bool Started {get;set;}
    public Dictionary<string,SideMove> Moves {get;set;}=new();
}
public static class RhunSides
{
    public static bool Enabled(Room room)=>room.Mode=="3v3"&&room.MapPath.Equals(GameMap.Rhun.Path,StringComparison.OrdinalIgnoreCase);
    public static bool Apply(Room room,LobbyReport report,string sender)
    {
        var lobby=report.Lobby;
        if(!Enabled(room)||lobby.Map!=room.MapPath||sender!=lobby.HostAddress||
           !Guid.TryParseExact(report.Session,"N",out _)||lobby.Slots.Count>8)return false;
        var humans=lobby.Slots.Where(s=>s.State==6).ToArray();
        if(humans.Select(s=>s.Ip).Distinct().Count()!=humans.Length||
           humans.Any(s=>!room.Players.Any(p=>p.Address==s.Ip)||s.Team is < -1 or >3||s.Spot is < -1 or >7))return false;
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
            if(existing.Ready||existing.Cancelled||existing.Started)return false;
            // An explicit edit outside the planned old/intermediate/final states wins.
            foreach(var entry in existing.Moves)
            {
                var slot=humans.FirstOrDefault(s=>s.Ip==entry.Key);
                if(slot==null||slot.Observer){existing.Cancelled=true;return true;}
                var m=entry.Value;var team=slot.Team+1;var spot=slot.Spot+1;
                if(!((team==m.FromTeam&&spot==m.FromSpot)||(team==m.Team&&(spot==0||spot==m.Spot))))
                {existing.Cancelled=true;return true;}
            }
            if(existing.Moves.All(e=>humans.Any(s=>s.Ip==e.Key&&s.Team+1==e.Value.Team&&(s.Spot<0||s.Spot+1==e.Value.Spot))))
            {existing.Ready=true;return true;}
            return false;
        }
        var active=humans.Where(s=>s.Active).ToArray();
        if(host.Team is not (0 or 1)||active.Length==0||active.Any(s=>s.Team is not (0 or 1)))return false;
        var left=room.Spots.Where(s=>s.X<.5).OrderBy(s=>s.Y).ToArray();
        var right=room.Spots.Where(s=>s.X>=.5).OrderBy(s=>s.Y).ToArray();
        if(left.Length!=3||right.Length!=3)return false;
        bool swapped=room.LastRhunHost.Length>0&&room.LastRhunHost!=sender&&room.LastRhunLineup.ContainsKey(sender);
        int Group(NativeLobby.Slot s)=>swapped&&room.LastRhunLineup.TryGetValue(s.Ip,out var old)?old.Team:s.Team+1;
        int hostingGroup=Group(host);
        var plan=new SidePlan{Session=report.Session,Host=sender};
        foreach(var group in active.GroupBy(s=>Group(s)==hostingGroup?2:1))
        {
            if(group.Count()>3)return false;
            var side=group.Key==2?right:left;var free=side.ToList();
            int Original(NativeLobby.Slot slot)=>slot.Spot>=0?slot.Spot+1:
                room.LastRhunLineup.TryGetValue(slot.Ip,out var old)?old.Spot:0;
            // Current selections win; Random falls back to the last match even
            // when the same player hosts a replay. Allocate known positions before
            // new substitutes so an unknown preference cannot take a saved seat.
            foreach(var slot in group.OrderByDescending(s=>s.Spot>=0)
                .ThenByDescending(s=>room.Spots.Any(p=>p.Number==Original(s)))
                .ThenBy(s=>s.Ip,StringComparer.Ordinal))
            {
                int original=Original(slot);
                var coordinate=room.Spots.FirstOrDefault(s=>s.Number==original);
                var target=free.OrderBy(s=>coordinate==null?0:Math.Abs(s.Y-coordinate.Y)).ThenBy(s=>s.Number).First();
                free.Remove(target);
                plan.Moves[slot.Ip]=new(slot.Team+1,slot.Spot+1,group.Key,target.Number);
            }
        }
        // Apply only the shared plan; native clients clear occupied positions before
        // claiming their targets. Fellowship's roster changes atomically, not piecemeal.
        room.SidePlan=plan;
        foreach(var player in room.Players)
        {
            if(player.Spot>0)player.LastMapSpots[room.MapPath]=player.Spot;
            if(plan.Moves.TryGetValue(player.Address,out var move)){player.Team=move.Team;player.Spot=move.Spot;}
            else if(humans.Any(s=>s.Ip==player.Address&&s.Observer))player.Spot=0;
        }
        return true;
    }
}

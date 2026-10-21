namespace Fellowship;

// Learns native team positions, independent of Fellowship's room mode or capacity.
// Each helper writes only its own player; stable roster ordering gives all helpers
// the same allocation when several replacements arrive together.
public sealed class LobbyRotation
{
    readonly Dictionary<int,(int Team,string Player)> seats=new();
    Dictionary<string,NativeLobby.Slot> previous=new();
    readonly HashSet<string> candidates=new();
    readonly HashSet<string> activePlayers=new();
    public bool WasActive(NativeLobby.Slot slot)=>activePlayers.Contains(slot.Key);
    string? map,host,signature;
    uint identity;
    TimeSpan since;
    int readings;
    public void NewSession(){activePlayers.Clear();previous.Clear();candidates.Clear();Suspend();}
    public void Suspend(){signature=null;readings=0;}
    public bool Observe(NativeLobby.Snapshot snapshot,TimeSpan now)
    {
        if(map!=snapshot.Map||host!=snapshot.HostAddress)
        {
            seats.Clear();activePlayers.Clear();previous.Clear();candidates.Clear();
            map=snapshot.Map;host=snapshot.HostAddress;Suspend();
        }
        if(identity!=snapshot.Identity)
        {
            identity=snapshot.Identity;activePlayers.Clear();previous.Clear();candidates.Clear();Suspend();
        }
        var slots=snapshot.Slots.Where(s=>s.State>1).OrderBy(s=>s.Key,StringComparer.Ordinal).ToArray();
        if(slots.Select(s=>s.Key).Distinct().Count()!=slots.Length){Suspend();return false;}
        var next=string.Join("|",slots.Select(s=>$"{s.Key}:{s.State}:{s.Faction}:{s.Team}:{s.Spot}"));
        if(signature!=next){signature=next;since=now;readings=1;return false;}
        if(++readings<5||now-since<TimeSpan.FromSeconds(2))return false;
        foreach(var slot in slots)
        {
            if(slot.Active)activePlayers.Add(slot.Key);
            if(slot.Active&&slot.Team>=0&&slot.Spot>=0)
            {
                seats[slot.Spot]=(slot.Team,slot.Key);
                candidates.Remove(slot.Key);
            }
            else if(slot.Active&&slot.Spot<0)
            {
                // Clearing an established spot manually means Random; respect it.
                if(!previous.TryGetValue(slot.Key,out var old)||old.Observer)candidates.Add(slot.Key);
            }
            else candidates.Remove(slot.Key);
        }
        previous=slots.ToDictionary(s=>s.Key);
        candidates.RemoveWhere(key=>!previous.ContainsKey(key));
        return true;
    }
    public LivePlacement.Choice? Replacement(NativeLobby.Snapshot snapshot)
    {
        var occupied=snapshot.Slots.Where(s=>s.Active&&s.Spot>=0).Select(s=>s.Spot).ToHashSet();
        var available=seats.Where(s=>!occupied.Contains(s.Key)).ToDictionary();
        var waiting=snapshot.Slots.Where(s=>s.State==6&&s.Active&&s.Team>=0&&s.Spot<0&&candidates.Contains(s.Key))
            .OrderBy(s=>s.Key,StringComparer.Ordinal).ToArray();
        var assigned=new Dictionary<string,int>();
        // Free spots are suggestions for Random players, never reservations for
        // former occupants. Explicit native choices always take precedence.
        foreach(var slot in waiting)
        {
            var free=available.Where(s=>s.Value.Team==slot.Team).OrderBy(s=>s.Key).Select(s=>(int?)s.Key).FirstOrDefault();
            if(free is {} spot){assigned[slot.Key]=spot;available.Remove(spot);}
        }
        var local=snapshot.Local;
        return assigned.TryGetValue(local.Key,out var target)?new(local.Team+1,target+1,local.Color+1):null;
    }
}

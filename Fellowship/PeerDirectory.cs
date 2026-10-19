namespace Fellowship;

// Updated by the UI dispatcher. A failed probe is uncertainty, not an immediate logout.
public sealed class PeerDirectory
{
    public sealed record Entry(string Address,string Name,Room? Room,DateTimeOffset Seen,bool Reconnecting,bool GameRunning=false,int? IdleSeconds=null,bool ManualAfk=false)
    {public bool IsAfk=>IdlePresence.IsAfk(IdleSeconds,ManualAfk);public int? IdleNow(DateTimeOffset now)=>IdleSeconds is {} seconds?(int)Math.Min(604800,seconds+Math.Max(0,(now-Seen).TotalSeconds)):null;}
    readonly Dictionary<string,Entry> entries=new();
    readonly Settings? settings;
    readonly Dictionary<string,PlayerActivity> history;
    readonly HashSet<string> offlineRecorded=new();
    DateTimeOffset lastSave;
    public PeerDirectory(Settings? settings=null){this.settings=settings;history=settings?.PlayerActivityHistory??new();}
    public PlayerActivity? History(string address)=>history.GetValueOrDefault(address);
    void Save(DateTimeOffset now,bool force=false){if(force||now-lastSave>TimeSpan.FromSeconds(30)){lastSave=now;settings?.Save();}}
    void Expire(DateTimeOffset now){foreach(var entry in entries.Values.Where(e=>now-e.Seen>Grace))if(history.TryGetValue(entry.Address,out var activity)&&offlineRecorded.Add(entry.Address)){activity.OfflineAt=entry.Seen+Grace;Save(now,true);}}

    public void Clear()=>entries.Clear();
    public static readonly TimeSpan Grace=TimeSpan.FromSeconds(20);
    public void Success(string address,string name,Room? room,DateTimeOffset now,bool gameRunning=false,int? idleSeconds=null,bool manualAfk=false)
    {
        Expire(now);bool connected=!entries.TryGetValue(address,out var previous)||now-previous.Seen>Grace;
        if(!history.TryGetValue(address,out var activity))history[address]=activity=new();
        if(connected){activity.OnlineSince=now;}
        offlineRecorded.Remove(address);activity.LastSeen=now;entries[address]=new(address,name,room,now,false,gameRunning,IdlePresence.Normalize(idleSeconds),manualAfk);
        if(history.Count>1000)foreach(var key in history.OrderBy(p=>p.Value.LastSeen).Take(history.Count-1000).Select(p=>p.Key).ToArray())history.Remove(key);
        Save(now,connected);
    }
    public void Failure(string address){if(entries.TryGetValue(address,out var entry))entries[address]=entry with{Reconnecting=true};}
    public Entry? Find(string address,DateTimeOffset now){Expire(now);return entries.TryGetValue(address,out var entry)&&now-entry.Seen<=Grace?entry:null;}
    public IEnumerable<Entry> Active(DateTimeOffset now){Expire(now);return entries.Values.Where(e=>now-e.Seen<=Grace).OrderBy(e=>e.Name);}
    public IEnumerable<string> Known(DateTimeOffset now)
    {
        foreach(var key in entries.Where(p=>now-p.Value.Seen>TimeSpan.FromMinutes(10)).Select(p=>p.Key).ToArray())entries.Remove(key);
        return entries.Keys.ToArray();
    }
}

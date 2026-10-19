namespace Fellowship;

// Updated by the UI dispatcher. A failed probe is uncertainty, not an immediate logout.
public sealed class PeerDirectory
{
    public sealed record Entry(string Address,string Name,Room? Room,DateTimeOffset Seen,bool Reconnecting,bool GameRunning=false);
    readonly Dictionary<string,Entry> entries=new();
    public static readonly TimeSpan Grace=TimeSpan.FromSeconds(20);
    public void Success(string address,string name,Room? room,DateTimeOffset now,bool gameRunning=false)=>entries[address]=new(address,name,room,now,false,gameRunning);
    public void Failure(string address){if(entries.TryGetValue(address,out var entry))entries[address]=entry with{Reconnecting=true};}
    public Entry? Find(string address,DateTimeOffset now)=>entries.TryGetValue(address,out var entry)&&now-entry.Seen<=Grace?entry:null;
    public IEnumerable<Entry> Active(DateTimeOffset now)=>entries.Values.Where(e=>now-e.Seen<=Grace).OrderBy(e=>e.Name);
    public IEnumerable<string> Known(DateTimeOffset now)
    {
        foreach(var key in entries.Where(p=>now-p.Value.Seen>TimeSpan.FromMinutes(10)).Select(p=>p.Key).ToArray())entries.Remove(key);
        return entries.Keys.ToArray();
    }
}

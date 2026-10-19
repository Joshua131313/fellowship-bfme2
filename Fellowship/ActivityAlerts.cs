namespace Fellowship;

// Observe completed discovery rounds, retaining the directory's reconnect grace period.
public sealed class ActivityAlerts
{
    public sealed record Alert(string Title,string Message,bool? Online=null,string Address="",string Name="",string RoomId="");
    readonly Dictionary<string,bool> friends=new();
    readonly HashSet<string> rooms=new();
    readonly Queue<string> roomOrder=new();
    bool initialized;
    public List<Alert> Observe(Settings settings,IEnumerable<PeerDirectory.Entry> peers)
    {
        var active=peers.ToArray();var result=new List<Alert>();
        var addresses=settings.Friends.Select(f=>f.Address).ToHashSet();
        foreach(var removed in friends.Keys.Where(a=>!addresses.Contains(a)).ToArray())friends.Remove(removed);
        foreach(var friend in settings.Friends)
        {
            bool online=active.Any(p=>p.Address==friend.Address);
            if(initialized&&friends.TryGetValue(friend.Address,out var previous)&&previous!=online&&!settings.IsBlocked(friend.Address))
                result.Add(new(online?"Friend online":"Friend offline",$"{friend.Name} is now {(online?"online":"offline")}.",online,friend.Address,friend.Name));
            friends[friend.Address]=online;
        }
        foreach(var peer in active.Where(p=>!p.Reconnecting&&p.Room!=null&&!settings.IsBlocked(p.Address)))
        {
            var room=peer.Room!;
            if(!rooms.Add(room.Id))continue;
            roomOrder.Enqueue(room.Id);
            if(initialized)result.Add(new("Room created",$"{room.Host}: {room.Title}",null,peer.Address,room.Host,room.Id));
        }
        while(roomOrder.Count>2048)rooms.Remove(roomOrder.Dequeue());
        initialized=true;return result;
    }
}

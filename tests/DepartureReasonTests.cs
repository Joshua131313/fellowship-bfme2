using Fellowship;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

static class DepartureReasonTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static Settings SettingsFor(string name){var settings=Settings.InMemory();settings.Name=name;return settings;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object Gate(PeerService peer)=>typeof(PeerService).GetField("gate",Hidden)!.GetValue(peer)!;
    static Player? Member(PeerService peer,string id)=>peer.Current?.Players.FirstOrDefault(player=>player.Id==id);
    static void Hold(PeerService peer,bool held)=>typeof(PeerService).GetField("reconnectAllowed",Hidden)!.SetValue(peer,held?(Func<string,bool>)(_=>false):null);
    static void Drop(PeerService peer)=>((IDisposable)typeof(PeerService).GetField("upstream",Hidden)!.GetValue(peer)!).Dispose();
    static async Task Until(Func<bool> condition,string description)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(8);
        do{if(condition()){Console.WriteLine("PASS "+description);return;}await Task.Delay(20);}while(DateTimeOffset.UtcNow<deadline);
        throw new Exception("FAIL "+description);
    }
    static RoomDeparture Last(PeerService owner,string id)=>owner.Current!.Departures.Last(departure=>departure.PlayerId==id);
    public static async Task Run()
    {
        await ExplicitLeaveAndExit();await NetworkRecoveryAndExpiry();await RemovalAndLegacy();await PlannedHandoff();
        var room=new Room();for(int i=0;i<25;i++)room.RecordDeparture(new(){Id="player-"+i,Address="127.1.1.1",Name="Same name"},i==24?"unrecognized":"left");
        Check(room.Departures.Count==16&&room.Departures.Select(item=>item.Id).Distinct().Count()==16&&room.Departures[0].PlayerId=="player-9"&&room.Departures[^1].Reason=="unknown","Departure metadata is not bounded, uniquely identified or normalized");
        var restored=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(room))!;
        Check(restored.Departures.SequenceEqual(room.Departures),"Departure identity/cause did not survive room snapshots");
        Check(JsonSerializer.Deserialize<Room>("{}")!.Departures.Count==0&&!JsonSerializer.Deserialize<Player>("{}")!.ConnectionLost,"Legacy snapshots fabricated departure or disconnect evidence");
        MalformedSnapshots();
        Console.WriteLine("PASS authoritative departure reasons, explicit leave/exit, transport reservations/resume, timeout, removal, legacy uncertainty and planned handoff");
    }
    static void MalformedSnapshots()
    {
        var empty=JsonSerializer.Deserialize<Room>("{\"Departures\":null}")!;
        Check(empty.Departures.Count==0,"Explicit null departure list bypassed the empty legacy fallback");
        empty.RecordDeparture(new(){Id="member",Address="127.1.1.1",Name="Friend"},"left");
        Check(empty.Departures.Count==1&&Guid.TryParseExact(empty.Departures[0].Id,"N",out _),"Recording after a null-list snapshot did not produce a fresh departure UUID");
        const string json="""
            {"Departures":[null,
              {"Id":null,"PlayerId":"member","Address":"127.1.1.1","Name":"Ignored","Reason":"left"},
              {"Id":" ","PlayerId":"member","Address":"127.1.1.1","Name":"Ignored","Reason":"left"},
              {"Id":"no-identity","PlayerId":null,"Address":null,"Name":null,"Reason":null},
              {"Id":"by-player","PlayerId":"member","Address":null,"Name":null,"Reason":null},
              {"Id":"by-address","PlayerId":null,"Address":"127.1.1.1","Name":null,"Reason":"not-a-cause"},
              {"Id":"removed","PlayerId":"member","Address":"127.1.1.1","Name":"Friend","Reason":"removed"}]}
            """;
        var sanitized=JsonSerializer.Deserialize<Room>(json)!;
        Check(sanitized.Departures.Count==3&&sanitized.Departures.All(item=>item!=null&&item.Id.Length>0&&item.PlayerId!=null&&item.Address!=null&&item.Name!=null&&item.Reason!=null),"Malformed departure records or null strings reached snapshot consumers");
        Check(sanitized.Departures[0] is{PlayerId:"member",Address:"",Name:"",Reason:"unknown"}&&sanitized.Departures[1] is{PlayerId:"",Address:"127.1.1.1",Reason:"unknown"}&&sanitized.Departures[2].Reason=="removed","Departure normalization lost usable identity or trusted an invalid cause");
        var records=Enumerable.Range(0,40).Select(i=>(object?)new RoomDeparture("event-"+i,"member-"+i,"127.1.1.1","Friend","left")).ToList();records.Add(null);
        records.Add(new RoomDeparture("",null!,null!,null!,null!));
        var capped=JsonSerializer.Deserialize<Room>("{\"Departures\":"+JsonSerializer.Serialize(records)+"}")!;
        Check(capped.Departures.Count==16&&capped.Departures[0].Id=="event-24"&&capped.Departures[^1].Id=="event-39","Oversized incoming departure history was not capped to the newest valid records");
        capped.RecordDeparture(new(){Id="new-member",Address="127.1.1.2",Name="New friend"},"disconnected");
        Check(capped.Departures.Count==16&&capped.Departures[0].Id=="event-25"&&Guid.TryParseExact(capped.Departures[^1].Id,"N",out _),"Appending to normalized history broke its bound or fresh event identity");
        Console.WriteLine("PASS null/invalid departure snapshots normalize safely, retain usable identities, and cap to the newest 16 valid records");
    }
    static async Task ExplicitLeaveAndExit()
    {
        using var host=new PeerService(SettingsFor("Departure owner"));using var guest=new PeerService(SettingsFor("Departing guest"));using var observer=new PeerService(SettingsFor("Healthy observer"));
        host.Start("127.0.0.227");guest.Start("127.0.0.228");observer.Start("127.0.0.229");host.Host(new(){Capacity=3},[]);await guest.Join(host.Address);await observer.Join(host.Address);
        string id=guest.LocalId;await guest.LeaveRoom();
        await Until(()=>Member(host,id)==null&&host.Current!.Departures.Any(item=>item.PlayerId==id),"explicit room leave records a confirmed voluntary departure");
        var left=Last(host,id);Check(left.Reason=="left"&&left.Address==guest.Address&&left.Name=="Departing guest","Explicit departure lost its stable identity or cause");
        await Until(()=>observer.Current!.Departures.Any(item=>item.Id==left.Id),"departure cause is carried to the healthy observer snapshot");
        await guest.Join(host.Address);string replacement=guest.LocalId;Check(replacement!=id,"Rejoin reused the departed member ID");await guest.LeaveRoom(applicationExit:true);
        await Until(()=>Member(host,replacement)==null&&host.Current!.Departures.Any(item=>item.PlayerId==replacement),"application exit is recorded separately from voluntary room leave");
        Check(Last(host,replacement).Reason=="disconnected"&&Last(host,replacement).Id!=left.Id,"App exit reused stale voluntary departure metadata");
        var disposed=new PeerService(SettingsFor("Disposed peer"));bool disposedAlready=false;
        try
        {
            disposed.Start("127.0.0.230");await disposed.Join(host.Address);string disposedId=disposed.LocalId;disposed.Dispose();disposedAlready=true;
            await Until(()=>Member(host,disposedId)==null&&host.Current!.Departures.Any(item=>item.PlayerId==disposedId),"direct application disposal sends disconnect intent before cancelling its reader");
            Check(Last(host,disposedId).Reason=="disconnected","Application disposal was mislabeled as an explicit room leave");
        }
        finally{if(!disposedAlready)disposed.Dispose();}
    }
    static async Task NetworkRecoveryAndExpiry()
    {
        using var host=new PeerService(SettingsFor("Transport owner"));using var guest=new PeerService(SettingsFor("Temporarily disconnected"));
        host.Start("127.0.0.231");guest.Start("127.0.0.232");host.Host(new(){Capacity=2},[]);await guest.Join(host.Address);string id=guest.LocalId;
        Hold(guest,true);Drop(guest);
        await Until(()=>guest.IsReconnecting&&Member(host,id) is{Reconnecting:true,ConnectionLost:true},"genuine transport failure marks ConnectionLost without removing the member");
        Check(host.Current!.Departures.Count==0&&!guest.IsHost,"A temporary outage fabricated departure or ownership change");
        Hold(guest,false);await Until(()=>!guest.IsReconnecting&&Member(host,id) is{Reconnecting:false,ConnectionLost:false},"successful resume clears genuine loss without a departure record");
        Check(host.Current!.Departures.Count==0&&guest.LocalId==id,"Reconnect fabricated a departure or a fresh member identity");
        Hold(guest,true);Drop(guest);await Until(()=>Member(host,id) is{ConnectionLost:true},"timeout fixture is held in genuine disconnected state");
        lock(Gate(host))((Dictionary<string,DateTimeOffset>)typeof(PeerService).GetField("reconnectDeadlines",Hidden)!.GetValue(host)!)[id]=DateTimeOffset.UtcNow.AddSeconds(-1);
        await Until(()=>Member(host,id)==null&&host.Current!.Departures.Any(item=>item.PlayerId==id),"expired reservation records a disconnect instead of a voluntary leave");
        Check(Last(host,id).Reason=="disconnected"&&host.Current!.Departures.Count==1,"Reservation expiry duplicated or mislabeled its departure");
    }
    static async Task RemovalAndLegacy()
    {
        using var host=new PeerService(SettingsFor("Removal owner"));using var guest=new PeerService(SettingsFor("Removed member"));
        host.Start("127.0.0.233");guest.Start("127.0.0.234");host.Host(new(){Capacity=2},[]);await guest.Join(host.Address);string kicked=guest.LocalId;
        Hold(guest,true);Drop(guest);await Until(()=>Member(host,kicked) is{ConnectionLost:true},"removed-member fixture retains its disconnected reservation");await host.Kick(kicked);
        Check(Last(host,kicked).Reason=="removed","Kicking a disconnected member was mislabeled as transport loss");guest.Leave();Hold(guest,false);await guest.Join(host.Address);string restricted=guest.LocalId;
        var draft=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(host.Current))!;draft.FriendsOnly=true;await host.EditRoom(draft);
        Check(Member(host,restricted)==null&&Last(host,restricted).Reason=="removed","Privacy removal was mislabeled as a voluntary leave");
        draft=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(host.Current))!;draft.FriendsOnly=false;await host.EditRoom(draft);
        using var legacy=new TcpClient(new IPEndPoint(IPAddress.Parse("127.0.0.235"),0));await legacy.ConnectAsync(IPAddress.Parse(host.Address),PeerService.Port);var stream=legacy.GetStream();
        await Write(stream,"{\"Type\":\"join\",\"Name\":\"Legacy peer\"}");var welcome=await Read(stream);await Read(stream);
        await Write(stream,"{\"Type\":\"leave\"}");
        await Until(()=>host.Current!.Departures.Any(item=>item.PlayerId==welcome.Id),"old leave frames retain an explicit unknown cause");
        Check(Last(host,welcome.Id).Reason=="unknown","Legacy app disposal and room leave were guessed as voluntary leave");
    }
    static async Task PlannedHandoff()
    {
        using var original=new PeerService(SettingsFor("Original owner"));using var successor=new PeerService(SettingsFor("Next owner"));using var observer=new PeerService(SettingsFor("Migrating member"));
        original.Start("127.0.0.236");successor.Start("127.0.0.237");observer.Start("127.0.0.238");original.Host(new(){Capacity=3},[]);await successor.Join(original.Address);await observer.Join(original.Address);
        bool plannedSeen=false;successor.Changed+=()=>{if(successor.IsHost&&successor.Current!.Players.Any(player=>player.Id!="host"&&player.Reconnecting&&!player.ConnectionLost))plannedSeen=true;};
        await original.Promote(successor.LocalId);
        await Until(()=>successor.IsHost&&successor.Current!.Players.Count==3&&successor.Current.Players.All(player=>!player.Reconnecting),"planned handoff reconnects members without actual connection loss");
        Check(plannedSeen&&successor.Current!.Players.All(player=>!player.ConnectionLost)&&successor.Current.Departures.Count==0,"Intentional host migration fabricated a disconnect or departure");
        await successor.LeaveRoom(applicationExit:true);
        await Until(()=>original.IsHost&&original.Current!.Players.Count==2,"quitting owner hands the room to its confirmed successor");
        var exit=original.Current!.Departures.Single();Check(exit.Address==successor.Address&&exit.Reason=="disconnected","Quitting host's handoff snapshot lost its actual departure cause");
        await original.LeaveRoom();
        await Until(()=>observer.IsHost&&observer.Current!.Players.Count==1,"voluntarily leaving owner preserves confirmed handoff");
        Check(observer.Current!.Departures.Any(item=>item.Id==exit.Id)&&observer.Current.Departures.Last().Address==original.Address&&observer.Current.Departures.Last().Reason=="left","Handoff discarded previous departure identity or mislabeled the leaving owner");
    }
    static async Task Write(NetworkStream stream,string json)
    {
        var bytes=Encoding.UTF8.GetBytes(json);await stream.WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length)));await stream.WriteAsync(bytes);
    }
    static async Task<Wire> Read(NetworkStream stream)
    {
        using var timeout=new CancellationTokenSource(3000);var header=new byte[4];await stream.ReadExactlyAsync(header,timeout.Token);int length=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header));
        if(length is <1 or >32768)throw new InvalidDataException("Invalid test frame");var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,timeout.Token);return JsonSerializer.Deserialize<Wire>(bytes)!;
    }
}

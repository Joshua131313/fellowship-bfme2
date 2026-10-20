using System.IO;
using Fellowship;
using System.Net;
using System.Net.Sockets;
Settings MemorySettings(string name){var settings=Settings.InMemory();settings.Name=name;return settings;}
if(args.Length==0||args.Contains("--radmin-group"))
{
    var hs=MemorySettings("Group host");hs.RadminGroup="Server A";hs.RadminMembership=new("Server A",["127.0.0.122"],DateTimeOffset.UtcNow);
    using var h=new PeerService(hs);using var a=new PeerService(MemorySettings("Member A"));using var b=new PeerService(MemorySettings("Member B"));
    h.Start("127.0.0.121");a.Start("127.0.0.122");b.Start("127.0.0.123");h.Host(new(){Capacity=3});b.Host(new(){Title="Other group"});
    async Task Denied(Func<Task> action,string description){bool denied=false;try{await action();}catch(Exception e) when(e is IOException or SocketException){denied=true;}if(!denied)throw new Exception(description);}
    await Denied(()=>b.Join(h.Address),"Other group joined filtered host");await a.Join(h.Address);await Until(()=>h.Current?.Players.Count==2,"selected group member joins");
    bool delivered=false;a.PrivateMessage+=(_,_,_)=>delivered=true;await h.SendPrivate(a.Address,"Selected group message");await Until(()=>delivered,"selected group private messages work");
    await Denied(()=>h.SendPrivate(b.Address,"Must not send"),"Outgoing message escaped group");await Denied(()=>b.SendPrivate(h.Address,"Must not arrive"),"Incoming message escaped group");
    await Denied(()=>h.Join(b.Address),"Direct-IP join escaped group");
    var incoming=new HashSet<string>();h.Discovered+=ip=>incoming.Add(ip);bool outsideDiscoveredHost=false;b.Discovered+=ip=>{if(ip==h.Address)outsideDiscoveredHost=true;};
    await a.Discover(h.Address,true);await b.Discover(h.Address,true);await h.Discover(presence:true);await h.Discover(b.Address,true);await Task.Delay(250);
    if(!incoming.Contains(a.Address)||incoming.Contains(b.Address)||outsideDiscoveredHost)throw new Exception("UDP discovery leaked outside selected group");
    hs.RadminMembership=new("Server A",[b.Address],DateTimeOffset.UtcNow);h.RecheckNetworkScope();if(h.Current!=null)throw new Exception("Removed group member retained the old room");
    if((await PeerService.QueryPeer(b.Address,a.Address)).Name!="Member B")throw new Exception("Unrelated peer connection was modified");
    hs.RadminMembership=new("Server A",[b.Address],DateTimeOffset.UtcNow.AddMinutes(-3));if(h.AllowsPeer(b.Address))throw new Exception("Expired membership enabled connections");
    hs.RadminMembership=RadminMembership.Empty;if(h.AllowsPeer(b.Address))throw new Exception("Missing membership fell back to all groups");
    hs.NetworkName="Ethernet";if(!h.AllowsPeer(b.Address))throw new Exception("Radmin setting incorrectly filtered a different adapter");
    var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(hs))!;if(restored.RadminGroup!="Server A"||restored.RadminMembership!=RadminMembership.Empty)throw new Exception("Group preference/runtime membership persistence incorrect");
    Console.WriteLine("PASS Radmin group inbound/outbound messaging and joins, scoped UDP, roster changes, expired/unknown data, independent other peers and persistence");
    if(args.Contains("--radmin-group"))return;
}
if(args.Length==0||args.Contains("--player-room"))
{
    var hs=MemorySettings("Room owner");hs.Friends.Add(new("Participant","127.0.0.122"));
    using var h=new PeerService(hs);using var g=new PeerService(MemorySettings("Participant"));using var viewer=new PeerService(MemorySettings("Viewer"));
    h.Start("127.0.0.121");g.Start("127.0.0.122");viewer.Start("127.0.0.123");h.Host(new(){Capacity=4});
    await g.Join(h.Address);await Until(()=>g.Current?.Players.Count==2,"guest presence test joined");await g.SetHostGameRunning(true);
    var visible=await PeerService.QueryPeer(g.Address,viewer.Address);
    if(visible.Room?.Id!=h.Current!.Id||visible.Room.HostAddress!=h.Address||!visible.GameRunning||visible.Name!="Participant")throw new Exception("Guest presence lost player identity or actual room owner");
    if(await PeerService.Query(g.Address,viewer.Address)!=null)throw new Exception("Direct joining incorrectly treats a participant as a host");
    var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(h.Current))!;draft.FriendsOnly=true;await h.EditRoom(draft);
    if((await PeerService.QueryPeer(g.Address,viewer.Address)).Room!=null)throw new Exception("Guest presence exposed a friends-only room");
    hs.Friends.Add(new("Viewer",viewer.Address));
    if((await PeerService.QueryPeer(g.Address,viewer.Address)).Room?.Id!=h.Current.Id)throw new Exception("Authorized friend could not resolve guest room");
    hs.BlockedUsers=[new("Viewer",viewer.Address)];
    if((await PeerService.QueryPeer(g.Address,viewer.Address)).Room!=null)throw new Exception("Guest presence bypassed the room owner's block");
    g.Leave();var outside=await PeerService.QueryPeer(g.Address,viewer.Address);
    if(outside.Room!=null||!outside.GameRunning)throw new Exception("Game outside Fellowship advertised stale room or lost game status");
    Console.WriteLine("PASS participant room discovery, canonical owner, friends-only privacy, blocking and game without room");
    if(args.Contains("--player-room"))return;
}
if(args.Contains("--rhun-sides"))
{
    using var h=new PeerService(MemorySettings("Fellowship owner"));using var a=new PeerService(MemorySettings("BFME2 host"));using var b=new PeerService(MemorySettings("Participant"));
    h.Start("127.0.0.121");a.Start("127.0.0.122");b.Start("127.0.0.123");
    h.Host(new(){Capacity=3,Mode="3v3"},Enumerable.Range(0,6).Select(i=>new MapSpot(i+1,i<3?.2:.8,(i%3)*.4+.1)).ToList());
    await a.Join(h.Address);await b.Join(h.Address);
    await Until(()=>a.Current?.Players.Count==3&&b.Current?.Players.Count==3,"Rhûn participants joined");
    var lobby=new NativeLobby.Snapshot(123,h.Current!.MapPath,a.Address,[
        new(0,6,0,0,true,Address:a.Address),new(1,6,3,1,false,Address:h.Address),new(2,6,1,0,false,Address:b.Address)]);
    var report=new LobbyReport(Guid.NewGuid().ToString("N"),lobby);
    await b.SyncRhunLayout(report);
    await Task.Delay(100);
    if(h.Current.SidePlan!=null)throw new Exception("Accepted another player's native host report");
    await a.SyncRhunLayout(report);
    await Until(()=>h.Current.SidePlan?.Host==a.Address&&a.Current?.SidePlan?.Host==a.Address&&b.Current?.SidePlan?.Host==a.Address,"native host plan propagated independently of Fellowship ownership");
    foreach(var peer in new[]{h,a,b})
    {
        var room=peer.Current!;
        if(room.Players.Single(p=>p.Address==a.Address).Team!=2||room.Players.Single(p=>p.Address==h.Address).Team!=1)
            throw new Exception("Fellowship owner incorrectly determined hosting team");
        foreach(var p in room.Players)
            if((room.Spots.Single(s=>s.Number==p.Spot).X>.5)!=(p.Team==2))throw new Exception("Wrong native hosting side");
    }
    if(!h.IsHost||a.IsHost)throw new Exception("Native hosting changed Fellowship ownership");
    var cleared=lobby with{Slots=lobby.Slots.Select(s=>s with{Team=h.Current.SidePlan!.Moves[s.Ip].Team-1,Spot=-1}).ToList()};
    await a.SyncRhunLayout(report with{Lobby=cleared});
    await Until(()=>b.Current?.SidePlan?.Ready==true,"native clear phase propagated");
    var final=lobby with{Slots=lobby.Slots.Select(s=>s with{Team=h.Current.SidePlan!.Moves[s.Ip].Team-1,Spot=h.Current.SidePlan.Moves[s.Ip].Spot-1}).ToList()};
    await a.SyncRhunLayout(report with{Lobby=final,Started=true});
    await Until(()=>b.Current?.LastRhunHost==a.Address,"actual BFME2 host retained in match history");
    Console.WriteLine("PASS distinct Fellowship owner and BFME2 host, sender validation, correct sides, shared plan and match history");return;
}
if(args.Contains("--ready-request"))
{
    using var h=new PeerService(MemorySettings("Host"));using var a=new PeerService(MemorySettings("First"));using var b=new PeerService(MemorySettings("Second"));
    h.Start("127.0.0.121");a.Start("127.0.0.122");b.Start("127.0.0.123");h.Host(new(){Capacity=3});
    await a.Join(h.Address);await b.Join(h.Address);
    await Until(()=>a.Current?.Players.Count==3&&b.Current?.Players.Count==3,"ready request room joined");
    int first=0,second=0;a.ReadyRequested+=id=>{if(id==h.Current?.Id)Interlocked.Increment(ref first);};b.ReadyRequested+=id=>{if(id==h.Current?.Id)Interlocked.Increment(ref second);};
    await h.RequestReady();await Until(()=>first==1&&second==1,"ready request delivered to all participants");
    if(h.Current!.Players.Where(p=>p.Id!="host").Any(p=>p.Ready))throw new Exception("Request automatically changed consent");
    bool rejected=false;try{await a.RequestReady();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Non-host sent request");
    await a.SetReady(true);await Until(()=>h.Current!.Players.Single(p=>p.Id==a.LocalId).Ready,"ready response reaches host");
    b.Leave();await Until(()=>h.Current!.Players.Count==2,"participant left");await h.RequestReady();await Until(()=>first==2,"remaining participant receives repeat request");
    Console.WriteLine("PASS host-only requests, all recipients, explicit consent, ready response and departure");return;
}
var installation=GameInstallation.Find();
if(File.Exists(installation))
{
    var maps=GameInstallation.Maps(installation);
    if(!maps.Any(m=>m.Name=="Rhûn")||!maps.Any(m=>m.Name=="Buckland")||!maps.Any(m=>m.Name=="Grey Mountains"))throw new Exception("Installed map catalog missing expected names");
    Console.WriteLine($"PASS installation detection and {maps.Count} installed multiplayer map entries");
}
var originalPrefs="UserName = a_00s_00d_00\r\nRts:Color = 8\r\nRts:Map = old\r\n rts:map = duplicate\r\nRts:UseSystemMapDir = no\r\n";
var updatedPrefs=ProfilePreset.MapPreferences(originalPrefs);
if(!updatedPrefs.Contains("UserName = a_00s_00d_00\r\nRts:Color = 8")||updatedPrefs.Contains("old")||updatedPrefs.Contains("duplicate")||!updatedPrefs.Contains("Rts:UseSystemMapDir = yes"))throw new Exception("Map preset did not preserve unrelated settings or replace old keys");
var encodedMap=updatedPrefs.Split('\n').Single(l=>l.StartsWith("Rts:Map = "))[11..].Trim();
var decodedMap=string.Concat(encodedMap.Split('_',StringSplitOptions.RemoveEmptyEntries).Select(v=>(char)Convert.ToByte(v,16)));
if(decodedMap!=@"maps\map wor rhun\map wor rhun.map"||ProfilePreset.MapPreferences(updatedPrefs)!=updatedPrefs)throw new Exception("Invalid map encoding or non-idempotent preference update");
Console.WriteLine("PASS Rhûn map encoding, preservation, duplicate replacement and idempotence");
if (args.Length == 2 && args[0] == "--ui-peer")
{
    using var uiGuest = new PeerService(new() { Name = "Test player" });
    await uiGuest.Join(args[1]);
    await Task.Delay(300);
    await uiGuest.Say("Two-peer smoke test: room chat delivered.");
    await uiGuest.SetReady(true);
    await uiGuest.SendPrivate(args[1], "Private chat delivery verified from the local test peer.");
    Console.WriteLine("UI peer connected for 45 seconds. No game launch handler is installed.");
    await Task.Delay(45000);
    return;
}
static async Task Until(Func<bool> test, string description)
{
    for (int i = 0; i < 100; i++) { if (test()) { Console.WriteLine("PASS " + description); return; } await Task.Delay(50); }
    throw new Exception("FAIL " + description);
}
using(var switchPeer=new PeerService(MemorySettings("Switch test")))
{
    switchPeer.Start("127.0.0.21");switchPeer.Start("127.0.0.22");
    if((await PeerService.QueryPeer("127.0.0.22")).Name!="Switch test")throw new Exception("New network listener not active");
    using(var released=new TcpListener(IPAddress.Parse("127.0.0.21"),PeerService.Port)){released.Start();released.Stop();}
    var discoveredSwitch=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    switchPeer.Discovered+=_=>discoveredSwitch.TrySetResult(true);
    using var probe=new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.23"),0));
    await probe.SendAsync(System.Text.Encoding.ASCII.GetBytes("FELLOWSHIP_PRESENCE_1"),new IPEndPoint(IPAddress.Parse("127.0.0.22"),PeerService.DiscoveryPort));
    await discoveredSwitch.Task.WaitAsync(TimeSpan.FromSeconds(3));
    using(var occupiedSwitch=new TcpListener(IPAddress.Parse("127.0.0.24"),PeerService.Port))
    {occupiedSwitch.Start();try{switchPeer.Start("127.0.0.24");throw new Exception("Expected bind failure");}catch(SocketException){} }
    if((await PeerService.QueryPeer("127.0.0.22")).Name!="Switch test")throw new Exception("Failed switch lost working listener");
    switchPeer.Start("127.0.0.21");
    if((await PeerService.QueryPeer("127.0.0.21")).Name!="Switch test")throw new Exception("Repeated network switch failed");
    Console.WriteLine("PASS live network rebinding, UDP discovery, old port release and failed-switch rollback");
}
// A failed bind must not poison retries or advertise an address that never bound.
using(var occupied=new TcpListener(IPAddress.Parse("127.0.0.3"),PeerService.Port))
using(var retryPeer=new PeerService(new(){Name="Retry test"}))
{
    occupied.Start();
    try{retryPeer.Start("127.0.0.3");throw new Exception("Expected port conflict");}
    catch(SocketException e) when(e.SocketErrorCode==SocketError.AddressAlreadyInUse){}
    if(retryPeer.Address!="")throw new Exception("Failed listener advertised an address");
    occupied.Stop();
    retryPeer.Start("127.0.0.3");
    retryPeer.Host(new(){Title="Recovered listener"});
    if((await PeerService.Query("127.0.0.3"))?.Title!="Recovered listener")throw new Exception("Listener did not recover after a port conflict");
    Console.WriteLine("PASS listener recovers after port conflict");
}
// Public discovery and friend-only enforcement use the actual connection address.
var privacySettings=new Settings{Name="Private host"};
using(var privateHost=new PeerService(privacySettings))
using(var allowed=new PeerService(new(){Name="Allowed"}))
using(var outsider=new PeerService(new(){Name="Outsider"}))
{
    privateHost.Start("127.0.0.4");allowed.Start("127.0.0.5");outsider.Start("127.0.0.6");
    privateHost.Host(new(){Title="Secret room",FriendsOnly=true});
    if(await PeerService.Query("127.0.0.4","127.0.0.6")!=null)throw new Exception("Private room leaked to stranger");
    try{await outsider.Join("127.0.0.4");throw new Exception("Private room accepted stranger");}catch(IOException){}
    privacySettings.Friends.Add(new("Allowed","127.0.0.5"));
    if((await PeerService.Query("127.0.0.4","127.0.0.5"))?.Title!="Secret room")throw new Exception("Friend cannot see private room");
    await allowed.Join("127.0.0.4");await Until(()=>allowed.Current?.Players.Count==2,"private friend join and live roster");
    var edited=new Room{Title="Updated",FriendsOnly=false,Capacity=4,Map="Buckland",MapPath=@"maps\map wor buckland\map wor buckland.map",Match=new(){Resources=2000,CommandPoints=400,CustomHeroes=true}};
    await privateHost.EditRoom(edited);
    await Until(()=>allowed.Current?.Match.Resources==2000&&allowed.Current.Title=="Updated","typed rules and room edits synchronize");
    string found="";outsider.Discovered+=ip=>found=ip;
    // An offline peer sends an ICMP port-unreachable response on Windows. It must
    // not permanently terminate discovery of every other room.
    await outsider.Discover("127.0.0.7");await Task.Delay(250);
    await outsider.Discover("127.0.0.4");await Until(()=>found=="127.0.0.4","public UDP discovery without friendship");
    await outsider.Join("127.0.0.4");
    await Until(()=>outsider.Current?.Players.Count==3&&allowed.Current?.Players.Count==3,"public stranger joins and all roster counts update");
    var invalidEdit=new Room{Title="Too small",Capacity=2};
    try{await privateHost.EditRoom(invalidEdit);throw new Exception("Room capacity allowed below roster size");}
    catch(InvalidOperationException){}
    if(privateHost.Current?.Title!="Updated")throw new Exception("Invalid edit changed the room");
    edited.FriendsOnly=true;await privateHost.EditRoom(edited);
    await Until(()=>outsider.Current==null&&allowed.Current?.Players.Count==2,"switching to private removes nonfriends and keeps friends");
    if(await PeerService.Query("127.0.0.4","127.0.0.6")!=null)throw new Exception("Privacy change failed");
    privateHost.Leave();
    if(await PeerService.Query("127.0.0.4","127.0.0.5")!=null)throw new Exception("Closed room still advertised");
    Console.WriteLine("PASS private visibility, denied join, edits, public discovery, closed room removal");
}
using var host = new PeerService(new() { Name = "Host" });
using var guest = new PeerService(new() { Name = "Guest" });
var defaults = new Room();
if (defaults.Map != "Rhûn" || defaults.Patch != "1.00" || defaults.Capacity != 6 || !defaults.Rules.Contains("4000") || !defaults.Rules.Contains("2x")) throw new Exception("Incorrect 1.00 defaults");
if(!defaults.Rules.Contains("no Ring Heroes")||!defaults.Rules.Contains("no Custom Heroes"))throw new Exception("Hero defaults must be disabled");
Console.WriteLine("PASS Rhun / 3v3 / 4k / 2x / heroes off / 1.00 defaults");
try { host.Host(new() { Patch = "1.09" }); throw new Exception("Unsupported patch accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS unsupported patch rejected"); }
host.Start("127.0.0.1"); guest.Start("127.0.0.2");
host.Host(new() { Title = "Integration room", Capacity = 2 });
var advertised = await PeerService.Query("127.0.0.1");
if (advertised?.Title != "Integration room") throw new Exception("Discovery failed");
Console.WriteLine("PASS direct room discovery");
await guest.Join("127.0.0.1");
string? requestedRoom=null;guest.ReadyRequested+=id=>requestedRoom=id;
await host.RequestReady();await Until(()=>requestedRoom==host.Current?.Id,"host ready request delivered to guest");
try{await guest.RequestReady();throw new Exception("Guest sent host-only ready request");}catch(InvalidOperationException){}

await Until(() => guest.Current?.Players.Count == 2, "join and roster synchronization");
if (await PeerService.Query("127.0.0.2") != null) throw new Exception("Guest advertised a room it does not host");
Console.WriteLine("PASS only hosts advertise rooms");
using var third = new PeerService(new() { Name = "Third" });
try { await third.Join("127.0.0.1"); throw new Exception("Capacity not enforced"); }
catch (IOException e) when (e.Message == "The room is full.") { Console.WriteLine("PASS full room rejection"); }
string roomMessage = "", dm = "";
guest.Chat += s => roomMessage = s;
await host.Say("hello room");
await Until(() => roomMessage.Contains("hello room"), "room chat delivery");
host.PrivateMessage += (_, _, s) => dm = s;
await guest.SendPrivate("127.0.0.1", "private hello");
await Until(() => dm == "private hello", "acknowledged private message");
try { await host.Launch(); throw new Exception("Unready launch allowed"); }
catch (InvalidOperationException) { Console.WriteLine("PASS unready launch blocked"); }
await guest.SetReady(true);
await Until(() => host.Current!.Players.All(p => p.Ready), "ready state delivery");
await host.UpdateRoom("Buckland", "1.00", "No custom heroes");
await Until(() => guest.Current?.Map == "Buckland" && guest.Current.Players.Single(p => p.Id != "host").Ready, "settings changes preserve readiness");
await guest.SetReady(true);
await Until(() => host.Current!.Players.All(p => p.Ready), "renewed readiness");
int launches = 0; guest.LaunchRequested += () => launches++;
await host.Launch();
await Until(() => launches == 1, "host launch signal");
string launchReport="";host.Notice+=text=>launchReport=text;
guest.LaunchHandler=()=>Task.FromResult("Could not launch: test permission cancellation");
await host.Launch();
await Until(()=>launchReport.Contains("test permission cancellation"),"guest launch failure reported to host");
guest.LaunchHandler=null;

await Until(()=>guest.Current!.Players.All(p=>p.Ready),"launch preserves guest readiness");
if(host.Current!.Players.Any(p=>!p.Ready))throw new Exception("Launching reset readiness");
await host.SetHostGameRunning(true);
await Until(() => guest.Current?.Phase == "Host in BFME2", "running status broadcast");
await host.SetHostGameRunning(false);
await Until(() => guest.Current?.Phase == "Gathering", "game exit status broadcast");
guest.Leave();
await Until(() => host.Current!.Players.Count == 1, "disconnect releases seat");
await guest.Join("127.0.0.1");
await Until(() => guest.Current?.Players.Count == 2, "rejoin after launch");
if (launches != 1) throw new Exception("Rejoin unexpectedly launched game");
Console.WriteLine("PASS reconnect does not auto-launch");
using (var bad = new TcpClient())
{
    await bad.ConnectAsync(IPAddress.Loopback, PeerService.Port);
    await bad.GetStream().WriteAsync(new byte[] { 127, 255, 255, 255 });
}
await Task.Delay(100);
if ((await PeerService.Query("127.0.0.1")) == null) throw new Exception("Invalid frame killed listener");
Console.WriteLine("PASS oversized frame rejected; listener survives");
host.Leave();
await Until(() => guest.IsHost, "lost host promotes designated successor");
guest.Leave();
// A synthetic BIG archive keeps placement tests independent of installed game files.
var fixture=Path.Combine(Path.GetTempPath(),"Fellowship-placement-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
try
{
    var ini=System.Text.Encoding.ASCII.GetBytes("MapCache maps_5Ctest_2Emap\n extentMin = X:0 Y:0 Z:0\n extentMax = X:100 Y:100 Z:0\n numPlayers = 2\n Player_1_Start = X:20 Y:30 Z:0\n Player_2_Start = X:80 Y:70 Z:0\nEND\n");
    var entry=System.Text.Encoding.ASCII.GetBytes("maps\\mapcache.ini\0");
    using(var output=File.Create(Path.Combine(fixture,"Maps.big")))
    using(var writer=new BinaryWriter(output))
    {
        void Big(int value){var bytes=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes,value);writer.Write(bytes);}
        writer.Write(System.Text.Encoding.ASCII.GetBytes("BIGF"));Big(24+entry.Length+ini.Length);Big(1);Big(24+entry.Length);Big(24+entry.Length);Big(ini.Length);writer.Write(entry);writer.Write(ini);
    }
    var savedSettings=Settings.InMemory();savedSettings.GamePath=Path.Combine(fixture,"lotrbfme2.exe");
    using(var savedHost=new PeerService(savedSettings))
    {
        savedSettings.LastHostedSetup=new("Other","maps\\other.map",6,4000,200,false,false,3,2);
        savedHost.Host(new(){MapPath=@"maps\test.map"});
        if(savedHost.Current!.Players[0].Spot!=0||savedHost.Current.Players[0].Team!=3)throw new Exception("A saved spot from another map was restored");
        savedSettings.LastHostedSetup=savedSettings.LastHostedSetup with{MapPath=@"maps\test.map",Spot=2};
        savedHost.Host(new(){MapPath=@"maps\test.map"});
        if(savedHost.Current!.Players[0].Spot!=2)throw new Exception("Valid same-map saved spot was lost");
        savedSettings.LastHostedSetup=savedSettings.LastHostedSetup with{Spot=8};
        savedHost.Host(new(){MapPath=@"maps\test.map"});
        if(savedHost.Current!.Players[0].Spot!=0)throw new Exception("Invalid saved spot was restored");
        savedSettings.LastHostedSetup=savedSettings.LastHostedSetup with{Spot=2,Color=4};
        savedSettings.PreferredMapSpots[@"maps\test.map"]=1;savedSettings.PreferredColor=7;
        savedHost.Host(new(){MapPath=@"maps\test.map"});
        if(savedHost.Current!.Players[0].Spot!=1||savedHost.Current.Players[0].Color!=7||savedHost.Current.Players[0].Team!=3)throw new Exception("Explicit hosting preferences must override last selections");
        savedHost.Current.Players[0].Spot=2;savedSettings.RememberHost(savedHost.Current);
        savedHost.Host(new(){MapPath=@"maps\test.map"});
        if(savedHost.Current!.Players[0].Spot!=1||savedSettings.PreferredSpot(@"maps\test.map")!=1)throw new Exception("Last host selection overwrote explicit preference");
        Console.WriteLine("PASS saved spots are map-specific and validated against current map metadata");
    }
    using var placementHost=new PeerService(new(){Name="Placement host",GamePath=Path.Combine(fixture,"lotrbfme2.exe")});
    using var first=new PeerService(MemorySettings("First"));using var second=new PeerService(MemorySettings("Second"));
    placementHost.Start("127.0.0.10");placementHost.Host(new(){Map="Test",MapPath=@"maps\test.map"});
    await first.Join("127.0.0.10");await second.Join("127.0.0.10");
    await Until(()=>first.Current?.Players.Count==3&&second.Current?.Spots.Count==2,"map spots synchronized to guests");
    int firstRequests=0,secondRequests=0;first.ReadyRequested+=_=>Interlocked.Increment(ref firstRequests);second.ReadyRequested+=_=>Interlocked.Increment(ref secondRequests);
    await placementHost.RequestReady();await Until(()=>firstRequests==1&&secondRequests==1,"ready request reaches every participant");
    if(placementHost.Current!.Players.Where(p=>p.Id!="host").Any(p=>p.Ready))throw new Exception("Ready request changed consent automatically");

    async Task<bool> Claim(PeerService peer){try{await peer.SelectPlacement(1,1);return true;}catch(InvalidOperationException){return false;}}
    var claims=await Task.WhenAll(Claim(first),Claim(second));
    if(claims.Count(x=>x)!=1||placementHost.Current!.Players.Count(p=>p.Spot==1)!=1)throw new Exception("Conflicting spot claims accepted");
    Console.WriteLine("PASS simultaneous spot claims resolve to one player");
    var winner=claims[0]?first:second;var loser=claims[0]?second:first;
    await loser.SelectPlacement(2,2);
    await Until(()=>first.Current!.Players.Any(p=>p.Spot==2&&p.Team==2),"spot and team broadcast");
    await first.SetReady(true);await second.SetReady(true);await Until(()=>placementHost.Current!.Players.All(p=>p.Ready),"placement players ready");
    async Task<bool> ClaimColor(PeerService peer){try{await peer.SelectPlacement(color:3);return true;}catch(InvalidOperationException){return false;}}
    var colorClaims=await Task.WhenAll(ClaimColor(first),ClaimColor(second));
    if(colorClaims.Count(x=>x)!=1||placementHost.Current!.Players.Count(p=>p.Color==3)!=1)throw new Exception("Concurrent duplicate colors accepted");
    await Until(()=>first.Current!.Players.Count(p=>p.Color==3)==1&&second.Current!.Players.Count(p=>p.Color==3)==1,"color broadcasts to both guests");
    try{await loser.SelectPlacement(color:11);throw new Exception("Invalid color accepted");}catch(InvalidOperationException){}
    await first.SelectPlacement(color:0);await second.SelectPlacement(color:0);
    if(placementHost.Current!.Players.Any(p=>!p.Ready||p.Color!=0))throw new Exception("Random colors or readiness preservation failed");
    Console.WriteLine("PASS simultaneous color claims, shared random colors, bounds and readiness preservation");
    await winner.SelectPlacement(team:2);
    await Until(()=>placementHost.Current!.Players.All(p=>p.Ready),"team changes preserve everyone’s readiness");
    try{await loser.SelectPlacement(8);throw new Exception("Invalid map spot accepted");}catch(InvalidOperationException){}
    try{await loser.SelectPlacement(team:5);throw new Exception("Invalid team accepted");}catch(InvalidOperationException){}
    await loser.SelectPlacement(team:4);
    if(placementHost.Current!.Players.Single(p=>p.Id==loser.LocalId).Team!=4)throw new Exception("Team 4 rejected");
    await winner.SelectPlacement(0);await loser.SelectPlacement(1);
    if(placementHost.Current!.Players.Any(p=>!p.Ready))throw new Exception("Spot changes reset readiness");
    var oldMap=placementHost.Current.MapPath;
    try{await winner.SelectPlacement(2,expectedMap:"maps\\stale.map");throw new Exception("Stale-map selection accepted");}catch(InvalidOperationException){}
    if(placementHost.Current.Players.Single(p=>p.Id==winner.LocalId).Spot!=0)throw new Exception("Rejected stale request mutated placement");
    Console.WriteLine("PASS spot edits preserve readiness; stale-map imports are rejected without mutation");
    if(placementHost.Current!.Players.Single(p=>p.Id==loser.LocalId).Spot!=1)throw new Exception("Released spot unavailable");
    loser.Leave();await Until(()=>placementHost.Current!.Players.Count==2,"disconnect releases claimed spot");
    await winner.SelectPlacement(1);
    await placementHost.EditRoom(new(){Map="Other",MapPath=@"maps\other.map"});
    await Until(()=>winner.Current?.Spots.Count==0&&winner.Current.Players.All(p=>p.Spot==0),"map changes clear spots and retain teams");
    if(placementHost.Current!.Players.Any(p=>!p.Ready))throw new Exception("Map edit reset readiness");
    if(winner.Current!.Players.Single(p=>p.Id==winner.LocalId).Team!=2)throw new Exception("Map change lost team");
    Console.WriteLine("PASS invalid selections rejected, releases, disconnect and map changes");
}
finally{Directory.Delete(fixture,true);}
using(var original=new PeerService(new(){Name="Original"}))
using(var successor=new PeerService(MemorySettings("Successor")))
using(var remaining=new PeerService(MemorySettings("Remaining")))
{
    original.Start("127.0.0.20");successor.Start("127.0.0.21");remaining.Start("127.0.0.22");
    original.Host(new(){Title="Handoff test",Match=new(){Resources=2000,CommandPoints=400}});
    await successor.Join("127.0.0.20");await remaining.Join("127.0.0.20");
    await Until(()=>remaining.Current?.Players.Count==3&&successor.Current?.Players.Count==3,"handoff roster ready");
    await successor.SelectPlacement(team:3,color:2);await remaining.SelectPlacement(team:4,color:3);
    await successor.SetReady(true);await remaining.SetReady(true);
    await Until(()=>original.Current!.Players.All(p=>p.Ready),"handoff ready states replicated");
    var roomId=original.Current!.Id;
    try{await remaining.Kick("host");throw new Exception("Guest kick allowed");}catch(InvalidOperationException){}
    try{await remaining.Promote("host");throw new Exception("Guest promotion allowed");}catch(InvalidOperationException){}
    bool advertisedBeforeLeaving=false;
    original.HostTransferred+=(ip,name,room)=>advertisedBeforeLeaving=original.Current!=null&&room?.HostAddress==ip&&ip==successor.Address;
    await original.Promote(successor.LocalId);
    if(!advertisedBeforeLeaving)throw new Exception("Confirmed successor room was not delivered before leaving");
    await Until(()=>successor.IsHost&&!original.IsHost&&original.Current?.Players.Count==3&&remaining.Current?.Host=="Successor","manual promotion reconnects all players");
    if(successor.Current?.Id!=roomId||successor.Current.Match.Resources!=2000||successor.Current.Players.Single(p=>p.Name=="Remaining").Team!=4||successor.Current.Players.Single(p=>p.Id=="host").Team!=3)throw new Exception("Handoff lost room/rules/teams");
    if(!successor.Current!.Players.All(p=>p.Ready)||successor.Current.Players.Single(p=>p.Name=="Remaining").Color!=3||successor.Current.Players.Single(p=>p.Id=="host").Color!=2)throw new Exception("Handoff reset readiness or colors");
    if(await PeerService.Query("127.0.0.20")!=null)throw new Exception("Former host still advertises");
    await successor.LeaveRoom();
    await Until(()=>original.IsHost&&remaining.Current?.Host=="Original"&&original.Current?.Players.Count==2&&successor.Current==null,"host leave promotes next player");
    if(!original.Current!.Players.All(p=>p.Ready)||original.Current.Players.Single(p=>p.Name=="Remaining").Team!=4||original.Current.Players.Single(p=>p.Name=="Remaining").Color!=3)throw new Exception("Leaving host reset player state");
    await original.Kick(remaining.LocalId);
    await Until(()=>remaining.Current==null&&original.Current?.Players.Count==1,"host kick disconnects only selected guest");
    await original.LeaveRoom();if(original.Current!=null)throw new Exception("Last host leave failed");
    Console.WriteLine("PASS host-only permissions, room preservation, promotion, leave and kick");
}
using(var readyHost=new PeerService(new(){Name="Host"}))
using(var noListener=new PeerService(new(){Name="Needs listener"}))
{
    readyHost.Start("127.0.0.30");readyHost.Host(new());await noListener.Join("127.0.0.30");
    await Until(()=>readyHost.Current?.Players.Count==2,"handoff preflight roster");
    try{await readyHost.LeaveRoom();throw new Exception("Handoff accepted a peer without a listener");}catch(InvalidOperationException){}
    if(!readyHost.IsHost||readyHost.Current?.Players.Count!=2)throw new Exception("Failed preflight discarded room");
    Console.WriteLine("PASS failed handoff preflight preserves existing room");
}
var latest=new Room{Id="ordered",Revision=10};
if(Room.AcceptsUpdate(latest,new(){Id="ordered",Revision=9})||!Room.AcceptsUpdate(latest,new(){Id="ordered",Revision=11})||!Room.AcceptsUpdate(latest,new(){Id="new-room",Revision=0}))throw new Exception("Room revision guard failed");
Console.WriteLine("PASS stale room snapshots cannot overwrite newer selections");
Console.WriteLine("ALL PROTOCOL CHECKS PASSED");


using(var presenceHost=new PeerService(new(){Name="Presence host"}))
{
    presenceHost.Start("127.0.0.39");presenceHost.Host(new());
    await presenceHost.SetHostGameRunning(true);
    if(presenceHost.Current!.Phase!="Host in BFME2")throw new Exception("Running game not reflected");
    await presenceHost.SetHostGameRunning(false);
    if(presenceHost.Current.Phase!="Gathering")throw new Exception("Game exit left stale status");
    await presenceHost.Launch();
    if(presenceHost.Current.Phase!="Gathering")throw new Exception("Launch request falsely marks game running");
    Console.WriteLine("PASS game exit clears presence and launch requests do not imply running game");
}

var directory=new PeerDirectory();var now=DateTimeOffset.UtcNow;
directory.Success("26.1.1.1","Idle friend",new Room(),now);
directory.Failure("26.1.1.1");
if(directory.Find("26.1.1.1",now.AddSeconds(5)) is not {Reconnecting:true,Room:not null})throw new Exception("Transient failure removed peer/room");
if(directory.Find("26.1.1.1",now.AddSeconds(21))!=null)throw new Exception("Offline presence never expires");
if(!directory.Known(now.AddSeconds(21)).Contains("26.1.1.1"))throw new Exception("Offline peer lost retry address");
directory.Success("26.1.1.1","Idle friend",null,now.AddSeconds(22));
if(directory.Find("26.1.1.1",now.AddSeconds(22)) is not {Reconnecting:false,Room:null})throw new Exception("Confirmed room closure not immediate");
Console.WriteLine("PASS presence grace, expiry, retry retention, recovery and immediate room closure");
using(var idle=new PeerService(new(){Name="Idle player"}))
using(var seeker=new PeerService(new(){Name="Seeker"}))
{
 idle.Start("127.0.0.40");seeker.Start("127.0.0.41");bool seen=false;
 seeker.Discovered+=ip=>{if(ip=="127.0.0.40")seen=true;};
 await seeker.Discover("127.0.0.40",presence:true);
 await Until(()=>seen,"idle non-host presence discovery");
 var peer=await PeerService.QueryPeer("127.0.0.40","127.0.0.41");
 if(peer.Name!="Idle player"||peer.Room!=null)throw new Exception("Idle identity query failed");
 Console.WriteLine("PASS idle player identity without a room or chat activity");
}

using(var isolatedHost=new PeerService(new(){Name="Large room"}))
{
 isolatedHost.Start("127.0.0.50");isolatedHost.Host(new(){Capacity=64});
 var crowd=new List<PeerService>();
 try
 {
  for(int i=0;i<9;i++){var peer=new PeerService(new(){Name="Guest "+i});crowd.Add(peer);peer.Start("127.0.0."+(51+i));await peer.Join("127.0.0.50");}
  await Until(()=>crowd.All(p=>p.Current?.Players.Count==10),"ten-player room roster");
  if((await PeerService.Query("127.0.0.50","127.0.0.60"))?.Players.Count!=10)throw new Exception("Large room discovery failed");
  bool received=false;isolatedHost.PrivateMessage+=(_,_,text)=>received=text=="still connected";
  await crowd[0].SendPrivate("127.0.0.50","still connected");await Until(()=>received,"private chat remains available in isolated room");
  try{await isolatedHost.EditRoom(new(){Capacity=65});throw new Exception("Invalid room capacity accepted");}catch(InvalidOperationException){}
 }
 finally{foreach(var peer in crowd)peer.Dispose();}
 Console.WriteLine("PASS larger room capacity, discovery and chat");
}

using(var blockedHost=new PeerService(Settings.InMemory()))
using(var blockedGuest=new PeerService(Settings.InMemory()))
{
 blockedHost.Start("127.0.0.70");blockedGuest.Start("127.0.0.71");blockedHost.Host(new(){Title="Block test"});
 await blockedGuest.Join("127.0.0.70");await Until(()=>blockedGuest.Current?.Players.Count==2,"block test joined");
 bool delivered=false,discoveredBlocked=false;blockedHost.PrivateMessage+=(_,_,_)=>delivered=true;
 blockedGuest.Discovered+=ip=>{if(ip=="127.0.0.70")discoveredBlocked=true;};
 await blockedHost.SetBlocked("Guest","127.0.0.71",true);
 await Until(()=>blockedGuest.Current==null&&blockedHost.Current!.Players.Count==1,"blocking disconnects existing guest");
 async Task Denied(Func<Task> action){bool denied=false;try{await action();}catch(Exception e) when(e is IOException or SocketException or InvalidOperationException){denied=true;}if(!denied)throw new Exception("Blocked action succeeded");}
 if((await PeerService.QueryPeer("127.0.0.70","127.0.0.71")).Room!=null)throw new Exception("Blocked peer can see host room");
 await Denied(()=>blockedGuest.Join("127.0.0.70"));await Denied(()=>blockedGuest.SendPrivate("127.0.0.70","blocked message"));
 await Denied(()=>blockedHost.SendPrivate("127.0.0.71","outgoing blocked"));
 await blockedGuest.Discover("127.0.0.70",presence:true);await Task.Delay(250);
 if(delivered||discoveredBlocked)throw new Exception("Blocked peer received discovery or delivered DM");
 await blockedHost.SetBlocked("Guest","127.0.0.71",false);await blockedGuest.Join("127.0.0.70");
 await Until(()=>blockedGuest.Current?.Players.Count==2,"unblock restores joining");
 await blockedGuest.SendPrivate("127.0.0.70","allowed");if(!delivered)throw new Exception("Unblock failed to restore messaging");
 Console.WriteLine("PASS persisted blocking enforcement: existing guest removal, discovery, query, joins, both DM directions and unblock");
}

using(var sharedHost=new PeerService(Settings.InMemory()))
using(var blockingGuest=new PeerService(Settings.InMemory()))
using(var otherGuest=new PeerService(Settings.InMemory()))
{
 sharedHost.Start("127.0.0.72");blockingGuest.Start("127.0.0.73");otherGuest.Start("127.0.0.74");sharedHost.Host(new(){Title="Shared",Capacity=6});
 await blockingGuest.Join("127.0.0.72");await otherGuest.Join("127.0.0.72");
 await Until(()=>blockingGuest.Current?.Players.Count==3,"shared room roster");
 await blockingGuest.SetBlocked("Other","127.0.0.74",true);
 await Until(()=>blockingGuest.Current?.Players.Count==3&&sharedHost.Current?.Players.Count==3,"blocked guests can share another host room");
 bool hidden=false;blockingGuest.RoomChat+=(text,blocked)=>{if(text.Contains("hidden test"))hidden=blocked;};
 await otherGuest.Say("hidden test");await Until(()=>hidden,"blocked room message marked for reveal");
 await blockingGuest.Join("127.0.0.72");await Task.Delay(200);
 if(blockingGuest.Current==null)throw new Exception("Shared room rejected blocked co-player");
 Console.WriteLine("PASS shared-room blocking hides messages without removing players");
}

var nicknameSettings=Settings.InMemory();
using(var nameHost=new PeerService(nicknameSettings))
using(var nameGuest=new PeerService(Settings.InMemory()))
{
 nameHost.Start("127.0.0.75");nameGuest.Start("127.0.0.76");nameHost.Host(new(){Title="Names"});await nameGuest.Join("127.0.0.75");
 await Until(()=>nameGuest.Current?.Players.Count==2,"nickname room joined");await nameGuest.SetReady(true);
 await nameHost.SetNickname("New Host");await nameGuest.SetNickname("New Guest");
 await Until(()=>nameHost.Current!.Players.Any(p=>p.Name=="New Guest")&&nameGuest.Current?.Host=="New Host","nicknames update across current room");
 if(!nameHost.Current!.Players.All(p=>p.Ready))throw new Exception("Nickname changed readiness");
 if(System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(nicknameSettings))!.Name!="New Host")throw new Exception("Nickname did not persist");
 if((await PeerService.QueryPeer("127.0.0.75","127.0.0.76")).Name!="New Host")throw new Exception("Discovery nickname stale");
 Console.WriteLine("PASS nickname persistence, live guest/host roster and discovery without readiness reset");
}

using(var failedHost=new PeerService(new(){Name="Failed host"}))
using(var recoveryHost=new PeerService(new(){Name="Recovery host"}))
using(var recoveryGuest=new PeerService(MemorySettings("Recovery guest")))
{
 failedHost.Start("127.0.0.81");recoveryHost.Start("127.0.0.82");recoveryGuest.Start("127.0.0.83");
 failedHost.Host(new(){Title="Crash recovery",Match=new(){Resources=2000,CommandPoints=400}});
 await recoveryHost.Join(failedHost.Address);await recoveryGuest.Join(failedHost.Address);
 await recoveryGuest.SelectPlacement(team:3,color:4);
 await Until(()=>recoveryHost.Current?.Players.Count==3,"recovery roster replicated");
 var id=failedHost.Current!.Id;
 failedHost.Leave();
 await Until(()=>recoveryHost.IsHost&&recoveryGuest.Current?.HostAddress==recoveryHost.Address&&recoveryHost.Current?.Players.Count==2,"lost-host recovery reconnects surviving guest");
 if(recoveryHost.Current!.Id!=id||recoveryHost.Current.Match.Resources!=2000||recoveryHost.Current.Players.Single(p=>p.Name=="Recovery guest").Team!=3)throw new Exception("Recovery lost room selections");
 await recoveryHost.Kick(recoveryGuest.LocalId);
 await Until(()=>recoveryGuest.Current==null,"kicked guest must not recover or recreate room");
 recoveryHost.Leave();
}

using(var seriesHost=new PeerService(new(){GamePath=installation}))
using(var seriesGuest=new PeerService(new(){GamePath=installation}))
{
 seriesHost.Start("127.0.0.90");seriesGuest.Start("127.0.0.91");seriesHost.Host(new(){Title="Series"});await seriesGuest.Join(seriesHost.Address);
 var setup=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(seriesGuest.Current))!;
 setup.Match=new(){Resources=2000,CommandPoints=400};
 await seriesGuest.SyncNativeRules(setup);
 await Until(()=>seriesHost.Current?.Match.Resources==2000&&seriesGuest.Current?.Match.CommandPoints==400,"native-host guest rules reach Fellowship host and room");
 setup.Match=new(){Resources=1000,CommandPoints=100};await seriesGuest.SyncNativeRules(setup);await Task.Delay(200);
 if(seriesHost.Current!.Match.Resources!=2000)throw new Exception("Stale native rules replaced current setup");
 seriesGuest.Leave();seriesHost.Leave();
}

var prefA=Settings.InMemory();prefA.PreferredColor=2;prefA.PreferredMapSpots[new Room().MapPath]=1;
var prefB=Settings.InMemory();prefB.PreferredColor=2;prefB.PreferredMapSpots[new Room().MapPath]=1;
var roundTrip=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(prefA))!;
if(roundTrip.PreferredColor!=2||roundTrip.PreferredSpot(new Room().MapPath)!=1||roundTrip.PreferredSpot("other map")!=0)throw new Exception("Preferences persistence or per-map lookup failed");
using(var preferenceHost=new PeerService(Settings.InMemory()))
using(var preferenceA=new PeerService(prefA))
using(var preferenceB=new PeerService(prefB))
{
 preferenceHost.Start("127.0.0.94");preferenceA.Start("127.0.0.95");preferenceB.Start("127.0.0.96");preferenceHost.Host(new(){Title="Preferences"});
 preferenceHost.Current!.Spots=[new(1,.2,.2),new(2,.8,.8)];
 await Task.WhenAll(preferenceA.Join(preferenceHost.Address),preferenceB.Join(preferenceHost.Address));
 if(preferenceHost.Current.Players.Count(p=>p.Spot==1)!=1||preferenceHost.Current.Players.Count(p=>p.Color==2)!=1)throw new Exception("Simultaneous preferences collided");
 var extra=new Player{Id="extra"};preferenceHost.Current.ApplyPreferences(extra,3,1,preferenceHost.Current.MapPath);
 if(extra.Color!=3||extra.Spot!=0)throw new Exception("Occupied spot blocked independent color preference");
 var wrongMap=new Player{Id="wrong-map"};preferenceHost.Current.ApplyPreferences(wrongMap,99,2,"different map");
 if(wrongMap.Color!=0||wrongMap.Spot!=0)throw new Exception("Invalid preferences applied");
 preferenceA.Leave();preferenceB.Leave();preferenceHost.Leave();
 Console.WriteLine("PASS saved per-map preferences, simultaneous joins, occupied choices and invalid input");
}

if(File.Exists(installation))
{
 var hostPrefs=Settings.InMemory();hostPrefs.GamePath=installation;
 var map=GameInstallation.Maps(installation).First(m=>m.Name=="Buckland");
 var spots=GameMapData.Spots(installation,map.Path);
 hostPrefs.PreferredMapSpots[map.Path]=spots[0].Number;
 var a=Settings.InMemory();a.PreferredMapSpots[map.Path]=spots[1].Number;
 var b=Settings.InMemory();b.PreferredMapSpots[map.Path]=spots[1].Number;
 using var h=new PeerService(hostPrefs);using var ga=new PeerService(a);using var gb=new PeerService(b);
 h.Start("127.0.0.97");ga.Start("127.0.0.98");gb.Start("127.0.0.99");h.Host(new());
 await ga.Join(h.Address);await gb.Join(h.Address);await ga.SetReady(true);await gb.SetReady(true);
 var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(h.Current))!;
 draft.Map=map.Name;draft.MapPath=map.Path;await h.EditRoom(draft);
 await Until(()=>h.Current!.Players.Count(p=>p.Spot!=0)==2&&ga.Current!.Players.Count(p=>p.Spot!=0)==2&&gb.Current!.Players.Count(p=>p.Spot!=0)==2,"map change applies host and guest preferences with conflict resolution");
 if(h.Current!.Players[0].Spot!=spots[0].Number||h.Current.Players.Count(p=>p.Spot==spots[1].Number)!=1||h.Current.Players.First(p=>p.Id==gb.LocalId).Spot!=spots[1].Number||h.Current.Players.Any(p=>!p.Ready))throw new Exception("Map preference claims collided or changed readiness");
 ga.Leave();gb.Leave();h.Leave();
}

{
 var room=new Room{MapPath="test",Spots=[new(1,.1,.1),new(2,.2,.2),new(3,.3,.3)]};
 var oldest=new Player{Id="old",PreferredMapSpots=new(){{"test",1}},LastMapSpots=new(){{"test",2}}};
 var middle=new Player{Id="middle",PreferredMapSpots=new(){{"test",1}},LastMapSpots=new(){{"test",2}}};
 var newest=new Player{Id="new",PreferredMapSpots=new(){{"test",1}},LastMapSpots=new(){{"test",3}}};
 room.Players=[oldest,middle,newest];room.ApplyMapSpotChoices();
 if(newest.Spot!=1||middle.Spot!=2||oldest.Spot!=0)throw new Exception("Preference/fallback priority failed");
 oldest.LastMapSpots=new(){{"other-map",3}};room.ApplyMapSpotChoices();
 if(oldest.Spot!=0)throw new Exception("Fallback used another map's spot");
 Console.WriteLine("PASS newest-first preferred spot, last selected fallback and both occupied leave empty");
}

{
 var room=new Room();var older=new Player{PreferredColor=2,Color=3};var middle=new Player{PreferredColor=2,Color=3};var newest=new Player{PreferredColor=2,Color=4};
 room.Players=[older,middle,newest];older.Id="a";middle.Id="b";newest.Id="c";room.ApplyMapSpotChoices();
 if(newest.Color!=2||middle.Color!=3||older.Color!=0)throw new Exception("Preferred color, last color fallback, random conflict priority failed");
 var colorGuest=new Player{Id="colorGuest"};room.ApplyColorChoice(colorGuest,2,4);if(colorGuest.Color!=4)throw new Exception("Join color fallback failed");
 room.ApplyColorChoice(colorGuest,2,3);if(colorGuest.Color!=0)throw new Exception("Conflicting colors must use random");
 Console.WriteLine("PASS color preference, last-selected fallback, random and newest-first priority");
}

{
 var hs=Settings.InMemory();hs.Friends.Add(new("Guest","127.0.0.102"));var gs=Settings.InMemory();gs.Friends.Add(new("Host","127.0.0.101"));
 using var h=new PeerService(hs);using var g=new PeerService(gs);h.Start("127.0.0.101");g.Start("127.0.0.102");h.Host(new());
 Room? invitation=null;g.Invitation+=(_,_,room)=>invitation=room;await h.Invite(g.Address);
 if(invitation?.Id!=h.Current!.Id||invitation.HostAddress!=h.Address||g.Current!=null)throw new Exception("Invitation did not preserve target or joined without consent");
 await g.Join(h.Address);await g.SetReady(true);await g.SetHostGameRunning(true);
 await Until(()=>h.Current!.Players.Single(p=>p.Id==g.LocalId).GameRunning,"guest game status reaches room");
 await g.SetHostGameRunning(false);await Until(()=>!h.Current!.Players.Single(p=>p.Id==g.LocalId).GameRunning,"guest game exit clears status");
 if(!h.Current!.Players.Single(p=>p.Id==g.LocalId).Ready)throw new Exception("Status reset readiness");
 g.Leave();await g.SetHostGameRunning(true);
 if(!(await PeerService.QueryPeer(g.Address,h.Address)).GameRunning)throw new Exception("Game status missing outside a room");
 await g.SetHostGameRunning(false);
 if((await PeerService.QueryPeer(g.Address,h.Address)).GameRunning)throw new Exception("Closed game still advertised in presence");
 Console.WriteLine("PASS game presence outside rooms and game-exit clearing");
 gs.BlockedUsers=[new("Host",h.Address)];invitation=null;
 try{await h.Invite(g.Address);throw new Exception("Blocked invitation accepted");}catch(IOException){}
 if(invitation!=null)throw new Exception("Blocked invitation displayed");
 h.Leave();Console.WriteLine("PASS invitations, consent, blocked invitations and per-player game lifecycle");
}

using Fellowship;
using System.Net;
using System.Net.Sockets;
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
await Until(() => guest.Current == null, "host closure disconnects guest");
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
        Console.WriteLine("PASS saved spots are map-specific and validated against current map metadata");
    }
    using var placementHost=new PeerService(new(){Name="Placement host",GamePath=Path.Combine(fixture,"lotrbfme2.exe")});
    using var first=new PeerService(new(){Name="First"});using var second=new PeerService(new(){Name="Second"});
    placementHost.Start("127.0.0.10");placementHost.Host(new(){Map="Test",MapPath=@"maps\test.map"});
    await first.Join("127.0.0.10");await second.Join("127.0.0.10");
    await Until(()=>first.Current?.Players.Count==3&&second.Current?.Spots.Count==2,"map spots synchronized to guests");
    async Task<bool> Claim(PeerService peer){try{await peer.SelectPlacement(1,1);return true;}catch(InvalidOperationException){return false;}}
    var claims=await Task.WhenAll(Claim(first),Claim(second));
    if(claims.Count(x=>x)!=1||placementHost.Current!.Players.Count(p=>p.Spot==1)!=1)throw new Exception("Conflicting spot claims accepted");
    Console.WriteLine("PASS simultaneous spot claims resolve to one player");
    var winner=claims[0]?first:second;var loser=claims[0]?second:first;
    await loser.SelectPlacement(2,2);
    await Until(()=>first.Current!.Players.Any(p=>p.Spot==2&&p.Team==2),"spot and team broadcast");
    await first.SetReady(true);await second.SetReady(true);await Until(()=>placementHost.Current!.Players.All(p=>p.Ready),"placement players ready");
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
using(var successor=new PeerService(new(){Name="Successor"}))
using(var remaining=new PeerService(new(){Name="Remaining"}))
{
    original.Start("127.0.0.20");successor.Start("127.0.0.21");remaining.Start("127.0.0.22");
    original.Host(new(){Title="Handoff test",Match=new(){Resources=2000,CommandPoints=400}});
    await successor.Join("127.0.0.20");await remaining.Join("127.0.0.20");
    await Until(()=>remaining.Current?.Players.Count==3&&successor.Current?.Players.Count==3,"handoff roster ready");
    await successor.SelectPlacement(team:3);await remaining.SelectPlacement(team:4);
    var roomId=original.Current!.Id;
    try{await remaining.Kick("host");throw new Exception("Guest kick allowed");}catch(InvalidOperationException){}
    try{await remaining.Promote("host");throw new Exception("Guest promotion allowed");}catch(InvalidOperationException){}
    await original.Promote(successor.LocalId);
    await Until(()=>successor.IsHost&&!original.IsHost&&original.Current?.Players.Count==3&&remaining.Current?.Host=="Successor","manual promotion reconnects all players");
    if(successor.Current?.Id!=roomId||successor.Current.Match.Resources!=2000||successor.Current.Players.Single(p=>p.Name=="Remaining").Team!=4||successor.Current.Players.Single(p=>p.Id=="host").Team!=3)throw new Exception("Handoff lost room/rules/teams");
    if(await PeerService.Query("127.0.0.20")!=null)throw new Exception("Former host still advertises");
    await successor.LeaveRoom();
    await Until(()=>original.IsHost&&remaining.Current?.Host=="Original"&&original.Current?.Players.Count==2&&successor.Current==null,"host leave promotes next player");
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

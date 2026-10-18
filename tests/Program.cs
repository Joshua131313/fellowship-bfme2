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
await Until(() => guest.Current?.Map == "Buckland" && !guest.Current.Players.Single(p => p.Id != "host").Ready, "settings sync revokes readiness");
await guest.SetReady(true);
await Until(() => host.Current!.Players.All(p => p.Ready), "renewed readiness");
int launches = 0; guest.LaunchRequested += () => launches++;
await host.Launch();
await Until(() => launches == 1, "host launch signal");
await Until(() => guest.Current?.Phase.Contains("launched") == true, "persistent launched room");
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
Console.WriteLine("ALL PROTOCOL CHECKS PASSED");


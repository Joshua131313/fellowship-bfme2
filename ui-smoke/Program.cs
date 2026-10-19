using Fellowship;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program
{
 [STAThread] static void Main(string[] args)
 {
  if(args.Length==2&&args[0]=="--prewarm-probe")
  {PrewarmProbe.Run(args[1]).GetAwaiter().GetResult();return;}
  if(args.Length==4&&args[0]=="--setup-probe")
  {LiveProbe.Setup(args[1],int.Parse(args[2]),args[3]).GetAwaiter().GetResult();return;}
  if(args.Length==5&&args[0]=="--live-connection")
  {LiveProbe.Connection(args[1],int.Parse(args[2]),args[3],args[4]).GetAwaiter().GetResult();return;}
  if(args.Length==4&&args[0]=="--live-probe")
  {LiveProbe.Run(args[1],int.Parse(args[2]),args[3]).GetAwaiter().GetResult();return;}
  var abortControl=new LivePlacement.Control();
  if(abortControl.Abort().GetAwaiter().GetResult())throw new Exception("Disconnected helper accepted Abort");
  var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
  var connectControl=typeof(LivePlacement.Control).GetMethod("Connect",flags)!;
  var pendingControl=typeof(LivePlacement.Control).GetProperty("Pending",flags)!;
  var completeControl=typeof(LivePlacement.Control).GetMethod("Complete",flags)!;
  using(var connection=(IDisposable)connectControl.Invoke(abortControl,null)!)
  {
    var request=abortControl.Abort();
    if(request.IsCompleted||!ReferenceEquals(request,abortControl.Abort()))throw new Exception("Abort did not await one helper acknowledgement");
    completeControl.Invoke(abortControl,new[]{pendingControl.GetValue(abortControl),new LivePlacement.Reply("Stopped",Aborted:true)});
    if(!request.GetAwaiter().GetResult())throw new Exception("Abort acknowledgement lost");
    var second=abortControl.Abort();connection.Dispose();
    try{second.GetAwaiter().GetResult();throw new Exception("Disconnected Abort incorrectly succeeded");}catch(IOException){}
  }
  var localCommand=new LivePlacement.Command("room","map","host",new(1,2));
  var commandJson=System.Text.Json.JsonSerializer.Serialize(localCommand with{Abort=true});
  if(localCommand.Abort||System.Text.Json.JsonSerializer.Deserialize<LivePlacement.Command>(commandJson)?.Abort!=true)throw new Exception("Abort pipe command serialization failed");
  Console.WriteLine("PASS local helper Abort acknowledgement, deduplication, disconnect and command serialization");
  var nativeMemory=new Dictionary<long,byte[]>();
  void Put(long address,uint value)=>nativeMemory[address]=BitConverter.GetBytes(value);
  byte[] ReadMemory(long address,int size)=>nativeMemory[address].Take(size).ToArray();
  Put(0xdfe1f4,0x1000);Put(0xdf9800,0x2000);Put(0x2000,0xc3afb8);Put(0x2044,0x3000);Put(0x3000,0xc3ae60);Put(0x3010,0);
  if(!NativeLobby.DecodeClosed(ReadMemory,0x400000))throw new Exception("Inactive LAN room with retained setup UI was not closed");
  Put(0x3010,1);
  if(NativeLobby.DecodeClosed(ReadMemory,0x400000))throw new Exception("Active LAN room treated as closed");
  Put(0x3000,0);Put(0x3010,0);
  if(NativeLobby.DecodeClosed(ReadMemory,0x400000))throw new Exception("Unknown native object treated as closed");
  Put(0x2044,0);
  if(!NativeLobby.DecodeClosed(ReadMemory,0x400000))throw new Exception("Recorded room-null transition with retained setup was not recognized");
  Put(0xdfe1f4,0);
  if(!NativeLobby.DecodeClosed(ReadMemory,0x400000))throw new Exception("Destroyed setup not recognized");
  Console.WriteLine("PASS native room teardown with retained UI, active-room rejection and unknown-pointer rejection");
  var guard=new LivePlacement.SessionGuard();
  bool Stable(uint id){bool result=false;for(int i=0;i<5;i++)result=guard.Observe(id,false);return result;}
  if(guard.Observe(null,false)||guard.Stopped||!Stable(123))throw new Exception("Initial stability gate failed");
  if(guard.Observe(null,true)||!guard.Stopped||Stable(123))throw new Exception("Countdown re-armed sync");
  // A transient read failure or a different pointer cannot establish a new session.
  guard.Observe(null,false);if(Stable(456))throw new Exception("Pointer change bypassed teardown gate");
  guard.Observe(null,false,true);
  guard.Observe(null,false); // An incomplete read while rebuilding is not a new match.
  if(!Stable(123)||guard.Stopped||guard.Generation!=2)throw new Exception("Reused-address new lobby did not reconnect");
  guard.Observe(null,true);
  for(int i=0;i<3;i++)guard.Observe(null,false,true);
  if(!Stable(456)||guard.Generation!=3)throw new Exception("Third lobby did not reconnect");
  Console.WriteLine("PASS repeated lobbies, reused addresses, countdown protection and transient-read rejection");
  // Recreated native lobbies must reset every remembered write, including color.
  guard.Observe(null,true);guard.Observe(null,false,true);
  if(!Stable(789)||guard.Generation!=4)throw new Exception("Post-match lobby did not re-arm");
  var blockedPreferences=Settings.InMemory();blockedPreferences.BlockedUsers=[new("Blocked player","26.1.2.3")];
  if(!System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(blockedPreferences))!.IsBlocked("26.1.2.3"))throw new Exception("Block preference did not persist");
  var preferences=Settings.InMemory();
  var remembered=new Room{Map="Rhûn",MapPath=GameMap.Rhun.Path,Capacity=6,Match=new(){Resources=2000,CommandPoints=400,CustomHeroes=true},Players=[new(){Id="host",Team=3,Spot=2,Color=7}]};
  preferences.RememberHost(remembered);
  var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(preferences))!;
  var nextRoom=restored.NewHostedRoom();
  if(nextRoom.MapPath!=remembered.MapPath||nextRoom.Match.Resources!=2000||nextRoom.Match.CommandPoints!=400||!nextRoom.Match.CustomHeroes||restored.LastHostedSetup?.Team!=3||restored.LastHostedSetup?.Spot!=2||restored.LastHostedSetup?.Color!=7||nextRoom.Players.Count!=0)throw new Exception("Last selections did not survive preferences roundtrip");
  nextRoom.Match.Resources=1000;
  if(restored.NewHostedRoom().Match.Resources!=2000)throw new Exception("Room draft mutated saved rules");
  Console.WriteLine("PASS saved host map, rules, team and spot; independent next-room draft");
  var sync=new LivePlacement.Reconciler();
  var first=new LivePlacement.Choice(3,2);var edited=new LivePlacement.Choice(4,1);
  if(!sync.ShouldWrite(first))throw new Exception("Initial selection must apply");
  sync.Remember(first,first);
  if(sync.ShouldWrite(first)||sync.Changed(first))throw new Exception("Unchanged state echoed");
  if(!sync.Changed(edited))throw new Exception("Native edit not detected");
  sync.Imported(edited);
  if(sync.ShouldWrite(edited)||sync.Changed(edited))throw new Exception("Imported native edit echoed");
  if(!sync.ShouldWrite(first))throw new Exception("Later Fellowship edit not detected");
  using(var badFrame=new MemoryStream(BitConverter.GetBytes(4097)))
  {
   try{LivePlacement.Receive<LivePlacement.Command>(badFrame,default).GetAwaiter().GetResult();throw new Exception("Oversized IPC accepted");}catch(IOException){}
  }
  Console.WriteLine("PASS live sync initial apply, both edit directions, echo suppression and IPC size bound");
  if(args.Length==3&&args[0]=="--native-read")
  {
   try{using var reader=new NativeLobby(int.Parse(args[1]));File.WriteAllText(args[2],System.Text.Json.JsonSerializer.Serialize(reader.Current()));}
   catch(Exception e){File.WriteAllText(args[2],e.ToString());}
   return;
  }
  if(args.Contains("--download-check"))
  {
    using var http=new System.Net.Http.HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("Fellowship-test");
    using var release=System.Text.Json.JsonDocument.Parse(http.GetStringAsync("https://api.github.com/repos/Joshua131313/fellowship-bfme2/releases/tags/v0.1.8").GetAwaiter().GetResult());
    var asset=release.RootElement.GetProperty("assets").EnumerateArray().Single(a=>a.GetProperty("name").GetString()=="Fellowship-0.1.8-win-x64.zip");
    var package=new AppUpdates.Release(new Version("0.1.8"),asset.GetProperty("browser_download_url").GetString()!,asset.GetProperty("digest").GetString()![7..]);
    var exe=AppUpdates.Download(package,Path.GetFullPath("dist/update-test")).GetAwaiter().GetResult();
    if(!File.Exists(exe)||File.Exists(Path.Combine(Path.GetDirectoryName(exe)!,"update.zip")))throw new Exception("Update extraction/cleanup failed");
    Console.WriteLine("PASS real GitHub update download, checksum, extraction, version validation and ZIP cleanup");return;
  }
  if(!Settings.InMemory().LiveGameSync)throw new Exception("Existing default sync behavior changed");
  var pausedSettings=Settings.InMemory();pausedSettings.LiveGameSync=false;
  var pausedCopy=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(pausedSettings))!;
  if(pausedCopy.LiveGameSync)throw new Exception("Sync pause setting did not persist");
  Console.WriteLine("PASS sync enabled by default and explicit pause retained");
  var launchPath=Path.Combine(Path.GetTempPath(),"BFME2 test","lotrbfme2.exe");
  var gameStart=LaunchContext.Game(launchPath);
  if(!gameStart.UseShellExecute||gameStart.FileName!=launchPath||gameStart.WorkingDirectory!=Path.GetDirectoryName(launchPath)||gameStart.Verb!=""||gameStart.ArgumentList.Count!=0||gameStart.Arguments!="")throw new Exception("Game launch must preserve default shell behavior without forced elevation or arguments");
  if(LaunchContext.Worker(launchPath,false).Verb!="open"||LaunchContext.Worker(launchPath,true).Verb!="runas")throw new Exception("Worker elevation policy mismatch");
  _=LaunchContext.IsElevated(Environment.ProcessId);
  Console.WriteLine("PASS direct game launch, conditional helper elevation and process token query");
  var nativeSnapshot=new NativeLobby.Snapshot(123,"maps\\test.map","127.0.0.1",[new NativeLobby.Slot(0,6,1,2,true)]);
  var outsiderLobby=new NativeLobby.Snapshot(123,"maps\\test.map","26.1.2.3",[
    new(0,6,0,0,false),new(1,6,1,0,true),new(2,3,2,1,false)]);
  var guestCommand=new LivePlacement.Command("room","maps\\test.map","26.1.2.3",new(4,1));
  if(!LivePlacement.MatchesLobby(guestCommand,outsiderLobby)||LivePlacement.MatchesLobby(guestCommand with{Host="26.9.9.9"},outsiderLobby)||LivePlacement.MatchesLobby(guestCommand with{Map="maps\\other.map"},outsiderLobby))throw new Exception("Wrong host/map lobby was eligible");
  if(LivePlacement.MatchesLobby(guestCommand with{Setup=new("maps\\test.map",4000,200,false,false)},outsiderLobby))throw new Exception("Native guest allowed shared host-rule writes");
  if(LivePlacement.ResolveOccupiedSpot(outsiderLobby,new(4,1))!=new LivePlacement.Choice(4,2)||LivePlacement.ResolveOccupiedSpot(outsiderLobby,new(4,3))!=new LivePlacement.Choice(4,2))throw new Exception("External player or AI spot would be overwritten");
  if(LivePlacement.ResolveOccupiedSpot(outsiderLobby,new(4,4))!=new LivePlacement.Choice(4,4)||LivePlacement.ResolveOccupiedSpot(outsiderLobby,new(4,0))!=new LivePlacement.Choice(4,0))throw new Exception("Free/random spot rejected");
  var duplicates=outsiderLobby with{Slots=[new(0,6,0,0,false),new(1,6,0,0,true)]};
  if(!LivePlacement.HasDuplicateSpots(duplicates)||LivePlacement.HasDuplicateSpots(outsiderLobby))throw new Exception("Native duplicate spot detection failed");
  if(LivePlacement.ContextKey(guestCommand,outsiderLobby)==LivePlacement.ContextKey(guestCommand with{Host="26.9.9.9"},outsiderLobby)||LivePlacement.ContextKey(guestCommand,outsiderLobby)==LivePlacement.ContextKey(guestCommand with{Setup=new("maps\\test.map",4000,200,false,false)},outsiderLobby))throw new Exception("Handoff did not invalidate reconciliation context");
  Console.WriteLine("PASS host and role changes invalidate sync context; wrong native host remains protected");
  var rotating=guestCommand with{Host="26.99.99.99",Members=outsiderLobby.HostAddress,Setup=new("maps\\test.map",4000,200,false,false)};
  if(!LivePlacement.MatchesLobby(rotating,outsiderLobby)||LivePlacement.MatchesLobby(rotating with{Members="26.99.99.99"},outsiderLobby))throw new Exception("Rotating native host membership guard failed");
  if(rotating!=(rotating with{Members=new string(rotating.Members!.ToCharArray())}))throw new Exception("Sync command membership must use value equality");
  Console.WriteLine("PASS native host can differ from Fellowship host; nonmember lobbies rejected; command equality preserved");
  var rejected=new LivePlacement.Reconciler();rejected.Remember(new(4,1),new(4,2));
  if(rejected.ShouldWrite(new(4,1))||!rejected.ShouldWrite(new(4,4)))throw new Exception("Rejected spot retries without a new user choice");
  Console.WriteLine("PASS native external-player/AI collisions, duplicate slots, wrong host/map, host-role checks and rejected-choice retry suppression");
  var colorLobby=outsiderLobby with{Slots=[new(0,6,0,0,false,2),new(1,6,1,0,true,4),new(2,3,2,1,false,6)]};
  if(LivePlacement.ResolveOccupiedColor(colorLobby,new(1,2,3)).Color!=5||LivePlacement.ResolveOccupiedColor(colorLobby,new(1,2,7)).Color!=5||LivePlacement.ResolveOccupiedColor(colorLobby,new(1,2,0)).Color!=0)throw new Exception("Native human/AI color collision guard failed");
  var colorBytes=NativePlacement.RequestBytes(1234,nativeSnapshot,4,1,color:10);
  if(BitConverter.ToInt32(colorBytes,1100)!=9||BitConverter.ToInt32(NativePlacement.RequestBytes(1234,nativeSnapshot,0,0,color:0),1100)!=-1)throw new Exception("Native color encoding failed");
  try{NativePlacement.RequestBytes(1234,nativeSnapshot,0,0,color:11);throw new Exception("Invalid color accepted");}catch(InvalidDataException){}
  var colorSync=new LivePlacement.Reconciler();colorSync.Remember(new(1,2,1),new(1,2,1));if(!colorSync.Changed(new(1,2,2))||!colorSync.ShouldWrite(new(1,2,3)))throw new Exception("Color-only edits ignored");
  Console.WriteLine("PASS native color bounds, encoding, human/AI conflicts and bidirectional color-only edits");
  var command=NativePlacement.RequestBytes(1234,nativeSnapshot,4,1);
  if(command.Length!=1104||BitConverter.ToInt32(command,4)!=3||BitConverter.ToInt32(command,20)!=3||BitConverter.ToInt32(command,24)!=0||BitConverter.ToInt32(command,28)!=0||System.Text.Encoding.ASCII.GetString(command,56,13)!="maps\\test.map")throw new Exception("Native placement ABI mismatch");
  var setupBytes=NativePlacement.SetupRequestBytes(1234,nativeSnapshot,new("maps\\test.map",4000,200,false,true));
  if(BitConverter.ToInt32(setupBytes,568)!=1||BitConverter.ToInt32(setupBytes,1084)!=4000||BitConverter.ToInt32(setupBytes,1088)!=200||BitConverter.ToInt32(setupBytes,1096)!=1)throw new Exception("Native setup ABI mismatch");
  var random=NativePlacement.RequestBytes(1234,nativeSnapshot,0,0,true);
  if(BitConverter.ToInt32(random,20)!=-1||BitConverter.ToInt32(random,24)!=-1||BitConverter.ToInt32(random,28)!=1)throw new Exception("Native random selection encoding failed");
  try{NativePlacement.RequestBytes(1234,nativeSnapshot,5,1);throw new Exception("Invalid native team accepted");}catch(InvalidDataException){}
  Console.WriteLine("PASS native bridge layout, zero-based conversion, unset choices and bounds");
  var allocated=new Room{MapPath=nativeSnapshot.Map,Spots=[new(1,.2,.2),new(2,.8,.8)]};
  allocated.Players=[new(){Id="old",Team=2,PreferredColor=3,Color=4,PreferredMapSpots=new(){{nativeSnapshot.Map,1}},LastMapSpots=new(){{nativeSnapshot.Map,2}}},new(){Id="middle",Team=3,PreferredColor=3,Color=4,PreferredMapSpots=new(){{nativeSnapshot.Map,1}},LastMapSpots=new(){{nativeSnapshot.Map,2}}},new(){Id="new",Team=4,PreferredColor=3,PreferredMapSpots=new(){{nativeSnapshot.Map,1}}}];
  allocated.ApplyMapSpotChoices();
  foreach(var selected in allocated.Players)
  {
      var choice=new LivePlacement.Choice(selected.Team,selected.Spot,selected.Color);
      var payload=NativePlacement.RequestBytes(1234,nativeSnapshot,choice.Team,choice.Spot,color:choice.Color);
      if(BitConverter.ToInt32(payload,20)!=choice.Team-1||BitConverter.ToInt32(payload,24)!=choice.Spot-1||BitConverter.ToInt32(payload,1100)!=choice.Color-1)throw new Exception("Allocated preference/fallback/random choice lost in native request");
      var reconciliation=new LivePlacement.Reconciler();reconciliation.Remember(choice,choice);
      if(reconciliation.ShouldWrite(choice))throw new Exception("Stable selection repeats writes");
      reconciliation=new(); // Production resets on host context or native session generation changes.
      if(!reconciliation.ShouldWrite(choice))throw new Exception("New lobby or host did not restore allocated selection");
  }
  if(allocated.Players[0].Spot!=0||allocated.Players[0].Color!=0||allocated.Players[1].Spot!=2||allocated.Players[1].Color!=4)throw new Exception("Preference test did not exercise fallback and empty/random choices");
  Console.WriteLine("PASS allocated preferred/fallback/empty/random choices encode for native sync and resend after context reset");

  if(!SearchableMapBox.Matches("Rhûn","rhun")||!SearchableMapBox.Matches("Woody End","END")||SearchableMapBox.Matches("Rhûn","mordor"))throw new Exception("Map search matching failed");
  var spots=MapPreview.ParseSpots("MapCache maps_5Ctest_2Emap\n extentMin = X:100 Y:200 Z:0\n extentMax = X:1100 Y:2200 Z:0\n numPlayers = 2\n Player_1_Start = X:350 Y:700 Z:0\n Player_2_Start = X:850 Y:1700 Z:0\nEND",@"maps\test.map");
  if(spots.Count!=2||spots[0].X!=0.25||spots[0].Y!=0.75||spots[1].X!=0.75||spots[1].Y!=0.25)throw new Exception("Map spot coordinate normalization failed");
  var defaultPresets=RoomPreset.Defaults(GameInstallation.Maps(GameInstallation.Find()));
  foreach(var preset in defaultPresets)if(string.IsNullOrEmpty(preset.MapPath))throw new Exception("Missing default preset map: "+preset.Map);
  if(defaultPresets.Count!=5||defaultPresets.Any(p=>p.CommandPoints!=200||p.CustomHeroes||p.RingHeroes)||defaultPresets[0].Resources!=4000||defaultPresets[2].Resources!=1000)throw new Exception("Incorrect built-in preset rules");
  var presetSettings=Settings.InMemory();presetSettings.RoomPresets.Add(defaultPresets[0] with{Name="Custom",BuiltIn=false});
  var persisted=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(presetSettings))!;
  if(persisted.RoomPresets.Single().Teams.Length!=6||persisted.RoomPresets.Single().Name!="Custom")throw new Exception("Custom preset did not persist");
  var arranged=new Room{InitialTeams=defaultPresets[0].Teams};for(int j=0;j<6;j++)arranged.Players.Add(new(){Team=arranged.NextTeam()});
  if(arranged.Players.Count(p=>p.Team==1)!=3||arranged.Players.Count(p=>p.Team==2)!=3||arranged.NextTeam()!=0)throw new Exception("Starting teams were not assigned correctly");
  Console.WriteLine("PASS default preset maps/rules, custom persistence and 3v3 team allocation");
  presetSettings.Friends.Add(new("Friend","26.1.2.3"));presetSettings.BlockedUsers=[new("Blocked","26.1.2.4")];presetSettings.PreferredColor=7;
  var backup=SettingsBackup.Parse(SettingsBackup.Export(presetSettings));var restoredBackup=Settings.InMemory();restoredBackup.GamePath="local-game";restoredBackup.BindAddress="26.8.8.8";SettingsBackup.Restore(restoredBackup,backup);
  if(restoredBackup.PreferredColor!=7||restoredBackup.Friends.Count!=1||restoredBackup.BlockedUsers.Length!=1||restoredBackup.RoomPresets.Count!=1||restoredBackup.GamePath!="local-game"||restoredBackup.BindAddress!="26.8.8.8")throw new Exception("Backup restore lost data or local paths");
  try{SettingsBackup.Parse("{\"Format\":99}");throw new Exception("Invalid backup accepted");}catch(System.IO.InvalidDataException){}
  Console.WriteLine("PASS backup roundtrip, local path preservation and unsupported format rejection");

  Console.WriteLine("PASS map bounds, slot numbers and vertical coordinate conversion");
  var preview=MapPreview.Load(GameInstallation.Find(),GameMap.Rhun.Path);
  if(File.Exists(GameInstallation.Find())&&(preview==null||preview.PixelWidth!=320))throw new Exception("Installed map preview failed");
  try{MapPreview.Decode(new byte[18]);throw new Exception("Invalid preview accepted");}catch(InvalidDataException){}
  string ReleaseJson(string tag,string url,string digest)=>System.Text.Json.JsonSerializer.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name="Fellowship-0.1.92-win-x64.zip",browser_download_url=url,digest}}});
  var goodUrl="https://github.com/Joshua131313/fellowship-bfme2/releases/download/v0.1.92/Fellowship-0.1.92-win-x64.zip";
  if(AppUpdates.Parse(ReleaseJson("v0.1.92",goodUrl,"sha256:"+new string('a',64)))==null)throw new Exception("New update not detected");
  if(AppUpdates.Parse(ReleaseJson("v0.1.6",goodUrl,"sha256:"+new string('a',64)))!=null)throw new Exception("Downgrade offered");
  try{AppUpdates.Parse(ReleaseJson("v0.1.92","https://example.com/update.zip","sha256:"+new string('a',64)));throw new Exception("Untrusted update accepted");}catch(InvalidDataException){}
  var msiJson=ReleaseJson("v0.1.92",goodUrl,"sha256:"+new string('a',64)).Replace(".zip",".msi");
  if(AppUpdates.Parse(msiJson) is not {Installer:true})throw new Exception("Installer update not detected");
  Console.WriteLine("PASS installer update selection");
  UpdateDownloadTests.Run().GetAwaiter().GetResult();
  var repairVersion=new Version(0,1,66);
  string RepairJson(string digest,string tag="v0.1.66")=>System.Text.Json.JsonSerializer.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name="Fellowship-0.1.66-win-x64.msi",browser_download_url="https://github.com/Joshua131313/fellowship-bfme2/releases/download/v0.1.66/Fellowship-0.1.66-win-x64.msi",digest}}});
  if(InstallerRecovery.ParseRelease(RepairJson("sha256:"+new string('a',64)),repairVersion).Version!=repairVersion)throw new Exception("Exact recovery version rejected");
  foreach(var invalid in new[]{RepairJson("sha256:"+new string('z',64)),RepairJson("sha256:"+new string('a',64),"v0.1.65")})
  {try{InstallerRecovery.ParseRelease(invalid,repairVersion);throw new Exception("Unsafe recovery identity accepted");}catch(InvalidDataException){}}
  if(!InstallerRecovery.MissingSource(1603,"RemoveExistingProducts Error 1612")||InstallerRecovery.MissingSource(1603,"Other failure")||InstallerRecovery.MissingSource(0,"1612"))throw new Exception("Recovery failure classification incorrect");
  foreach(var product in InstallerRecovery.Registered().Where(p=>File.Exists(p.CachedPackage)))
  {
      InstallerRecovery.VerifyPackage(product.CachedPackage,product);
      try{InstallerRecovery.VerifyPackage(product.CachedPackage,product with{Code=Guid.NewGuid().ToString("B")});throw new Exception("Wrong MSI identity accepted");}catch(InvalidDataException){}
  }
  Console.WriteLine("PASS recovery release verification, actual MSI identity checks and targeted failure detection");
  UpdateCacheTests.Run();
  bool noSpace=false;try{Installation.ValidateFreeSpace("C:",0);}catch(IOException){noSpace=true;}
  if(!noSpace||!Installation.InstallerError(112).Contains("disk is full"))throw new Exception("Disk-full detection failed");
  Installation.ValidateFreeSpace("C:",Installation.UpdateSpaceReserve);
  Console.WriteLine("PASS low-disk-space preflight and installer code 112 guidance");
  if(preview!=null){Directory.CreateDirectory("dist");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(preview));using var output=File.Create("dist/preview-overhead.png");encoder.Save(output);}
  Console.WriteLine("PASS installed map preview and update version/source validation");
  Directory.CreateDirectory("dist");
  var app=new App();app.InitializeComponent();Appearance.Apply("Dark");
  var selectPreview=new System.Windows.Controls.ComboBox{Style=(Style)app.FindResource(typeof(System.Windows.Controls.ComboBox)),Width=240,ItemsSource=new[]{"Radmin VPN","Ethernet 6","ZeroTier","Wi-Fi","Loopback"},SelectedIndex=0};
  selectPreview.Measure(new Size(240,34));selectPreview.Arrange(new Rect(0,0,240,34));selectPreview.ApplyTemplate();
  typeof(App).GetMethod("SelectLoaded",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(app,new object[]{selectPreview,new RoutedEventArgs()});
  var selectPopup=(System.Windows.Controls.Primitives.Popup)selectPreview.Template.FindName("PART_Popup",selectPreview);
  var placements=selectPopup.CustomPopupPlacementCallback(new Size(240,180),new Size(240,34),new Point());
  if(placements[0].Point.X!=0||placements[0].Point.Y!=38||placements[1].Point.X!=0)throw new Exception("Dropdown is not left aligned with its control");
  var menuBody=(FrameworkElement)selectPopup.Child;menuBody.Measure(new Size(240,300));menuBody.Arrange(new Rect(0,0,240,menuBody.DesiredSize.Height));menuBody.UpdateLayout();
  if(Math.Abs(menuBody.Width-240)>1)throw new Exception("Dropdown width does not match its control");
  var selectWheel=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,-120){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent};
  selectPreview.RaiseEvent(selectWheel);
  if(selectPreview.SelectedIndex!=0||!selectWheel.Handled)throw new Exception("Closed dropdown wheel changes selection");
  Console.WriteLine("PASS shared dropdown left alignment, matched width and closed-wheel selection protection");

  var picker=new SearchableMapBox{ItemsSource=new[]{"Rhûn","Woody End"},SelectedItem="Rhûn"};
  picker.ApplyTemplate();
  if(picker.Template.FindName("PART_MapSearch",picker) is not System.Windows.Controls.TextBox {Visibility:Visibility.Visible})throw new Exception("Map search template missing");
  var wheelBox=new System.Windows.Controls.ComboBox{ItemsSource=new[]{1,2,3},SelectedIndex=1};
  wheelBox.Style=(Style)Application.Current.FindResource(typeof(System.Windows.Controls.ComboBox));
  var wheel=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,-120){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent};
  wheelBox.RaiseEvent(wheel);
  if(!wheel.Handled||wheelBox.SelectedIndex!=1)throw new Exception("Closed select accepts accidental wheel changes");
  Console.WriteLine("PASS closed dropdown wheel input cannot change selection");
  bool validInput=false;
  var guarded=UI.Require(UI.Button("Test",()=>{}),()=>validInput);
  if(guarded.Command.CanExecute(null))throw new Exception("Invalid form action enabled");
  validInput=true;if(!guarded.Command.CanExecute(null))throw new Exception("Valid form action disabled");
  if(UI.ValidAddress("")||UI.ValidAddress("abc")||UI.ValidAddress("26.1")||UI.ValidAddress("0.0.0.0")||!UI.ValidAddress("26.42.214.47"))throw new Exception("Host IP validation failed");
  UI.DisabledHint(guarded,()=>validInput?"Ready":"Enter a value");
  validInput=false;
  if(!System.Windows.Controls.ToolTipService.GetShowOnDisabled(guarded)||((Func<string>)guarded.Resources["DisabledReason"])()!="Enter a value")throw new Exception("Disabled tooltip reason unavailable");
  Console.WriteLine("PASS disabled controls expose explanatory tooltip reasons");
  Console.WriteLine("PASS action guards and full IPv4 validation");
  var menuHeading=UI.MenuGroup("File",UI.MenuAction("Example",()=>{}));menuHeading.ApplyTemplate();
  if(menuHeading.Template.FindName("PART_Popup",menuHeading) is not System.Windows.Controls.Primitives.Popup)throw new Exception("Menu dropdown missing");
  menuHeading.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
  var headingPopup=(System.Windows.Controls.Primitives.Popup)menuHeading.Template.FindName("PART_Popup",menuHeading);
  var positions=headingPopup.CustomPopupPlacementCallback(new Size(220,120),new Size(60,30),new Point());
  if(positions[0].Point!=new Point(0,30))throw new Exception("Menu is not anchored below its heading");
  bool canRun=true;int runs=0;
  var refreshItem=UI.MenuAction("Refresh rooms",()=>runs++,()=>canRun);
  var menuTestWindow=new Window();UI.Shell(menuTestWindow,"Menu test",400,300);UI.Menus(menuTestWindow,UI.MenuGroup("View",refreshItem));
  if(menuTestWindow.InputBindings.Count!=1||refreshItem.InputGestureText!="F5")throw new Exception("Menu shortcut label/binding missing");
  refreshItem.Command.Execute(null);canRun=false;refreshItem.Command.Execute(null);
  if(runs!=1||refreshItem.Command.CanExecute(null))throw new Exception("Menu shortcut ignored enabled state");
  Console.WriteLine("PASS menu left-edge placement, shortcut labels/bindings and disabled commands");
  Console.WriteLine("PASS application menu dropdown template");
  var demoSettings=Settings.InMemory();demoSettings.Name="Frodo";
  var browser=new BrowserWindow(demoSettings);
  browser.Presence.Success("26.1.1.2","Aragorn",null,DateTimeOffset.UtcNow);
  browser.Presence.Success("26.1.1.3","Gandalf",null,DateTimeOffset.UtcNow);
  browser.Presence.Failure("26.1.1.3");
  typeof(BrowserWindow).GetMethod("RenderActivePlayers",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(browser,null);
  var availableProperty=typeof(AppUpdates).GetProperty("Available")!;
  availableProperty.SetValue(null,new AppUpdates.Release(new Version("0.1.92"),"https://github.com/Joshua131313/fellowship-bfme2/releases/download/v0.1.92/test.msi",new string('a',64),true));
  Render(new BrowserWindow(demoSettings),"dist/preview-update-notice.png");
  availableProperty.SetValue(null,null);
  Render(new GamePreferencesWindow(browser,demoSettings),"dist/preview-game-preferences.png");
  Render(browser.CreateSettingsWindow(),"dist/preview-settings.png");
  var settingsFull=browser.CreateSettingsWindow();settingsFull.Height=960;Render(settingsFull,"dist/preview-settings-full.png");
  var dashboardPreview=new BrowserWindow(demoSettings);
  dashboardPreview.Presence.Success("26.1.1.2","Aragorn",new Room{Title="Evening on Rhûn",Host="Aragorn",HostAddress="26.1.1.2"},DateTimeOffset.UtcNow);
  typeof(BrowserWindow).GetMethod("UpdateDiscoveredRooms",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(dashboardPreview,null);
  var roomModeFilter=(System.Windows.Controls.ComboBox)typeof(BrowserWindow).GetField("modeFilter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(dashboardPreview)!;
  var roomTable=(System.Windows.Controls.DataGrid)typeof(BrowserWindow).GetField("rooms",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(dashboardPreview)!;
  roomModeFilter.SelectedItem="1v1";if(roomTable.Items.Count!=0)throw new Exception("Mode filter did not exclude custom room");
  roomModeFilter.SelectedItem="Custom";if(roomTable.Items.Count!=1)throw new Exception("Mode filter did not include custom room");
  if(demoSettings.RoomModeFilter!="Custom")throw new Exception("Mode selection was not saved");
  var reopenedSettings=Settings.InMemory();reopenedSettings.RoomModeFilter=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(demoSettings))!.RoomModeFilter;
  var reopenedBrowser=new BrowserWindow(reopenedSettings);
  var restoredFilter=(System.Windows.Controls.ComboBox)typeof(BrowserWindow).GetField("modeFilter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(reopenedBrowser)!;
  if(restoredFilter.SelectedItem as string!="Custom")throw new Exception("Mode filter was not restored after restart");
  typeof(BrowserWindow).GetMethod("SelectDashboard",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(reopenedBrowser,new object[]{"Friends"});
  typeof(BrowserWindow).GetMethod("SelectDashboard",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(reopenedBrowser,new object[]{"Game rooms"});
  if(restoredFilter.SelectedItem as string!="Custom")throw new Exception("Mode filter lost across tabs");
  roomModeFilter.SelectedIndex=0;if(roomTable.Items.Count!=1)throw new Exception("All modes did not restore room list");
  Console.WriteLine("PASS mode filter includes, excludes and resets rooms");
  Render(dashboardPreview,"dist/preview-dashboard-rooms.png");
  var rowStyle=(Style)Application.Current.FindResource(typeof(System.Windows.Controls.DataGridRow));
  var testRow=new System.Windows.Controls.DataGridRow{Style=rowStyle};
  if(((SolidColorBrush)testRow.Background).Color.ToString()!="#FF171A20")throw new Exception("Room row is not dark");
  testRow.IsSelected=true;if(((SolidColorBrush)testRow.Background).Color.ToString()!="#FF293A58")throw new Exception("Selected room row is not dark");
  var socialPreview=new BrowserWindow(demoSettings);
  typeof(BrowserWindow).GetMethod("SelectDashboard",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(socialPreview,new object[]{"Friends"});
  Render(socialPreview,"dist/preview-dashboard-friends.png");
  var tabsSettings=Settings.InMemory();tabsSettings.Name="Tab draft";tabsSettings.GamePath=GameInstallation.Find();
  var tabsBrowser=new BrowserWindow(tabsSettings);
  void SelectTab(BrowserWindow target,string tab)=>typeof(BrowserWindow).GetMethod("SelectDashboard",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(target,new object[]{tab});
  IEnumerable<DependencyObject> TabNodes(DependencyObject root){yield return root;foreach(var node in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var child in TabNodes(node))yield return child;}
  if(UI.MenuAction("Settings",()=>{}).InputGestureText!="Ctrl+,")throw new Exception("Settings shortcut label is not readable");
  int windowsBefore=Application.Current.Windows.Count;
  SelectTab(tabsBrowser,"Settings");
  var nicknameDraft=TabNodes(tabsBrowser).OfType<System.Windows.Controls.TextBox>().Single(t=>t.Text=="Tab draft");nicknameDraft.Text="Unsaved tab edit";
  SelectTab(tabsBrowser,"Game preferences");SelectTab(tabsBrowser,"Settings");
  if(!TabNodes(tabsBrowser).Contains(nicknameDraft)||nicknameDraft.Text!="Unsaved tab edit"||Application.Current.Windows.Count!=windowsBefore)throw new Exception("Dashboard tabs lost draft or opened another window");
  var themePicker=TabNodes(tabsBrowser).OfType<System.Windows.Controls.ComboBox>().Single(b=>b.SelectedItem as string=="Dark");themePicker.SelectedItem="Light";
  var soundSlider=TabNodes(tabsBrowser).OfType<System.Windows.Controls.Slider>().Single();soundSlider.Value=37;
  if(tabsSettings.Appearance!="Light"||tabsSettings.SoundVolume!=37)throw new Exception("Settings did not apply immediately");
  if(TabNodes(tabsBrowser).OfType<System.Windows.Controls.Button>().Any(b=>b.Content as string=="Save settings"))throw new Exception("Manual save button remains");
  themePicker.SelectedItem="Dark";
  Render(tabsBrowser,"dist/preview-dashboard-settings.png");
  var prefsBrowser=new BrowserWindow(tabsSettings);SelectTab(prefsBrowser,"Game preferences");
  var preferenceColor=TabNodes(prefsBrowser).OfType<System.Windows.Controls.ComboBox>().Single(b=>b.SelectedItem is PlayerColor);
  preferenceColor.SelectedIndex=preferenceColor.SelectedIndex==1?2:1;
  if(!TabNodes(prefsBrowser).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="✓ Game preferences saved"))throw new Exception("Inline preference save feedback missing");
  Render(prefsBrowser,"dist/preview-dashboard-preferences.png");
  foreach(var page in new[]{"Settings","Game preferences"})
  {
    var compact=new BrowserWindow(tabsSettings){Width=1100,Height=650};SelectTab(compact,page);
    Render(compact,"dist/preview-compact-"+page.Replace(" ","-")+".png");
  }
  var modes=RoomPreset.Defaults([]).Select(p=>p.Mode).ToArray();
  if(!modes.SequenceEqual(new[]{"3v3","2v2","1v1","1v1","FFA"}))throw new Exception("Preset mode labels incorrect");
  if((RoomPreset.Defaults([])[0] with {BuiltIn=false}).Mode!="Custom")throw new Exception("Saved custom preset must show Custom");
  var modeRoom=new Room{Mode="2v2"};
  if(System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(modeRoom))?.Mode!="2v2")throw new Exception("Room mode lost in network serialization");
  Console.WriteLine("PASS room preset mode labels and network serialization");
  Console.WriteLine("PASS settings and preference tabs stay inline, retain draft edits and show save feedback");
  Console.WriteLine("PASS dashboard navigation and dark normal/selected room row styles");
  Render(browser,"dist/preview-browser.png");
  var searchPlayers=(System.Windows.Controls.TextBox)typeof(BrowserWindow).GetField("playerSearch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(browser)!;
  searchPlayers.Text="Aragorn";
  var playerRows=(System.Windows.Controls.StackPanel)typeof(BrowserWindow).GetField("activePlayers",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(browser)!;
  if(playerRows.Children.Count!=1||playerRows.Children[0] is not System.Windows.Controls.Button)throw new Exception("Player search failed");
  searchPlayers.Text="";
  for(int i=4;i<=30;i++)browser.Presence.Success("26.1.1."+i,"Player "+i,null,DateTimeOffset.UtcNow);
  typeof(BrowserWindow).GetMethod("RenderActivePlayers",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(browser,null);
  var playerPopup=(System.Windows.Controls.Primitives.Popup)typeof(BrowserWindow).GetField("playersPopup",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(browser)!;
  var popupChild=playerPopup.Child;playerPopup.Child=null;
  Render(new Window{Width=340,Height=400,Content=popupChild},"dist/preview-players-dropdown.png");
  Console.WriteLine("PASS searchable player dropdown and 30-player layout");
  Render(browser.CreatePlayerInfo(browser,"Aragorn","26.42.214.47",new Player{Name="Aragorn",Team=2,Spot=3,Ready=true}),"dist/preview-player-info.png");
  Render(browser.CreateProfileEditor(browser),"dist/preview-profile-editor.png");
  var updatePreview=new UpdateProgressWindow("0.1.92");updatePreview.Report("Downloading update…","34.5 / 70.0 MB · 49%",49);Render(updatePreview,"dist/preview-update-progress.png");
  var presetPickerSettings=Settings.InMemory();presetPickerSettings.GamePath=GameInstallation.Find();presetPickerSettings.RoomPresets.Add(defaultPresets[0] with{Name="Temporary preset",BuiltIn=false});
  var presetPicker=new RoomPresetPicker(presetPickerSettings);int picked=0;presetPicker.Picked+=_=>picked++;
  var customItem=presetPicker.Items.OfType<System.Windows.Controls.ComboBoxItem>().Single(i=>i.Tag is RoomPreset{BuiltIn:false});
  var deleteButton=((System.Windows.Controls.Grid)customItem.Content).Children.OfType<System.Windows.Controls.Button>().Single();
  deleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
  if(presetPickerSettings.RoomPresets.Count!=0||picked!=0)throw new Exception("Deleting preset applied it or failed to persist removal");
  presetPicker.SelectedIndex=1;if(picked!=1)throw new Exception("Preset presetPicker selection failed");
  Console.WriteLine("PASS preset dropdown selection and inline deletion without applying deleted preset");
  Render(UI.Information(null,"About Fellowship","Fellowship 0.1.92\nBFME2 1.00 · Radmin VPN"),"dist/preview-about.png");
  var alertSettings=Settings.InMemory();alertSettings.Friends.Add(new("Friend","26.1.1.1"));
  var alerts=new ActivityAlerts();var alertTime=DateTimeOffset.UtcNow;
  var onlinePeer=new PeerDirectory.Entry("26.1.1.1","Friend",null,alertTime,false);
  if(alerts.Observe(alertSettings,Array.Empty<PeerDirectory.Entry>()).Count!=0)throw new Exception("Startup alerts were not suppressed");
  var onlineAlert=alerts.Observe(alertSettings,new[]{onlinePeer}).Single();
  if(onlineAlert.Address!="26.1.1.1"||onlineAlert.Name!="Friend")throw new Exception("Friend notification target missing");
  if(onlineAlert.Online!=true)throw new Exception("Online alert missing");
  if(alerts.Observe(alertSettings,new[]{onlinePeer with{Reconnecting=true}}).Count!=0)throw new Exception("Reconnect produced false offline alert");
  if(alerts.Observe(alertSettings,Array.Empty<PeerDirectory.Entry>()).Single().Online!=false)throw new Exception("Offline alert missing");
  var roomPeer=onlinePeer with{Room=new Room{Title="New room"}};
  var roomAlerts=alerts.Observe(alertSettings,new[]{roomPeer});
  if(roomAlerts.Single(a=>a.RoomId.Length>0).RoomId!=roomPeer.Room!.Id)throw new Exception("Room notification target missing");
  if(roomAlerts.Count!=2)throw new Exception("Room and online alerts missing");
  if(alerts.Observe(alertSettings,new[]{roomPeer}).Count!=0)throw new Exception("Duplicate activity alert");
  var baselineAlerts=new ActivityAlerts();if(baselineAlerts.Observe(alertSettings,new[]{roomPeer}).Count!=0)throw new Exception("Existing room notified at startup");
  var allPlayers=new ActivityAlerts();allPlayers.Observe(Settings.InMemory(),Array.Empty<PeerDirectory.Entry>());
  var strangerAlert=allPlayers.Observe(Settings.InMemory(),new[]{onlinePeer}).Single();
  if(strangerAlert.Notify||strangerAlert.Online!=true)throw new Exception("Non-friend presence should produce sound only");
  if(allPlayers.Observe(Settings.InMemory(),Array.Empty<PeerDirectory.Entry>()).Single().Online!=false)throw new Exception("Player departure sound missing");
  Console.WriteLine("PASS activity transitions, reconnect grace, startup suppression and room deduplication");
  Render(UI.Confirmation(null,"Remove friend?","Remove gabga from your friends list? You can add them again later.","Remove friend",_=>{}),"dist/preview-confirmation.png");
  Render(UI.InvitationSent(new Window(),"gabga"),"dist/preview-invitation-sent.png");
  var editor=new RoomEditor(null,new Settings{GamePath=GameInstallation.Find()},new Room{Title="Battle for Rhun"});
  IEnumerable<DependencyObject> PresetNodes(DependencyObject root){yield return root;foreach(var node in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var child in PresetNodes(node))yield return child;}
  var editorPicker=PresetNodes(editor).OfType<RoomPresetPicker>().Single();
  var editorMap=PresetNodes(editor).OfType<SearchableMapBox>().Single();
  editorPicker.SelectedIndex=1;if(editorMap.IsEnabled)throw new Exception("Named preset left map editable");
  editorPicker.SelectedIndex=0;if(!editorMap.IsEnabled)throw new Exception("Custom preset did not unlock map");
  Console.WriteLine("PASS named preset locks map and Custom preset unlocks editing");
  Render(editor,"dist/preview-room-settings.png");

  browser.Peer.Host(new Room{Title="Battle for Rhûn"});
  if(browser.Peer.Current!.Spots.Count>0)browser.Peer.SelectPlacement(browser.Peer.Current.Spots[0].Number,1).GetAwaiter().GetResult();
  var roomWindow=new RoomWindow(browser);
  var syncCommand=typeof(RoomWindow).GetMethod("SyncCommand",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
  browser.Settings.LiveGameSync=false;
  if(syncCommand.Invoke(roomWindow,null)!=null)throw new Exception("Disabled sync still produces game commands");
  browser.Settings.LiveGameSync=true;
  if(syncCommand.Invoke(roomWindow,null)==null)throw new Exception("Enabled sync failed to resume commands");
  Console.WriteLine("PASS disabling live sync suppresses commands while room stays open; enabling resumes");
  typeof(RoomWindow).GetMethod("PauseLiveSync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(roomWindow,null);
  if(!browser.Settings.LiveGameSync)throw new Exception("Helper failure changed saved sync preference");
  roomWindow.RetryLiveSync();
  if((bool)typeof(RoomWindow).GetField("syncPaused",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(roomWindow)!)throw new Exception("Settings save failed to resume paused sync");
  Console.WriteLine("PASS helper failure preserves sync preference and explicit retry clears session pause");
  if(browser.Peer.Current.Spots.Count>1)
  {
    var map=(MapPreview)typeof(RoomWindow).GetField("mapPreview",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(roomWindow)!;
    var layer=(System.Windows.Controls.Canvas)typeof(MapPreview).GetField("selectionLayer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(map)!;
    ((System.Windows.Controls.Button)layer.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    if(browser.Peer.Current.Players[0].Spot!=browser.Peer.Current.Spots[1].Number)throw new Exception("Map button failed to claim spot");
    Console.WriteLine("PASS clicking map marker updates host selection");
  }
  var resources=(System.Windows.Controls.ComboBox)typeof(RoomWindow).GetField("resourceChoice",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(roomWindow)!;
  resources.SelectedItem=2000;
  if(browser.Peer.Current!.Match.Resources!=2000)throw new Exception("Inline resource change did not save");
  roomWindow.Refresh();
  if((int)resources.SelectedItem!=2000)throw new Exception("Refresh lost inline rule selection");
  Render(roomWindow,"dist/preview-room-inline.png");
  Render(new RoomPreviewWindow(null,browser.Peer.Current!,browser.Settings.GamePath),"dist/preview-room-details.png");
  var livePreview=new RoomPreviewWindow(browser,browser.Peer.Current!,browser.Settings.GamePath);
  if(!PresetNodes(livePreview).OfType<System.Windows.Controls.DockPanel>().Any(p=>p.ContextMenu!=null))throw new Exception("Preview player menu missing");
  browser.Peer.Current!.Title="Updated preview room";
  typeof(BrowserWindow).GetMethod("RenderRooms",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(browser,null);
  if(!PresetNodes(livePreview).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Updated preview room"))throw new Exception("Preview did not refresh");
  livePreview.Close();
  Console.WriteLine("PASS preview player menus and automatic room refresh");
  browser.Peer.Dispose();
  Console.WriteLine("PASS inline host rule selection saves and survives refresh");
  var customization=Settings.InMemory();customization.SoundVolume=23;customization.SoundsEnabled=false;customization.Appearance="Light";customization.MutedSounds.Add("sent");
  var restoredCustomization=Settings.InMemory();SettingsBackup.Restore(restoredCustomization,SettingsBackup.Parse(SettingsBackup.Export(customization)));
  if(restoredCustomization.SoundVolume!=23||restoredCustomization.SoundsEnabled||restoredCustomization.Appearance!="Light"||!restoredCustomization.MutedSounds.Contains("sent"))throw new Exception("Customization backup lost settings");
  var audioType=typeof(App).Assembly.GetType("Fellowship.MessageAlert")!;
  var scale=audioType.GetMethod("Scale",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
  foreach(var resource in typeof(App).Assembly.GetManifestResourceNames().Where(n=>n.EndsWith(".wav")))
  {using var stream=typeof(App).Assembly.GetManifestResourceStream(resource)!;using var bytes=new MemoryStream();stream.CopyTo(bytes);scale.Invoke(null,new object[]{bytes.ToArray(),0});scale.Invoke(null,new object[]{bytes.ToArray(),50});}
  tabsSettings.Appearance="Light";Appearance.Apply("Light");
  var interactionPanel=new System.Windows.Controls.StackPanel{Margin=new Thickness(24),Background=UI.Brush("#101216")};
  interactionPanel.Children.Add(UI.Text("Light theme interaction states",20,bold:true));
  var hoverKey=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
  foreach(var label in new[]{"Normal","Hover","Selected navigation"}){
    var button=UI.Button(label,()=>{});button.Margin=new Thickness(0,8,0,0);
    if(label=="Hover")button.SetValue(hoverKey,true);
    if(label=="Selected navigation")button.Tag="SelectedNavigation";
    interactionPanel.Children.Add(button);
  }
  var selectedOption=new System.Windows.Controls.ComboBoxItem{Content="Selected dropdown option",IsSelected=true};interactionPanel.Children.Add(selectedOption);
  var selectedList=new System.Windows.Controls.ListBoxItem{Content="Selected friend",IsSelected=true};interactionPanel.Children.Add(selectedList);
  Render(new Window{Width=480,Height=320,Content=interactionPanel},"dist/preview-interactions-light.png");
  selectedOption.ApplyTemplate();selectedList.ApplyTemplate();
  foreach(var control in new System.Windows.Controls.Control[]{selectedOption,selectedList}){
    var frame=(System.Windows.Controls.Border)control.Template.FindName("Frame",control);
    if(((SolidColorBrush)frame.Background).Color.ToString()!="#FFDFE9FF")throw new Exception("Selected control palette differs");
  }
  if(((SolidColorBrush)testRow.Background).Color.ToString()!="#FFDFE9FF")throw new Exception("Existing selected row did not update with theme");
  var lightBrowser=new BrowserWindow(tabsSettings);SelectTab(lightBrowser,"Settings");Render(lightBrowser,"dist/preview-settings-light.png");lightBrowser.Peer.Dispose();
  var lightPrefs=new BrowserWindow(tabsSettings);SelectTab(lightPrefs,"Game preferences");Render(lightPrefs,"dist/preview-preferences-light.png");lightPrefs.Peer.Dispose();
  Appearance.Apply("Dark");
  Console.WriteLine("PASS customization backup, WAV volume processing, and light/dark theme rendering");
  Console.WriteLine("PASS WPF resources, icon, browser and room editor render without opening desktop windows");
 }
 static void Render(Window window,string path)
 {
  var content=(FrameworkElement)window.Content;window.Content=null;
  content.Measure(new Size(window.Width,window.Height));content.Arrange(new Rect(0,0,window.Width,window.Height));content.UpdateLayout();
  var image=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);image.Render(content);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
 }
}

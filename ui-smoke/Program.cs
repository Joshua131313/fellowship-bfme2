using Fellowship;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program
{
 [STAThread] static void Main(string[] args)
 {
  if(args.Length==4&&args[0]=="--setup-probe")
  {LiveProbe.Setup(args[1],int.Parse(args[2]),args[3]).GetAwaiter().GetResult();return;}
  if(args.Length==5&&args[0]=="--live-connection")
  {LiveProbe.Connection(args[1],int.Parse(args[2]),args[3],args[4]).GetAwaiter().GetResult();return;}
  if(args.Length==4&&args[0]=="--live-probe")
  {LiveProbe.Run(args[1],int.Parse(args[2]),args[3]).GetAwaiter().GetResult();return;}
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
  guard.Observe(null,false,true);if(Stable(123))throw new Exception("Transient null setup bypassed teardown gate");
  for(int i=0;i<3;i++)guard.Observe(null,false,true);
  if(!Stable(123)||guard.Stopped||guard.Generation!=2)throw new Exception("Reused-address new lobby did not reconnect");
  guard.Observe(null,true);
  for(int i=0;i<3;i++)guard.Observe(null,false,true);
  if(!Stable(456)||guard.Generation!=3)throw new Exception("Third lobby did not reconnect");
  Console.WriteLine("PASS repeated lobbies, reused addresses, countdown protection and transient-read rejection");
  var preferences=Settings.InMemory();
  var remembered=new Room{Map="Rhûn",MapPath=GameMap.Rhun.Path,Capacity=6,Match=new(){Resources=2000,CommandPoints=400,CustomHeroes=true},Players=[new(){Id="host",Team=3,Spot=2}]};
  preferences.RememberHost(remembered);
  var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(preferences))!;
  var nextRoom=restored.NewHostedRoom();
  if(nextRoom.MapPath!=remembered.MapPath||nextRoom.Match.Resources!=2000||nextRoom.Match.CommandPoints!=400||!nextRoom.Match.CustomHeroes||restored.LastHostedSetup?.Team!=3||restored.LastHostedSetup?.Spot!=2||nextRoom.Players.Count!=0)throw new Exception("Last selections did not survive preferences roundtrip");
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
  var command=NativePlacement.RequestBytes(1234,nativeSnapshot,4,1);
  if(command.Length!=1100||BitConverter.ToInt32(command,4)!=2||BitConverter.ToInt32(command,20)!=3||BitConverter.ToInt32(command,24)!=0||BitConverter.ToInt32(command,28)!=0||System.Text.Encoding.ASCII.GetString(command,56,13)!="maps\\test.map")throw new Exception("Native placement ABI mismatch");
  var setupBytes=NativePlacement.SetupRequestBytes(1234,nativeSnapshot,new("maps\\test.map",4000,200,false,true));
  if(BitConverter.ToInt32(setupBytes,568)!=1||BitConverter.ToInt32(setupBytes,1084)!=4000||BitConverter.ToInt32(setupBytes,1088)!=200||BitConverter.ToInt32(setupBytes,1096)!=1)throw new Exception("Native setup ABI mismatch");
  var random=NativePlacement.RequestBytes(1234,nativeSnapshot,0,0,true);
  if(BitConverter.ToInt32(random,20)!=-1||BitConverter.ToInt32(random,24)!=-1||BitConverter.ToInt32(random,28)!=1)throw new Exception("Native random selection encoding failed");
  try{NativePlacement.RequestBytes(1234,nativeSnapshot,5,1);throw new Exception("Invalid native team accepted");}catch(InvalidDataException){}
  Console.WriteLine("PASS native bridge layout, zero-based conversion, unset choices and bounds");
  if(!SearchableMapBox.Matches("Rhûn","rhun")||!SearchableMapBox.Matches("Woody End","END")||SearchableMapBox.Matches("Rhûn","mordor"))throw new Exception("Map search matching failed");
  var spots=MapPreview.ParseSpots("MapCache maps_5Ctest_2Emap\n extentMin = X:100 Y:200 Z:0\n extentMax = X:1100 Y:2200 Z:0\n numPlayers = 2\n Player_1_Start = X:350 Y:700 Z:0\n Player_2_Start = X:850 Y:1700 Z:0\nEND",@"maps\test.map");
  if(spots.Count!=2||spots[0].X!=0.25||spots[0].Y!=0.75||spots[1].X!=0.75||spots[1].Y!=0.25)throw new Exception("Map spot coordinate normalization failed");
  Console.WriteLine("PASS map bounds, slot numbers and vertical coordinate conversion");
  var preview=MapPreview.Load(GameInstallation.Find(),GameMap.Rhun.Path);
  if(File.Exists(GameInstallation.Find())&&(preview==null||preview.PixelWidth!=320))throw new Exception("Installed map preview failed");
  try{MapPreview.Decode(new byte[18]);throw new Exception("Invalid preview accepted");}catch(InvalidDataException){}
  string ReleaseJson(string tag,string url,string digest)=>System.Text.Json.JsonSerializer.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name="Fellowship-0.1.22-win-x64.zip",browser_download_url=url,digest}}});
  var goodUrl="https://github.com/Joshua131313/fellowship-bfme2/releases/download/v0.1.22/Fellowship-0.1.22-win-x64.zip";
  if(AppUpdates.Parse(ReleaseJson("v0.1.22",goodUrl,"sha256:"+new string('a',64)))==null)throw new Exception("New update not detected");
  if(AppUpdates.Parse(ReleaseJson("v0.1.6",goodUrl,"sha256:"+new string('a',64)))!=null)throw new Exception("Downgrade offered");
  try{AppUpdates.Parse(ReleaseJson("v0.1.22","https://example.com/update.zip","sha256:"+new string('a',64)));throw new Exception("Untrusted update accepted");}catch(InvalidDataException){}
  var msiJson=ReleaseJson("v0.1.22",goodUrl,"sha256:"+new string('a',64)).Replace(".zip",".msi");
  if(AppUpdates.Parse(msiJson) is not {Installer:true})throw new Exception("Installer update not detected");
  Console.WriteLine("PASS installer update selection");
  if(preview!=null){Directory.CreateDirectory("dist");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(preview));using var output=File.Create("dist/preview-overhead.png");encoder.Save(output);}
  Console.WriteLine("PASS installed map preview and update version/source validation");
  Directory.CreateDirectory("dist");
  var app=new App();app.InitializeComponent();
  var picker=new SearchableMapBox{ItemsSource=new[]{"Rhûn","Woody End"},SelectedItem="Rhûn"};
  picker.ApplyTemplate();
  if(picker.Template.FindName("PART_MapSearch",picker) is not System.Windows.Controls.TextBox {Visibility:Visibility.Visible})throw new Exception("Map search template missing");
  var browser=new BrowserWindow(Settings.InMemory());
  Render(browser,"dist/preview-browser.png");
  var editor=new RoomEditor(null,new Settings{GamePath=GameInstallation.Find()},new Room{Title="Battle for Rhun"});
  Render(editor,"dist/preview-room-settings.png");
  browser.Peer.Host(new Room{Title="Inline rules test"});
  if(browser.Peer.Current!.Spots.Count>0)browser.Peer.SelectPlacement(browser.Peer.Current.Spots[0].Number,1).GetAwaiter().GetResult();
  var roomWindow=new RoomWindow(browser);
  var syncCommand=typeof(RoomWindow).GetMethod("SyncCommand",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
  browser.Settings.LiveGameSync=false;
  if(syncCommand.Invoke(roomWindow,null)!=null)throw new Exception("Disabled sync still produces game commands");
  browser.Settings.LiveGameSync=true;
  if(syncCommand.Invoke(roomWindow,null)==null)throw new Exception("Enabled sync failed to resume commands");
  Console.WriteLine("PASS disabling live sync suppresses commands while room stays open; enabling resumes");
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
  browser.Peer.Dispose();
  Console.WriteLine("PASS inline host rule selection saves and survives refresh");
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


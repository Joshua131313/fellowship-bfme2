using Fellowship;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program
{
 [STAThread] static void Main()
 {
  var landmarks=GameNavigation.LoadLandmarks();
  foreach(var (file,expected) in new[]{("main","Main"),("network","Network"),("host","HostLobby"),("multiplayer","Multiplayer")})
  {
   var capture="research/navigation-"+file+".png";
   if(!File.Exists(capture)){Console.WriteLine("SKIP optional local capture: "+file);continue;}
   using var frame=new System.Drawing.Bitmap(capture);
   var found=GameNavigation.Recognize(frame,landmarks);
   if(found!=expected)throw new Exception($"Navigation scene {file}: expected {expected}, found {found}");
   foreach(int width in new[]{2560,5120})
   {
    using var scaled=new System.Drawing.Bitmap(width,width*9/16);
    using(var graphics=System.Drawing.Graphics.FromImage(scaled))graphics.DrawImage(frame,0,0,scaled.Width,scaled.Height);
    if(GameNavigation.Recognize(scaled,landmarks)!=expected)throw new Exception($"Navigation failed at {width} pixels: {expected}");
   }
   Console.WriteLine("PASS navigation recognition at 2048, 2560 and 5120 pixels: "+expected);
  }
  using(var blank=new System.Drawing.Bitmap(2048,1152))
   if(GameNavigation.Recognize(blank,landmarks)!="")throw new Exception("Navigation accepted a blank screen");
  if(!double.IsPositiveInfinity(GameNavigation.Difference([],[])))throw new Exception("Invalid navigation sample accepted");
  Console.WriteLine("PASS unknown/invalid navigation frames rejected");
  var preview=MapPreview.Load(GameInstallation.Find(),GameMap.Rhun.Path);
  if(File.Exists(GameInstallation.Find())&&(preview==null||preview.PixelWidth!=220))throw new Exception("Installed map preview failed");
  try{MapPreview.Decode(new byte[18]);throw new Exception("Invalid preview accepted");}catch(InvalidDataException){}
  string ReleaseJson(string tag,string url,string digest)=>System.Text.Json.JsonSerializer.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name="Fellowship-0.1.8-win-x64.zip",browser_download_url=url,digest}}});
  var goodUrl="https://github.com/Joshua131313/fellowship-bfme2/releases/download/v0.1.8/Fellowship-0.1.8-win-x64.zip";
  if(AppUpdates.Parse(ReleaseJson("v0.1.8",goodUrl,"sha256:"+new string('a',64)))==null)throw new Exception("New update not detected");
  if(AppUpdates.Parse(ReleaseJson("v0.1.6",goodUrl,"sha256:"+new string('a',64)))!=null)throw new Exception("Downgrade offered");
  try{AppUpdates.Parse(ReleaseJson("v0.1.8","https://example.com/update.zip","sha256:"+new string('a',64)));throw new Exception("Untrusted update accepted");}catch(InvalidDataException){}
  Console.WriteLine("PASS installed map preview and update version/source validation");
  Directory.CreateDirectory("dist");
  var app=new App();app.InitializeComponent();
  var browser=new BrowserWindow();
  Render(browser,"dist/preview-browser.png");
  var editor=new RoomEditor(null,new Settings{GamePath=GameInstallation.Find()},new Room{Title="Battle for Rhun"});
  Render(editor,"dist/preview-room-settings.png");
  browser.Peer.Host(new Room{Title="Inline rules test"});
  var roomWindow=new RoomWindow(browser);
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


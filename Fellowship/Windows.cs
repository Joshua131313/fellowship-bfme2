using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
namespace Fellowship;

public static class UI
{
    public const string Muted = "#919AA9", Accent = "#83A5FF", Green = "#74D6AB";
    public static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    public static TextBlock Text(string text, double size = 13, string color = "#E8EBF2", bool bold = false) => new()
    { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text }; if (primary) { b.Background = Brush("#537BEF"); b.BorderBrush = Brush("#698DFC"); b.Foreground = Brushes.White; }
        b.Click += (_, _) => { try { action(); } catch (Exception e) { Error(e.Message); } }; return b;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false, Func<bool>? canExecute = null)
    {
        var b = Button(text, () => { }, primary);
        b.Click += async (_, _) => { b.IsEnabled = false; try { await action(); } catch (Exception e) { Error(e.Message); } finally { if (b.IsLoaded) b.IsEnabled = canExecute?.Invoke()??true; } }; return b;
    }
    public static StackPanel Row(params UIElement[] items) { var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; foreach (var i in items) p.Children.Add(i); return p; }
    public static StackPanel Stack(params UIElement[] items) { var p = new StackPanel(); foreach (var i in items) p.Children.Add(i); return p; }
    public static Border Card(UIElement child, string background = "#1B1F26", int padding = 16) => new() { Background = Brush(background), BorderBrush = Brush("#2C313B"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(padding), Child = child };
    public static Border Badge(string text, string color = Accent) => new() { Background = Brush("#242D3E"), CornerRadius = new(4), Padding = new(8,4,8,4), Child = Text(text, 10, color, true), VerticalAlignment = VerticalAlignment.Center };
    public static Border Avatar(string name, int size = 34) => new() { Width = size, Height = size, CornerRadius = new(8), Background = Brush("#2C3854"), Margin = new(0,0,10,0), Child = new TextBlock { Text = string.IsNullOrWhiteSpace(name) ? "?" : name[..1].ToUpperInvariant(), FontSize = size * .42, Foreground = Brush("#B9CCFF"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    public static void Info(Window owner,string title,string text)
    {
        var window=new Window{Owner=owner};var body=Shell(window,title,480,430);body.Children.Add(Scroll(new Border{Padding=new Thickness(24),Child=Text(text,14)}));window.ShowDialog();
    }
    public static void Error(string text) => MessageBox.Show(text, "Fellowship", MessageBoxButton.OK, MessageBoxImage.Information);
    public static void Reveal(Window window)
    {
        if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
        window.Show();window.Activate();
    }
    public static Grid Shell(Window w, string title, double width, double height)
    {
        w.Style = (Style)Application.Current.FindResource(typeof(Window));
        w.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Fellowship;component/Assets/Fellowship.ico"));
        w.Title = title + " · Fellowship"; w.Width = width; w.Height = height; w.MinWidth = Math.Min(width, 460); w.MinHeight = Math.Min(height, 420);
        w.SourceInitialized+=(_,_)=>
        {
            // Let DWM round the native frame without a transparent layered window.
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
            {
                int preference=2;
                DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(w).Handle,33,ref preference,sizeof(int));
            }
        };
        w.WindowStyle = WindowStyle.None; w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowChrome.SetWindowChrome(w, new() { CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0), CornerRadius = new(8) });
        var root = new DockPanel();
        w.Content = new Border { BorderBrush = Brush("#363C48"), BorderThickness = new(1), Background = Brush("#111419"), Child = root };
        var chrome = new Grid { Height = 48, Background = Brush("#181C22") }; chrome.ColumnDefinitions.Add(new()); chrome.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = Row(Text("◈", 24, Accent), Text("  FELLOWSHIP", 12, "#F0F2F7", true), Text("    /    " + title, 12, Muted)); brand.Margin = new(18,0,0,0); chrome.Children.Add(brand);
        chrome.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else w.DragMove(); };
        Button Caption(string name,string geometry,Action action,bool close=false)
        {
            var button=Button("",action);button.Style=(Style)Application.Current.FindResource("CaptionButton");button.Tag=close?"Close":"";
            button.ToolTip=name;System.Windows.Automation.AutomationProperties.SetName(button,name);
            var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),StrokeThickness=1.2,Width=10,Height=10,Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding("Foreground"){Source=button});button.Content=icon;return button;
        }
        var minimize=Caption("Minimize","M 0,5 L 10,5",()=>w.WindowState=WindowState.Minimized);
        var maximize=Caption("Maximize","M 0,0 L 10,0 10,10 0,10 Z",()=>w.WindowState=w.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
        w.StateChanged+=(_,_)=>
        {
            bool restored=w.WindowState==WindowState.Maximized;maximize.ToolTip=restored?"Restore":"Maximize";
            System.Windows.Automation.AutomationProperties.SetName(maximize,(string)maximize.ToolTip);
            ((System.Windows.Shapes.Path)maximize.Content).Data=Geometry.Parse(restored?"M 2,0 L 10,0 10,8 M 0,2 L 8,2 8,10 0,10 Z":"M 0,0 L 10,0 10,10 0,10 Z");
        };
        var controls=Row(minimize,maximize,Caption("Close","M 0,0 L 10,10 M 0,10 L 10,0",w.Close,true));
        Grid.SetColumn(controls,1); chrome.Children.Add(controls); DockPanel.SetDock(chrome,Dock.Top); root.Children.Add(chrome);
        var body = new Grid(); root.Children.Add(body); return body;
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    public static TextBox Field(Panel p, string label, string value = "", int max = 1000)
    {
        var l = Text(label, 10, Muted, true); l.Margin = new(0,14,0,7); p.Children.Add(l);
        var box = new TextBox { Text = value, MaxLength = max }; p.Children.Add(box); return box;
    }
    public static void At(Grid g, UIElement element, int row, int col = 0) { Grid.SetRow(element,row); Grid.SetColumn(element,col); g.Children.Add(element); }
    public static void Rows(Grid g, params GridLength[] rows) { foreach(var r in rows) g.RowDefinitions.Add(new() { Height = r }); }
    public static GridLength Auto => GridLength.Auto;
    public static GridLength Star => new(1,GridUnitType.Star);
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}

public sealed class BrowserWindow : Window
{
    public Settings Settings { get; } = Settings.Load();
    public PeerService Peer { get; }
    readonly DataGrid rooms = new();
    readonly TextBlock status = UI.Text("Not connected", 11, UI.Muted);
    readonly TextBlock count = UI.Text("0 rooms", 12, UI.Muted);
    readonly TextBox address = new() { Width = 185, MaxLength = 15, ToolTip = "Host's Radmin IPv4 address" };
    readonly TextBox search = new() { Width = 230, ToolTip = "Filter rooms by name, host or map" };
    readonly Border empty;
    readonly TextBlock emptyTitle=UI.Text("Your next battle is out there",19,"#DCE2ED",true);
    readonly TextBlock emptyHint=UI.Text("Public rooms on your VPN appear automatically.\nIf a room is missing, enter the host's VPN address and refresh.",13,UI.Muted);
    readonly List<RoomEntry> discovered = [];
    RoomWindow? roomWindow;
    FriendsWindow? friendsWindow;
    readonly Dictionary<string,ChatWindow> chats = new();
    bool online,refreshing;
    readonly System.Windows.Threading.DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    readonly HashSet<string> discoveredAddresses=[];
    public BrowserWindow()
    {
        if(!File.Exists(Settings.GamePath))Settings.GamePath=GameInstallation.Find();
        Peer = new(Settings);
        var body = UI.Shell(this,"Room browser",1080,700); MinWidth = 900; MinHeight = 600;
        UI.Rows(body,UI.Auto,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        var toolbar = new Grid { Margin = new(22,20,14,18) }; toolbar.ColumnDefinitions.Add(new()); toolbar.ColumnDefinitions.Add(new() { Width = UI.Auto });
        var headings = UI.Stack(UI.Text("Game rooms",25,"#F3F4F8",true), UI.Text("A good game starts with good company.",12,UI.Muted)); headings.Children[1].SetValue(MarginProperty,new Thickness(0,5,0,0)); toolbar.Children.Add(headings);
        var actions = UI.Row(UI.Button("♧  Friends",OpenFriends),UI.Button("⚙  Settings",OpenSettings),UI.Button("＋  Host room",CreateRoom,true)); Grid.SetColumn(actions,1); toolbar.Children.Add(actions); UI.At(body,toolbar,0);
        var filters = new Grid { Margin = new(22,0,22,14) }; filters.ColumnDefinitions.Add(new()); filters.ColumnDefinitions.Add(new() { Width = UI.Auto });
        filters.Children.Add(UI.Row(UI.Badge("THE BATTLE FOR MIDDLE-EARTH II  ·  1.00"), UI.Text("    Radmin VPN",12,UI.Muted)));
        var filterRight = UI.Row(UI.Text("Find  ",12,UI.Muted),search); Grid.SetColumn(filterRight,1); filters.Children.Add(filterRight); UI.At(body,filters,1);
        var table = new Grid();
        void Column(string heading, string property, double width, bool star = false) => rooms.Columns.Add(new DataGridTextColumn { Header = heading, Binding = new Binding(property), MinWidth = star ? (property == "Title" ? 180 : 100) : width, Width = star ? new DataGridLength(width,DataGridLengthUnitType.Star) : new DataGridLength(width) });
        Column("ROOM NAME","Title",2,true); Column("HOST","Host",1,true); Column("MAP","Map",1.25,true); Column("PLAYERS","Slots",75); Column("PATCH","Patch",75); Column("STATUS","Status",110);
        table.Children.Add(rooms);
        var emptyContent = UI.Stack(UI.Text("◈",42,"#5B719F",true),emptyTitle,emptyHint);
        foreach (FrameworkElement el in emptyContent.Children) { el.HorizontalAlignment = HorizontalAlignment.Center; el.Margin = new(0,0,0,12); if(el is TextBlock tb)tb.TextAlignment = TextAlignment.Center; }
        empty = new Border { Child = emptyContent, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new(0,38,0,0) }; table.Children.Add(empty);
        var tableCard = UI.Card(table,"#171A20",0); tableCard.Margin = new(22,0,22,0); UI.At(body,tableCard,2);
        var footer = new Grid { Margin = new(22,14,14,18) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = UI.Auto });
        var direct = UI.Row(UI.Text("HOST IP  ",10,UI.Muted,true),address,UI.AsyncButton("↻  Refresh",Refresh),UI.AsyncButton("Join room  →",Join,true)); address.Margin = new(0,0,10,0); footer.Children.Add(direct);
        count.Margin = new(10,0,8,0); Grid.SetColumn(count,1); footer.Children.Add(count); UI.At(body,footer,3);
        var bottom = new Grid { Background = UI.Brush("#181C22"), Height = 36 }; bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = UI.Auto }); status.Margin = new(22,0,0,0); bottom.Children.Add(status);
        var hint = UI.Text("v0.1.15   ·   RUNS IN SYSTEM TRAY",9,UI.Muted,true); hint.Margin = new(0,0,22,0); Grid.SetColumn(hint,1); bottom.Children.Add(hint); UI.At(body,bottom,4);
        var roomMenu=new ContextMenu();
        var preview=new MenuItem{Header="Room preview"};preview.Click+=(_,_)=>{if(rooms.SelectedItem is RoomEntry entry)UI.Info(this,"Room preview",$"{entry.Room.Title}\nHosted by {entry.Host}\n{entry.Room.Map} · {entry.Room.Players.Count}/{entry.Room.Capacity} players\n{entry.Room.Match.Summary}\n{(entry.Room.FriendsOnly?"Friends only":"Public on VPN")}\n\n"+string.Join("\n",entry.Room.Players.Select(p=>$"{p.Name} · {(p.Ready?"Ready":"Not ready")}")));};
        var joinItem=new MenuItem{Header="Join / open room"};joinItem.Click+=async(_,_)=>{try{await Join();}catch(Exception ex){UI.Error(ex.Message);}};
        roomMenu.Items.Add(joinItem);roomMenu.Items.Add(preview);rooms.ContextMenu=roomMenu;
        rooms.PreviewMouseRightButtonDown+=(_,e)=>{var row=ItemsControl.ContainerFromElement(rooms,e.OriginalSource as DependencyObject) as DataGridRow;if(row!=null)row.IsSelected=true;};
        Peer.Discovered+=ip=>Dispatcher.InvokeAsync(()=>{if(discoveredAddresses.Count<256)discoveredAddresses.Add(ip);});
        refreshTimer.Tick+=async(_,_)=>{if(online)try{await Refresh();}catch{}};
        refreshTimer.Start();
        search.TextChanged += (_,_) => RenderRooms();
        rooms.SelectionChanged += (_,_) => { if(rooms.SelectedItem is RoomEntry r) address.Text = r.Address; };
        rooms.MouseDoubleClick += async (_,_) => { if(rooms.SelectedItem != null) try { await Join(); } catch(Exception e){ UI.Error(e.Message); } };
        Peer.Changed += () => Dispatcher.InvokeAsync(() => { roomWindow?.Refresh(); if(Peer.Current != null && roomWindow == null) ShowRoom(); RenderRooms(); });
        Peer.Notice += text => Dispatcher.InvokeAsync(() => status.Text = text);
        Peer.LaunchRequested += () => Dispatcher.InvokeAsync(() => roomWindow?.HandleLaunch());
        Peer.PrivateMessage += (ip,name,text) => Dispatcher.InvokeAsync(() =>
        {
            OpenChat(ip,name,false).Receive(name,text);
            if(Settings.MessageSounds)MessageAlert.Play();
        });
        Closing += (_,e) => { if(Application.Current is App { IsQuitting:false }){e.Cancel=true;Hide();} };
        Closed += (_,_) => { refreshTimer.Stop();Peer.Dispose(); };
        Loaded += (_,_) => { if(string.IsNullOrEmpty(Settings.BindAddress)) OpenSettings(); else EnsureOnline(); };
    }
    void RenderRooms()
    {
        var list = discovered.Where(r => r.Address != Peer.Address).ToList();
        if(Peer.Current is {} current){var host=Peer.IsHost?Peer.Address:current.HostAddress;list.RemoveAll(r=>r.Room.Id==current.Id||r.Address==host);list.Insert(0,new(host,current));}
        string filter = search.Text.Trim();
        emptyTitle.Text=filter.Length>0?"No matching rooms":"Your next battle is out there";
        emptyHint.Text=filter.Length>0?"Try another room name, host or map.":"Public rooms on your VPN appear automatically.\nIf a room is missing, enter the host's VPN address and refresh.";
        list = list.Where(r => $"{r.Title} {r.Host} {r.Map}".Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        var selected=(rooms.SelectedItem as RoomEntry)?.Room.Id;
        rooms.ItemsSource = list;if(selected!=null)rooms.SelectedItem=list.FirstOrDefault(r=>r.Room.Id==selected); empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed; count.Text = $"{list.Count} room{(list.Count == 1 ? "" : "s")}";
    }
    public bool EnsureOnline(bool quiet=false)
    {
        if(online)return true;
        try
        {
            if(string.IsNullOrWhiteSpace(Settings.BindAddress)){ if(!quiet)UI.Error("Select your Radmin VPN interface in Settings first."); return false; }
            Peer.Start(Settings.BindAddress); online = true; status.Text = $"●  {Settings.Name}   ·   {Peer.Address}   ·   Ready to play"; status.Foreground = UI.Brush(UI.Green); return true;
        }
        catch(Exception e)
        {
            status.Text="○  Connection unavailable · check Settings";status.Foreground=UI.Brush("#E5B77A");
            var message=e is System.Net.Sockets.SocketException {SocketErrorCode:System.Net.Sockets.SocketError.AddressAlreadyInUse}
                ? "This VPN address is already in use on port 28742. Close any older Fellowship version, then click Refresh to retry."
                : "Could not connect on the selected interface. Check that Radmin VPN is connected and choose its current address in Settings.\n\n"+e.Message;
            if(!quiet)UI.Error(message);return false;
        }
    }
    public void OpenSettings()
    {
        var w = new Window { Owner = this }; var body = UI.Shell(w,"Settings",560,630); w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var p = new StackPanel { Margin = new(24,20,24,24) }; body.Children.Add(UI.Scroll(p));
        p.Children.Add(UI.Text("Ready when you are.",23,"#F1F3F8",true)); p.Children.Add(UI.Text("Set up your player profile and local connection.",12,UI.Muted));
        var name = UI.Field(p,"DISPLAY NAME",Settings.Name,40);
        var game = UI.Field(p,"BFME2 EXECUTABLE",Settings.GamePath);
        var installed = @"C:\Program Files (x86)\Electronic Arts\The Battle for Middle-earth (tm) II\lotrbfme2.exe";
        if(game.Text == "" && File.Exists(installed))game.Text = installed;
        var browse = UI.Button("Browse files…",() => { var d = new OpenFileDialog { Filter = "Game executable (*.exe)|*.exe" }; if(d.ShowDialog(w)==true)game.Text = d.FileName; }); browse.HorizontalAlignment = HorizontalAlignment.Left; browse.Margin = new(0,8,0,0); p.Children.Add(browse);
        var label = UI.Text("NETWORK INTERFACE",10,UI.Muted,true); label.Margin = new(0,18,0,7); p.Children.Add(label);
        var interfaces = PeerService.Interfaces(); var network = new ComboBox { ItemsSource = interfaces.Select(i => $"{i.Name}  ·  {i.Address}").ToList() };
        network.SelectedIndex = interfaces.FindIndex(i => i.Address == Settings.BindAddress);
        if(network.SelectedIndex<0)network.SelectedIndex = interfaces.FindIndex(i => i.Name.Contains("Radmin",StringComparison.OrdinalIgnoreCase));
        p.Children.Add(network);
        var info = UI.Text("Choose Radmin VPN on both PCs. Interface changes need an app restart. BFME2's Network IP should use the same adapter.",12,UI.Muted); info.Margin = new(0,12,0,18); p.Children.Add(info);
        var sounds=new CheckBox{Content="Play a sound for incoming private messages",IsChecked=Settings.MessageSounds,Margin=new(0,0,0,20)};p.Children.Add(sounds);
        var startup=new CheckBox{Content="Launch Fellowship at Windows sign-in",IsChecked=Installation.StartsWithWindows,IsEnabled=Installation.IsInstalled,Margin=new(0,0,0,10)};p.Children.Add(startup);
        p.Children.Add(UI.Text(Installation.IsInstalled?"Starts quietly in the system tray.":"Install Fellowship to enable launch at sign-in.",11,UI.Muted));
        p.Children.Add(UI.Button("Open preferences folder",()=>{Directory.CreateDirectory(Settings.Folder);Process.Start(new ProcessStartInfo(Settings.Folder){UseShellExecute=true});}));
        p.Children.Add(UI.Button("Save settings",() =>
        {
            if(name.Text.Trim().Length==0 || network.SelectedIndex<0){UI.Error("Enter a name and choose a network interface.");return;}
            Settings.Name=name.Text.Trim();Settings.GamePath=game.Text.Trim();Settings.BindAddress=interfaces[network.SelectedIndex].Address;Settings.MessageSounds=sounds.IsChecked==true;Settings.MenuNavigation=false;if(Installation.IsInstalled)Installation.SetStartup(startup.IsChecked==true);Settings.Save();w.Close();EnsureOnline();
        },true)); p.Children.Add(AppUpdates.Controls(()=>Peer.Current!=null)); w.ShowDialog();
    }
    async Task Refresh()
    {
        if(refreshing||!EnsureOnline())return;
        refreshing=true;
        try
        {
            await Peer.Discover();
            var addresses=Settings.Friends.Select(f=>f.Address).Concat(discoveredAddresses).Append(address.Text.Trim()).Where(a=>a.Length>0&&a!=Peer.Address).Distinct().ToList();
            var results=await Task.WhenAll(addresses.Select(async ip=>{try{return(ip,room:await PeerService.Query(ip,Peer.Address),reachable:true);}catch{return(ip,room:(Room?)null,reachable:false);}}));
            discovered.Clear();foreach(var(ip,room,_)in results)if(room!=null)discovered.Add(new(ip,room));
            foreach(var result in results.Where(r=>!r.reachable))discoveredAddresses.Remove(result.ip);
            RenderRooms();
            status.Text=$"●  {Peer.Address} · Discovering on VPN · {discovered.Count} room(s) · {results.Count(r=>!r.reachable)} unreachable";
        }
        finally{refreshing=false;}
    }
    async Task Join()
    {
        if(!EnsureOnline())return;
        if(Peer.Current!=null&&rooms.SelectedItem is RoomEntry selected&&selected.Room.Id==Peer.Current.Id){ShowRoom();return;}
        if(address.Text.Trim()==Peer.Address && Peer.IsHost){ShowRoom();return;}
        if(!CanLeave())return;
        await CloseRoom();await Peer.Join(address.Text.Trim());ShowRoom();
    }
    bool CanLeave()=>Peer.Current==null || MessageBox.Show("Leave your current room? If you host, the next player takes over.","Switch room",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
    async void CreateRoom()
    {
        if(!EnsureOnline() || !CanLeave())return;
        var editor=new RoomEditor(this,Settings,new(){Title=Settings.Name+"'s room"});
        if(editor.ShowDialog()==true&&editor.Result is {} room){try{await CloseRoom();Peer.Host(room);ShowRoom();}catch(Exception e){UI.Error(e.Message);}}
    }

    bool closingRoom;
    public async Task CloseRoom()
    {
        if(closingRoom)throw new InvalidOperationException("Leaving the room is already in progress.");closingRoom=true;
        try{await Peer.LeaveRoom();var old=roomWindow;roomWindow=null;if(old!=null){old.AllowClose=true;old.Close();}}
        finally{closingRoom=false;}
    }
    public void OpenCurrentRoom(){if(Peer.Current!=null)ShowRoom();}
    void ShowRoom()
    {
        if(roomWindow!=null){UI.Reveal(roomWindow);return;}
        // Non-modal windows are peers: ownership would force them above the browser.
        roomWindow=new(this);roomWindow.Closed+=(_,_)=>{roomWindow=null;};roomWindow.Show();
    }
    public void OpenFriends()
    {
        if(friendsWindow!=null){UI.Reveal(friendsWindow);return;}
        friendsWindow=new(this);friendsWindow.Closed+=(_,_)=>friendsWindow=null;friendsWindow.Show();
    }
    public ChatWindow OpenChat(string ip,string name,bool activate=true)
    {
        if(chats.TryGetValue(ip,out var old)){if(activate)UI.Reveal(old);return old;}
        var w=new ChatWindow(this,ip,name){ShowActivated=activate};chats[ip]=w;w.Closed+=(_,_)=>chats.Remove(ip);w.Show();return w;
    }
    sealed record RoomEntry(string Address,Room Room)
    {
        public string Title=>Room.Title; public string Host=>Room.Host; public string Map=>Room.Map;
        public string Slots=>$"{Room.Players.Count} / {Room.Capacity}";public string Patch=>Room.Patch;
        public string Status=>Room.Phase=="Gathering"?(Room.FriendsOnly?"●  Friends only":"●  Public"):"↗  In game";
    }
}

public sealed class RoomWindow : Window
{
    public bool AllowClose { get; set; }
    readonly BrowserWindow app;
    readonly TextBlock title=UI.Text("Connecting…",23,"#F2F4F9",true);
    readonly TextBlock phase=UI.Text("Waiting for host",11,UI.Green);
    readonly TextBlock playerCount=UI.Text("PLAYERS",10,UI.Muted,true);
    readonly TextBlock presetStatus=UI.Text("",11,UI.Green,true);
    readonly TextBlock setupSummary=UI.Text("",13,"#E2E8F5");
    readonly TextBlock readySummary=UI.Text("",11,UI.Muted);
    readonly StackPanel customSetup=new();
    readonly TextBox roomName=new(){MaxLength=80};
    readonly ComboBox privacyChoice=new(){ItemsSource=new[]{"Public on VPN","Friends only"}};
    readonly MapPreview mapPreview=new();
    readonly ComboBox teamChoice=new();
    readonly TextBlock placementStatus=UI.Text("",11,UI.Muted);
    readonly Button clearSpot;
    bool syncingPlacement,changingPlacement,watchingNative;
    readonly System.Threading.CancellationTokenSource syncLifetime=new();
    readonly TextBlock liveStatus=UI.Text("Live sync starts when BFME2 opens.",10,UI.Muted);
    sealed record TeamOption(int Value){public override string ToString()=>Value==0?"No team / free for all":"Team "+Value;}
    string lastRoomName="";bool? lastPrivacy;
    readonly ListBox players=new();
    readonly TextBox map,patch,rules;
    readonly StackPanel messages=new(){Margin=new(14,12,14,12)};
    readonly ScrollViewer chatScroll;
    readonly CheckBox consent=new(){Content="Ready · allow host to launch BFME2"};
    readonly Button launch;
    readonly Border setupBanner;
    readonly StackPanel inlineRules=new();
    readonly ComboBox resourceChoice=new(), commandChoice=new(), mapChoice=new SearchableMapBox(), capacityChoice=new();
    readonly CheckBox customChoice=new(){Content="Custom Heroes"}, ringChoice=new(){Content="Ring Heroes"};
    readonly TextBlock ruleStatus=UI.Text("Changes save automatically",10,UI.Muted);
    bool syncingRules, savingRules;
    sealed record CommandFactor(int Value){public override string ToString()=>$"{Value/100.0:0.##}×";}
    bool allowLaunch;
    string lastSettings="";
    public RoomWindow(BrowserWindow app)
    {
        this.app=app;var body=UI.Shell(this,"Battle room",1020,760);MinWidth=900;MinHeight=660;
        UI.Rows(body,UI.Auto,UI.Star,UI.Auto);
        var top=new Grid{Margin=new(22,18,22,18)};top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var titles=UI.Stack(title,phase);phase.Margin=new(0,6,0,0);top.Children.Add(titles);
        var tag=UI.Badge("BFME II  1.00   /   VPN");Grid.SetColumn(tag,1);top.Children.Add(tag);UI.At(body,top,0);
        var columns=new Grid{Margin=new(22,0,22,16)};columns.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new(300)});
        var left=new Grid{Margin=new(0,0,14,0)};UI.Rows(left,new GridLength(210),new GridLength(12),UI.Star);
        var roster=new Grid();UI.Rows(roster,UI.Auto,UI.Star);playerCount.Margin=new(14,12,14,6);UI.At(roster,playerCount,0);UI.At(roster,players,1);UI.At(left,UI.Card(roster,"#191D24",0),0);
        var chat=new Grid();UI.Rows(chat,UI.Auto,UI.Star,UI.Auto);
        var chatHead=UI.Text("ROOM CHAT",10,UI.Muted,true);chatHead.Margin=new(14,12,0,5);UI.At(chat,chatHead,0);
        chatScroll=UI.Scroll(messages);UI.At(chat,chatScroll,1);
        var compose=new Grid{Margin=new(12)};compose.ColumnDefinitions.Add(new());compose.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var input=new TextBox{MaxLength=2000,ToolTip="Message your room · Enter to send",Margin=new(0,0,8,0)};
        async Task Send(){if(string.IsNullOrWhiteSpace(input.Text))return;await app.Peer.Say(input.Text);input.Clear();}
        input.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;try{await Send();}catch(Exception ex){UI.Error(ex.Message);}}};
        compose.Children.Add(input);var send=UI.AsyncButton("Send",Send,true);send.Margin=new(0);Grid.SetColumn(send,1);compose.Children.Add(send);UI.At(chat,compose,2);UI.At(left,UI.Card(chat,"#191D24",0),2);columns.Children.Add(left);
        var right=new StackPanel();right.Children.Add(UI.Text("MATCH SETUP",10,UI.Muted,true));
        setupSummary.Margin=new(0,12,0,0);setupSummary.LineHeight=22;
        var banner=setupBanner=UI.Card(UI.Stack(UI.Text("THE BATTLE FOR MIDDLE-EARTH II",10,"#AABCE1",true),setupSummary),"#252F43",16);banner.Margin=new(0,14,0,12);right.Children.Add(banner);
        right.Children.Add(presetStatus);right.Children.Add(mapPreview);
        right.Children.Add(UI.Text("YOUR TEAM",10,UI.Muted,true));teamChoice.ItemsSource=Enumerable.Range(0,5).Select(n=>new TeamOption(n)).ToArray();teamChoice.Margin=new(0,6,0,8);right.Children.Add(teamChoice);
        teamChoice.SelectionChanged+=async(_,_)=>{if(!syncingPlacement&&teamChoice.SelectedItem is TeamOption team)await ChangePlacement(null,team.Value);};
        mapPreview.SpotSelected+=async spot=>await ChangePlacement(spot,null);
        clearSpot=UI.AsyncButton("Clear my spot",()=>ChangePlacement(0,null));clearSpot.Margin=new(0,0,0,8);right.Children.Add(clearSpot);
        right.Children.Add(placementStatus);right.Children.Add(liveStatus);var placementHint=UI.Text("Click an empty spot to claim it. Teams and spots sync both ways in the BFME2 LAN lobby. The host's map and rules sync too. Approve Windows permission once per game session.",10,UI.Muted);placementHint.Margin=new(0,8,0,12);right.Children.Add(placementHint);
        map=new TextBox();patch=new TextBox();rules=new TextBox();
        void AddChoice(string label,ComboBox choice,object[] values)
        {
            var caption=UI.Text(label,10,UI.Muted,true);caption.Margin=new(0,12,0,6);
            inlineRules.Children.Add(caption);choice.ItemsSource=values;inlineRules.Children.Add(choice);
            choice.SelectionChanged+=async(_,_)=>await SaveInlineRules();
        }
        AddChoice("MAP",mapChoice,GameInstallation.Maps(app.Settings.GamePath).Cast<object>().ToArray());
        AddChoice("PLAYER LIMIT",capacityChoice,Enumerable.Range(2,7).Cast<object>().ToArray());
        AddChoice("STARTING RESOURCES",resourceChoice,MatchRules.ResourceValues.Cast<object>().ToArray());
        AddChoice("COMMAND POINTS",commandChoice,MatchRules.CommandValues.Select(n=>(object)new CommandFactor(n)).ToArray());
        foreach(var check in new[]{customChoice,ringChoice})
        {
            check.Margin=new(0,12,0,0);inlineRules.Children.Add(check);
            check.Click+=async(_,_)=>await SaveInlineRules();
        }
        ruleStatus.Margin=new(0,12,0,8);inlineRules.Children.Add(ruleStatus);right.Children.Add(inlineRules);
        customSetup.Margin=new(0,18,0,0);
        customSetup.Children.Add(UI.Text("ROOM NAME",10,UI.Muted,true));roomName.Margin=new(0,8,0,8);customSetup.Children.Add(roomName);
        var confirmName=UI.AsyncButton("Confirm name",()=>SaveRoomDetails(false));confirmName.Margin=new(0,4,0,0);confirmName.HorizontalAlignment=HorizontalAlignment.Stretch;customSetup.Children.Add(confirmName);
        var privacyLabel=UI.Text("VISIBILITY",10,UI.Muted,true);privacyLabel.Margin=new(0,16,0,8);customSetup.Children.Add(privacyLabel);
        customSetup.Children.Add(privacyChoice);var confirmPrivacy=UI.AsyncButton("Confirm privacy",()=>SaveRoomDetails(true));confirmPrivacy.Margin=new(0,10,0,0);confirmPrivacy.HorizontalAlignment=HorizontalAlignment.Stretch;customSetup.Children.Add(confirmPrivacy);
        right.Children.Add(customSetup);
        var guide=UI.Stack(UI.Text("IN BFME2",10,UI.Muted,true),UI.Text("1   Open Multiplayer → Network\n2   Host creates; guests join\n3   Fellowship applies your team and spot",12,"#B5C0D3"));guide.Margin=new(0,20,0,0);guide.Children[1].SetValue(MarginProperty,new Thickness(0,8,0,0));right.Children.Add(guide);
        var note=UI.Text("The Fellowship host must also host BFME2. Map and rules sync through the host; each player's team and spot sync locally. Guests still join the native lobby manually.",11,"#B5A98D");note.Margin=new(0,14,0,0);right.Children.Add(note);
        var settingsCard=UI.Card(UI.Scroll(right),"#191D24",16);Grid.SetColumn(settingsCard,1);columns.Children.Add(settingsCard);UI.At(body,columns,1);
        var bottom=new Grid{Background=UI.Brush("#191D24"),Margin=new(0)};bottom.ColumnDefinitions.Add(new());bottom.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var state=UI.Stack(consent,readySummary,UI.Text("Room stays open when you exit the game.",10,UI.Muted));state.Margin=new(22,14,10,14);readySummary.Margin=new(0,6,0,6);bottom.Children.Add(state);
        consent.Click+=async(_,_)=>{try{if(consent.IsChecked==true)GameLauncher.Validate(app.Settings.GamePath);allowLaunch=consent.IsChecked==true;await app.Peer.SetReady(allowLaunch);}catch(Exception e){allowLaunch=false;consent.IsChecked=false;UI.Error(e.Message);}};
        launch=UI.AsyncButton("▶  Launch all",async()=>{GameLauncher.Validate(app.Settings.GamePath);if(HostPreset.Matches(app.Peer.Current))HostPreset.ValidateBinary(app.Settings.GamePath);await app.Peer.Launch();},true,()=>!launching&&app.Peer.Current?.Players.All(p=>p.Ready)==true);
        var actions=UI.Row(UI.AsyncButton("Leave",app.CloseRoom),UI.Button("Launch mine",LaunchLocal),launch);actions.Margin=new(0,12,14,12);Grid.SetColumn(actions,1);bottom.Children.Add(actions);UI.At(body,bottom,2);
        Append("SYSTEM","Welcome to the room. Match patches with your friends, agree on the settings, then ready up.");
        Closing+=async(_,e)=>
        {
            if(!AllowClose&&Application.Current is App {IsQuitting:false})
            {e.Cancel=true;try{await app.CloseRoom();}catch(Exception ex){UI.Error(ex.Message);}}
        };
        app.Peer.Chat+=OnChat;Closed+=(_,_)=>{app.Peer.Chat-=OnChat;syncLifetime.Cancel();};Refresh();
        Loaded+=async(_,_)=>await WatchNative();
    }
    LivePlacement.Command? SyncCommand()
    {
        if(syncLifetime.IsCancellationRequested||app.Peer.Current is not {} room)return null;
        var player=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId);
        return player==null?null:new(room.Id,room.MapPath,room.HostAddress,new(player.Team,player.Spot),app.Peer.IsHost?NativeSetup.From(room):null);
    }
    async Task WatchNative()
    {
        if(watchingNative)return;watchingNative=true;
        int attempted=0;
        try
        {
            while(!syncLifetime.IsCancellationRequested)
            {
                int pid=0;
                foreach(var process in System.Diagnostics.Process.GetProcessesByName("game.dat").Concat(System.Diagnostics.Process.GetProcessesByName("game")))
                    using(process){pid=process.Id;break;}
                if(pid==0)attempted=0;
                if(pid!=0&&pid!=attempted&&!launching&&SyncCommand()!=null)
                {
                    attempted=pid;
                    try
                    {
                        await LivePlacement.Listen(app.Settings.GamePath,pid,SyncCommand,async choice=>
                        {
                            changingPlacement=true;Refresh();
                            try{await app.Peer.SelectPlacement(choice.Spot,choice.Team);}
                            finally{changingPlacement=false;Refresh();}
                        },async setup=>
                        {
                            if(!app.Peer.IsHost||app.Peer.Current is not {} room)return;
                            var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(room))!;
                            var selected=GameInstallation.Maps(app.Settings.GamePath).FirstOrDefault(m=>m.Path.Equals(setup.Map,StringComparison.OrdinalIgnoreCase))??throw new IOException("Native map is not installed.");
                            draft.Map=selected.Name;draft.MapPath=selected.Path;draft.Match=setup.Rules;
                            savingRules=true;Refresh();
                            try{await app.Peer.EditRoom(draft);}
                            finally{savingRules=false;Refresh();}
                        },message=>liveStatus.Text=message,syncLifetime.Token);
                    }
                    catch(OperationCanceledException) when(syncLifetime.IsCancellationRequested){break;}
                    catch(Exception error)
                    {
                        liveStatus.Text="Live sync paused. Native lobby remains available.";
                        Append("SYSTEM","Live sync stopped: "+error.Message+" Reopen the Fellowship room to reconnect. You can correct selections in BFME2 manually.");
                    }
                }
                await Task.Delay(1000,syncLifetime.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    async Task ChangePlacement(int? spot,int? team)
    {
        if(changingPlacement)return;
        changingPlacement=true;teamChoice.IsEnabled=false;clearSpot.IsEnabled=false;placementStatus.Text="Confirming with host…";
        try{await app.Peer.SelectPlacement(spot,team);}
        catch(Exception e){Append("SYSTEM",e.Message);}
        finally{changingPlacement=false;Refresh();}
    }
    async Task SaveRoomDetails(bool privacy)
    {
        if(!app.Peer.IsHost||app.Peer.Current is not {} current)return;
        var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;
        if(privacy)draft.FriendsOnly=privacyChoice.SelectedIndex==1;
        else{if(string.IsNullOrWhiteSpace(roomName.Text)){ruleStatus.Text="Enter a room name.";return;}draft.Title=roomName.Text.Trim();}
        customSetup.IsEnabled=false;
        try{await app.Peer.EditRoom(draft);ruleStatus.Text=privacy?"Visibility updated":"Room name updated";}
        catch(Exception e){ruleStatus.Text=e.Message;}
        finally{customSetup.IsEnabled=true;Refresh();}
    }
    async Task SaveInlineRules()
    {
        if(syncingRules||savingRules||!app.Peer.IsHost||app.Peer.Current is not {} current||
           mapChoice.SelectedItem is not GameMap selectedMap||resourceChoice.SelectedItem is not int resources||
           commandChoice.SelectedItem is not CommandFactor factor||capacityChoice.SelectedItem is not int capacity)return;
        var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;
        draft.Map=selectedMap.Name;draft.MapPath=selectedMap.Path;draft.Capacity=capacity;
        draft.Match=new(){Resources=resources,CommandPoints=factor.Value,CustomHeroes=customChoice.IsChecked==true,RingHeroes=ringChoice.IsChecked==true};
        savingRules=true;inlineRules.IsEnabled=false;ruleStatus.Text="Saving…";
        try{await app.Peer.EditRoom(draft);ruleStatus.Text="Saved · players must ready up again";}
        catch(Exception e){ruleStatus.Text=e.Message;}
        finally{savingRules=false;inlineRules.IsEnabled=true;Refresh();}
    }
    void SyncInlineRules(Room room)
    {
        syncingRules=true;
        try
        {
            mapChoice.SelectedItem=mapChoice.Items.Cast<GameMap>().FirstOrDefault(m=>m.Path==room.MapPath);
            capacityChoice.SelectedItem=room.Capacity;resourceChoice.SelectedItem=room.Match.Resources;
            commandChoice.SelectedItem=commandChoice.Items.Cast<CommandFactor>().FirstOrDefault(f=>f.Value==room.Match.CommandPoints);
            customChoice.IsChecked=room.Match.CustomHeroes;ringChoice.IsChecked=room.Match.RingHeroes;
        }
        finally{syncingRules=false;}
    }
    void OnChat(string text)=>Dispatcher.InvokeAsync(()=>{var split=text.IndexOf("   ",StringComparison.Ordinal);Append(split<0?"Room":text[..split],split<0?text:text[(split+3)..]);});
    void Append(string name,string text)
    {
        var line=UI.Stack(UI.Text(name+"  ·  "+DateTime.Now.ToString("HH:mm"),10,name=="SYSTEM"?UI.Muted:UI.Accent,true),UI.Text(text,12,name=="SYSTEM"?UI.Muted:"#DEE3ED"));line.Margin=new(0,0,0,12);line.Children[1].SetValue(MarginProperty,new Thickness(0,4,0,0));messages.Children.Add(line);if(messages.Children.Count>200)messages.Children.RemoveAt(0);chatScroll?.ScrollToEnd();
    }
    public void Refresh()
    {
        var room=app.Peer.Current;
        if(room==null){players.Items.Clear();playerCount.Text="PLAYERS · DISCONNECTED";allowLaunch=false;phase.Text="Disconnected · rejoin from the room browser";launch.IsEnabled=false;consent.IsEnabled=false;customSetup.IsEnabled=false;inlineRules.IsEnabled=false;mapPreview.IsEnabled=false;teamChoice.IsEnabled=false;clearSpot.IsEnabled=false;readySummary.Text="Return to Game rooms to reconnect.";return;}
        mapPreview.ShowMap(app.Settings.GamePath,room.MapPath);mapPreview.IsEnabled=!launching&&!changingPlacement;
        mapPreview.ShowSelections(room,app.Peer.LocalId,!launching&&!changingPlacement);
        var self=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId);
        syncingPlacement=true;
        try{teamChoice.SelectedItem=teamChoice.Items.Cast<TeamOption>().FirstOrDefault(t=>t.Value==(self?.Team??0));}
        finally{syncingPlacement=false;}
        teamChoice.IsEnabled=!launching&&!changingPlacement;clearSpot.IsEnabled=!launching&&!changingPlacement&&self?.Spot>0;
        placementStatus.Text=changingPlacement?"Confirming with host…":room.Spots.Count==0?"Starting spots unavailable for this map.":$"Your spot: {(self?.Spot>0?self.Spot.ToString():"not selected")} · {(self?.Team>0?"Team "+self.Team:"No team")}";
        if(lastRoomName!=room.Title){roomName.Text=room.Title;lastRoomName=room.Title;}
        if(lastPrivacy!=room.FriendsOnly){privacyChoice.SelectedIndex=room.FriendsOnly?1:0;lastPrivacy=room.FriendsOnly;}
        title.Text=room.Title;phase.Text=$"●  {room.Phase}   ·   Hosted by {room.Host}";playerCount.Text=$"PLAYERS   {room.Players.Count} / {room.Capacity}";
        players.Items.Clear();
        foreach(var p in room.Players)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=UI.Auto});
            row.Children.Add(UI.Row(UI.Avatar(p.Name,30),UI.Text(p.Name,13,"#E6EAF2",true),UI.Text(p.Id=="host"?"   HOST":"",9,UI.Accent,true)));
            var ready=UI.Text($"{(p.Spot>0?"Spot "+p.Spot:"No spot")} · {(p.Team>0?"Team "+p.Team:"No team")}  |  "+(p.Ready?"● Ready":"○ Not ready"),10,p.Ready?UI.Green:MapPreview.TeamColor(p.Team));Grid.SetColumn(ready,1);row.Children.Add(ready);
            var item=new ListBoxItem{Content=row,Padding=new(12,6,12,6),Tag=p};
            var menu=new ContextMenu();var info=new MenuItem{Header="Player info"};info.Click+=(_,_)=>UI.Info(this,"Player info",$"{p.Name}\nVPN address: {p.Address}\n{(p.Id=="host"?"Host":"Guest")} · {(p.Ready?"Ready":"Not ready")}");menu.Items.Add(info);
            if(app.Peer.IsHost&&p.Id!="host")
            {
                menu.Items.Add(new Separator());
                var promote=new MenuItem{Header="Promote to host"};promote.Click+=async(_,_)=>{try{await app.Peer.Promote(p.Id);}catch(Exception ex){UI.Error(ex.Message);}};menu.Items.Add(promote);
                var kick=new MenuItem{Header="Kick from room"};kick.Click+=async(_,_)=>{try{await app.Peer.Kick(p.Id);}catch(Exception ex){UI.Error(ex.Message);}};menu.Items.Add(kick);
            }
            item.ContextMenu=menu;players.Items.Add(item);
        }
        var signature=$"{room.Map}|{room.Patch}|{room.Rules}";
        if(signature!=lastSettings){map.Text=room.Map;patch.Text=room.Patch;rules.Text=room.Rules;lastSettings=signature;}
        bool host=app.Peer.IsHost;map.IsReadOnly=rules.IsReadOnly=!host;patch.IsReadOnly=true;
        bool defaultPreset=HostPreset.Matches(room);
        setupSummary.Text=$"{room.Map}   /   {room.Capacity} players\n{room.Match.Resources:N0} resources · {room.Match.CommandPoints/100.0:0.##}× CP\nCustom Heroes {(room.Match.CustomHeroes?"on":"off")}\nRing Heroes {(room.Match.RingHeroes?"on":"off")}\n{(room.FriendsOnly?"Friends only":"Public on VPN")}";
        presetStatus.Text=defaultPreset?"●  Automatic host preset":"○  Manual setup in BFME2";
        presetStatus.Foreground=UI.Brush(defaultPreset?UI.Green:"#E5B77A");
        setupBanner.Visibility=host?Visibility.Collapsed:Visibility.Visible;
        customSetup.Visibility=inlineRules.Visibility=host?Visibility.Visible:Visibility.Collapsed;
        customSetup.IsEnabled=!launching;inlineRules.IsEnabled=!savingRules&&!launching;
        if(!savingRules)SyncInlineRules(room);
        int readyCount=room.Players.Count(p=>p.Ready);
        readySummary.Text=launching?"Launching BFME2…":host?$"{readyCount} / {room.Players.Count} players ready":room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId)?.Ready==true?"Ready for the host to launch":"Check Ready when you want to play.";
        launch.Visibility=host?Visibility.Visible:Visibility.Collapsed;
        launch.IsEnabled=!launching&&room.Players.All(p=>p.Ready);
        consent.Visibility=host?Visibility.Collapsed:Visibility.Visible;consent.IsEnabled=true;
        consent.IsChecked=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId)?.Ready==true;
        if(consent.IsChecked!=true)allowLaunch=false;
    }
    public void HandleLaunch(){if(app.Peer.IsHost||allowLaunch){allowLaunch=false;LaunchLocal();}}
    bool launching;
    async void LaunchLocal()=>await LaunchLocalCore();
    async Task LaunchLocalCore()
    {
        if(launching)return;
        launching=true;
        Refresh();
        try
        {
            bool preset=app.Peer.IsHost&&HostPreset.Matches(app.Peer.Current);
            Append("SYSTEM",preset?"Preparing host preset. Approve the Windows permission prompt if shown.":"Launching BFME2…");
            var current=app.Peer.Current??throw new IOException("Join a Fellowship room first.");
            await GameLauncher.Launch(app.Settings.GamePath,current,preset,false,app.Peer.LocalId,false);
            Append("SYSTEM","Open Multiplayer → Network and create/join the host lobby. Team and spot sync starts automatically when the matching lobby opens.");
        }
        catch(Exception e){UI.Error(e.Message);}
        finally{launching=false;Refresh();}
    }
}

public sealed class FriendsWindow : Window
{
    readonly BrowserWindow app;
    readonly ListBox list=new();
    readonly TextBlock summary=UI.Text("YOUR FRIENDS",10,UI.Muted,true);
    readonly Dictionary<string,bool> presence=new();
    readonly System.Windows.Threading.DispatcherTimer presenceTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool checkingPresence;
    public FriendsWindow(BrowserWindow app)
    {
        this.app=app;var body=UI.Shell(this,"Friends",390,620);MinWidth=360;
        UI.Rows(body,UI.Auto,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        var me=UI.Row(UI.Avatar(app.Settings.Name,40),UI.Stack(UI.Text(app.Settings.Name,16,"#F1F3F8",true),UI.Text("●  Available on your VPN",11,UI.Green)));me.Margin=new(20,20,20,22);UI.At(body,me,0);
        var bar=new Grid{Margin=new(20,0,12,10)};bar.ColumnDefinitions.Add(new());bar.ColumnDefinitions.Add(new(){Width=UI.Auto});bar.Children.Add(summary);
        var refresh=UI.AsyncButton("↻",RefreshPresence);refresh.ToolTip="Check which friends are online";refresh.Padding=new(10,5,10,5);Grid.SetColumn(refresh,1);bar.Children.Add(refresh);UI.At(body,bar,1);
        list.Margin=new(8,0,8,0);UI.At(body,list,2);
        var actions=UI.Row(UI.Button("＋ Add friend",AddFriend),UI.Button("Message",Message,true));actions.Margin=new(20,12,12,12);UI.At(body,actions,3);
        var footer=UI.Text("Double-click a friend to chat.\nDirect messages · no offline delivery",10,UI.Muted);footer.Margin=new(20,0,20,18);UI.At(body,footer,4);
        list.MouseDoubleClick+=(_,_)=>Message();
        var menu=new ContextMenu();var remove=new MenuItem{Header="Remove friend"};remove.Click+=(_,_)=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f}){app.Settings.Friends.Remove(f);app.Settings.Save();Render();}};var message=new MenuItem{Header="Send message"};message.Click+=(_,_)=>Message();menu.Items.Add(message);
        var info=new MenuItem{Header="Friend info"};info.Click+=(_,_)=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f})UI.Info(this,"Friend info",$"{f.Name}\n{f.Address}\n{(presence.GetValueOrDefault(f.Address)?"Online":"Offline / unreachable")}");};menu.Items.Add(info);menu.Items.Add(new Separator());menu.Items.Add(remove);list.ContextMenu=menu;
        list.PreviewMouseRightButtonDown+=(_,e)=>{var item=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;if(item?.Tag is Friend)item.IsSelected=true;};
        presenceTimer.Tick+=async(_,_)=>await RefreshPresence();Loaded+=async(_,_)=>await RefreshPresence();Closed+=(_,_)=>presenceTimer.Stop();presenceTimer.Start();Render();
    }
    void Render()
    {
        list.Items.Clear();summary.Text=$"YOUR FRIENDS   {app.Settings.Friends.Count}";
        bool? lastGroup=null;
        foreach(var f in app.Settings.Friends.OrderByDescending(f=>presence.GetValueOrDefault(f.Address)).ThenBy(f=>f.Name))
        {
            bool isOnline=presence.GetValueOrDefault(f.Address);
            if(lastGroup!=isOnline){list.Items.Add(new ListBoxItem{IsEnabled=false,Content=UI.Text(isOnline?"ONLINE":"OFFLINE / NOT REACHABLE",10,UI.Muted,true),Padding=new(12,16,12,8)});lastGroup=isOnline;}
            string state=presence.TryGetValue(f.Address,out bool up)?(up?"●  Online":"○  Unreachable"):"○  Not checked";
            var labels=UI.Stack(UI.Text(f.Name,13,"#E4E9F2",true),UI.Text(state+"  ·  "+f.Address,10,state.Contains("Online")?UI.Green:UI.Muted));labels.Children[1].SetValue(MarginProperty,new Thickness(0,5,0,0));
            list.Items.Add(new ListBoxItem{Content=UI.Row(UI.Avatar(f.Name),labels),Tag=f});
        }
        if(app.Settings.Friends.Count==0)list.Items.Add(new ListBoxItem{IsEnabled=false,Content=UI.Text("Your fellowship starts here.\nAdd a friend's Radmin VPN address\nto find their rooms and chat.",13,UI.Muted),Padding=new(12,30,12,12)});
    }
    async Task RefreshPresence()
    {
        if(checkingPresence||string.IsNullOrEmpty(app.Peer.Address))return;
        checkingPresence=true;
        try
        {
            var selected=(list.SelectedItem as ListBoxItem)?.Tag as Friend;
            var results=await Task.WhenAll(app.Settings.Friends.ToArray().Select(async f=>{try{await PeerService.Query(f.Address,app.Peer.Address);return(f.Address,up:true);}catch{return(f.Address,up:false);}}));
            foreach(var(ip,up)in results)presence[ip]=up;Render();
            if(selected!=null)list.SelectedItem=list.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Tag is Friend f&&f.Address==selected.Address);
        }
        finally{checkingPresence=false;}
    }
    void Message(){if(list.SelectedItem is ListBoxItem{Tag:Friend f}&&app.EnsureOnline())app.OpenChat(f.Address,f.Name);}
    void AddFriend()
    {
        var w=new Window{Owner=this};var body=UI.Shell(w,"Add friend",430,360);w.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=new StackPanel{Margin=new(24)};body.Children.Add(p);p.Children.Add(UI.Text("Bring your friends along.",20,"#F1F3F8",true));
        var name=UI.Field(p,"NAME","",40);var address=UI.Field(p,"RADMIN IPv4 ADDRESS","",15);
        var add=UI.Button("Add friend",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)||!System.Net.IPAddress.TryParse(address.Text.Trim(),out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork){UI.Error("Enter a name and valid IPv4 address.");return;}
            app.Settings.Friends.RemoveAll(f=>f.Address==ip.ToString());app.Settings.Friends.Add(new(name.Text.Trim(),ip.ToString()));app.Settings.Save();Render();w.Close();
        },true);add.Margin=new(0,22,0,0);p.Children.Add(add);w.ShowDialog();
    }
}
public sealed class ChatWindow : Window
{
    readonly string contactName;
    int unread;
    readonly StackPanel messages=new(){Margin=new(18)};
    readonly ScrollViewer scroll;
    public ChatWindow(BrowserWindow app,string address,string name)
    {
        contactName=name;
        var body=UI.Shell(this,"Private chat",500,600);MinWidth=420;UI.Rows(body,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        Title=name+" · Fellowship";
        Activated+=(_,_)=>{unread=0;Title=contactName+" · Fellowship";};
        var header=UI.Row(UI.Avatar(name,40),UI.Stack(UI.Text(name,19,"#F1F3F8",true),UI.Text(address+"  ·  Direct connection",11,UI.Muted)));header.Margin=new(20);UI.At(body,header,0);
        scroll=UI.Scroll(messages);UI.At(body,scroll,1);
        Append("SYSTEM","This conversation stays between these app windows. Messages aren't saved when you close the chat.");
        var composer=new Grid{Margin=new(18,8,18,10)};composer.ColumnDefinitions.Add(new());composer.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var input=new TextBox{MaxLength=2000,ToolTip="Write a message · Enter to send",Margin=new(0,0,8,0)};composer.Children.Add(input);
        async Task Send(){if(string.IsNullOrWhiteSpace(input.Text))return;await app.Peer.SendPrivate(address,input.Text);Append("You",input.Text);input.Clear();}
        input.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;try{await Send();}catch(Exception ex){UI.Error(ex.Message);}}};
        var send=UI.AsyncButton("Send  ↑",Send,true);send.Margin=new(0);Grid.SetColumn(send,1);composer.Children.Add(send);UI.At(body,composer,2);
        var hint=UI.Text("ENTER TO SEND   ·   BOTH PLAYERS MUST BE ONLINE",9,UI.Muted);hint.Margin=new(20,0,20,16);UI.At(body,hint,3);
    }
    public void Receive(string name,string text)
    {
        Append(name,text);
        if(!IsActive){unread++;Title=$"({unread}) {contactName} · Fellowship";}
    }
    public void Append(string name,string text)
    {
        bool mine=name=="You";var stack=UI.Stack(UI.Text(name+"  ·  "+DateTime.Now.ToString("HH:mm"),10,UI.Muted),UI.Text(text,13,"#E7EBF3"));stack.Children[1].SetValue(MarginProperty,new Thickness(0,5,0,0));
        var bubble=UI.Card(stack,mine?"#263657":"#1E232C",12);bubble.Margin=new(0,0,0,12);bubble.MaxWidth=365;bubble.HorizontalAlignment=mine?HorizontalAlignment.Right:HorizontalAlignment.Left;messages.Children.Add(bubble);if(messages.Children.Count>200)messages.Children.RemoveAt(0);scroll.ScrollToEnd();
    }
}
public static class GameLauncher
{
    public static void Validate(string path)
    {
        if(!File.Exists(path)||!string.Equals(Path.GetFileName(path),"lotrbfme2.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Select lotrbfme2.exe from your BFME2 installation in Settings first.");
        var data = Path.Combine(Path.GetDirectoryName(path)!, "game.dat");
        if (!File.Exists(data)) throw new InvalidOperationException("Select lotrbfme2.exe from your BFME2 installation folder; game.dat was not found beside it.");
        var version = FileVersionInfo.GetVersionInfo(data);
        if (version.FileMajorPart != 1 || version.FileMinorPart != 0 || version.FileBuildPart != 2194 || version.FilePrivatePart != 40862)
            throw new InvalidOperationException($"This build supports BFME2 1.00 only. Found game.dat version {version.FileVersion}. Switch your installation to 1.00 first.");
    }
    public static async Task Launch(string path,Room? room,bool preset=false,bool navigate=false,string localId="",bool applyOnly=false)
    {
        if(navigate||applyOnly)throw new InvalidOperationException("Cursor navigation is disabled.");
        Validate(path);
        if(!applyOnly&&IsRunning())throw new InvalidOperationException("BFME2 is already running. Switch to its window, or exit before relaunching.");
        Directory.CreateDirectory(Settings.Folder);File.WriteAllText(Path.Combine(Settings.Folder,"last-session.json"),System.Text.Json.JsonSerializer.Serialize(room));
        try
        {
            if(preset||navigate||applyOnly)
            {
                HostPreset.ValidateBinary(path);
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WorkingDirectory=AppContext.BaseDirectory};
                start.ArgumentList.Add(applyOnly?(preset?"--host-placement":"--guest-placement"):navigate?(preset?"--host-navigation":"--guest-navigation"):"--host-preset");start.ArgumentList.Add(path);start.ArgumentList.Add(Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(room)));start.ArgumentList.Add(localId);
                using var helper=Process.Start(start)??throw new InvalidOperationException("Could not start the preset helper.");
                await helper.WaitForExitAsync();
                if(helper.ExitCode!=0)throw new InvalidOperationException("Game setup or navigation failed. "+(File.Exists(HostPreset.LogPath)?File.ReadAllText(HostPreset.LogPath):"No diagnostic was written."));
                return;
            }
            // The Windows shell handles executables marked as requiring elevation.
            // Fellowship itself stays unelevated; each player approves their own UAC prompt.
            Process.Start(new ProcessStartInfo(path){WorkingDirectory=Path.GetDirectoryName(path)!,UseShellExecute=true});
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("BFME2 was not launched because the Windows permission prompt was cancelled. Click Launch mine to try again.", e);
        }
    }
    public static bool IsRunning()
    {
        foreach(var name in new[]{"game","game.dat","lotrbfme2"})
        {
            var processes=Process.GetProcessesByName(name);
            try { if(processes.Length>0)return true; }
            finally { foreach(var process in processes)process.Dispose(); }
        }
        return false;
    }
}


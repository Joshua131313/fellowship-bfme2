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
    public static Task<bool> ShowPopup(Window window)
    {
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed+=(_,_)=>completion.TrySetResult(window.Resources["Accepted"] is true);
        window.Show();return completion.Task;
    }
    public static void FinishPopup(Window window,bool accepted){window.Resources["Accepted"]=accepted;window.Close();}
    public static async Task<bool> Ask(Window owner,string title,string text,string action)
        =>await ShowPopup(Confirmation(owner,title,text,action,_=>{},false));
    public static Task<bool?> PickFile(Microsoft.Win32.CommonDialog dialog)
    {
        var completion=new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new System.Threading.Thread(()=>{try{completion.SetResult(dialog.ShowDialog());}catch(Exception error){completion.SetException(error);}});
        thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.IsBackground=true;thread.Start();return completion.Task;
    }
    public const string Muted = "#919AA9", Accent = "#83A5FF", Green = "#74D6AB";
    public static Brush Brush(string hex) => Appearance.Brush(hex);
    public static TextBlock Text(string text, double size = 13, string color = "#E8EBF2", bool bold = false) => new()
    { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    public static TextBlock LocalizedText(string text,double size=13,string color="#E8EBF2",bool bold=false)
    {var block=Text(text,size,color,bold);L.Bind(block,TextBlock.TextProperty,text);return block;}
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text }; L.Bind(b,ContentControl.ContentProperty,text); if (primary) { b.Tag="Primary"; b.Background = Brush("#537BEF"); b.BorderBrush = Brush("#698DFC"); b.Foreground = Brushes.White; }
        b.Click += (_, _) => { try { action(); } catch (Exception e) { Error(e.Message); } }; return b;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false, Func<bool>? canExecute = null)
    {
        var b = Button(text, () => { }, primary);
        b.Click += async (_, _) => { b.Resources["BusyAction"]=true;b.IsEnabled = false; try { await action(); } catch (Exception e) { Error(e.Message); } finally {b.Resources["BusyAction"]=false; if (b.IsLoaded) b.IsEnabled = canExecute?.Invoke()??true; } }; DisabledHint(b,()=>"Please wait for the current action to finish.");return b;
    }
    public static bool ValidAddress(string value)=>System.Net.IPAddress.TryParse(value.Trim(),out var ip)&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&ip.ToString()==value.Trim()&&value.Trim()!="0.0.0.0"&&value.Trim()!="255.255.255.255";
    public static Button Require(Button button,Func<bool> valid,Func<string>? reason=null)
    {button.Command=new ValidAction(valid);DisabledHint(button,reason??(()=>"Complete the required fields to enable this action."));return button;}
    public static void DisabledHint(Button button,Func<string> reason)
    {
        bool attached=button.Resources.Contains("DisabledReason");button.Resources["DisabledReason"]=reason;
        ToolTipService.SetShowOnDisabled(button,true);
        if(attached)return;
        button.ToolTip="Complete the required fields to enable this action.";
        button.ToolTipOpening+=(_,e)=>
        {
            if(button.IsEnabled){e.Handled=true;return;}
            button.ToolTip=L.T(button.Resources["BusyAction"] is true?"Please wait for the current action to finish.":((Func<string>)button.Resources["DisabledReason"])());
        };
    }
    sealed class ValidAction(Func<bool> valid) : ICommand
    {
        public bool CanExecute(object? parameter)=>valid();
        public void Execute(object? parameter){}
        public event EventHandler? CanExecuteChanged {add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
    }
    public static StackPanel Row(params UIElement[] items) { var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; foreach (var i in items) p.Children.Add(i); return p; }
    public static StackPanel Stack(params UIElement[] items) { var p = new StackPanel(); foreach (var i in items) p.Children.Add(i); return p; }
    public static TextBox ChatText(string text,int size=12,string color="#DEE3ED")
    {
        var message=new TextBox{Text=text,Style=(Style)Application.Current.FindResource("ChatMessageText"),FontSize=size,Foreground=Brush(color)};
        void Copy(string value){try{if(value.Length>0)Clipboard.SetText(value);}catch(System.Runtime.InteropServices.ExternalException){message.ToolTip="Clipboard is busy. Try copying again.";}}
        var menu=new ContextMenu();menu.Items.Add(MenuAction("Copy message",()=>Copy(message.Text)));
        menu.Items.Add(MenuAction("Copy selected text",()=>Copy(message.SelectedText),()=>message.SelectionLength>0));
        menu.Items.Add(MenuAction("Select all",()=>{message.Focus();message.SelectAll();}));message.ContextMenu=menu;
        return message;
    }
    public static Border Card(UIElement child, string background = "#1B1F26", int padding = 16) => new() { Background = Brush(background), BorderBrush = Brush("#2C313B"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(padding), Child = child };
    public static Border Badge(string text, string color = Accent) => new() { Background = Brush("#242D3E"), CornerRadius = new(4), Padding = new(8,4,8,4), Child = Text(text, 10, color, true), VerticalAlignment = VerticalAlignment.Center };
    public static Border Avatar(string name, int size = 34) => new() { Width = size, Height = size, CornerRadius = new(8), Background = Brush("#2C3854"), Margin = new(0,0,10,0), Child = new TextBlock { Text = string.IsNullOrWhiteSpace(name) ? "?" : name[..1].ToUpperInvariant(), FontSize = size * .42, Foreground = Brush("#B9CCFF"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    public static void Info(Window owner,string title,string text)
    {
        Information(owner,title,text).Show();
    }
    public static Window Information(Window? owner,string title,string text)
    {
        var window=new Window{ResizeMode=ResizeMode.NoResize};if(owner?.IsLoaded==true)window.Owner=owner;
        var body=Shell(window,title,420,220);window.MinHeight=0;window.SizeToContent=SizeToContent.Height;
        window.WindowStartupLocation=window.Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
        var close=Button("Close",window.Close,true);close.IsDefault=true;close.IsCancel=true;close.Margin=new(0,20,0,0);close.HorizontalAlignment=HorizontalAlignment.Right;
        var message=LocalizedText(text,13,Muted);message.VerticalAlignment=VerticalAlignment.Top;
        var scroll=Scroll(message);scroll.MaxHeight=Math.Max(120,SystemParameters.WorkArea.Height-180);
        body.Children.Add(new Border{Padding=new(22,20,22,20),Child=Stack(LocalizedText(title,18,bold:true),new Border{Margin=new(0,10,0,0),Child=scroll},close)});
        return window;
    }
    public static Window Confirmation(Window? owner,string title,string description,string action,Action<bool> confirmed,bool showRemember=true)
    {
        var window=new Window{ResizeMode=ResizeMode.NoResize};if(owner?.IsLoaded==true)window.Owner=owner;
        var body=Shell(window,title,420,260);window.MinHeight=0;window.SizeToContent=SizeToContent.Height;
        window.WindowStartupLocation=window.Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
        var remember=new CheckBox{Content="Don't ask again",Margin=new(0,18,0,20),Visibility=showRemember?Visibility.Visible:Visibility.Collapsed};
        var cancel=Button("Cancel",window.Close);cancel.IsCancel=true;cancel.Margin=new(0,0,8,0);
        var accept=Button(action,()=>{confirmed(remember.IsChecked==true);FinishPopup(window,true);});accept.Tag="Danger";accept.Margin=new(0);
        var buttons=Row(cancel,accept);buttons.HorizontalAlignment=HorizontalAlignment.Right;buttons.Margin=new(0,showRemember?0:22,0,0);
        body.Children.Add(new Border{Padding=new(22,20,22,20),Child=Stack(LocalizedText(title,18,bold:true),new Border{Margin=new(0,10,0,0),Child=LocalizedText(description,13,Muted)},remember,buttons)});
        return window;
    }
    public static Window InvitationSent(Window owner,string name)
    {
        var window=new Window{ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};if(owner.IsLoaded)window.Owner=owner;
        var body=Shell(window,"Invitation sent",360,190);window.MinWidth=360;window.MinHeight=190;
        var done=Button("Done",window.Close,true);done.IsDefault=true;done.IsCancel=true;done.Margin=new(0);done.HorizontalAlignment=HorizontalAlignment.Right;
        var heading=Row(new Border{Margin=new(0,0,10,0),Child=Text("✓",20,Green,true)},Text("Invitation sent",17,bold:true));
        var detail=Text("Sent to "+name,12,Muted);detail.TextTrimming=TextTrimming.CharacterEllipsis;
        body.Children.Add(new Border{Padding=new(20,16,20,16),Child=Stack(heading,new Border{Margin=new(0,7,0,18),Child=detail},done)});
        return window;
    }
    sealed class MenuCommand(Action action,Func<bool>? enabled) : ICommand
    {
        public bool CanExecute(object? parameter)=>enabled?.Invoke()??true;
        public void Execute(object? parameter){if(!CanExecute(parameter))return;try{action();}catch(Exception error){Error(error.Message);}}
        public event EventHandler? CanExecuteChanged {add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
    }
    public static MenuItem MenuAction(string title,Action action,Func<bool>? enabled=null)
    {
        var item=new MenuItem{Header=title,Command=new MenuCommand(action,enabled)};L.Bind(item,HeaderedItemsControl.HeaderProperty,title);
        KeyGesture? gesture=title switch
        {
            "Host room" or "Add friend"=>new(Key.N,ModifierKeys.Control),
            "Settings"=>new(Key.OemComma,ModifierKeys.Control,"Ctrl+,"),
            "Hide to tray"=>new(Key.H,ModifierKeys.Control),
            "Quit Fellowship"=>new(Key.Q,ModifierKeys.Control),
            "Clear room filter"=>new(Key.L,ModifierKeys.Control),
            "Refresh rooms" or "Refresh presence"=>new(Key.F5),
            "Active players" or "Online players"=>new(Key.P,ModifierKeys.Control|ModifierKeys.Shift),
            "Friends list" or "Friends"=>new(Key.D1,ModifierKeys.Control,"Ctrl+1"),
            "Getting started" or "Game setup guide"=>new(Key.F1),
            "Launch game"=>new(Key.F9),
            "Leave room" or "Close window"=>new(Key.W,ModifierKeys.Control),
            "Room browser"=>new(Key.B,ModifierKeys.Control),
            "Send message"=>new(Key.M,ModifierKeys.Control),
            _=>null
        };
        if(gesture!=null){item.Resources["Shortcut"]=gesture;item.InputGestureText=gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.CurrentUICulture);}
        return item;
    }
    public static MenuItem MenuGroup(string title,params MenuItem[] items)
    {
        var group=new MenuItem{Header=title,Style=(Style)Application.Current.FindResource("AppMenuHeading")};
        L.Bind(group,HeaderedItemsControl.HeaderProperty,title);foreach(var item in items)group.Items.Add(item);
        group.Loaded+=(_,_)=>
        {
            group.ApplyTemplate();
            if(group.Template.FindName("PART_Popup",group) is System.Windows.Controls.Primitives.Popup popup)
            {
                popup.PlacementTarget=group;
                popup.Placement=System.Windows.Controls.Primitives.PlacementMode.Custom;
                popup.CustomPopupPlacementCallback=(popupSize,targetSize,offset)=>new[]{
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0,targetSize.Height),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0,-popupSize.Height),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};
            }
        };
        return group;
    }
    public static void Menus(Window window,params MenuItem[] groups)
    {
        var root=(DockPanel)((Border)window.Content).Child;
        var menu=new Menu{Background=Brush("#151A21"),Padding=new(10,2,10,2)};
        foreach(var group in groups)
        {
            menu.Items.Add(group);
            foreach(var item in group.Items.OfType<MenuItem>())
                if(item.Resources["Shortcut"] is KeyGesture gesture)window.InputBindings.Add(new KeyBinding(item.Command,gesture));
        }
        DockPanel.SetDock(menu,Dock.Top);root.Children.Insert(1,menu);
    }
    public static void OpenGuide()=>Process.Start(new ProcessStartInfo("https://github.com/Joshua131313/fellowship-bfme2#readme"){UseShellExecute=true});
    public static void Error(string text)
    {
        var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive);
        var window=new Window{ResizeMode=ResizeMode.NoResize,SizeToContent=SizeToContent.Height};if(owner!=null)window.Owner=owner;
        var body=Shell(window,"Error",520,260);window.MinHeight=0;
        var message=Text("",13,Muted);L.BindError(message,TextBlock.TextProperty,text);
        var copy=Button("Copy diagnostic details",()=>{try{Clipboard.SetText(text);}catch{}});copy.Margin=new(0,16,8,0);
        var close=Button("Close",window.Close,true);close.Margin=new(0,16,0,0);
        body.Children.Add(new Border{Padding=new(24),Child=Stack(message,Row(copy,close))});window.Show();
    }
    public static void Reveal(Window window)
    {
        if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
        window.Show();window.Activate();
    }
    public static Grid Shell(Window w, string title, double width, double height,TextBlock? rawTitle=null)
    {
        w.Style = (Style)Application.Current.FindResource(typeof(Window));L.Window(w);
        w.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Fellowship;component/Assets/Fellowship.ico"));
        if(rawTitle==null)L.Bind(w,Window.TitleProperty,title + " · Fellowship");else w.Title=title+" · Fellowship";w.Width = width; w.Height = height; w.MinWidth = Math.Min(width, 460); w.MinHeight = Math.Min(height, 420);
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
        var root = new DockPanel();root.SetBinding(FrameworkElement.FlowDirectionProperty,new Binding(nameof(L.Direction)){Source=L.Current});
        w.Content = new Border { BorderBrush = Brush("#363C48"), BorderThickness = new(1), Background = Brush("#111419"), Child = root };
        var chrome = new Grid { FlowDirection=FlowDirection.LeftToRight,Height = 48, Background = Brush("#181C22") }; chrome.ColumnDefinitions.Add(new()); chrome.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var compact=w.ResizeMode==ResizeMode.NoResize;
        var brand=new Grid{Margin=new(18,0,12,0),VerticalAlignment=VerticalAlignment.Center};brand.ColumnDefinitions.Add(new(){Width=GridLength.Auto});if(!compact)brand.ColumnDefinitions.Add(new(){Width=GridLength.Auto});brand.ColumnDefinitions.Add(new());
        brand.Children.Add(Text("◈",compact?22:24,Accent));
        if(!compact){var name=Text("  FELLOWSHIP",12,"#F0F2F7",true);name.TextWrapping=TextWrapping.NoWrap;name.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(name,1);brand.Children.Add(name);}
        var caption=rawTitle??LocalizedText(title,12,compact?"#F0F2F7":Muted,compact);caption.TextWrapping=TextWrapping.NoWrap;caption.TextTrimming=TextTrimming.CharacterEllipsis;caption.Margin=new(12,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;
        if(rawTitle!=null){caption.Text=title;caption.ToolTip=new ToolTip{Content=Text(title)};caption.SetBinding(FrameworkElement.FlowDirectionProperty,new Binding(nameof(L.Direction)){Source=L.Current});}
        Grid.SetColumn(caption,compact?1:2);brand.Children.Add(caption);chrome.Children.Add(brand);
        chrome.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && !compact) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else w.DragMove(); };
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
        if(compact){minimize.Visibility=Visibility.Collapsed;maximize.Visibility=Visibility.Collapsed;}
        var controls=Row(minimize,maximize,Caption("Close","M 0,0 L 10,10 M 0,10 L 10,0",w.Close,true));
        Grid.SetColumn(controls,1); chrome.Children.Add(controls); DockPanel.SetDock(chrome,Dock.Top); root.Children.Add(chrome);
        var body = new Grid(); root.Children.Add(body); return body;
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    public static TextBox Field(Panel p, string label, string value = "", int max = 1000)
    {
        var l = LocalizedText(label, 10, Muted, true); l.Margin = new(0,14,0,7); p.Children.Add(l);
        var box = new TextBox { Text = value, MaxLength = max }; p.Children.Add(box); return box;
    }
    public static void At(Grid g, UIElement element, int row, int col = 0) { Grid.SetRow(element,row); Grid.SetColumn(element,col); g.Children.Add(element); }
    public static void Rows(Grid g, params GridLength[] rows) { foreach(var r in rows) g.RowDefinitions.Add(new() { Height = r }); }
    public static GridLength Auto => GridLength.Auto;
    public static GridLength Star => new(1,GridUnitType.Star);
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}

public sealed partial class BrowserWindow : Window
{
    public Settings Settings { get; }
    public PeerService Peer { get; }
    readonly DataGrid rooms = new();
    readonly Dictionary<DataGridColumn,DataGridLength> defaultRoomWidths=new();
    string roomSort="";
    System.ComponentModel.ListSortDirection roomSortDirection;
    readonly TextBlock status = UI.LocalizedText("Not connected", 11, UI.Muted);
    readonly TextBlock count = UI.LocalizedText("0 rooms", 12, UI.Muted);
    readonly TextBox address = new() { Width = 180, Height=34, Padding=new(10,5,10,5), MaxLength = 15, ToolTip = "Host's selected VPN IPv4 address" };
    readonly ComboBox modeFilter=new(){ItemsSource=new[]{"All modes","1v1","2v2","3v3","FFA","Custom"},SelectedIndex=0,MinWidth=160,Height=double.NaN,MinHeight=34,ToolTip="Filter rooms by mode"};
    readonly TextBox search = new() { Width = 220, Height=34, Padding=new(10,5,10,5), ToolTip = "Filter rooms by name, host or map" };
    readonly Border empty;
    readonly TextBlock emptyTitle=UI.LocalizedText("No rooms found",19,"#DCE2ED",true);
    readonly TextBlock emptyHint=UI.LocalizedText("Public rooms on your VPN appear automatically.\nIf a room is missing, enter the host's VPN address and refresh.",13,UI.Muted);
    ActivityAlerts activityAlerts=new();
    readonly Button networkCaption=new(){Width=220,Height=34,Padding=new(10,5,10,5),Background=UI.Brush("#15181E"),BorderBrush=UI.Brush("#343942"),Foreground=UI.Brush("#EFF1F5"),Margin=new(12,0,0,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip="Switch network · leaves your current room"};
    readonly TextBlock serverHint=UI.LocalizedText("Select a server to find its rooms",10,UI.Muted);
    public PlayerLatency Latency { get; }=new();
    readonly List<RoomEntry> discovered = [];
    RoomWindow? roomWindow;
    FriendsWindow? friendsWindow;
    readonly Dictionary<string,ChatWindow> chats = new();
    readonly Dictionary<string,Window> playerInfoWindows = new(StringComparer.OrdinalIgnoreCase);
    bool online,refreshing,initialDiscovery=true;
    readonly ProgressBar discoveryProgress=new(){Width=180,Height=5,IsIndeterminate=true,Foreground=UI.Brush("#6789F5"),Background=UI.Brush("#263042"),BorderThickness=new(0),Margin=new(0,18,0,0),Visibility=Visibility.Collapsed};
    readonly System.Windows.Threading.DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    readonly HashSet<string> discoveredAddresses=[];
    public PeerDirectory Presence { get; }
    readonly System.Windows.Threading.DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromMinutes(2)};
    bool checkingUpdate;
    DateTimeOffset lastUpdateCheck;
    async Task CheckUpdateNotice(){if(checkingUpdate||AppUpdates.Installing||DateTimeOffset.UtcNow-lastUpdateCheck<TimeSpan.FromMinutes(1))return;checkingUpdate=true;lastUpdateCheck=DateTimeOffset.UtcNow;try{await AppUpdates.Check();}catch{/* Offline checks stay quiet; manual checks show errors. */}finally{checkingUpdate=false;}}
    readonly StackPanel activePlayers=new();
    readonly TextBox playerSearch=new(){ToolTip="Search players or VPN address",Height=34,Padding=new(10,5,10,5),Margin=new(0,0,0,10),MaxLength=80};
    readonly TextBlock onlineCount=UI.LocalizedText("1 online",11,UI.Green);
    readonly System.Windows.Controls.Primitives.Popup playersPopup=new(){AllowsTransparency=true,StaysOpen=false,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
    public BrowserWindow(Settings? settings=null)
    {
        lastUpdateCheck=AppUpdates.LastCheckAttempt;
        Settings=settings??Settings.Load();Presence=new(Settings);Settings.SelectDefaultNetwork(PeerService.Interfaces());MessageAlert.Configure(Settings);
        roomSort=new[]{"Title","Mode","Host","Map","PlayerCount","Patch","Ping","Status"}.Contains(Settings.RoomSortColumn)?Settings.RoomSortColumn:"";roomSortDirection=Settings.RoomSortDescending?System.ComponentModel.ListSortDirection.Descending:System.ComponentModel.ListSortDirection.Ascending;
        Settings.NetworkName=PeerService.Interfaces().FirstOrDefault(i=>i.Address==Settings.BindAddress).Name??Settings.NetworkName;
        modeFilter.SelectedItem=modeFilter.Items.Contains(Settings.RoomModeFilter)?Settings.RoomModeFilter:"All modes";
        if(!File.Exists(Settings.GamePath))Settings.GamePath=GameInstallation.Find();
        Peer = new(Settings);
        var body = UI.Shell(this,"Dashboard",1240,760); MinWidth = 1100; MinHeight = 650;
        UI.Rows(body,UI.Auto,UI.Auto,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        var updateText=UI.LocalizedText("",12,"#E5EBFF");var updateRow=new Grid();updateRow.ColumnDefinitions.Add(new());updateRow.ColumnDefinitions.Add(new(){Width=UI.Auto});updateRow.Children.Add(updateText);
        var updateButton=UI.Require(UI.AsyncButton("Download & restart",async()=>{if(AppUpdates.Available is {} release)await AppUpdates.Install(release,()=>Peer.Current!=null,text=>L.Bind(updateText,TextBlock.TextProperty,text));},true),()=>Peer.Current==null,()=>"Leave your room before downloading and restarting.");updateButton.Margin=new(16,0,0,0);Grid.SetColumn(updateButton,1);updateRow.Children.Add(updateButton);
        var updateBanner=UI.Card(updateRow,"#24324E",12);updateBanner.Margin=new(22,12,22,0);updateBanner.Visibility=Visibility.Collapsed;UI.At(body,updateBanner,0);
        void UpdateBanner(){updateBanner.Visibility=AppUpdates.Installing||AppUpdates.Available==null?Visibility.Collapsed:Visibility.Visible;L.Bind(updateText,TextBlock.TextProperty,AppUpdates.Available is {} release?$"Fellowship {release.Version.ToString(3)} is available. Update for the latest improvements.":"");}
        AppUpdates.AvailabilityChanged+=UpdateBanner;AppUpdates.InstallationChanged+=UpdateBanner;UpdateBanner();updateTimer.Tick+=async(_,_)=>await CheckUpdateNotice();
        updateTimer.Start();Dispatcher.BeginInvoke(new Action(async()=>await CheckUpdateNotice()));
        Activated+=async(_,_)=>await CheckUpdateNotice();
        Loaded+=async(_,_)=>await CheckUpdateNotice();Closed+=(_,_)=>{updateTimer.Stop();AppUpdates.AvailabilityChanged-=UpdateBanner;AppUpdates.InstallationChanged-=UpdateBanner;};
        var filters = new Grid();filters.ColumnDefinitions.Add(new(){Width=new(240)});filters.ColumnDefinitions.Add(new(){Width=new(172)});filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=UI.Auto});
        ConfigureNetworkPicker();
        serverHint.Margin=new(0,10,0,0);
        var networkLabel=UI.LocalizedText("SERVER",10,UI.Muted,true);networkLabel.Margin=new(0,0,0,8);
        networkCaption.Margin=new(0);System.Windows.Automation.AutomationProperties.SetName(networkCaption,"Server — rooms are discovered on the selected network");
        networkCaption.Width=double.NaN;
        var networkFilter=UI.Stack(networkLabel,networkCaption);networkFilter.Margin=new(0,0,16,0);filters.Children.Add(networkFilter);
        modeFilter.Margin=new(0);modeFilter.MinWidth=0;
        var modeLabel=UI.LocalizedText("MODE",10,UI.Muted,true);modeLabel.Margin=new(0,0,0,8);var modeField=UI.Stack(modeLabel,modeFilter);modeField.Margin=new(0,0,16,0);Grid.SetColumn(modeField,1);filters.Children.Add(modeField);
        var searchBox=new Grid();search.Width=double.NaN;searchBox.Children.Add(search);var searchHint=UI.LocalizedText("Search rooms…",12,UI.Muted);searchHint.Margin=new(12,0,0,0);searchHint.VerticalAlignment=VerticalAlignment.Center;searchHint.IsHitTestVisible=false;searchBox.Children.Add(searchHint);search.TextChanged+=(_,_)=>searchHint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;System.Windows.Automation.AutomationProperties.SetName(search,"Search rooms");
        var searchLabel=UI.LocalizedText("SEARCH",10,UI.Muted,true);searchLabel.Margin=new(0,0,0,8);var searchField=UI.Stack(searchLabel,searchBox);Grid.SetColumn(searchField,2);filters.Children.Add(searchField);
        var hostRoom=UI.Button("＋  Host room",CreateRoom,true);hostRoom.Height=34;hostRoom.Padding=new(12,5,12,5);hostRoom.Margin=new(12,0,0,0);hostRoom.VerticalAlignment=VerticalAlignment.Bottom;System.Windows.Automation.AutomationProperties.SetName(hostRoom,"Host room");Grid.SetColumn(hostRoom,3);filters.Children.Add(hostRoom);
        var filterCard=UI.Card(UI.Stack(filters,serverHint),"#191D24",12);filterCard.Margin=new(24,12,24,12);UI.At(body,filterCard,2);ConfigureRadminGroups();
        var table = new Grid();
        void Column(string heading, string property, double width, bool star = false) => rooms.Columns.Add(new DataGridTextColumn { Header = heading, SortMemberPath=property=="Slots"?"PlayerCount":property, Binding = property=="Status"?L.Display(property):new Binding(property), MinWidth = star ? (property == "Title" ? 160 : 100) : width, Width = star ? new DataGridLength(width,DataGridLengthUnitType.Star) : new DataGridLength(width) });
        var gameTemplate=new DataTemplate();var gameImage=new FrameworkElementFactory(typeof(Image));gameImage.SetBinding(Image.SourceProperty,new Binding("Icon"));gameImage.SetValue(Image.WidthProperty,28.0);gameImage.SetValue(Image.HeightProperty,28.0);gameImage.SetValue(Image.ToolTipProperty,"The Battle for Middle-earth II");gameTemplate.VisualTree=gameImage;
        rooms.Columns.Add(new DataGridTemplateColumn{Header="",Width=56,CanUserSort=false,CanUserResize=false,CellTemplate=gameTemplate});
        Column("ROOM NAME","Title",2,true); Column("HOST","Host",1,true); Column("MAP","Map",1.25,true); Column("PLAYERS","Slots",75); Column("PATCH","Patch",65); Column("STATUS","Status",110);
        var modeTemplate=new DataTemplate();var pill=new FrameworkElementFactory(typeof(Border));
        pill.SetValue(Border.BackgroundProperty,UI.Brush("#26344D"));pill.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));pill.SetValue(Border.PaddingProperty,new Thickness(10,4,10,4));pill.SetValue(Border.HorizontalAlignmentProperty,HorizontalAlignment.Left);
        var modeText=new FrameworkElementFactory(typeof(TextBlock));modeText.SetBinding(TextBlock.TextProperty,L.Display("Mode"));modeText.SetValue(TextBlock.ForegroundProperty,UI.Brush("#A9C1FF"));modeText.SetValue(TextBlock.FontSizeProperty,11.0);modeText.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);pill.AppendChild(modeText);modeTemplate.VisualTree=pill;
        rooms.Columns.Insert(2,new DataGridTemplateColumn{Header="MODE",Width=100,MinWidth=100,SortMemberPath="Mode",CellTemplate=modeTemplate});
        var pingTemplate=new DataTemplate();var pingFactory=new FrameworkElementFactory(typeof(PingBadge));pingFactory.SetBinding(PingBadge.MillisecondsProperty,new Binding("Ping"));pingTemplate.VisualTree=pingFactory;
        rooms.Columns.Insert(7,new DataGridTemplateColumn{Header="PING",Width=90,SortMemberPath="Ping",CellTemplate=pingTemplate});
        foreach(var column in rooms.Columns)if(column.Header is string title)L.Bind(column,DataGridColumn.HeaderProperty,title);
        L.Choices(modeFilter);
        var baseMinimums=rooms.Columns.Where(c=>c.CanUserResize).ToDictionary(c=>c,c=>c.MinWidth);
        double HeaderMinimum(DataGridColumn column)
        {
            string heading=column.SortMemberPath switch{"Title"=>"ROOM NAME","Mode"=>"MODE","Host"=>"HOST","Map"=>"MAP","PlayerCount"=>"PLAYERS","Patch"=>"PATCH","Ping"=>"PING","Status"=>"STATUS",_=>""};
            var label=new FormattedText(L.T(heading),System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),11,UI.Brush(UI.Muted),1);
            return Math.Max(baseMinimums[column],Math.Ceiling(label.WidthIncludingTrailingWhitespace)+54);
        }
        foreach(var column in rooms.Columns.Where(c=>c.CanUserResize))
        {
            defaultRoomWidths[column]=column.Width;
            column.MinWidth=HeaderMinimum(column);
            if(!column.Width.IsStar)column.Width=new DataGridLength(Math.Max(column.Width.Value,column.MinWidth));
            if(Settings.RoomColumnWidths?.TryGetValue(column.SortMemberPath,out var width)==true&&double.IsFinite(width)&&width>0)column.Width=new DataGridLength(Math.Clamp(width,column.MinWidth,2000));
        }
        void RefreshHeaderLanguage(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
        {
            foreach(var column in baseMinimums.Keys){column.MinWidth=HeaderMinimum(column);if(!column.Width.IsStar&&column.Width.Value<column.MinWidth)column.Width=new DataGridLength(column.MinWidth);}
            LoadNetworkChoices();
        }
        L.Current.PropertyChanged+=RefreshHeaderLanguage;Closed+=(_,_)=>L.Current.PropertyChanged-=RefreshHeaderLanguage;
        rooms.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,e)=>{
            var source=e.OriginalSource as DependencyObject;
            while(source!=null&&source is not System.Windows.Controls.Primitives.DataGridColumnHeader)source=VisualTreeHelper.GetParent(source);
            if(source!=null)SaveRoomColumnWidths();
        }),true);
        rooms.Sorting+=(_,e)=>{e.Handled=true;var member=e.Column.SortMemberPath;if(string.IsNullOrEmpty(member))return;roomSortDirection=roomSort==member&&roomSortDirection==System.ComponentModel.ListSortDirection.Ascending?System.ComponentModel.ListSortDirection.Descending:System.ComponentModel.ListSortDirection.Ascending;roomSort=member;Settings.RoomSortColumn=roomSort;Settings.RoomSortDescending=roomSortDirection==System.ComponentModel.ListSortDirection.Descending;Settings.Save();RenderRooms();};
        rooms.RowStyle=(Style)Application.Current.FindResource(typeof(DataGridRow));
        rooms.PreviewMouseLeftButtonUp+=(_,e)=>{if(ItemsControl.ContainerFromElement(rooms,e.OriginalSource as DependencyObject) is DataGridRow{Item:RoomEntry entry}&&Peer.Current?.Id==entry.Room.Id)ShowRoom();};
        table.Children.Add(rooms);
        var emptyContent = UI.Stack(UI.LocalizedText("◈",42,"#5B719F",true),emptyTitle,emptyHint,discoveryProgress);
        foreach (FrameworkElement el in emptyContent.Children) { el.HorizontalAlignment = HorizontalAlignment.Center; el.Margin = new(0,0,0,12); if(el is TextBlock tb)tb.TextAlignment = TextAlignment.Center; }
        empty = new Border { Child = emptyContent, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new(0,38,0,0) }; table.Children.Add(empty);
        table.SizeChanged+=(_,_)=>table.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,table.ActualWidth,table.ActualHeight),7,7);
        var tableCard = UI.Card(table,"#171A20",0); tableCard.Margin = new(24,0,24,0); UI.At(body,tableCard,3);
        var footer = new Grid { Margin = new(24,16,24,20) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = UI.Auto });
        var direct = UI.Row(UI.LocalizedText("HOST IP  ",10,UI.Muted,true),address,UI.AsyncButton("↻  Refresh",Refresh),UI.Require(UI.AsyncButton("Join room  →",Join,true),()=>!joining&&UI.ValidAddress(address.Text),()=>joining?"Connecting to the room. Please wait.":"Select a room or enter a valid host IPv4 address, such as 26.42.214.47.")); address.Margin = new(0,0,10,0);
        foreach(var button in direct.Children.OfType<Button>()){button.Height=34;button.MinWidth=90;button.Padding=new(12,5,12,5);}
        footer.Children.Add(direct);
        count.Margin = new(10,0,0,0); onlineCount.Margin=new(18,0,0,0);var footerRight=UI.Row(count,onlineCount);Grid.SetColumn(footerRight,1);footer.Children.Add(footerRight);UI.At(body,footer,4);
        var bottom = new Grid { Background = UI.Brush("#181C22"), Height = 36 }; bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = UI.Auto }); status.Margin = new(22,0,0,0); bottom.Children.Add(status);
        var hint = UI.LocalizedText("v0.1.139   ·   RUNS IN SYSTEM TRAY",9,UI.Muted,true); hint.Margin = new(0,0,22,0); Grid.SetColumn(hint,1); bottom.Children.Add(hint); UI.At(body,bottom,5);
        var playerScroll=UI.Scroll(activePlayers);playerScroll.MaxHeight=280;
        var popupBody=UI.Stack(UI.LocalizedText("PLAYER SEARCH",10,UI.Muted,true),playerScroll);
        ((FrameworkElement)popupBody.Children[0]).Margin=new(0,0,0,10);
        var popupCard=UI.Card(popupBody,"#1B222D",14);popupCard.Width=320;popupCard.Margin=new(6);
        popupCard.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=12,ShadowDepth=3,Opacity=.3};
        playersPopup.Child=popupCard;playersPopup.PlacementTarget=playerSearch;
        playerSearch.TextChanged+=(_,_)=>{RenderActivePlayers();playersPopup.IsOpen=playerSearch.IsKeyboardFocused&&playerSearch.Text.Trim().Length>0;};
        playerSearch.GotKeyboardFocus+=(_,_)=>{if(playerSearch.Text.Trim().Length>0){RenderActivePlayers();playersPopup.IsOpen=true;}};
        UI.Menus(this,
            UI.MenuGroup("_File",UI.MenuAction("Host room",CreateRoom),UI.MenuAction("Hide to tray",Hide),UI.MenuAction("Quit Fellowship",()=>((App)Application.Current).Quit())),
            UI.MenuGroup("_Edit",UI.MenuAction("Settings",OpenSettings),UI.MenuAction("Clear room filter",()=>search.Clear(),()=>search.Text.Length>0)),
            UI.MenuGroup("_View",UI.MenuAction("Reset column widths",ResetRoomColumnWidths),UI.MenuAction("Reset column ordering",ResetRoomColumnOrder),UI.MenuAction("Refresh rooms",async()=>{try{await Refresh();}catch(Exception error){UI.Error(error.Message);}}),UI.MenuAction("Online players",()=>SelectDashboard("Online players"))),
            UI.MenuGroup("F_riends",UI.MenuAction("Friends list",OpenFriends),UI.MenuAction("Blocked players",ShowBlockedPlayers)),
            UI.MenuGroup("_Help",UI.MenuAction("Getting started",UI.OpenGuide),UI.MenuAction("About Fellowship",()=>UI.Info(this,"About Fellowship","Fellowship "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)+"\nBFME2 · Radmin VPN"))));
        playerSearch.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){playersPopup.IsOpen=false;rooms.Focus();e.Handled=true;}};
        BuildDashboard(body);
        Closed+=(_,_)=>playersPopup.IsOpen=false;
        var roomMenu=new ContextMenu();
        var preview=new MenuItem{Header="Room preview"};preview.Click+=(_,_)=>{if(rooms.SelectedItem is RoomEntry entry)new RoomPreviewWindow(this,entry.Room,Settings.GamePath).Show();};
        var joinItem=new MenuItem{Header="Join room"};joinItem.Click+=async(_,_)=>{try{await Join();}catch(Exception ex){UI.Error(ex.Message);}};
        roomMenu.Opened+=(_,_)=>{joinItem.Header=rooms.SelectedItem is RoomEntry selectedRoom&&Peer.Current?.Id==selectedRoom.Room.Id?"Open room":"Join room";joinItem.IsEnabled=rooms.SelectedItem is RoomEntry;preview.IsEnabled=joinItem.IsEnabled;};
        roomMenu.Items.Add(joinItem);roomMenu.Items.Add(preview);roomMenu.Items.Add(new Separator());roomMenu.Items.Add(UI.MenuAction("Reset column widths",ResetRoomColumnWidths));roomMenu.Items.Add(UI.MenuAction("Reset column ordering",ResetRoomColumnOrder));rooms.ContextMenu=roomMenu;
        rooms.PreviewMouseRightButtonDown+=(_,e)=>{var row=ItemsControl.ContainerFromElement(rooms,e.OriginalSource as DependencyObject) as DataGridRow;if(row!=null)row.IsSelected=true;};
        Peer.Discovered+=ip=>Dispatcher.InvokeAsync(async()=>{if(discoveredAddresses.Count<256)discoveredAddresses.Add(ip);await QueryDiscoveredPeer(ip);});
        Peer.HostTransferred+=(ip,name,room)=>Dispatcher.InvokeAsync(()=>
        {
            queryVersions[ip]=queryVersions.GetValueOrDefault(ip)+1;
            Presence.Success(ip,name,room,DateTimeOffset.UtcNow,room?.Players.FirstOrDefault(p=>p.Address==ip)?.GameRunning??false);
            if(discoveredAddresses.Count<256)discoveredAddresses.Add(ip);
            UpdateDiscoveredRooms();
        });
        refreshTimer.Tick+=async(_,_)=>{if(online)try{await Peer.SetHostGameRunning(GameLauncher.IsGameActive());await Refresh();}catch{}};
        refreshTimer.Start();
        search.TextChanged += (_,_) => RenderRooms();
        modeFilter.SelectionChanged+=(_,_)=>{Settings.RoomModeFilter=modeFilter.SelectedItem as string??"All modes";Settings.Save();RenderRooms();};
        rooms.SelectionChanged += (_,_) => { if(rooms.SelectedItem is RoomEntry r) address.Text = r.Address; };
        rooms.MouseDoubleClick += async (_,_) => { if(rooms.SelectedItem != null) try { await Join(); } catch(Exception e){ UI.Error(e.Message); } };
        Peer.Changed += () => Dispatcher.InvokeAsync(() => { roomWindow?.Refresh(); if(!closingRoom && Peer.Current != null && roomWindow == null) ShowRoom(); RenderRooms(); });
        Peer.Notice += text => Dispatcher.InvokeAsync(() => {L.Bind(status,TextBlock.TextProperty,text);roomWindow?.ShowNotice(text);});
        Peer.ReadyRequested+=id=>Dispatcher.InvokeAsync(()=>{if(Peer.Current?.Id!=id||Peer.IsHost)return;ShowRoom();roomWindow?.PromptReady(id);});
        Peer.LaunchHandler=async()=>await await Dispatcher.InvokeAsync(()=>{if(Peer.Current==null)return Task.FromResult("Room is disconnected.");if(roomWindow==null)ShowRoom();return roomWindow?.HandleLaunch()??Task.FromResult("Room window is unavailable.");});
        Peer.LaunchRequested += () => {_=Peer.LaunchHandler();};
        Peer.Invitation+=(ip,name,room)=>Dispatcher.InvokeAsync(()=>ShowInvitation(ip,name,room));
        Peer.RoomMessageReceived+=()=>Dispatcher.InvokeAsync(()=>{if(Settings.MessageSounds)MessageAlert.Effect("room-message");});
        Peer.PrivateMessage += (ip,name,text) => Dispatcher.InvokeAsync(() =>
        {
            OpenChat(ip,name,false).Receive(name,text);
            if(Settings.MessageSounds)MessageAlert.Play();
        });
        Closing += (_,e) => { if(Application.Current is App { IsQuitting:false }){e.Cancel=true;Hide();} };
        Closed += (_,_) => { refreshTimer.Stop();Peer.Dispose(); };
        Loaded += (_,_) => { if(string.IsNullOrEmpty(Settings.BindAddress)) OpenSettings(); else EnsureOnline(quiet:true); };
    }
    public event Action? RoomPreviewsChanged;
    public Room? PreviewRoom(string id)=>Peer.Current?.Id==id?Peer.Current:discovered.Where(r=>r.Room.Id==id&&!Settings.IsBlocked(r.Address)).OrderByDescending(r=>r.Room.Revision).Select(r=>r.Room).FirstOrDefault();
    void ResetRoomColumnWidths()
    {
        foreach(var pair in defaultRoomWidths)pair.Key.Width=pair.Value;
        Settings.RoomColumnWidths=[];Settings.Save();
    }
    void ResetRoomColumnOrder()
    {
        for(int index=0;index<rooms.Columns.Count;index++)rooms.Columns[index].DisplayIndex=index;
    }
    void SaveRoomColumnWidths()
    {
        Settings.RoomColumnWidths=rooms.Columns.Where(c=>c.CanUserResize&&c.SortMemberPath.Length>0&&double.IsFinite(c.ActualWidth)&&c.ActualWidth>0).ToDictionary(c=>c.SortMemberPath,c=>Math.Max(c.MinWidth,c.ActualWidth));
        Settings.Save();
    }
    void RenderRooms()
    {
        var list = discovered.Where(r => r.Address != Peer.Address&&!Settings.IsBlocked(r.Address)).GroupBy(r=>r.Room.Id).Select(g=>g.OrderByDescending(r=>r.Room.Revision).First()).ToList();
        if(Peer.Current is {} current){var host=Peer.IsHost?Peer.Address:current.HostAddress;list.RemoveAll(r=>r.Room.Id==current.Id||r.Address==host);list.Insert(0,new(host,current));}

        if(online&&!switchingNetwork)
        {
            int totalRooms=list.Select(r=>r.Room.Id).Distinct().Count();
            L.Bind(status,TextBlock.TextProperty,$"●  {Peer.Address} · VPN discovery active · {totalRooms} room{(totalRooms==1?"":"s")}");
            status.Foreground=UI.Brush(UI.Green);
        }
        RenderDashboard();
        RoomPreviewsChanged?.Invoke();
        list=list.Select(r=>r with{Ping=Latency.Get(r.Address,Peer.Address),Icon=GameIcon.Load(Settings.GamePath)}).ToList();
        string filter = search.Text.Trim();bool filtered=filter.Length>0||modeFilter.SelectedIndex>0;
        discoveryProgress.Visibility=initialDiscovery&&online?Visibility.Visible:Visibility.Collapsed;
        L.Bind(emptyTitle,TextBlock.TextProperty,initialDiscovery&&online?"Finding rooms…":filtered?"No matching rooms":"No rooms found");
        L.Bind(emptyHint,TextBlock.TextProperty,initialDiscovery&&online?"Connecting to players on your VPN…":filtered?"Try another mode, room name, host or map.":"Public rooms on your VPN appear automatically.\nIf a room is missing, enter the host's VPN address and refresh.");
        list = list.Where(r => (modeFilter.SelectedIndex<=0||r.Mode.Equals(modeFilter.SelectedItem as string,StringComparison.OrdinalIgnoreCase))&& $"{r.Title} {r.Host} {r.Map} {r.Mode}".Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        var selected=(rooms.SelectedItem as RoomEntry)?.Room.Id;
        rooms.ItemsSource = list;if(roomSort.Length>0){System.Windows.Data.CollectionViewSource.GetDefaultView(list).SortDescriptions.Add(new System.ComponentModel.SortDescription(roomSort,roomSortDirection));foreach(var column in rooms.Columns)column.SortDirection=column.SortMemberPath==roomSort?roomSortDirection:null;}if(selected!=null)rooms.SelectedItem=list.FirstOrDefault(r=>r.Room.Id==selected); empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed; L.Bind(count,TextBlock.TextProperty,$"{list.Count} room{(list.Count == 1 ? "" : "s")}");
    }
    string? networkWarningAddress;
    string? connectionFailure;
    void ShowNetworkWarning(string message)
    {
        if(networkWarningAddress==Settings.BindAddress)return;
        networkWarningAddress=Settings.BindAddress;
        UI.Information(IsVisible?this:null,"Connection unavailable",message).Show();
    }
    public async Task CheckStartupConnection()
    {
        await Task.Delay(3000);
        var warning=await Task.Run(()=>StartupConnection.Warning(Settings));
        if(warning!=null||connectionFailure!=null)ShowNetworkWarning(warning??connectionFailure!);
    }
    async void OpenActivity(ActivityAlerts.Alert alert)
    {
        try
        {
            UI.Reveal(this);
            if(Settings.IsBlocked(alert.Address))return;
            if(alert.Online==true){OpenChat(alert.Address,alert.Name);return;}
            if(alert.RoomId.Length==0){OpenFriends();return;}
            await Refresh();
            var room=PreviewRoom(alert.RoomId);
            if(room==null){UI.Info(this,"Room unavailable","This room is no longer available.");return;}
            new RoomPreviewWindow(this,room,Settings.GamePath).Show();
        }
        catch(Exception error){UI.Error(error.Message);}
    }
    public async Task JoinPreview(string id)
    {
        if(joining)return;
        await Refresh();var room=PreviewRoom(id);
        if(room==null)throw new IOException("This room is no longer available.");
        if(Peer.Current?.Id==id){OpenCurrentRoom();return;}
        address.Text=room.HostAddress;
        rooms.SelectedItem=null;
        await Join();
    }
    public bool EnsureOnline(bool quiet=false)
    {
        if(online&&Peer.Address==Settings.BindAddress)return true;
        try
        {
            if(string.IsNullOrWhiteSpace(Settings.BindAddress)){ if(!quiet)UI.Error("Select your VPN network interface in Settings first."); return false; }
            Peer.Start(Settings.BindAddress);connectionFailure=null;networkWarningAddress=null; online = true; L.Bind(status,TextBlock.TextProperty,$"●  {Settings.Name}   ·   {Peer.Address}   ·   Ready to play"); status.Foreground = UI.Brush(UI.Green);RenderRooms();Dispatcher.BeginInvoke(new Action(async()=>{try{await Refresh();}catch(Exception error){L.Bind(status,TextBlock.TextProperty,"Discovery failed: "+error.Message);}})); return true;
        }
        catch(Exception e)
        {
            L.Bind(status,TextBlock.TextProperty,"○  Connection unavailable · check Settings");status.Foreground=UI.Brush("#E5B77A");
            var message=e is System.Net.Sockets.SocketException {SocketErrorCode:System.Net.Sockets.SocketError.AddressAlreadyInUse}
                ? "This VPN address is already in use on port 28742. Close any older Fellowship version, then click Refresh to retry."
                : "Could not connect on the selected interface. Check that the selected VPN is connected and choose its current address in Settings.";
            System.Diagnostics.Debug.WriteLine(e);connectionFailure=message;if(!quiet)ShowNetworkWarning(message);return false;
        }
    }
    public Window CreateProfileEditor(Window owner)
    {
        var w=new Window{WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};if(owner.IsLoaded)w.Owner=owner;
        var body=UI.Shell(w,"Your profile",420,280);w.MinHeight=0;w.SizeToContent=SizeToContent.Height;
        var panel=new StackPanel{Margin=new(22)};body.Children.Add(panel);
        var preview=UI.Avatar(Settings.Name,42);panel.Children.Add(UI.Row(preview,UI.LocalizedText("Your profile",18,bold:true)));
        var nickname=UI.Field(panel,"NICKNAME",Settings.Name,40);
        nickname.TextChanged+=(_,_)=>((TextBlock)preview.Child).Text=string.IsNullOrWhiteSpace(nickname.Text)?"?":nickname.Text.Trim()[..1].ToUpperInvariant();
        var actions=new Grid{Margin=new(0,20,0,0)};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var cancel=UI.Button("Cancel",w.Close);cancel.HorizontalAlignment=HorizontalAlignment.Left;cancel.IsCancel=true;actions.Children.Add(cancel);
        var save=UI.Require(UI.AsyncButton("Save profile",async()=>{await Peer.SetNickname(nickname.Text);friendsWindow?.Render();roomWindow?.Refresh();w.Close();},true),()=>!string.IsNullOrWhiteSpace(nickname.Text)&&nickname.Text.Trim()!=Settings.Name,()=>string.IsNullOrWhiteSpace(nickname.Text)?"Enter a nickname.":"Change your nickname to save.");
        save.IsDefault=true;save.Margin=new(0);Grid.SetColumn(save,1);actions.Children.Add(save);panel.Children.Add(actions);
        w.Loaded+=(_,_)=>{nickname.Focus();nickname.SelectAll();};return w;
    }
    public void OpenSettings(){UI.Reveal(this);SelectDashboard("Settings");}
    public Window CreateSettingsWindow()
    {
        var w=new Window{WindowStartupLocation=WindowStartupLocation.CenterOwner};if(IsLoaded)w.Owner=this;
        var body=UI.Shell(w,"Settings",740,700);w.MinWidth=640;w.MinHeight=560;
        body.Children.Add(CreateSettingsContent(w,()=>{},w.Close));return w;
    }
    public Grid CreateSettingsContent(Window w,Action saved,Action restored,Action<string>? feedback=null)
    {
        var body=new Grid();UI.Rows(body,UI.Star,UI.Auto);
        var p=new StackPanel{Margin=new(24,20,24,18)};UI.At(body,UI.Scroll(p),0);
        p.Children.Add(UI.LocalizedText("Settings",23,"#F1F3F8",true));
        var profile=new Grid{Margin=new(0,16,0,0)};profile.ColumnDefinitions.Add(new());profile.ColumnDefinitions.Add(new());
        var profileName=new StackPanel{Margin=new(0,0,12,0)};var profileNetwork=new StackPanel();Grid.SetColumn(profileNetwork,1);
        profile.Children.Add(profileName);profile.Children.Add(profileNetwork);p.Children.Add(profile);
        var name=UI.Field(profileName,"NICKNAME",Settings.Name,40);
        var networkLabel=UI.LocalizedText("NETWORK INTERFACE",10,UI.Muted,true);networkLabel.Margin=new(0,14,0,7);profileNetwork.Children.Add(networkLabel);
        var interfaces=PeerService.Interfaces();
        var network=new ComboBox{ItemsSource=interfaces.Select(i=>$"{i.Name} · {i.Address}").ToList()};
        var networkTemplate=new DataTemplate();var networkText=new FrameworkElementFactory(typeof(TextBlock));networkText.SetBinding(TextBlock.TextProperty,new Binding());networkText.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);networkText.SetBinding(TextBlock.ToolTipProperty,new Binding());networkTemplate.VisualTree=networkText;network.ItemTemplate=networkTemplate;
        network.SelectedIndex=interfaces.FindIndex(i=>i.Address==Settings.BindAddress);
        if(network.SelectedIndex<0&&string.IsNullOrWhiteSpace(Settings.BindAddress))network.SelectedIndex=interfaces.FindIndex(i=>i.Name.Contains("Radmin",StringComparison.OrdinalIgnoreCase));var networkBox=new Grid();networkBox.Children.Add(network);
        var networkPlaceholder=UI.LocalizedText("Select a network…",12,UI.Muted);networkPlaceholder.Margin=new(11,0,32,0);networkPlaceholder.IsHitTestVisible=false;networkBox.Children.Add(networkPlaceholder);profileNetwork.Children.Add(networkBox);
        void NetworkPlaceholder()=>networkPlaceholder.Visibility=network.SelectedIndex<0?Visibility.Visible:Visibility.Collapsed;
        network.SelectionChanged+=(_,_)=>NetworkPlaceholder();NetworkPlaceholder();
        NetworkChanged+=()=>{network.SelectedIndex=interfaces.FindIndex(i=>i.Address==Settings.BindAddress);NetworkPlaceholder();};
        network.DropDownOpened+=(_,_)=>
        {
            var selectedAddress=network.SelectedIndex>=0&&network.SelectedIndex<interfaces.Count?interfaces[network.SelectedIndex].Address:Settings.BindAddress;
            interfaces=PeerService.Interfaces();network.ItemsSource=interfaces.Select(i=>$"{i.Name} · {i.Address}").ToList();network.SelectedIndex=interfaces.FindIndex(i=>i.Address==selectedAddress);NetworkPlaceholder();
        };
        var networkHint=UI.LocalizedText("Select the same VPN network on both PCs. Selecting a network switches immediately and leaves your current room.",11,UI.Muted);networkHint.Margin=new(0,10,0,0);p.Children.Add(networkHint);profileNetwork.Children.Add(RadminGroupSelector());
        var game=UI.Field(p,"BFME2 EXECUTABLE",Settings.GamePath);
        game.Height=58;game.TextWrapping=TextWrapping.Wrap;game.VerticalContentAlignment=VerticalAlignment.Center;
        game.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;game.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        var browse=UI.AsyncButton("Browse files…",async()=>{var d=new OpenFileDialog{Filter="Game executable (*.exe)|*.exe"};if(await UI.PickFile(d)==true)game.Text=d.FileName;});browse.HorizontalAlignment=HorizontalAlignment.Left;browse.Margin=new(0,8,0,0);p.Children.Add(browse);
        var gamePreferences=UI.Button("Game preferences…",()=>SelectDashboard("Game preferences"));gamePreferences.HorizontalAlignment=HorizontalAlignment.Left;gamePreferences.Margin=new(0,12,0,0);p.Children.Add(gamePreferences);
        var options=new StackPanel();
        var sounds=new CheckBox{Content="Chat, invitation and room activity sounds",IsChecked=Settings.MessageSounds,Margin=new(0,0,0,14)};options.Children.Add(sounds);
        var friendSounds=new CheckBox{Content="Player online and offline sounds",IsChecked=Settings.FriendActivitySounds,Margin=new(0,0,0,14)};options.Children.Add(friendSounds);
        var notifications=new CheckBox{Content="Windows notifications for friends and new rooms",IsChecked=Settings.DesktopNotifications,Margin=new(0,0,0,14)};options.Children.Add(notifications);
        var liveSync=new CheckBox{Content="Live BFME2 synchronization",IsChecked=Settings.LiveGameSync,Margin=new(0,0,0,14),ToolTip="Synchronize map, rules, team, starting spot and color in the native LAN lobby."};options.Children.Add(liveSync);
        var startup=new CheckBox{Content="Start in the tray at Windows sign-in",IsChecked=Installation.StartsWithWindows,IsEnabled=Installation.IsInstalled,ToolTip=Installation.IsInstalled?"Fellowship starts quietly in the system tray.":"Install Fellowship to enable startup."};options.Children.Add(startup);ToolTipService.SetShowOnDisabled(startup,true);
        var optionCard=UI.Card(options,"#171C24",16);optionCard.Margin=new(0,20,0,0);p.Children.Add(optionCard);
        p.Children.Add(AppUpdates.Controls(()=>Peer.Current!=null));
        var appearanceChoice=new ComboBox{ItemsSource=new[]{"Dark","Black","Light","System"},SelectedItem=Settings.Appearance,Margin=new(0,12,0,0)};
        if(appearanceChoice.SelectedIndex<0)appearanceChoice.SelectedIndex=0;
        var soundMaster=new CheckBox{Content="Enable app sounds",IsChecked=Settings.SoundsEnabled,Margin=new(0,12,0,12)};
        L.Bind(soundMaster,ContentControl.ContentProperty,"Enable app sounds");
        var volume=new Slider{Minimum=0,Maximum=100,Value=Math.Clamp(Settings.SoundVolume,0,100),TickFrequency=1,IsSnapToTickEnabled=true,Height=28,Margin=new(0,4,0,10)};
        var volumeLabel=UI.LocalizedText("Volume · "+(int)volume.Value+"%",11,UI.Muted);
        System.Windows.Automation.AutomationProperties.SetName(volume,"Sound volume");
        volume.ValueChanged+=(_,_)=>L.Bind(volumeLabel,TextBlock.TextProperty,"Volume · "+(int)volume.Value+"%");
        var soundOptions=new StackPanel();var soundChecks=new Dictionary<string,CheckBox>();
        foreach(var sound in MessageAlert.Choices)
        {
            bool groupEnabled=sound.Key.StartsWith("friend-")?Settings.FriendActivitySounds:Settings.MessageSounds;
            var check=new CheckBox{Content=sound.Label,IsChecked=groupEnabled&&!Settings.MutedSounds.Contains(sound.Key),Margin=new(0,0,0,10)};
            soundChecks[sound.Key]=check;soundOptions.Children.Add(check);
        }
        var previewChoice=new ComboBox{ItemsSource=MessageAlert.Choices.Select(c=>c.Label).ToArray(),SelectedIndex=0,MinWidth=150};L.Choices(previewChoice);
        var previewSound=UI.Button("Play",()=>MessageAlert.Preview(MessageAlert.Choices[previewChoice.SelectedIndex].Key,(int)volume.Value));previewSound.Margin=new(8,0,0,0);
        soundOptions.Children.Add(UI.Row(previewChoice,previewSound));
        var soundEvents=new Expander{Header="Choose sounds & preview",Content=new Border{Padding=new(12),Child=soundOptions},Margin=new(0,6,0,0)};
        void SoundEnabled(){volume.IsEnabled=soundMaster.IsChecked==true;soundEvents.IsEnabled=volume.IsEnabled;}
        soundMaster.Checked+=(_,_)=>SoundEnabled();soundMaster.Unchecked+=(_,_)=>SoundEnabled();SoundEnabled();
        var footer=new Grid{Margin=new(24,14,24,20)};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=UI.Auto});
        L.Bind(soundEvents,HeaderedContentControl.HeaderProperty,"Choose sounds & preview");
        var export=UI.AsyncButton("Export backup",async()=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Fellowship backup (*.json)|*.json",FileName="Fellowship-backup.json"};
            if(await UI.PickFile(dialog)==true)try{File.WriteAllText(dialog.FileName,SettingsBackup.Export(Settings));}catch(Exception error){UI.Error(error.Message);}
        });
        var restore=UI.AsyncButton("Restore backup",async ()=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Fellowship backup (*.json)|*.json"};if(await UI.PickFile(dialog)!=true)return;
            try
            {
                if(new FileInfo(dialog.FileName).Length>2_000_000)throw new IOException("Backup is too large.");
                var incoming=SettingsBackup.Parse(File.ReadAllText(dialog.FileName));
                if(!await UI.Ask(w,"Restore backup","Replace preferences, friends, blocked users and room presets with this backup? Your game path and VPN interface will stay unchanged.","Restore backup"))return;
                if(Peer.Current!=null)throw new InvalidOperationException("Leave your room before restoring a backup.");
                File.WriteAllText(Path.Combine(Settings.Folder,"before-restore.json"),SettingsBackup.Export(Settings));
                SettingsBackup.Restore(Settings,incoming);Appearance.Apply(Settings.Appearance);MessageAlert.Configure(Settings);friendsWindow?.Close();restored();await Refresh();
            }
            catch(Exception error){UI.Error(error.Message);}
        });
        UI.Require(restore,()=>Peer.Current==null,()=>"Leave the current room before restoring a backup.");
        var backupHeading=UI.LocalizedText("BACKUP & RESTORE",10,UI.Muted,true);backupHeading.Margin=new(0,0,0,12);
        export.Margin=new(0,0,8,0);restore.Margin=new(0);
        p.Children.Add(new Border{Margin=new(0,28,0,8),Child=UI.Stack(backupHeading,UI.Row(export,restore))});
        var preferences=UI.Button("Preferences folder",()=>{Directory.CreateDirectory(Settings.Folder);var marker=Path.Combine(Settings.Folder,"folder-"+Guid.NewGuid().ToString("N")+".tmp");try{File.WriteAllText(marker,"");var folder=Path.GetDirectoryName(NativePlacement.PhysicalPath(marker))!;Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}finally{File.Delete(marker);}});preferences.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(preferences);
        var status=UI.LocalizedText("Changes save automatically",12,UI.Muted);status.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(status,1);footer.Children.Add(status);UI.At(body,footer,1);
        void Persist(){Settings.Save();L.Bind(status,TextBlock.TextProperty,L.T("✓ Saved"));}
        void ApplyOptions()
        {
            try{Settings.MessageSounds=true;Settings.FriendActivitySounds=true;Settings.SoundsEnabled=soundMaster.IsChecked==true;Settings.SoundVolume=(int)volume.Value;Settings.MutedSounds=soundChecks.Where(p=>p.Value.IsChecked!=true).Select(p=>p.Key).ToList();Settings.Appearance=appearanceChoice.SelectedItem?.ToString()??"Dark";Appearance.Apply(Settings.Appearance);MessageAlert.Configure(Settings);Settings.DesktopNotifications=notifications.IsChecked==true;Settings.LiveGameSync=liveSync.IsChecked==true;Persist();}
            catch(Exception error){L.Bind(status,TextBlock.TextProperty,"Could not save: "+error.Message);}
        }
        appearanceChoice.SelectionChanged+=(_,_)=>ApplyOptions();volume.ValueChanged+=(_,_)=>ApplyOptions();
        foreach(var check in soundChecks.Values.Concat(new[]{soundMaster,notifications,liveSync})) {check.Checked+=(_,_)=>ApplyOptions();check.Unchecked+=(_,_)=>ApplyOptions();}
        liveSync.Checked+=(_,_)=>roomWindow?.RetryLiveSync();liveSync.Unchecked+=(_,_)=>roomWindow?.RetryLiveSync();
        void ApplyStartup(){try{if(Installation.IsInstalled)Installation.SetStartup(startup.IsChecked==true);Persist();}catch(Exception error){L.Bind(status,TextBlock.TextProperty,error.Message);}}
        startup.Checked+=(_,_)=>ApplyStartup();startup.Unchecked+=(_,_)=>ApplyStartup();
        var editTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
        async Task ApplyText()
        {
            editTimer.Stop();try{
                if(string.IsNullOrWhiteSpace(name.Text)){L.Bind(status,TextBlock.TextProperty,"Enter a nickname.");return;}
                var path=game.Text.Trim();if(path.Length>0&&(!File.Exists(path)||!Path.GetFileName(path).Equals("lotrbfme2.exe",StringComparison.OrdinalIgnoreCase))){L.Bind(status,TextBlock.TextProperty,"Select an existing lotrbfme2.exe file.");return;}
                Settings.GamePath=path;var nickname=name.Text.Trim();if(Settings.Name!=nickname){await Peer.SetNickname(nickname);Settings.Name=nickname;friendsWindow?.Render();}Persist();
            }catch(Exception error){L.Bind(status,TextBlock.TextProperty,"Could not save: "+error.Message);}
        }
        editTimer.Tick+=async(_,_)=>await ApplyText();
        name.TextChanged+=(_,_)=>{editTimer.Stop();editTimer.Start();};game.TextChanged+=(_,_)=>{editTimer.Stop();editTimer.Start();};
        body.Unloaded+=async(_,_)=>{if(editTimer.IsEnabled)await ApplyText();};
        bool changingNetwork=false;
        network.SelectionChanged+=async(_,_)=>{
            if(changingNetwork||network.SelectedIndex<0||interfaces[network.SelectedIndex].Address==Settings.BindAddress)return;
            var choice=interfaces[network.SelectedIndex];changingNetwork=true;network.IsEnabled=false;L.Bind(status,TextBlock.TextProperty,"Connecting to "+choice.Name+"…");
            try{await ChangeNetwork(choice.Name,choice.Address);while(refreshing)await Task.Delay(50);await Task.WhenAll(peerQueries.Values.ToArray());await Refresh();L.Bind(status,TextBlock.TextProperty,"✓ Connected · rooms and friends refreshed");}
            catch(Exception error){L.Bind(status,TextBlock.TextProperty,"Connection failed: "+error.Message);}
            finally{changingNetwork=false;network.IsEnabled=true;}
        };
        var children=p.Children.Cast<UIElement>().ToArray();p.Children.Clear();p.Children.Add(children[0]);
        var subtitle=UI.LocalizedText("Make Fellowship feel at home. Changes apply and save automatically.",12,UI.Muted);subtitle.Margin=new(0,8,0,20);p.Children.Add(subtitle);
        var sections=new Grid();sections.ColumnDefinitions.Add(new());sections.ColumnDefinitions.Add(new());
        sections.RowDefinitions.Add(new(){Height=UI.Auto});sections.RowDefinitions.Add(new(){Height=UI.Auto});
        var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,1);sections.Children.Add(left);sections.Children.Add(right);p.Children.Add(sections);
        Border Section(string title,string description,params UIElement[] controls)
        {
            var stack=UI.Stack(UI.LocalizedText(title,15,bold:true));
            if(description.Length>0){var text=UI.LocalizedText(description,11,UI.Muted);text.Margin=new(0,5,0,4);stack.Children.Add(text);}
            foreach(var control in controls)stack.Children.Add(control);
            var card=UI.Card(stack,"#171C24",18);card.Margin=new(0,0,0,16);return card;
        }
        profile.Margin=new(0);networkHint.Margin=new(0,10,0,0);
        left.Children.Add(Section("Profile & connection","How other players find you.",profile,networkHint));
        browse.Margin=new(0,10,8,0);gamePreferences.Margin=new(0,10,0,0);game.Height=48;
        var gameEnhancements=new GameEnhancementControls(()=>game.Text);
        game.TextChanged+=(_,_)=>gameEnhancements.Refresh();
        left.Children.Add(Section("Game installation","Connect your copy of The Battle for Middle-earth II.",children[3],game,UI.Row(browse,gamePreferences),gameEnhancements));
        var backup=(Border)children[^1];backup.Margin=new(0,12,0,0);
        left.Children.Add(Section("Backup & restore","Keep a copy of your preferences and contacts.",backup));
        left.Children.Add(Section("Updates","",children[^2]));
        var languagePicker=L.LanguagePicker(Settings);languagePicker.Margin=new(0,12,0,0);
        right.Children.Add(Section("Language","",languagePicker));
        L.Choices(appearanceChoice);
        right.Children.Add(Section("Appearance","Choose a theme, or follow Windows.",appearanceChoice));
        right.Children.Add(Section("Sound","Set the volume and choose which events you hear.",soundMaster,volumeLabel,volume,soundEvents));
        options.Children.Clear();notifications.Margin=new(0,12,0,14);liveSync.Margin=new(0,0,0,14);startup.Margin=new(0);
        foreach(var check in new[]{notifications,liveSync,startup})if(check.Content is string text)check.Content=UI.LocalizedText(text,12);
        right.Children.Add(Section("App behavior","Notifications, game sync and startup.",notifications,liveSync,startup));
        void ArrangeSettings(double width){bool wide=width>=760;sections.ColumnDefinitions[1].Width=wide?UI.Star:new GridLength(0);Grid.SetColumn(right,wide?1:0);Grid.SetRow(right,wide?0:1);left.Margin=new(0,0,wide?20:0,0);}
        body.SizeChanged+=(_,e)=>ArrangeSettings(e.NewSize.Width);ArrangeSettings(1000);
        return body;
    }
    readonly Dictionary<string,Task> peerQueries=new();
    readonly Dictionary<string,int> queryVersions=new();
    Task QueryDiscoveredPeer(string ip)
    {
        if(ip==Peer.Address||!Peer.AllowsPeer(ip)||(Settings.IsBlocked(ip)&&!Settings.Friends.Any(f=>f.Address==ip)))return Task.CompletedTask;
        if(peerQueries.TryGetValue(ip,out var pending))return pending;
        var task=QueryAndRender(ip);peerQueries[ip]=task;return task;
    }
    async Task QueryAndRender(string ip)
    {
        var version=queryVersions.GetValueOrDefault(ip);var source=Peer.Address;
        try
        {
            var result=await PeerService.QueryPeer(ip,source,true,Peer.AllowsPeer);
            if(source==Peer.Address&&version==queryVersions.GetValueOrDefault(ip)&&Peer.AllowsPeer(ip))
                Presence.Success(ip,result.Name,result.Room,DateTimeOffset.UtcNow,result.GameRunning,result.IdleSeconds,result.ManualAfk);
        }
        catch{if(source==Peer.Address&&version==queryVersions.GetValueOrDefault(ip))Presence.Failure(ip);}
        finally{peerQueries.Remove(ip);UpdateDiscoveredRooms();}
    }
    void UpdateDiscoveredRooms()
    {
        discovered.Clear();
        foreach(var peer in Presence.Active(DateTimeOffset.UtcNow))
            if(peer.Room!=null&&Peer.AllowsPeer(peer.Address)&&Peer.AllowsRoom(peer.Room)&&!Settings.IsBlocked(peer.Address)&&!Settings.IsBlocked(peer.Room.HostAddress))discovered.Add(new(peer.Room.HostAddress,peer.Room,peer.Reconnecting));
        RenderRooms();RenderActivePlayers();friendsWindow?.Render();
    }
    async Task RefreshLatencies(string[] addresses)
    {
        try{await Latency.Refresh(addresses.Where(Peer.AllowsPeer).ToArray(),Peer.Address);RenderRooms();roomWindow?.Refresh();}
        catch(Exception error){Debug.WriteLine("Ping refresh: "+error.Message);}
    }
    public async Task Refresh()
    {
        if(refreshing||!EnsureOnline())return;
        refreshing=true;
        if(initialDiscovery){L.Bind(status,TextBlock.TextProperty,"Finding peers and rooms…");RenderRooms();}
        try
        {
            await Peer.Discover(); // Compatibility with older room-only discovery.
            await Peer.Discover(presence:true);
            // Let the initial UDP responses arrive before building the first query list.
            if(initialDiscovery)await Task.Delay(400);
            var addresses=Settings.Friends.Select(f=>f.Address).Concat(discoveredAddresses).Concat(Presence.Known(DateTimeOffset.UtcNow)).Concat(Settings.RestrictRadmin?Settings.RadminMembership.Members:[]).Append(address.Text.Trim()).Where(a=>a.Length>0&&a!=Peer.Address&&Peer.AllowsPeer(a)&&(!Settings.IsBlocked(a)||Settings.Friends.Any(f=>f.Address==a))).Distinct().Take(256).ToArray();
            await Task.WhenAll(addresses.Select(QueryDiscoveredPeer));
            _=RefreshLatencies(addresses.Concat(Peer.Current?.Players.Select(p=>p.Address)??Array.Empty<string>()).ToArray());
            // Keep peers in the retry set through short VPN losses. A successful null
            // room response removes a closed/private room immediately.
            var now=DateTimeOffset.UtcNow;
            Presence.Keep(Peer.AllowsPeer);Peer.RecheckNetworkScope();UpdateDiscoveredRooms();
            foreach(var ip in Presence.Known(now))if(discoveredAddresses.Count<256)discoveredAddresses.Add(ip);
            RenderRooms();RenderActivePlayers();friendsWindow?.Render();
            foreach(var alert in activityAlerts.Observe(Settings,Presence.Active(now)))
            {
                if(Settings.FriendActivitySounds&&alert.Online is bool onlineFriend)MessageAlert.PlayPresence(onlineFriend);
                if(alert.Notify&&Settings.DesktopNotifications&&Application.Current is App application)application.NotifyActivity(alert,()=>OpenActivity(alert));
            }

        }
        finally{refreshing=false;initialDiscovery=false;RenderRooms();}
    }
    void RenderActivePlayers()
    {
        activePlayers.Children.Clear();
        var peers=Presence.Active(DateTimeOffset.UtcNow).Where(p=>Peer.AllowsPeer(p.Address)&&!Settings.IsBlocked(p.Address)).ToArray();
        L.Bind(onlineCount,TextBlock.TextProperty,$"{peers.Length+1} online");
        string query=playerSearch.Text.Trim();
        foreach(var peer in peers.Where(p=>SearchableMapBox.Matches(p.Name+" "+p.Address,query)))
        {
            var details=UI.Stack(UI.Text(peer.Name,12,peer.IsAfk?UI.Muted:"#EFF1F5",true),UI.LocalizedText(peer.Reconnecting?"Reconnecting":(peer.IsAfk?"AFK":peer.GameRunning?"In BFME2":"Online")+" · "+peer.Address,10,peer.Reconnecting?UI.Muted:peer.IsAfk?IdlePresence.AwayColor:UI.Green));
            var button=UI.Button("",()=>{playersPopup.IsOpen=false;OpenChat(peer.Address,peer.Name);});
            button.Style=(Style)Application.Current.FindResource("PlayerRowButton");
            button.Content=UI.Row(UI.Avatar(peer.Name,28),details);button.HorizontalContentAlignment=HorizontalAlignment.Left;
            button.ContextMenu=PlayerMenu(this,peer.Name,peer.Address);button.Padding=new(8);button.Margin=new(0,0,0,4);button.ToolTip="Send message";activePlayers.Children.Add(button);
        }
        if(activePlayers.Children.Count==0){var empty=UI.LocalizedText(query.Length==0?"No other players online":"No players match your search",12,UI.Muted);empty.Margin=new(8,14,8,14);activePlayers.Children.Add(empty);}
    }
    public bool CanAddFriend(string ip)=>UI.ValidAddress(ip)&&ip!=Peer.Address&&!Settings.IsBlocked(ip)&&!Settings.Friends.Any(f=>f.Address==ip);
    public void AddPlayerFriend(string name,string ip)
    {
        if(!CanAddFriend(ip))return;
        Settings.Friends.Add(new(name,ip));Settings.Save();friendsWindow?.Render();
        CommandManager.InvalidateRequerySuggested();
    }
    public Room? PlayerRoom(string ip)
    {
        if(Settings.IsBlocked(ip)||!Peer.AllowsPeer(ip))return null;
        if(Peer.Current is {} current && (ip==Peer.Address||current.Players.Any(p=>p.Address==ip)))return current;
        var peers=Presence.Active(DateTimeOffset.UtcNow).Where(p=>!p.Reconnecting&&!Settings.IsBlocked(p.Address));
        return peers.Where(p=>p.Room!=null&&Peer.AllowsRoom(p.Room)&&!Settings.IsBlocked(p.Room.HostAddress)&&(p.Address==ip||p.Room.Players.Any(member=>member.Address==ip)))
            .OrderByDescending(p=>p.Room!.Revision).Select(p=>p.Room).FirstOrDefault();
    }
    public FrameworkElement PlayerRoomActivity(string ip)
    {
        var room=PlayerRoom(ip);var presence=Presence.Find(ip,DateTimeOffset.UtcNow);
        var running=presence?.GameRunning??room?.Players.FirstOrDefault(p=>p.Address==ip)?.GameRunning??false;
        var state=Settings.IsBlocked(ip)?"Blocked":presence?.Reconnecting==true?"Reconnecting":running?"In game":room!=null?"Lobby":presence!=null||ip==Peer.Address?"Online":"Offline";
        var panel=UI.Row(UI.LocalizedText("● "+state,11,state=="Offline"?UI.Muted:running?UI.Green:UI.Accent));panel.Margin=new(0,3,0,0);panel.ToolTip=ip;
        if(room!=null){var title=UI.Text(room.Title,11,UI.Muted);title.TextTrimming=TextTrimming.CharacterEllipsis;title.MaxWidth=165;title.Margin=new(8,0,0,0);title.ToolTip=room.Title+"\n"+ip;panel.Children.Add(title);}
        return panel;
    }
    public ContextMenu PlayerMenu(Window owner,string name,string ip,Player? player=null,bool showRoomActions=true)
    {
        var menu=MenuPlacement.AtCursor(owner);
        menu.Items.Add(UI.MenuAction("Player info",()=>UI.Reveal(CreatePlayerInfo(owner,name,ip,player))));
        if(showRoomActions)
        {
            var roomInfo=UI.MenuAction("Show game room info",()=>{if(PlayerRoom(ip) is {} room)new RoomPreviewWindow(this,room,Settings.GamePath).Show();},()=>PlayerRoom(ip)!=null);
            var joinRoom=UI.MenuAction("Join game room",async()=>{try{if(PlayerRoom(ip) is {} room)await JoinPreview(room.Id);}catch(Exception error){UI.Error(error.Message);}},()=>PlayerRoom(ip) is {} room&&(Peer.Current?.Id==room.Id||room.Players.Count<room.Capacity));
            menu.Items.Add(roomInfo);menu.Items.Add(joinRoom);
            menu.Opened+=(_,_)=>{var room=PlayerRoom(ip);bool running=!Settings.IsBlocked(ip)&&(Presence.Find(ip,DateTimeOffset.UtcNow)?.GameRunning??player?.GameRunning??false);roomInfo.Visibility=joinRoom.Visibility=room!=null||running?Visibility.Visible:Visibility.Collapsed;L.Bind(joinRoom,HeaderedItemsControl.HeaderProperty,room!=null&&Peer.Current?.Id==room.Id?"Open room":"Join game room");foreach(var action in new[]{roomInfo,joinRoom}){ToolTipService.SetShowOnDisabled(action,true);if(room==null)L.Bind(action,FrameworkElement.ToolTipProperty,"No Fellowship room is available for this player.");else action.ToolTip=null;}};
        }
        if(ip!=Peer.Address)
        {
            var invite=UI.MenuAction("Invite to room",async()=>{try{await Peer.Invite(ip);UI.InvitationSent(owner,name).Show();}catch(Exception error){UI.Error(error.Message);}},()=>Peer.Current is {} current&&!current.Players.Any(p=>p.Address==ip)&&Settings.Friends.Any(f=>f.Address==ip)&&!Settings.IsBlocked(ip));
            ToolTipService.SetShowOnDisabled(invite,true);invite.ToolTip="Invite to room";invite.ToolTipOpening+=(_,_)=>invite.ToolTip=L.T(Peer.Current?.Players.Any(p=>p.Address==ip)==true?"This player is already in your room.":"Invite to room");menu.Items.Add(invite);
            menu.Items.Add(UI.MenuAction("Send message",()=>OpenChat(ip,name),()=>UI.ValidAddress(ip)&&Peer.AllowsPeer(ip)));
            var friend=UI.MenuAction("Add friend",async()=>await ToggleFriend(name,ip),()=>UI.ValidAddress(ip)&&(Settings.Friends.Any(f=>f.Address==ip)||!Settings.IsBlocked(ip)));friend.InputGestureText="";menu.Items.Add(friend);
            var block=UI.MenuAction("Block player",async()=>await BlockPlayer(name,ip,!Settings.IsBlocked(ip)));menu.Items.Add(block);
            menu.Opened+=(_,_)=>{bool saved=Settings.Friends.Any(f=>f.Address==ip);friend.Header=saved?"Remove friend":"Add friend";friend.Tag=saved?"Danger":null;block.Header=Settings.IsBlocked(ip)?"Unblock player":"Block player";block.Tag=Settings.IsBlocked(ip)?null:"Danger";};
        }
        return menu;
    }
    public async Task ToggleFriend(string name,string ip)
    {
        if(Settings.Friends.Any(f=>f.Address==ip)){if(!await ConfirmPlayerAction(name,false))return;Settings.Friends.RemoveAll(f=>f.Address==ip);Settings.Save();friendsWindow?.Render();}
        else AddPlayerFriend(name,ip);
        CommandManager.InvalidateRequerySuggested();
    }
    async Task<bool> ConfirmPlayerAction(string name,bool block)
    {
        if(block?Settings.SkipBlockConfirmation:Settings.SkipRemoveFriendConfirmation)return true;
        var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive)??this;
        return await UI.ShowPopup(UI.Confirmation(owner,block?"Block player?":"Remove friend?",block?$"Block {name}? Direct messages will be blocked and their room messages hidden. You can unblock them later.":$"Remove {name} from your friends list? You can add them again later.",block?"Block player":"Remove friend",remember=>
        {if(block)Settings.SkipBlockConfirmation=remember;else Settings.SkipRemoveFriendConfirmation=remember;Settings.Save();}));
    }
    public async Task BlockPlayer(string name,string ip,bool blocked)
    {
        if(blocked&&!Settings.IsBlocked(ip)&&!await ConfirmPlayerAction(name,true))return;
        try{await Peer.SetBlocked(name,ip,blocked);discovered.RemoveAll(p=>p.Address==ip);RenderRooms();RenderActivePlayers();friendsWindow?.Render();}
        catch(Exception error){UI.Error(error.Message);}
    }
    public void ShowBlockedPlayers()
    {
        var w=new Window{Owner=this,ResizeMode=ResizeMode.NoResize};var body=UI.Shell(w,"Blocked players",420,340);w.MinHeight=0;w.SizeToContent=SizeToContent.Height;w.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var rows=new StackPanel{Margin=new(18)};var blockedScroll=UI.Scroll(rows);blockedScroll.MaxHeight=360;body.Children.Add(blockedScroll);
        void Render(){rows.Children.Clear();if(Settings.BlockedUsers.Length==0)rows.Children.Add(UI.LocalizedText("No blocked players",12,UI.Muted));foreach(var user in Settings.BlockedUsers){var row=new Grid{Margin=new(0,0,0,12)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=UI.Auto});row.Children.Add(UI.Stack(UI.Text(user.Name,13,bold:true),UI.Text(user.Address,11,UI.Muted)));var button=UI.AsyncButton("Unblock",async()=>{await BlockPlayer(user.Name,user.Address,false);Render();});Grid.SetColumn(button,1);row.Children.Add(button);rows.Children.Add(row);}}
        Render();w.Show();
    }
    public Window CreatePlayerInfo(Window owner,string name,string ip,Player? player=null)
    {
        string key=ip.Length>0?ip:"name:"+name;
        if(playerInfoWindows.TryGetValue(key,out var existing))return existing;
        // Independent windows do not raise the dashboard's ownership group over chats.
        var w=new Window{ResizeMode=ResizeMode.NoResize};
        playerInfoWindows[key]=w;w.Closed+=(_,_)=>playerInfoWindows.Remove(key);
        var body=UI.Shell(w,"Player info",400,300);w.MinHeight=0;w.MinWidth=400;w.SizeToContent=SizeToContent.Height;
        var content=new StackPanel{Margin=new(20)};body.Children.Add(content);
        var presence=Presence.Find(ip,DateTimeOffset.UtcNow);
        string state=Settings.IsBlocked(ip)?"Blocked":ip==Peer.Address?"You":presence?.Reconnecting==true?"Reconnecting":player!=null||presence!=null?"Online":"Offline";
        var nameText=UI.Text(name,18,bold:true);nameText.TextWrapping=TextWrapping.Wrap;nameText.MaxWidth=280;
        var activityHeading=UI.Text(state,11,UI.Green);content.Children.Add(UI.Row(UI.Avatar(name,40),UI.Stack(nameText,activityHeading)));content.Children.Add(PlayerRoomActivity(ip));
        TextBlock Detail(string label,string value)
        {
            var row=new Grid{Margin=new(0,12,0,0)};row.ColumnDefinitions.Add(new(){Width=new GridLength(100)});row.ColumnDefinitions.Add(new());
            row.Children.Add(UI.LocalizedText(label,11,UI.Muted));var text=UI.LocalizedText(value,12);text.TextWrapping=TextWrapping.Wrap;Grid.SetColumn(text,1);row.Children.Add(text);content.Children.Add(row);return text;
        }
        var ipText=Detail("VPN address",string.IsNullOrEmpty(ip)?"Unavailable":ip);
        if(UI.ValidAddress(ip))
        {
            ipText.Cursor=Cursors.Hand;ipText.Focusable=true;ipText.Foreground=UI.Brush(UI.Accent);
            var tip=new ToolTip{Content="Click to copy IP address",PlacementTarget=ipText};ipText.ToolTip=tip;
            System.Windows.Automation.AutomationProperties.SetName(ipText,"Copy IP address "+ip);
            var reset=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
            reset.Tick+=(_,_)=>{reset.Stop();tip.IsOpen=false;L.Bind(tip,ContentControl.ContentProperty,"Click to copy IP address");};
            void CopyAddress()
            {
                reset.Stop();
                try{Clipboard.SetText(ip);L.Bind(tip,ContentControl.ContentProperty,"Copied");}
                catch(System.Runtime.InteropServices.ExternalException){L.Bind(tip,ContentControl.ContentProperty,"Clipboard busy · try again");}
                tip.IsOpen=true;reset.Start();
            }
            ipText.MouseLeftButtonUp+=(_,e)=>{CopyAddress();e.Handled=true;};
            ipText.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space){CopyAddress();e.Handled=true;}};
            w.Closed+=(_,_)=>{reset.Stop();tip.IsOpen=false;};
        }
        var pingRow=new Grid{Margin=new(0,12,0,0)};pingRow.ColumnDefinitions.Add(new(){Width=new GridLength(100)});pingRow.ColumnDefinitions.Add(new());
        pingRow.Children.Add(UI.LocalizedText("Ping",11,UI.Muted));var pingBadge=new PingBadge{Milliseconds=Latency.Get(ip,Peer.Address)};Grid.SetColumn(pingBadge,1);pingRow.Children.Add(pingBadge);content.Children.Add(pingRow);
        var activityStatus=Detail("Activity","");var idleDuration=Detail("Idle for","");var onlineSince=Detail("Online since","");var lastSeen=Detail("Last online","");var offlineAt=Detail("Offline since","");
        string When(DateTimeOffset? time)=>time?.ToLocalTime().ToString("g")??L.T("Not observed");
        void UpdateActivity(){var now=DateTimeOffset.UtcNow;var current=Presence.Find(ip,now);var livePlayer=Peer.Current?.Players.FirstOrDefault(p=>p.Address==ip);var idle=ip==Peer.Address?IdlePresence.Seconds():current?.IdleNow(now)??livePlayer?.IdleSeconds;var manual=ip==Peer.Address?Settings.ManualAfk:current?.ManualAfk??livePlayer?.ManualAfk??false;var afk=IdlePresence.IsAfk(idle,manual);var history=Presence.History(ip);activityStatus.Text=current?.Reconnecting==true?"Reconnecting":ip!=Peer.Address&&current==null&&livePlayer==null?"Offline / unreachable":afk?(manual?"AFK · set manually":"AFK · idle"):"Available";var activitySource=activityStatus.Text;L.Bind(activityStatus,TextBlock.TextProperty,activitySource);L.Bind(activityHeading,TextBlock.TextProperty,activitySource);activityHeading.Foreground=UI.Brush(afk?IdlePresence.AwayColor:current==null?UI.Muted:UI.Green);nameText.Foreground=UI.Brush(afk?UI.Muted:"#EFF1F5");idleDuration.Text=IdlePresence.Duration(idle);onlineSince.Text=When(ip==Peer.Address?new DateTimeOffset(Process.GetCurrentProcess().StartTime):history?.OnlineSince);lastSeen.Text=IdlePresence.LastOnline(history?.LastSeen,now);lastSeen.ToolTip=history?.LastSeen?.ToLocalTime().ToString("F");offlineAt.Text=When(history?.OfflineAt);var connected=ip==Peer.Address||current!=null||livePlayer!=null;
            ((FrameworkElement)onlineSince.Parent).Visibility=connected?Visibility.Visible:Visibility.Collapsed;
            ((FrameworkElement)lastSeen.Parent).Visibility=connected?Visibility.Collapsed:Visibility.Visible;
            ((FrameworkElement)offlineAt.Parent).Visibility=connected?Visibility.Collapsed:Visibility.Visible;
            ((FrameworkElement)idleDuration.Parent).Visibility=connected?Visibility.Visible:Visibility.Collapsed;
        }
        if(ip==Peer.Address){var availability=CreateAvailabilityButton();availability.Margin=new(0,16,0,0);availability.Click+=(_,_)=>UpdateActivity();content.Children.Add(availability);}
        UpdateActivity();
        var infoLatency=new PlayerLatency();bool infoClosed=false;
        var pingTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        async Task UpdatePing()
        {
            if(!Peer.AllowsPeer(ip)){pingBadge.Milliseconds=null;return;}
            try{await infoLatency.Refresh(new[]{ip},Peer.Address);if(!infoClosed)pingBadge.Milliseconds=infoLatency.Get(ip,Peer.Address);}
            catch(Exception error){Debug.WriteLine("Player info ping: "+error.Message);}
        }
        pingTimer.Tick+=async(_,_)=>{UpdateActivity();await UpdatePing();};
        w.Loaded+=async(_,_)=>{pingTimer.Start();await UpdatePing();};
        void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>UpdateActivity();L.Current.PropertyChanged+=LanguageChanged;
        w.Closed+=(_,_)=>{infoClosed=true;pingTimer.Stop();L.Current.PropertyChanged-=LanguageChanged;};
        if(player!=null){Detail("Room",(player.Id=="host"?"Host":"Guest")+" · "+(player.Ready?"Ready":"Not ready"));Detail("Selection",(player.Team>0?"Team "+player.Team:"No team")+" · "+(player.Spot>0?"Spot "+player.Spot:"Random spot"));}
        var actions=new Grid{Margin=new(0,20,0,0)};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new(){Width=UI.Auto});
        if(ip!=Peer.Address)
        {
            var message=UI.Require(UI.Button("Send message",()=>OpenChat(ip,name),true),()=>UI.ValidAddress(ip)&&Peer.AllowsPeer(ip)&&!Settings.IsBlocked(ip),()=>Settings.IsBlocked(ip)?"Unblock this player before messaging.":"Choose a valid recipient VPN address.");
            message.Margin=new(0,18,0,0);message.HorizontalAlignment=HorizontalAlignment.Left;content.Children.Add(message);
            var friend=UI.Button(Settings.Friends.Any(f=>f.Address==ip)?"Remove friend":"Add friend",()=>{});
            friend.Tag=Settings.Friends.Any(f=>f.Address==ip)?"Danger":null;
            friend.Click+=async(_,_)=>{await ToggleFriend(name,ip);L.Bind(friend,ContentControl.ContentProperty,Settings.Friends.Any(f=>f.Address==ip)?"Remove friend":"Add friend");friend.Tag=Settings.Friends.Any(f=>f.Address==ip)?"Danger":null;};
            UI.Require(friend,()=>UI.ValidAddress(ip)&&(Settings.Friends.Any(f=>f.Address==ip)||!Settings.IsBlocked(ip)),()=>"Unblock this player to change friendship.");friend.HorizontalAlignment=HorizontalAlignment.Left;actions.Children.Add(friend);
        }
        var close=UI.Button("Close",w.Close);close.Margin=new(0);Grid.SetColumn(close,1);actions.Children.Add(close);content.Children.Add(actions);return w;
    }
    readonly HashSet<string> pendingInvites=[];
    void ShowInvitation(string ip,string name,Room room)
    {
        if(!pendingInvites.Add(ip))return;
        if(Settings.MessageSounds)MessageAlert.Effect("invite");
        var w=new Window{WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize,Topmost=true,ShowActivated=false,ShowInTaskbar=true};
        var body=UI.Shell(w,"Room invitation",370,198);w.MinWidth=370;w.MinHeight=198;
        var join=UI.AsyncButton("Join room",async()=>
        {
            if(joining)return;
            var advertised=await PeerService.Query(room.HostAddress,Peer.Address);
            if(advertised?.Id!=room.Id)throw new InvalidOperationException("This invitation has expired or the room moved. Ask for a new invitation.");
            address.Text=room.HostAddress;await Join();if(Peer.Current?.Id==room.Id)w.Close();
        },true);
        UI.Require(join,()=>!joining,()=>"A room connection is already in progress.");
        var actions=UI.Row(UI.Button("Dismiss",w.Close),join);actions.HorizontalAlignment=HorizontalAlignment.Right;
        body.Children.Add(new Border{Padding=new(20,16,20,16),Child=UI.Stack(UI.LocalizedText(name+" invited you",16,bold:true),new Border{Margin=new(0,5,0,0),Child=UI.Text(room.Title,12,UI.Muted)},new Border{Margin=new(0,18,0,0),Child=actions})});
        w.Loaded+=(_,_)=>WindowAttention.Flash(w);w.Activated+=(_,_)=>WindowAttention.Stop(w);
        w.Closed+=(_,_)=>pendingInvites.Remove(ip);w.Show();
    }
    bool joining;
    async Task Join()
    {
        if(joining||!EnsureOnline())return;
        if(Peer.Current!=null&&rooms.SelectedItem is RoomEntry selected&&selected.Room.Id==Peer.Current.Id){ShowRoom();return;}
        if(address.Text.Trim()==Peer.Address && Peer.IsHost){ShowRoom();return;}
        if(!await CanLeave())return;
        var target=address.Text.Trim();joining=true;CommandManager.InvalidateRequerySuggested();
        var feedback=new Window{Owner=this,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var content=UI.Shell(feedback,"Joining room",380,200);feedback.MinHeight=200;
        var progress=new ProgressBar{Height=5,IsIndeterminate=true,Margin=new(0,18,0,0)};
        content.Children.Add(new Border{Padding=new(24),Child=UI.Stack(UI.LocalizedText("Joining room…",17,bold:true),UI.LocalizedText("Connecting to the host",12,UI.Muted),progress)});
        feedback.Closing+=(_,e)=>{if(joining)e.Cancel=true;};feedback.Show();
        try{await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);await CloseRoom();await Peer.Join(target);ShowRoom();}
        finally{joining=false;feedback.Close();CommandManager.InvalidateRequerySuggested();}
    }
    async Task<bool> CanLeave()=>Peer.Current==null || await UI.Ask(this,"Switch room","Leave your current room? If you host, the next player takes over.","Leave room");
    RoomEditor? activeRoomEditor;
    bool openingRoomEditor;
    async void CreateRoom()
    {
        if(activeRoomEditor!=null){activeRoomEditor.Activate();return;}
        if(openingRoomEditor||joining)return;
        openingRoomEditor=true;
        try
        {
        if(!EnsureOnline() || !await CanLeave())return;
        var previousRoom=Peer.Current?.Id;
        var draft=Settings.NewHostedRoom();
        var editor=new RoomEditor(this,Settings,draft,async room=>
        {
            if(Peer.Current?.Id!=previousRoom&&!await CanLeave())throw new InvalidOperationException("Room creation cancelled. Your current room is unchanged.");
            await CloseRoom();
            var spots=await Task.Run(()=>GameMapData.Spots(Settings.GamePath,room.MapPath,room.Patch));
            Peer.Host(room,spots);
            Settings.LastRoomTitle=room.Title;Settings.Save();
        });
        activeRoomEditor=editor;
        if(await UI.ShowPopup(editor))ShowRoom();
        }
        catch(Exception error){UI.Error(error.Message);}
        finally{activeRoomEditor=null;openingRoomEditor=false;}
    }

    bool closingRoom;
    public Task CloseRoom()=>CloseRoom(false);
    public async Task CloseRoom(bool applicationExit)
    {
        if(closingRoom)return;closingRoom=true;
        try{await Peer.LeaveRoom(applicationExit);var old=roomWindow;roomWindow=null;if(old!=null){old.AllowClose=true;old.Close();}}
        finally{closingRoom=false;}
    }
    public void OpenCurrentRoom(){if(Peer.Current!=null)ShowRoom();}
    void ShowRoom()
    {
        if(closingRoom)return;
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
        if(chats.TryGetValue(ip,out var old)){if(activate){UI.Reveal(old);old.FocusInput();}return old;}
        var w=new ChatWindow(this,ip,name){ShowActivated=activate};chats[ip]=w;w.Closed+=(_,_)=>chats.Remove(ip);w.Show();return w;
    }
    sealed record RoomEntry(string Address,Room Room,bool Reconnecting=false,long? Ping=null,ImageSource? Icon=null)
    {
        public string Mode=>Room.Mode; public string Title=>Room.Title; public string Host=>Room.Host; public string Map=>Room.Map;
        public int PlayerCount=>Room.Players.Count;
        public string Slots=>$"{Room.Players.Count} / {Room.Capacity}";public string Patch=>Room.Patch;
        public string Status=>Reconnecting?"◌ Reconnecting":Room.Phase=="Gathering"?(Room.FriendsOnly?"●  Friends only":"●  Public"):"●  Host in BFME2";
    }
}

public sealed class RoomWindow : Window
{
    public bool AllowClose { get; set; }
    readonly BrowserWindow app;
    readonly TextBlock title=UI.Text("",12,UI.Muted);
    readonly TextBlock playerCount=UI.LocalizedText("PLAYERS",10,UI.Muted,true);
    readonly TextBlock presetStatus=UI.LocalizedText("",11,UI.Green,true);
    readonly MatchSummary setupSummary=new();
    readonly TextBlock readySummary=UI.LocalizedText("",11,UI.Muted);
    readonly StackPanel customSetup=new();
    readonly TextBox roomName=new(){MaxLength=80};
    readonly ComboBox privacyChoice=new(){ItemsSource=new[]{"Public on VPN","Friends only"}};
    readonly MapPreview mapPreview=new();
    readonly ComboBox teamChoice=new(), colorChoice=new();
    readonly TextBlock placementStatus=UI.LocalizedText("",11,UI.Muted);
    readonly Button clearSpot;
    bool syncingPlacement,changingPlacement,watchingNative;
    TaskCompletionSource<bool>? syncPrepared;
    readonly LivePlacement.Control syncControl=new();
    readonly System.Threading.CancellationTokenSource syncLifetime=new();
    readonly TextBlock sideStatus=UI.LocalizedText("Rhûn 3v3 · Hosting team right · Other team left · Same top/middle/bottom position",11,UI.Accent,true);
    readonly TextBlock liveStatus=UI.LocalizedText("Live sync starts when BFME2 opens.",10,UI.Muted);
    readonly TextBlock patchNotice=UI.LocalizedText("",11,"#E5B77A");
    readonly Grid patchWarning=new();
    readonly Button switchPatch;
    bool switchingPatch;
    bool PatchChanging=>switchingPatch||PatchSwitcher.IsSwitching;
    readonly TextBlock placementGuide=UI.LocalizedText("",10,UI.Muted);
    string? lastPatchWarning;
    sealed record TeamOption(int Value){public override string ToString()=>Value==0?"No team":"Team "+Value;}
    string lastRoomName="";bool? lastPrivacy;
    readonly ListBox players=new();
    readonly TextBox map,patch,rules;
    readonly ChatTranscript messages=new();
    readonly ChatTranscript chatScroll;
    readonly RoomChatPanel chatPanel;
    readonly CheckBox consent=new(){Content="Ready · allow host to launch BFME2"};
    readonly Button launch;
    readonly Button launchMine,abort,requestReady;
    readonly MenuItem launchMenu,launchAllMenu;
    Window? readyPrompt;
    DateTimeOffset nextReadyRequest;
    readonly System.Windows.Threading.DispatcherTimer launchStateTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    System.Threading.CancellationTokenSource? launchCancellation;
    void UpdateLaunchActions()
    {
        bool active=launching||GameLauncher.IsRunning();
        requestReady.Visibility=!active&&app.Peer.IsHost?Visibility.Visible:Visibility.Collapsed;
        requestReady.IsEnabled=app.Peer.Current?.Players.Count>1&&DateTimeOffset.UtcNow>=nextReadyRequest;
        launchMine.Visibility=active?Visibility.Collapsed:Visibility.Visible;
        launchMine.IsEnabled=!app.Peer.IsReconnecting;
        launch.Visibility=!active&&app.Peer.IsHost?Visibility.Visible:Visibility.Collapsed;
        abort.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        launchAllMenu.Visibility=app.Peer.IsHost?Visibility.Visible:Visibility.Collapsed;
        CommandManager.InvalidateRequerySuggested();
    }
    async Task AbortGame()
    {
        launchCancellation?.Cancel();
        try{if(!await syncControl.Abort())await GameLauncher.AbortWithPermission(app.Settings.GamePath);}
        catch(OperationCanceledException){AppendSystem("Abort cancelled. BFME2 is still running; approve the Windows permission prompt to stop it.");UpdateLaunchActions();return;}
        AppendSystem("BFME2 stopped. You remain in the Fellowship room.");UpdateLaunchActions();
    }
    readonly Border setupBanner;
    bool customPresetRules=true;
    readonly StackPanel inlineRules=new(), mapField=new(){Margin=new(0,0,0,12)};
    readonly ComboBox resourceChoice=new(), commandChoice=new(), mapChoice=new SearchableMapBox();
    readonly TextBox capacityChoice=new(){MaxLength=2,ToolTip="Fellowship room limit: 2–64. BFME2 matches support at most 8 players."};
    readonly CheckBox customChoice=new(){Content="Custom Heroes"}, ringChoice=new(){Content="Ring Heroes"};
    readonly TextBlock ruleStatus=UI.LocalizedText("Changes save automatically",10,UI.Muted);
    bool syncingRules, savingRules;
    RoomPresetPicker? roomPresets;
    string inlinePatch="";
    sealed record CommandFactor(int Value){public override string ToString()=>$"{Value/100.0:0.##}×";}
    bool allowLaunch;
    string lastSettings="";
    public RoomWindow(BrowserWindow app)
    {
        this.app=app;var work=SystemParameters.WorkArea;
        var body=UI.Shell(this,app.Peer.Current?.Title??"",Math.Min(1160,Math.Max(1,work.Width-32)),Math.Min(840,Math.Max(1,work.Height-32)),title);MinWidth=Math.Min(900,Width);MinHeight=Math.Min(660,Height);
        launchMenu=UI.MenuAction("Launch game",LaunchLocal,()=>!app.Peer.IsReconnecting&&!launching&&!PatchChanging&&!GameLauncher.IsRunning()&&app.Peer.Current!=null&&File.Exists(app.Settings.GamePath));
        launchAllMenu=UI.MenuAction("Launch all",async()=>
        {
            try{GameLauncher.Validate(app.Settings.GamePath);await app.Peer.Launch();}catch(Exception error){UI.Error(error.Message);}
        },()=>app.Peer.IsHost&&!app.Peer.IsReconnecting&&!launching&&!PatchChanging&&!GameLauncher.IsRunning()&&app.Peer.Current?.AllGuestsReady==true&&File.Exists(app.Settings.GamePath));
        UI.Menus(this,UI.MenuGroup("_Room",launchMenu,launchAllMenu,UI.MenuAction("Leave room",async()=>{try{await app.CloseRoom();}catch(Exception error){UI.Error(error.Message);}})),UI.MenuGroup("_View",UI.MenuAction("Room browser",()=>UI.Reveal(app)),UI.MenuAction("Friends",app.OpenFriends)),UI.MenuGroup("_Help",UI.MenuAction("Game setup guide",UI.OpenGuide)));
        UI.Rows(body,UI.Auto,UI.Star,UI.Auto);
        patchWarning.ColumnDefinitions.Add(new());patchWarning.ColumnDefinitions.Add(new(){Width=UI.Auto});patchWarning.Margin=new(22,12,22,0);
        patchNotice.TextWrapping=TextWrapping.Wrap;patchWarning.Children.Add(patchNotice);
        switchPatch=UI.AsyncButton("",SwitchRoomPatch);switchPatch.Margin=new(14,0,0,0);switchPatch.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(switchPatch,1);patchWarning.Children.Add(switchPatch);
        UI.At(body,patchWarning,0);
        var columns=new Grid{Margin=new(22,12,22,16)};columns.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new(300)});
        var chatHead=UI.LocalizedText("ROOM CHAT",10,UI.Muted,true);
        chatScroll=messages;
        var compose=new Grid{Margin=new(12)};compose.ColumnDefinitions.Add(new());compose.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var input=new TextBox{MaxLength=2000,ToolTip="Message your room · Enter to send",Margin=new(0,0,8,0)};
        var roomId=app.Peer.Current?.Id;
        var outbox=new ChatOutbox(messages,async(text,id)=>
        {
            if(app.Peer.Current is not {} activeRoom||activeRoom.Id!=roomId)throw new IOException("You are not connected to a room.");
            await app.Peer.Say(text,id);
        },()=>{if(app.Settings.MessageSounds)MessageAlert.Effect("sent");});
        Task Send()
        {
            if(string.IsNullOrWhiteSpace(input.Text))return Task.CompletedTask;
            if(app.Peer.Current is not {} activeRoom||activeRoom.Id!=roomId){L.Bind(chatHead,TextBlock.TextProperty,"Join a room before sending a message.");return Task.CompletedTask;}
            if(app.Peer.IsReconnecting){L.Bind(chatHead,TextBlock.TextProperty,"Reconnecting to room…");return Task.CompletedTask;}
            string text=input.Text;input.Clear();outbox.Send(app.Settings.Name,text);
            L.Bind(chatHead,TextBlock.TextProperty,"ROOM CHAT");chatHead.ToolTip=null;input.Focus();return Task.CompletedTask;
        }
        input.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Send();}};
        compose.Children.Add(input);var send=UI.Require(UI.AsyncButton("Send",Send,true),()=>!app.Peer.IsReconnecting&&!string.IsNullOrWhiteSpace(input.Text)&&app.Peer.Current!=null,()=>app.Peer.IsReconnecting?"Reconnecting to room…":app.Peer.Current==null?"Join a room before sending a message.":"Type a message before sending.");send.Margin=new(0);Grid.SetColumn(send,1);compose.Children.Add(send);
        chatPanel=new(messages,players,playerCount,chatHead,compose);var chatCard=UI.Card(chatPanel,"#191D24",0);chatCard.Margin=new(0,0,14,0);columns.Children.Add(chatCard);
        var right=new StackPanel();right.Children.Add(UI.LocalizedText("MATCH SETUP",10,UI.Muted,true));
        setupSummary.Margin=new(0,12,0,0);
        var banner=setupBanner=UI.Card(UI.Stack(UI.LocalizedText("THE BATTLE FOR MIDDLE-EARTH II",10,"#AABCE1",true),setupSummary),"#1C2330",16);banner.Margin=new(0,14,0,12);right.Children.Add(banner);
        right.Children.Add(presetStatus);right.Children.Add(mapField);right.Children.Add(mapPreview);
        var choices=new Grid();choices.ColumnDefinitions.Add(new());choices.ColumnDefinitions.Add(new());
        var teamField=new StackPanel{Margin=new(0,0,6,0)};var colorField=new StackPanel{Margin=new(6,0,0,0)};Grid.SetColumn(colorField,1);
        teamField.Children.Add(UI.LocalizedText("TEAM",10,UI.Muted,true));colorField.Children.Add(UI.LocalizedText("COLOR",10,UI.Muted,true));
        L.Choices(teamChoice);teamChoice.ItemsSource=Enumerable.Range(0,5).Select(n=>new TeamOption(n)).ToArray();teamChoice.Margin=new(0,6,0,8);teamField.Children.Add(teamChoice);
        colorChoice.ItemsSource=PlayerColors.All;colorChoice.Margin=new(0,6,0,8);
        var colorTemplate=new DataTemplate(typeof(PlayerColor));var colorRow=new FrameworkElementFactory(typeof(StackPanel));colorRow.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var swatch=new FrameworkElementFactory(typeof(Border));swatch.SetValue(Border.WidthProperty,12.0);swatch.SetValue(Border.HeightProperty,12.0);swatch.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));swatch.SetValue(Border.MarginProperty,new Thickness(0,0,7,0));swatch.SetBinding(Border.BackgroundProperty,new Binding("Hex"));colorRow.AppendChild(swatch);
        var colorName=new FrameworkElementFactory(typeof(TextBlock));var colorBinding=new MultiBinding{Converter=new L.DisplayConverter()};colorBinding.Bindings.Add(new Binding("Name"));colorBinding.Bindings.Add(new Binding(nameof(L.Direction)){Source=L.Current});colorName.SetBinding(TextBlock.TextProperty,colorBinding);colorRow.AppendChild(colorName);colorTemplate.VisualTree=colorRow;colorChoice.ItemTemplate=colorTemplate;
        colorField.Children.Add(colorChoice);choices.Children.Add(teamField);choices.Children.Add(colorField);right.Children.Add(choices);
        colorChoice.SelectionChanged+=async(_,_)=>{if(!syncingPlacement&&colorChoice.SelectedItem is PlayerColor color)await ChangePlacement(null,null,color.Value);};
        teamChoice.SelectionChanged+=async(_,_)=>{if(!syncingPlacement&&teamChoice.SelectedItem is TeamOption team)await ChangePlacement(null,team.Value);};
        mapPreview.SpotSelected+=async spot=>await ChangePlacement(spot,null);
        clearSpot=UI.AsyncButton("Clear my spot",()=>ChangePlacement(0,null));UI.DisabledHint(clearSpot,()=>app.Peer.Current==null?"Join a room first.":launching||changingPlacement?"Wait for the current operation to finish.":"Select a starting spot on the map before clearing it.");clearSpot.Margin=new(0,0,0,8);right.Children.Add(clearSpot);
        sideStatus.TextWrapping=TextWrapping.Wrap;sideStatus.Margin=new(0,8,0,8);right.Children.Add(sideStatus);right.Children.Add(placementStatus);right.Children.Add(liveStatus);placementGuide.Margin=new(0,8,0,12);right.Children.Add(placementGuide);
        map=new TextBox();patch=new TextBox();rules=new TextBox();
        void AddChoice(string label,ComboBox choice,object[] values)
        {
            var caption=UI.LocalizedText(label,10,UI.Muted,true);caption.Margin=new(0,12,0,6);
            var container=choice==mapChoice?mapField:inlineRules;
            container.Children.Add(caption);choice.ItemsSource=values;L.Choices(choice);container.Children.Add(choice);
            choice.SelectionChanged+=async(_,_)=>await SaveInlineRules();
        }
        AddChoice("MAP",mapChoice,GameInstallation.Maps(app.Settings.GamePath,app.Peer.Current?.Patch??"1.00").Cast<object>().ToArray());
        roomPresets=new RoomPresetPicker(app.Settings,app.Peer.Current?.Patch??"1.00"){Margin=new(0,10,0,0)};
        mapField.Children.Insert(0,roomPresets);
        roomPresets.ModeChanged+=()=>{customPresetRules=roomPresets.IsCustom;if(!syncingRules)Refresh();};
        roomPresets.CustomPicked+=async ()=>
        {
            if(syncingRules||!app.Peer.IsHost||app.Peer.Current is not {} current||savingRules||launching)return;
            var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;draft.Mode="Custom";
            try{await app.Peer.EditRoom(draft);}catch(Exception error){UI.Error(error.Message);}
        };
        roomPresets.Picked+=async preset=>
        {
            if(syncingRules||!app.Peer.IsHost||app.Peer.Current is not {} current||savingRules||launching)return;
            if(!GameInstallation.Maps(app.Settings.GamePath,current.Patch).Any(m=>m.Path==preset.MapPath)){UI.Error("The preset map is not installed: "+preset.Map);return;}
            var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;
            draft.Mode=preset.Mode;draft.Map=preset.Map;draft.MapPath=preset.MapPath;draft.Match=new(){Resources=preset.Resources,CommandPoints=preset.CommandPoints,CustomHeroes=preset.CustomHeroes,RingHeroes=preset.RingHeroes};
            savingRules=true;mapField.IsEnabled=inlineRules.IsEnabled=false;
            try{await app.Peer.EditRoom(draft);L.Bind(ruleStatus,TextBlock.TextProperty,"Preset applied");}
            catch(Exception error){UI.Error(error.Message);}
            finally{savingRules=false;Refresh();}
        };
        var capacityLabel=UI.LocalizedText("ROOM LIMIT (2–64)",10,UI.Muted,true);capacityLabel.Margin=new(0,12,0,6);inlineRules.Children.Add(capacityLabel);inlineRules.Children.Add(capacityChoice);
        capacityChoice.LostKeyboardFocus+=async(_,_)=>await SaveInlineRules();capacityChoice.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await SaveInlineRules();}};
        AddChoice("STARTING RESOURCES",resourceChoice,MatchRules.ResourceValues.Cast<object>().ToArray());
        AddChoice("COMMAND POINTS",commandChoice,MatchRules.CommandValues.Select(n=>(object)new CommandFactor(n)).ToArray());
        foreach(var check in new[]{customChoice,ringChoice})
        {
            check.Margin=new(0,12,0,0);inlineRules.Children.Add(check);
            check.Click+=async(_,_)=>await SaveInlineRules();
        }
        ruleStatus.Margin=new(0,12,0,8);inlineRules.Children.Add(ruleStatus);right.Children.Add(inlineRules);
        customSetup.Margin=new(0,18,0,0);
        customSetup.Children.Add(UI.LocalizedText("ROOM NAME",10,UI.Muted,true));roomName.Margin=new(0,8,0,8);customSetup.Children.Add(roomName);
        var confirmName=UI.Require(UI.AsyncButton("Confirm name",()=>SaveRoomDetails(false)),()=>app.Peer.IsHost&&!string.IsNullOrWhiteSpace(roomName.Text)&&roomName.Text.Trim()!=app.Peer.Current?.Title,()=>!app.Peer.IsHost?"Only the host can rename the room.":string.IsNullOrWhiteSpace(roomName.Text)?"Enter a room name.":"Change the room name before confirming.");confirmName.Margin=new(0,4,0,0);confirmName.HorizontalAlignment=HorizontalAlignment.Stretch;customSetup.Children.Add(confirmName);
        var privacyLabel=UI.LocalizedText("VISIBILITY",10,UI.Muted,true);privacyLabel.Margin=new(0,16,0,8);customSetup.Children.Add(privacyLabel);
        customSetup.Children.Add(privacyChoice);var confirmPrivacy=UI.Require(UI.AsyncButton("Confirm privacy",()=>SaveRoomDetails(true)),()=>app.Peer.IsHost&&privacyChoice.SelectedIndex>=0&&(privacyChoice.SelectedIndex==1)!=app.Peer.Current?.FriendsOnly,()=>!app.Peer.IsHost?"Only the host can change room privacy.":"Select a different privacy option before confirming.");confirmPrivacy.Margin=new(0,10,0,0);confirmPrivacy.HorizontalAlignment=HorizontalAlignment.Stretch;customSetup.Children.Add(confirmPrivacy);
        right.Children.Add(customSetup);
        var guide=UI.Stack(UI.LocalizedText("IN BFME2",10,UI.Muted,true),UI.LocalizedText("1   Open Multiplayer → Network\n2   Host creates; guests join\n3   Check patch, rules, teams and spots",12,"#B5C0D3"));guide.Margin=new(0,20,0,0);guide.Children[1].SetValue(MarginProperty,new Thickness(0,8,0,0));right.Children.Add(guide);
        var note=UI.LocalizedText("Any room member can host BFME2. Use the patch shown by the room and join the native lobby manually.",11,"#B5A98D");note.Margin=new(0,14,0,0);right.Children.Add(note);
        var settingsCard=UI.Card(UI.Scroll(right),"#191D24",16);Grid.SetColumn(settingsCard,1);columns.Children.Add(settingsCard);UI.At(body,columns,1);
        var bottom=new Grid{Background=UI.Brush("#191D24"),Margin=new(0)};bottom.ColumnDefinitions.Add(new());bottom.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var state=UI.Stack(consent,readySummary);state.Margin=new(22,8,10,8);state.VerticalAlignment=VerticalAlignment.Center;consent.Margin=new(0);L.Bind(consent,ContentControl.ContentProperty,"Ready · allow host to launch BFME2");readySummary.TextWrapping=TextWrapping.NoWrap;readySummary.TextTrimming=TextTrimming.CharacterEllipsis;readySummary.Visibility=Visibility.Collapsed;bottom.Children.Add(state);
        consent.Click+=async(_,_)=>{try{if(consent.IsChecked==true)GameLauncher.Validate(app.Settings.GamePath);allowLaunch=consent.IsChecked==true;await app.Peer.SetReady(allowLaunch);}catch(Exception e){allowLaunch=false;consent.IsChecked=false;UI.Error(e.Message);}};
        launch=UI.AsyncButton("Launch all",async()=>{GameLauncher.Validate(app.Settings.GamePath);await app.Peer.Launch();},true,()=>!launching&&app.Peer.Current?.AllGuestsReady==true);
        UI.Require(launch,()=>app.Peer.IsHost&&app.Peer.Current!=null&&!launching&&!PatchChanging&&File.Exists(app.Settings.GamePath)&&app.Peer.Current.AllGuestsReady,()=>PatchChanging?"Switching patch…":launching?"Wait for the current launch to finish.":!File.Exists(app.Settings.GamePath)?"Choose your BFME2 executable in Settings.":"The host can launch once every player is ready.");
        launchMine=UI.Require(UI.Button("Launch game",LaunchLocal,true),()=>!app.Peer.IsReconnecting&&!launching&&!PatchChanging&&!GameLauncher.IsRunning()&&app.Peer.Current!=null&&File.Exists(app.Settings.GamePath));
        abort=UI.AsyncButton("Abort",AbortGame);abort.Tag="Danger";abort.ToolTip="Stop BFME2 on this computer";
        requestReady=UI.AsyncButton("Request ready",async()=>{await app.Peer.RequestReady();nextReadyRequest=DateTimeOffset.UtcNow.AddSeconds(5);UpdateLaunchActions();AppendSystem("Ready request sent to room participants.");});
        requestReady.ToolTip="Ask everyone in the room to ready up";
        var actions=UI.Row(UI.AsyncButton("Leave",app.CloseRoom),requestReady,launchMine,launch,abort);actions.Margin=new(0,8,14,8);Grid.SetColumn(actions,1);bottom.Children.Add(actions);UI.At(body,bottom,2);
        launchStateTimer.Tick+=(_,_)=>{UpdateLaunchActions();UpdatePatchNotice();};Loaded+=(_,_)=>launchStateTimer.Start();Closed+=(_,_)=>{launchStateTimer.Stop();launchCancellation?.Cancel();readyPrompt?.Close();};UpdateLaunchActions();

        bool leaveQueued=false;
        Closing+=(_,e)=>
        {
            if(!AllowClose&&Application.Current is App {IsQuitting:false})
            {
                e.Cancel=true;if(leaveQueued)return;leaveQueued=true;
                // LeaveRoom can finish synchronously. Close only after this Closing event returns.
                Dispatcher.BeginInvoke(new Action(async()=>
                {
                    try{await app.CloseRoom();}catch(Exception ex){UI.Error(ex.Message);}
                    finally{leaveQueued=false;}
                }));
            }
        };
        app.Peer.RoomChatReceived+=OnChat;Closed+=(_,_)=>{app.Peer.RoomChatReceived-=OnChat;syncLifetime.Cancel();};Refresh();
        Loaded+=async(_,_)=>await WatchNative();
        ContentRendered+=(_,_)=>{if(IsActive)input.Focus();};
    }
    LivePlacement.Command? SyncCommand()
    {
        if((!app.Settings.LiveGameSync)||app.Peer.IsReconnecting||PatchChanging||syncLifetime.IsCancellationRequested||app.Peer.Current is not {} room||!HostPreset.CanSynchronize(app.Settings.GamePath,room))return null;
        var player=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId);
        return player==null?null:new(room.Id,room.MapPath,room.HostAddress,new(player.Team,player.Spot,player.Color),NativeSetup.From(room),string.Join("|",room.Players.Select(p=>p.Address).Distinct().OrderBy(ip=>ip)),Rhun3v3:RhunSides.Enabled(room),Sides:room.SidePlan,AllowExternalHost:true);
    }
    bool syncPaused;
    public void RetryLiveSync()=>syncPaused=false;
    void PauseLiveSync()=>syncPaused=true;
    async Task WatchNative()
    {
        if(watchingNative)return;watchingNative=true;
        int attempted=0;
        try
        {
            while(!syncLifetime.IsCancellationRequested)
            {
                if(!app.Settings.LiveGameSync)
                {
                    attempted=0;L.Bind(liveStatus,TextBlock.TextProperty,"Live sync is off in Settings. Set game rules, team and spot in BFME2 manually.");
                    await Task.Delay(400,syncLifetime.Token);continue;
                }
                if(!HostPreset.CanSynchronize(app.Settings.GamePath,app.Peer.Current))
                {
                    attempted=0;L.Bind(liveStatus,TextBlock.TextProperty,"Automatic lobby sync is available for verified BFME2 1.00 only. Set rules, teams and spots in BFME2 manually for this patch.");
                    await Task.Delay(400,syncLifetime.Token);continue;
                }
                if(syncPaused){await Task.Delay(400,syncLifetime.Token);continue;}
                int pid=0;
                foreach(var process in System.Diagnostics.Process.GetProcessesByName("game.dat").Concat(System.Diagnostics.Process.GetProcessesByName("game")))
                    using(process){pid=process.Id;break;}
                if(pid==0)attempted=0;
                if((pid==0||pid!=attempted)&&SyncCommand()!=null)
                {
                    attempted=pid;
                    syncPrepared=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        await LivePlacement.Listen(app.Settings.GamePath,pid,SyncCommand,async (choice,map)=>
                        {
                            changingPlacement=true;Refresh();
                            try{await app.Peer.SelectPlacement(choice.Spot,choice.Team,map,choice.Color);}
                            finally{changingPlacement=false;Refresh();}
                        },async setup=>
                        {
                            if(app.Peer.Current is not {} room)return;
                            var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(room))!;
                            var selected=GameInstallation.Maps(app.Settings.GamePath,room.Patch).FirstOrDefault(m=>m.Path.Equals(setup.Map,StringComparison.OrdinalIgnoreCase))??throw new IOException("Native map is not installed.");
                            draft.Map=selected.Name;draft.MapPath=selected.Path;draft.Match=setup.Rules;
                            savingRules=true;Refresh();
                            try{await app.Peer.SyncNativeRules(draft);}
                            finally{savingRules=false;Refresh();}
                        },message=>L.Bind(liveStatus,TextBlock.TextProperty,message),syncLifetime.Token,()=>syncPrepared.TrySetResult(true),syncControl,app.Peer.SyncRhunLayout);
                        attempted=0; // A reconnect must be allowed even when BFME2 keeps the same PID.
                        syncPrepared.TrySetResult(false);
                    }
                    catch(OperationCanceledException) when(syncLifetime.IsCancellationRequested){break;}
                    catch(Exception error)
                    {
                        syncPrepared.TrySetResult(false);
                        L.Bind(liveStatus,TextBlock.TextProperty,"Live sync paused. Native lobby remains available.");
                        AppendSystem("Live sync stopped: "+error.Message+" Save Settings with live sync enabled to retry. You can correct selections in BFME2 manually.");
                        // Do not repeatedly reopen UAC after a refusal or setup error.
                        PauseLiveSync();attempted=0;
                    }
                    finally{syncPrepared=null;}
                }
                await Task.Delay(1000,syncLifetime.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    async Task ChangePlacement(int? spot,int? team,int? color=null)
    {
        if(changingPlacement)return;
        changingPlacement=true;teamChoice.IsEnabled=false;colorChoice.IsEnabled=false;clearSpot.IsEnabled=false;L.Bind(placementStatus,TextBlock.TextProperty,"Confirming with host…");
        try{await app.Peer.SelectPlacement(spot,team,color:color);}
        catch(Exception e){AppendSystem(e.Message);}
        finally{changingPlacement=false;Refresh();}
    }
    async Task SaveRoomDetails(bool privacy)
    {
        if(!app.Peer.IsHost||app.Peer.Current is not {} current)return;
        var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;
        if(privacy)draft.FriendsOnly=privacyChoice.SelectedIndex==1;
        else{if(string.IsNullOrWhiteSpace(roomName.Text)){L.Bind(ruleStatus,TextBlock.TextProperty,"Enter a room name.");return;}draft.Title=roomName.Text.Trim();}
        customSetup.IsEnabled=false;
        try{await app.Peer.EditRoom(draft);L.Bind(ruleStatus,TextBlock.TextProperty,privacy?"Visibility updated":"Room name updated");}
        catch(Exception e){L.Bind(ruleStatus,TextBlock.TextProperty,e.Message);}
        finally{customSetup.IsEnabled=true;Refresh();}
    }
    async Task SaveInlineRules()
    {
        if(syncingRules||savingRules||!app.Peer.IsHost||app.Peer.Current is not {} current||
           mapChoice.SelectedItem is not GameMap selectedMap||resourceChoice.SelectedItem is not int resources||
           commandChoice.SelectedItem is not CommandFactor factor||!int.TryParse(capacityChoice.Text,out int capacity)||capacity is <2 or >64)return;
        var draft=System.Text.Json.JsonSerializer.Deserialize<Room>(System.Text.Json.JsonSerializer.Serialize(current))!;
        draft.Map=selectedMap.Name;draft.MapPath=selectedMap.Path;draft.Capacity=capacity;
        draft.Mode="Custom";draft.Match=new(){Resources=resources,CommandPoints=factor.Value,CustomHeroes=customChoice.IsChecked==true,RingHeroes=ringChoice.IsChecked==true};
        savingRules=true;inlineRules.IsEnabled=mapField.IsEnabled=false;L.Bind(ruleStatus,TextBlock.TextProperty,"Saving…");
        try{await app.Peer.EditRoom(draft);L.Bind(ruleStatus,TextBlock.TextProperty,"Saved");}
        catch(Exception e){L.Bind(ruleStatus,TextBlock.TextProperty,e.Message);}
        finally{savingRules=false;inlineRules.IsEnabled=mapField.IsEnabled=true;Refresh();}
    }
    void SyncInlineRules(Room room)
    {
        syncingRules=true;
        try
        {
            if(inlinePatch!=room.Patch)
            {
                inlinePatch=room.Patch;mapChoice.ItemsSource=GameInstallation.Maps(app.Settings.GamePath,room.Patch);
                roomPresets?.SetPatch(room.Patch);
            }
            mapChoice.SelectedItem=mapChoice.Items.Cast<GameMap>().FirstOrDefault(m=>m.Path==room.MapPath);
            if(!capacityChoice.IsKeyboardFocused)capacityChoice.Text=room.Capacity.ToString();resourceChoice.SelectedItem=room.Match.Resources;
            commandChoice.SelectedItem=commandChoice.Items.Cast<CommandFactor>().FirstOrDefault(f=>f.Value==room.Match.CommandPoints);
            customChoice.IsChecked=room.Match.CustomHeroes;ringChoice.IsChecked=room.Match.RingHeroes;
        }
        finally{syncingRules=false;}
    }
    void OnChat(string text,bool blocked,bool mine,string messageId)=>Dispatcher.InvokeAsync(()=>
    {
        var split=text.IndexOf("   ",StringComparison.Ordinal);
        var name=split<0?"Room":text[..split];var body=split<0?text:text[(split+3)..];
        if(mine&&messages.ConfirmOutgoing(messageId,body))return;
        if(!blocked){messages.Append(name,body,mine);return;}
        messages.AppendBlocked(name,body);
    });
    void Append(string name,string text)=>messages.Append(name,text);
    void AppendSystem(string text,params object[] arguments)=>messages.AppendSystem(text,arguments);
    string? activityRoom;
    string activityHost="",activityHostName="";
    bool activityPaused;
    sealed record ActivityMember(string Id,string Address,string Name,bool ConnectionLost);
    Dictionary<string,ActivityMember> activityRoster=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> activityDisconnected=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> activityDepartureIds=new(StringComparer.Ordinal);
    readonly Queue<string> activityDepartureOrder=new();
    void RememberDepartures(IEnumerable<RoomDeparture> departures)
    {
        foreach(var departure in departures)
            if(activityDepartureIds.Add(departure.Id))activityDepartureOrder.Enqueue(departure.Id);
        while(activityDepartureOrder.Count>256)activityDepartureIds.Remove(activityDepartureOrder.Dequeue());
    }
    void AppendActivity(ChatTranscript.SystemEventKind kind,string text,params object[] arguments)=>messages.AppendSystem(kind,text,arguments);
    void ObserveRoomActivity(Room? room,bool reconnecting)
    {
        // Host handoff temporarily clears Current. An unavailable transport is
        // not evidence that everyone left, so retain the immutable baseline.
        if(room==null){if(activityRoom!=null)activityPaused=true;return;}
        static string Key(Player player)=>player.Address.Length>0?player.Address:"id:"+player.Id;
        var next=room.Players.GroupBy(Key,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>{var player=g.First();return new ActivityMember(player.Id,player.Address,player.Name,player.ConnectionLost);},StringComparer.OrdinalIgnoreCase);
        var host=room.Players.FirstOrDefault(p=>p.Id=="host");string nextHost=host==null?"":Key(host),nextHostName=host?.Name??"";
        if(activityRoom!=room.Id)
        {
            activityRoom=room.Id;activityRoster=next;activityHost=nextHost;activityHostName=nextHostName;activityPaused=reconnecting;
            activityDisconnected.Clear();activityDepartureIds.Clear();activityDepartureOrder.Clear();RememberDepartures(room.Departures);return;
        }
        if(reconnecting){activityPaused=true;return;}
        var departures=room.Departures.Where(departure=>!activityDepartureIds.Contains(departure.Id)).ToArray();
        RememberDepartures(room.Departures);
        bool changedHost=activityHost.Length>0&&nextHost.Length>0&&!StringComparer.OrdinalIgnoreCase.Equals(activityHost,nextHost),joined=false,left=false;
        void Depart(string key,ActivityMember member)
        {
            // Match only newly observed evidence. A previous leave at the same
            // address must not explain a later removal after that player rejoins.
            var departure=departures.LastOrDefault(d=>d.PlayerId==member.Id&&(member.Address.Length==0||d.Address.Equals(member.Address,StringComparison.OrdinalIgnoreCase)))
                ??departures.LastOrDefault(d=>member.Address.Length>0&&d.Address.Equals(member.Address,StringComparison.OrdinalIgnoreCase));
            string reason=departure?.Reason??"unknown",name=string.IsNullOrWhiteSpace(departure?.Name)?member.Name:departure.Name;
            bool announced=activityDisconnected.Remove(key);
            if(announced&&reason is not ("left" or "removed"))return;
            var kind=reason=="disconnected"?ChatTranscript.SystemEventKind.Disconnected:ChatTranscript.SystemEventKind.PlayerLeft;
            string message=reason switch{"left"=>"{0} left the room.","disconnected"=>"{0} disconnected from Fellowship.","removed"=>"{0} was removed from the room.",_=>"{0} is no longer in the room."};
            AppendActivity(kind,message,name);left=true;
        }
        if(!activityPaused)
        {
            // A leave and fresh join can arrive before the next UI refresh.
            // Explicit new evidence distinguishes that from a host ID remap.
            var replacements=activityRoster.Where(p=>next.TryGetValue(p.Key,out var current)&&p.Value.Id!=current.Id&&
                departures.Any(d=>d.PlayerId==p.Value.Id&&d.Address.Equals(p.Value.Address,StringComparison.OrdinalIgnoreCase))).Select(p=>p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var member in activityRoster.Where(p=>!next.ContainsKey(p.Key)||replacements.Contains(p.Key)))Depart(member.Key,member.Value);
            foreach(var member in next)
            {
                if(!activityRoster.TryGetValue(member.Key,out var previous)||replacements.Contains(member.Key))
                {AppendActivity(ChatTranscript.SystemEventKind.PlayerJoined,"{0} joined the room.",member.Value.Name);joined=true;}
                if(member.Value.ConnectionLost&&previous?.ConnectionLost!=true&&activityDisconnected.Add(member.Key))
                    AppendActivity(ChatTranscript.SystemEventKind.Disconnected,"{0} disconnected from Fellowship.",member.Value.Name);
                else if(!member.Value.ConnectionLost&&previous?.ConnectionLost==true&&activityDisconnected.Remove(member.Key))
                    AppendActivity(ChatTranscript.SystemEventKind.Reconnected,"{0} reconnected to the room.",member.Value.Name);
            }
        }
        else if(changedHost&&!next.ContainsKey(activityHost))
        {
            // A confirmed successor snapshot can establish that the old host
            // left. Other membership differences during an outage are unknown.
            if(activityRoster.TryGetValue(activityHost,out var previousHost))Depart(activityHost,previousHost);
        }
        if(changedHost)AppendActivity(ChatTranscript.SystemEventKind.HostChanged,"Room host is now {0}",nextHostName);
        if(app.Settings.MessageSounds){if(joined)MessageAlert.Effect("room-join");else if(left)MessageAlert.Effect("room-leave");}
        activityDisconnected.RemoveWhere(key=>!next.TryGetValue(key,out var member)||!member.ConnectionLost);
        activityRoster=next;activityHost=nextHost;activityHostName=nextHostName;activityPaused=false;
    }
    public void Refresh()
    {
        CommandManager.InvalidateRequerySuggested();
        var room=app.Peer.Current;
        ObserveRoomActivity(room,app.Peer.IsReconnecting);
        if(room==null){players.Items.Clear();L.Bind(playerCount,TextBlock.TextProperty,"PLAYERS · DISCONNECTED");allowLaunch=false;launch.IsEnabled=false;consent.IsEnabled=false;customSetup.IsEnabled=false;inlineRules.IsEnabled=mapField.IsEnabled=false;mapPreview.IsEnabled=false;teamChoice.IsEnabled=false;colorChoice.IsEnabled=false;clearSpot.IsEnabled=false;readySummary.Visibility=Visibility.Visible;L.Bind(readySummary,TextBlock.TextProperty,"Return to Game rooms to reconnect.");L.Bind(readySummary,FrameworkElement.ToolTipProperty,"Return to Game rooms to reconnect.");return;}
        if(app.Peer.IsHost)
        {
            try{app.Settings.RememberHost(room);}
            catch(IOException e){AppendSystem("Could not save last selections: "+e.Message);}
            catch(UnauthorizedAccessException e){AppendSystem("Could not save last selections: "+e.Message);}
        }
        sideStatus.Visibility=app.Settings.LiveGameSync&&RhunSides.Enabled(room)&&HostPreset.CanSynchronize(app.Settings.GamePath,room)?Visibility.Visible:Visibility.Collapsed;
        bool reconnecting=app.Peer.IsReconnecting;
        mapPreview.ShowMap(app.Settings.GamePath,room.MapPath,room.Patch);mapPreview.IsEnabled=!reconnecting&&!launching&&!changingPlacement;
        UpdatePatchNotice();
        mapPreview.ShowSelections(room,app.Peer.LocalId,!reconnecting&&!launching&&!changingPlacement);
        var self=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId);
        syncingPlacement=true;
        try{colorChoice.SelectedItem=PlayerColors.Get(self?.Color??0);teamChoice.SelectedItem=teamChoice.Items.Cast<TeamOption>().FirstOrDefault(t=>t.Value==(self?.Team??0));}
        finally{syncingPlacement=false;}
        teamChoice.IsEnabled=!reconnecting&&!launching&&!changingPlacement;colorChoice.IsEnabled=teamChoice.IsEnabled;clearSpot.IsEnabled=teamChoice.IsEnabled&&self?.Spot>0;
        L.Bind(placementStatus,TextBlock.TextProperty,changingPlacement?"Confirming with host…":room.Spots.Count==0?"Starting spots unavailable for this map.":$"Your spot: {(self?.Spot>0?self.Spot.ToString():"not selected")} · {(self?.Team>0?"Team "+self.Team:"No team")}");
        if(lastRoomName!=room.Title){roomName.Text=room.Title;lastRoomName=room.Title;}
        if(lastPrivacy!=room.FriendsOnly){privacyChoice.SelectedIndex=room.FriendsOnly?1:0;lastPrivacy=room.FriendsOnly;}
        if(title.Text!=room.Title){title.Text=room.Title;title.ToolTip=new ToolTip{Content=UI.Text(room.Title)};Title=room.Title+" · Fellowship";}L.Bind(playerCount,TextBlock.TextProperty,$"PLAYERS   {room.Players.Count} / {room.Capacity}");
        players.Items.Clear();
        foreach(var p in room.Players)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=UI.Auto});
            bool away=IdlePresence.IsAfk(p.IdleSeconds,p.ManualAfk),waiting=p.Reconnecting||reconnecting;
            var identity=new Grid{Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};identity.ColumnDefinitions.Add(new(){Width=UI.Auto});identity.ColumnDefinitions.Add(new());identity.ColumnDefinitions.Add(new(){Width=UI.Auto});
            var avatar=UI.Avatar(p.Name,20);avatar.CornerRadius=new(5);avatar.Margin=new(0,0,7,0);identity.Children.Add(avatar);
            var name=UI.Text(p.Name,12,waiting||away?UI.Muted:"#E6EAF2",true);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(name,1);identity.Children.Add(name);
            var badges=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};if(p.Id=="host"){var hostTag=UI.LocalizedText("HOST",9,UI.Accent,true);hostTag.Margin=new(7,0,0,0);badges.Children.Add(hostTag);}if(away)badges.Children.Add(IdlePresence.Badge(true,showAvailable:false));Grid.SetColumn(badges,2);identity.Children.Add(badges);row.Children.Add(identity);
            string selectionColor=p.Reconnecting||reconnecting?UI.Muted:MapPreview.TeamColor(p.Team);
            var details=new StackPanel{MaxWidth=320};details.Children.Add(UI.Text(p.Name,13,"#E6EAF2",true));
            var role=UI.LocalizedText((p.Id=="host"?"Host":"Guest")+" · "+(p.GameRunning?"IN BFME2":"NOT IN GAME"),11,UI.Muted);role.Margin=new(0,4,0,4);details.Children.Add(role);
            if(waiting)details.Children.Add(UI.LocalizedText("Reconnecting",11,UI.Muted));
            if(away)details.Children.Add(UI.LocalizedText(p.ManualAfk?"AFK · set manually":"AFK · idle",11,IdlePresence.AwayColor));
            if(app.Settings.IsBlocked(p.Address))details.Children.Add(UI.LocalizedText("BLOCKED",11,"#E8A6A6",true));
            var pingLabel=UI.LocalizedText("Ping",11,UI.Muted);pingLabel.Margin=new(0,0,8,0);details.Children.Add(UI.Row(pingLabel,new PingBadge{Milliseconds=app.Latency.Get(p.Address,app.Peer.Address)}));
            details.Children.Add(UI.LocalizedText((p.Spot>0?"Spot "+p.Spot:"No spot")+" · "+(p.Team>0?"Team "+p.Team:"No team"),11,selectionColor));
            var color=PlayerColors.Get(p.Color);var colorLabel=UI.LocalizedText("COLOR",10,UI.Muted);colorLabel.Margin=new(0,0,8,0);details.Children.Add(UI.Row(colorLabel,new Border{Width=10,Height=10,CornerRadius=new(2),Background=UI.Brush(color.Hex),Margin=new(0,0,5,0)},UI.LocalizedText(color.Name,11)));
            details.Children.Add(UI.LocalizedText(p.Ready?"Ready":"Not ready",11,p.Ready?UI.Green:UI.Muted));details.Children.Add(UI.Text(p.Address,10,UI.Muted));
            name.ToolTip=new ToolTip{Content=details};ToolTipService.SetShowDuration(name,30000);
            var ready=UI.LocalizedText(waiting?"Reconnecting":p.Ready?"● Ready":"○ Not ready",10,waiting?UI.Muted:p.Ready?UI.Green:UI.Muted);ready.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(ready,1);row.Children.Add(ready);
            var item=new ListBoxItem{Content=row,Padding=new(10,3,10,3),Tag=p};
            var menu=app.PlayerMenu(this,p.Name,p.Address,p,showRoomActions:false);
            if(app.Peer.IsHost&&p.Id!="host")
            {
                menu.Items.Add(new Separator());
                var promote=new MenuItem{Header="Transfer host"};promote.Click+=async(_,_)=>{try{await app.Peer.Promote(p.Id);}catch(Exception ex){UI.Error(ex.Message);}};menu.Items.Add(promote);
                var kick=new MenuItem{Header="Kick from room"};kick.Click+=async(_,_)=>{try{await app.Peer.Kick(p.Id);}catch(Exception ex){UI.Error(ex.Message);}};menu.Items.Add(kick);
            }
            item.ContextMenu=menu;players.Items.Add(item);
        }
        var signature=$"{room.Map}|{room.Patch}|{room.Rules}";
        if(signature!=lastSettings){map.Text=room.Map;patch.Text=room.Patch;rules.Text=room.Rules;lastSettings=signature;}
        bool host=app.Peer.IsHost;map.IsReadOnly=rules.IsReadOnly=!host;patch.IsReadOnly=true;
        bool defaultPreset=app.Settings.LiveGameSync&&HostPreset.CanSynchronize(app.Settings.GamePath,room);
        setupSummary.Show(room);
        L.Bind(presetStatus,TextBlock.TextProperty,defaultPreset?"●  Automatic host preset":"○  Manual setup in BFME2");
        presetStatus.Foreground=UI.Brush(defaultPreset?UI.Green:"#E5B77A");
        setupBanner.Visibility=host?Visibility.Collapsed:Visibility.Visible;
        customSetup.Visibility=inlineRules.Visibility=mapField.Visibility=host?Visibility.Visible:Visibility.Collapsed;
        customSetup.IsEnabled=!launching;inlineRules.IsEnabled=mapField.IsEnabled=!savingRules&&!launching;
        foreach(var input in new Control[]{mapChoice,resourceChoice,commandChoice,customChoice,ringChoice})
        {input.IsEnabled=customPresetRules;input.Opacity=customPresetRules?1:.55;ToolTipService.SetShowOnDisabled(input,true);input.ToolTip=customPresetRules?null:"Select Custom preset to edit.";}

        if(!savingRules)SyncInlineRules(room);
        int readyCount=room.Players.Count(p=>p.Ready&&!p.Reconnecting);
        string footerStatus=reconnecting?"Reconnecting to room…":room.Players.Any(p=>p.Reconnecting)?"Waiting for disconnected players to reconnect.":launching?"Launching BFME2…":host?$"{readyCount} / {room.Players.Count} players ready":"";
        L.Bind(readySummary,TextBlock.TextProperty,footerStatus);L.Bind(readySummary,FrameworkElement.ToolTipProperty,footerStatus);readySummary.Visibility=footerStatus.Length>0?Visibility.Visible:Visibility.Collapsed;readySummary.Margin=new(0,!host&&footerStatus.Length>0?4:0,0,0);
        UpdateLaunchActions();
        launch.IsEnabled=!launching&&room.AllGuestsReady;
        consent.Visibility=host?Visibility.Collapsed:Visibility.Visible;consent.IsEnabled=!reconnecting;
        consent.IsChecked=room.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId)?.Ready==true;
        if(consent.IsChecked!=true)allowLaunch=false;
    }
    void UpdatePatchNotice()
    {
        if(app.Peer.Current is not {} room){patchWarning.Visibility=Visibility.Collapsed;return;}
        string? warning=PatchCompatibility.Warning(app.Settings.GamePath,room.Patch);
        patchWarning.Visibility=warning==null&&!PatchChanging?Visibility.Collapsed:Visibility.Visible;
        patchNotice.Visibility=warning==null?Visibility.Collapsed:Visibility.Visible;
        if(lastPatchWarning!=warning){lastPatchWarning=warning;L.Bind(patchNotice,TextBlock.TextProperty,warning??"");}
        L.Bind(switchPatch,ContentControl.ContentProperty,PatchChanging?"Switching patch…":"Set to "+room.Patch);
        switchPatch.IsEnabled=!PatchChanging&&!launching;
        L.Bind(placementGuide,TextBlock.TextProperty,app.Settings.LiveGameSync&&HostPreset.CanSynchronize(app.Settings.GamePath,room)?
            "Click an empty spot to claim it. Teams, spots and colors sync both ways in the BFME2 LAN lobby. The host's map and rules sync too. Approve sync permission before launching BFME2.":
            "Set the room's map, rules, teams and spots manually in BFME2. Fellowship keeps the room and chat open.");
    }
    async Task SwitchRoomPatch()
    {
        if(PatchChanging||app.Peer.Current is not {} room)return;
        switchingPatch=true;UpdatePatchNotice();UpdateLaunchActions();
        try{await PatchSwitcher.Switch(app.Settings.GamePath,room.Patch);}
        finally{switchingPatch=false;UpdatePatchNotice();UpdateLaunchActions();}
    }
    public void PromptReady(string roomId)
    {
        if(app.Peer.IsReconnecting||app.Peer.IsHost||app.Peer.Current?.Id!=roomId)return;
        MessageAlert.Effect("ready-request");
        if(readyPrompt!=null){UI.Reveal(readyPrompt);return;}
        var prompt=new Window{Owner=this};readyPrompt=prompt;
        var body=UI.Shell(prompt,"Ready up",460,240);prompt.SizeToContent=SizeToContent.Height;
        var panel=new StackPanel{Margin=new(24)};body.Children.Add(panel);
        panel.Children.Add(UI.LocalizedText("The host is asking everyone to ready up.",17,bold:true));
        var hint=UI.LocalizedText("Marking ready allows the host to launch your game.",12,UI.Muted);hint.Margin=new(0,12,0,20);panel.Children.Add(hint);
        var ready=UI.AsyncButton("I'm ready",async()=>{
            if(app.Peer.Current?.Id!=roomId||app.Peer.IsHost){prompt.Close();return;}
            GameLauncher.Validate(app.Settings.GamePath);await app.Peer.SetReady(true);allowLaunch=true;consent.IsChecked=true;prompt.Close();
        },true);
        panel.Children.Add(UI.Row(UI.Button("Not yet",()=>prompt.Close()),ready));
        void CheckRoom(){if(app.Peer.IsReconnecting||app.Peer.Current?.Id!=roomId||app.Peer.IsHost)prompt.Close();}
        void Changed()=>Dispatcher.InvokeAsync(CheckRoom);
        app.Peer.Changed+=Changed;prompt.Closed+=(_,_)=>{app.Peer.Changed-=Changed;if(ReferenceEquals(readyPrompt,prompt))readyPrompt=null;};
        prompt.Show();
    }
    public void ShowNotice(string text)
    {
        // Peer.Notice also updates the browser's status. The room transcript
        // owns host notices through snapshots, including the transfer initiator.
        if(text.StartsWith("Host is now ",StringComparison.Ordinal)||text.StartsWith("Room handed to ",StringComparison.Ordinal)){ObserveRoomActivity(app.Peer.Current,app.Peer.IsReconnecting);return;}
        var kind=text switch
        {
            "Connection lost. Reconnecting to the room…"=>ChatTranscript.SystemEventKind.Disconnected,
            "Room connection restored."=>ChatTranscript.SystemEventKind.Reconnected,
            _=>ChatTranscript.SystemEventKind.Info
        };
        AppendActivity(kind,text);
    }
    public Task<string> HandleLaunch()=>app.Peer.IsHost||app.Peer.Current?.Players.FirstOrDefault(p=>p.Id==app.Peer.LocalId)?.Ready==true?LaunchLocalCore():Task.FromResult("Launch skipped: player is not ready.");
    bool launching;
    async void LaunchLocal()=>await LaunchLocalCore();
    async Task<string> LaunchLocalCore()
    {
        if(app.Peer.IsReconnecting)return "Reconnecting to room…";
        if(PatchChanging)return "Switching patch…";
        if(launching)return "BFME2 launch is already in progress.";
        launching=true;launchCancellation=new();
        Refresh();UpdateLaunchActions();
        try
        {
            bool useSync=app.Settings.LiveGameSync&&HostPreset.CanSynchronize(app.Settings.GamePath,app.Peer.Current);
            bool preset=app.Peer.IsHost&&useSync;
            AppendSystem(preset?"Launching BFME2. Your rules will sync when the LAN lobby opens.":"Launching BFME2…");
            var current=app.Peer.Current??throw new IOException("Join a Fellowship room first.");
            if(PatchCompatibility.Warning(app.Settings.GamePath,current.Patch) is {} warning)AppendSystem(warning);
            GameLauncher.Validate(app.Settings.GamePath);
            if(GameLauncher.IsRunning()){AppendSystem(GameLauncher.FocusRunning()?"Returned to BFME2.":"BFME2 is starting; waiting for its window.");return "BFME2 is already running.";}
            if(useSync)
            {
                // Launch is an explicit retry; never require a trip through Settings.
                RetryLiveSync();
                using var preparationTimeout=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(syncLifetime.Token,launchCancellation.Token);
                preparationTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                // The game must not start until the permission dialog is finished
                // and the authenticated helper has answered its first request.
                while(syncPrepared==null&&app.Settings.LiveGameSync&&!syncPaused)
                    await Task.Delay(100,preparationTimeout.Token);
                if(syncPaused)throw new IOException("Sync preparation failed. Save Settings to retry or disable live sync to launch manually.");
                if((app.Settings.LiveGameSync)&&syncPrepared!=null&&
                   !await syncPrepared.Task.WaitAsync(preparationTimeout.Token))
                    throw new IOException("Sync preparation failed. Turn live sync off in Settings to launch without it.");
            }
            launchCancellation.Token.ThrowIfCancellationRequested();
            await GameLauncher.Launch(app.Settings.GamePath,current,preset,false,app.Peer.LocalId,false);
            AppendSystem(useSync?"Open Multiplayer → Network and create/join the host lobby. Team and spot sync starts automatically when the matching lobby opens.":"Open Multiplayer → Network and set rules, teams and spots manually for this patch.");
        }
        catch(OperationCanceledException) when(launchCancellation?.IsCancellationRequested==true){return "BFME2 launch aborted.";}
        catch(Exception e){UI.Error(e.Message);return "Could not launch BFME2: "+e.Message;}
        finally{launching=false;launchCancellation?.Dispose();launchCancellation=null;Refresh();UpdateLaunchActions();}
        return "BFME2 launch started.";
    }
}

public sealed class FriendsWindow : Window
{
    readonly TextBlock ownStatus=UI.LocalizedText("",11,UI.Green);
    readonly TextBlock ownNickname=UI.LocalizedText("",16,"#F1F3F8",true);
    readonly Border ownAvatar=UI.Avatar("?",40);
    readonly BrowserWindow app;
    readonly ListBox list=new();
    readonly TextBlock summary=UI.LocalizedText("YOUR FRIENDS",10,UI.Muted,true);
    bool IsOnline(string address)=>app.Presence.Find(address,DateTimeOffset.UtcNow)!=null;
    readonly System.Windows.Threading.DispatcherTimer presenceTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool checkingPresence;
    public FriendsWindow(BrowserWindow app)
    {
        this.app=app;var body=UI.Shell(this,"Friends",390,620);MinWidth=360;MaxWidth=460;MinHeight=440;MaxHeight=800;
        UI.Menus(this,UI.MenuGroup("_Friends",UI.MenuAction("Add friend",AddFriend),UI.MenuAction("Blocked players",app.ShowBlockedPlayers),UI.MenuAction("Send message",Message,()=>list.SelectedItem is ListBoxItem{Tag:Friend}),UI.MenuAction("Refresh presence",async()=>{try{await RefreshPresence();}catch(Exception error){UI.Error(error.Message);}})),UI.MenuGroup("_View",UI.MenuAction("Room browser",()=>UI.Reveal(app)),UI.MenuAction("Close window",Close)));
        UI.Rows(body,UI.Auto,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        var me=UI.Button("",()=>app.CreateProfileEditor(this).Show());me.Style=(Style)Application.Current.FindResource("PlayerRowButton");me.Height=double.NaN;me.Padding=new(10);me.Margin=new(10,10,10,12);me.ToolTip="Edit your profile";me.Content=UI.Row(ownAvatar,UI.Stack(ownNickname,ownStatus));var myPresence=app.CreateAvailabilityButton(true);myPresence.Margin=new(8,0,14,0);myPresence.VerticalAlignment=VerticalAlignment.Center;
        var profile=new Grid();profile.ColumnDefinitions.Add(new());profile.ColumnDefinitions.Add(new(){Width=UI.Auto});profile.Children.Add(me);Grid.SetColumn(myPresence,1);profile.Children.Add(myPresence);UI.At(body,profile,0);
        var bar=new Grid{Margin=new(20,0,12,10)};bar.ColumnDefinitions.Add(new());bar.ColumnDefinitions.Add(new(){Width=UI.Auto});bar.Children.Add(summary);
        var refresh=UI.AsyncButton("↻",RefreshPresence);refresh.ToolTip="Check which friends are online";refresh.Padding=new(10,5,10,5);Grid.SetColumn(refresh,1);bar.Children.Add(refresh);UI.At(body,bar,1);
        list.Margin=new(8,0,8,0);UI.At(body,list,2);
        var actions=UI.Row(UI.Button("＋ Add friend",AddFriend),UI.Require(UI.Button("Message",Message,true),()=>list.SelectedItem is ListBoxItem{Tag:Friend},()=>"Select a friend to open a private chat."));actions.Margin=new(20,12,12,12);UI.At(body,actions,3);
        var footer=UI.LocalizedText("Double-click a friend to chat.\nDirect messages · no offline delivery",10,UI.Muted);footer.Margin=new(20,0,20,18);UI.At(body,footer,4);
        list.MouseDoubleClick+=(_,_)=>Message();
        var menu=MenuPlacement.AtCursor(this);var remove=new MenuItem{Header="Remove friend",Tag="Danger"};remove.Click+=async(_,_)=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f}){await app.ToggleFriend(f.Name,f.Address);Render();}};var message=new MenuItem{Header="Send message"};message.Click+=(_,_)=>Message();menu.Items.Add(message);
        var info=new MenuItem{Header="Friend info"};info.Click+=(_,_)=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f})UI.Reveal(app.CreatePlayerInfo(this,f.Name,f.Address));};menu.Items.Add(info);menu.Items.Add(new Separator());menu.Items.Add(remove);var block=new MenuItem{Header="Block player",Tag="Danger"};block.Click+=async(_,_)=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f})await app.BlockPlayer(f.Name,f.Address,!app.Settings.IsBlocked(f.Address));};menu.Items.Add(block);var invite=UI.MenuAction("Invite to room",async()=>{if(list.SelectedItem is ListBoxItem{Tag:Friend f})try{await app.Peer.Invite(f.Address);UI.InvitationSent(this,f.Name).Show();}catch(Exception error){UI.Error(error.Message);}},()=>app.Peer.Current is {} current&&list.SelectedItem is ListBoxItem{Tag:Friend f}&&!current.Players.Any(p=>p.Address==f.Address)&&!app.Settings.IsBlocked(f.Address));ToolTipService.SetShowOnDisabled(invite,true);invite.ToolTip="Invite to room";invite.ToolTipOpening+=(_,_)=>invite.ToolTip=L.T(list.SelectedItem is ListBoxItem{Tag:Friend selected}&&app.Peer.Current?.Players.Any(p=>p.Address==selected.Address)==true?"This player is already in your room.":"Invite to room");menu.Items.Insert(1,invite);list.ContextMenu=menu;menu.Opened+=(_,_)=>{bool selected=list.SelectedItem is ListBoxItem{Tag:Friend};message.IsEnabled=selected;info.IsEnabled=selected;remove.IsEnabled=selected;block.IsEnabled=selected;if(list.SelectedItem is ListBoxItem{Tag:Friend f}){block.Header=app.Settings.IsBlocked(f.Address)?"Unblock player":"Block player";block.Tag=app.Settings.IsBlocked(f.Address)?null:"Danger";}};
        list.PreviewMouseRightButtonDown+=(_,e)=>{var item=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;if(item?.Tag is Friend)item.IsSelected=true;};
        presenceTimer.Tick+=async(_,_)=>await RefreshPresence();Loaded+=async(_,_)=>await RefreshPresence();Closed+=(_,_)=>presenceTimer.Stop();presenceTimer.Start();Render();
    }
    public void Render()
    {
        var away=IdlePresence.IsAfk(IdlePresence.Seconds(),app.Settings.ManualAfk);L.Bind(ownStatus,TextBlock.TextProperty,"◷ AFK");ownStatus.Visibility=away?Visibility.Visible:Visibility.Collapsed;ownStatus.Foreground=UI.Brush(away?IdlePresence.AwayColor:UI.Green);
        var selected=(list.SelectedItem as ListBoxItem)?.Tag as Friend;
        ownNickname.Text=app.Settings.Name;((TextBlock)ownAvatar.Child).Text=string.IsNullOrWhiteSpace(app.Settings.Name)?"?":app.Settings.Name[..1].ToUpperInvariant();list.Items.Clear();L.Bind(summary,TextBlock.TextProperty,$"YOUR FRIENDS   {app.Settings.Friends.Count}");
        bool? lastGroup=null;
        foreach(var f in app.Settings.Friends.Where(f=>app.Peer.AllowsPeer(f.Address)).OrderByDescending(f=>IsOnline(f.Address)).ThenBy(f=>f.Name))
        {
            bool isOnline=IsOnline(f.Address);
            if(lastGroup!=isOnline){list.Items.Add(new ListBoxItem{IsEnabled=false,Content=UI.LocalizedText(isOnline?"ONLINE":"OFFLINE / NOT REACHABLE",10,UI.Muted,true),Padding=new(12,16,12,8)});lastGroup=isOnline;}
            var peer=app.Presence.Find(f.Address,DateTimeOffset.UtcNow);
            string state=peer==null?"○  Offline / unreachable":peer.Reconnecting?"◌  Reconnecting":peer.IsAfk?"◌  AFK":peer.GameRunning?"●  In BFME2":peer.Room!=null?"●  In a room":"●  Online";
            if(app.Settings.IsBlocked(f.Address))state+=" · Blocked";
            var nickname=peer?.Name??f.Name;
            var labels=UI.Stack(UI.Row(UI.Text(nickname,13,peer?.IsAfk==true?UI.Muted:"#E4E9F2",true),IdlePresence.Badge(peer?.IsAfk==true,showAvailable:false)),app.PlayerRoomActivity(f.Address));
            list.Items.Add(new ListBoxItem{Content=UI.Row(UI.Avatar(nickname),labels),Tag=f,Padding=new(10,8,10,8),ContextMenu=app.PlayerMenu(this,nickname,f.Address)});
        }
        if(selected!=null)list.SelectedItem=list.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Tag is Friend f&&f.Address==selected.Address);
        if(app.Settings.Friends.Count==0)list.Items.Add(new ListBoxItem{IsEnabled=false,Content=UI.LocalizedText("No friends added.",13,UI.Muted),Padding=new(12,30,12,12)});
    }
    async Task RefreshPresence()
    {
        if(checkingPresence||string.IsNullOrEmpty(app.Peer.Address))return;
        checkingPresence=true;
        try
        {
            var away=IdlePresence.IsAfk(IdlePresence.Seconds(),app.Settings.ManualAfk);L.Bind(ownStatus,TextBlock.TextProperty,"◷ AFK");ownStatus.Visibility=away?Visibility.Visible:Visibility.Collapsed;ownStatus.Foreground=UI.Brush(away?IdlePresence.AwayColor:UI.Green);
        var selected=(list.SelectedItem as ListBoxItem)?.Tag as Friend;
            await app.Refresh();Render();
            if(selected!=null)list.SelectedItem=list.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Tag is Friend f&&f.Address==selected.Address);
        }
        finally{checkingPresence=false;}
    }
    void Message(){if(list.SelectedItem is ListBoxItem{Tag:Friend f}&&!app.Settings.IsBlocked(f.Address)&&app.EnsureOnline())app.OpenChat(f.Address,f.Name);}
    void AddFriend()
    {
        var w=new Window{Owner=this,ResizeMode=ResizeMode.NoResize};var body=UI.Shell(w,"Add friend",400,320);w.MinHeight=0;w.SizeToContent=SizeToContent.Height;w.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=new StackPanel{Margin=new(24)};body.Children.Add(p);p.Children.Add(UI.LocalizedText("Add friend",20,"#F1F3F8",true));
        var name=UI.Field(p,"NAME","",40);var address=UI.Field(p,"RADMIN IPv4 ADDRESS","",15);
        var add=UI.Button("Add friend",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)||!System.Net.IPAddress.TryParse(address.Text.Trim(),out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork){UI.Error("Enter a name and valid IPv4 address.");return;}
            app.Settings.Friends.RemoveAll(f=>f.Address==ip.ToString());app.Settings.Friends.Add(new(name.Text.Trim(),ip.ToString()));app.Settings.Save();Render();w.Close();
        },true);UI.Require(add,()=>!string.IsNullOrWhiteSpace(name.Text)&&UI.ValidAddress(address.Text)&&address.Text.Trim()!=app.Peer.Address&&!app.Settings.Friends.Any(f=>f.Address==address.Text.Trim()),()=>string.IsNullOrWhiteSpace(name.Text)?"Enter your friend’s name.":!UI.ValidAddress(address.Text)?"Enter a valid IPv4 address.":address.Text.Trim()==app.Peer.Address?"Enter your friend’s address, not your own.":"This address is already in your friends list.");add.Margin=new(0,22,0,0);p.Children.Add(add);w.Show();
    }
}
public sealed class ChatWindow : Window
{
    readonly string contactName;
    public string ContactName=>contactName;
    int unread;
    readonly ChatTranscript messages=new();
    readonly ChatTranscript scroll;
    readonly TextBox input=new(){MaxLength=2000,ToolTip="Write a message · Enter to send",Margin=new(0,0,8,0)};
    public ChatWindow(BrowserWindow app,string address,string name)
    {
        contactName=name;
        var body=UI.Shell(this,"Private chat",500,600);MinWidth=420;UI.Rows(body,UI.Auto,UI.Star,UI.Auto,UI.Auto);
        Title=name+" · Fellowship";
        Activated+=(_,_)=>{unread=0;Title=contactName+" · Fellowship";};
        var header=UI.Row(UI.Avatar(name,40),UI.Stack(UI.Text(name,19,"#F1F3F8",true),UI.LocalizedText(address+"  ·  Direct connection",11,UI.Muted)));header.Margin=new(20);UI.At(body,header,0);
        scroll=messages;UI.At(body,scroll,1);
        AppendSystem("This conversation stays between these app windows. Messages aren't saved when you close the chat.");
        var composer=new Grid{Margin=new(18,8,18,10)};composer.ColumnDefinitions.Add(new());composer.ColumnDefinitions.Add(new(){Width=UI.Auto});
        composer.Children.Add(input);FocusManager.SetFocusedElement(this,input);
        ContentRendered+=(_,_)=>FocusInput();
        var hint=UI.LocalizedText("ENTER TO SEND   ·   BOTH PLAYERS MUST BE ONLINE",10,UI.Muted);hint.Margin=new(20,0,20,16);UI.At(body,hint,3);
        var outbox=new ChatOutbox(messages,(text,id)=>app.Peer.SendPrivate(address,text,id),
            ()=>{if(app.Settings.MessageSounds)MessageAlert.Effect("sent");});
        Task Send()
        {
            if(string.IsNullOrWhiteSpace(input.Text))return Task.CompletedTask;
            if(app.Settings.IsBlocked(address)){L.Bind(hint,TextBlock.TextProperty,"Unblock this player before messaging.");return Task.CompletedTask;}
            if(!UI.ValidAddress(address)){L.Bind(hint,TextBlock.TextProperty,"Choose a valid recipient VPN address.");return Task.CompletedTask;}
            string text=input.Text;input.Clear();outbox.Send(app.Settings.Name,text);
            L.Bind(hint,TextBlock.TextProperty,"ENTER TO SEND   ·   BOTH PLAYERS MUST BE ONLINE");hint.Foreground=UI.Brush(UI.Muted);hint.ToolTip=null;
            input.Focus();return Task.CompletedTask;
        }
        input.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Send();}};
        var send=UI.Require(UI.AsyncButton("Send  ↑",Send,true),()=>!string.IsNullOrWhiteSpace(input.Text)&&UI.ValidAddress(address)&&!app.Settings.IsBlocked(address),()=>app.Settings.IsBlocked(address)?"Unblock this player before messaging.":"Type a message and use a valid recipient VPN address.");send.Margin=new(0);Grid.SetColumn(send,1);composer.Children.Add(send);UI.At(body,composer,2);
    }
    public void Receive(string name,string text)
    {
        messages.Append(name,text);
        if(!IsActive){unread++;Title=$"({unread}) {contactName} · Fellowship";}
    }
    void AppendSystem(string text)=>messages.AppendSystem(text);
    public void FocusInput()=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>{if(IsActive){input.Focus();Keyboard.Focus(input);}}));
    public void Append(string name,string text)
    {
        messages.Append(name,text,name=="You");
    }
}
public static class GameLauncher
{
    public static void Validate(string path)
    {
        if(!File.Exists(path)||!string.Equals(Path.GetFileName(path),"lotrbfme2.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Select lotrbfme2.exe from your BFME2 installation in Settings first.");
        var data = Path.Combine(Path.GetDirectoryName(path)!, "game.dat");
        if (!File.Exists(data)) throw new InvalidOperationException("Select lotrbfme2.exe from your BFME2 installation folder; game.dat was not found beside it.");
        using var stream=File.OpenRead(data);
        if(stream.ReadByte()!='M'||stream.ReadByte()!='Z')throw new InvalidOperationException("The selected game.dat is not a Windows game executable.");
    }
    public static async Task Launch(string path,Room? room,bool preset=false,bool navigate=false,string localId="",bool applyOnly=false)
    {
        if(GameEnhancements.IsChanging)throw new InvalidOperationException("Wait for game enhancements to finish changing.");
        if(navigate||applyOnly)throw new InvalidOperationException("Cursor navigation is disabled.");
        Validate(path);
        if(!applyOnly&&IsRunning()){FocusRunning();return;}
        if(GameEnhancements.Inspect(path).State is GameEnhancementState.Partial or GameEnhancementState.RecoveryRequired)
            throw new InvalidOperationException("Repair or disable delay fix and borderless mode in Settings before launching BFME2.");
        Directory.CreateDirectory(Settings.Folder);File.WriteAllText(Path.Combine(Settings.Folder,"last-session.json"),System.Text.Json.JsonSerializer.Serialize(room));
        try
        {
            if(preset && room!=null)
            {
                HostPreset.ValidateBinary(path);
                var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"My Battle for Middle-earth(tm) II Files");
                if(Directory.Exists(profile))
                {
                    var pref=Path.Combine(profile,"NetworkPref.ini");
                    var original=File.Exists(pref)?File.ReadAllText(pref):"";
                    File.WriteAllText(Path.Combine(Settings.Folder,"NetworkPref-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".bak"),original);
                    File.WriteAllText(pref,ProfilePreset.MapPreferences(original,room.MapPath),new System.Text.UTF8Encoding(false));
                }
            }
            // Launch as the interactive user, never from the elevated sync worker.
            // Native lobby sync applies rules after the game has initialized.
            Process.Start(LaunchContext.Game(path));
            await Task.CompletedTask;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("BFME2 was not launched because the Windows permission prompt was cancelled. Try launching the game again.", e);
        }
    }
    public static bool IsGameActive()
    {
        foreach(var name in new[]{"game","game.dat"})
        {
            var processes=Process.GetProcessesByName(name);
            try { if(processes.Any(p=>!p.HasExited))return true; }
            finally { foreach(var process in processes)process.Dispose(); }
        }
        return false;
    }
    public static bool FocusRunning()
    {
        foreach(var name in new[]{"game.dat","game","lotrbfme2"})
        foreach(var process in Process.GetProcessesByName(name))
        using(process)
        {
            try
            {
                nint window=process.MainWindowHandle;
                if(window==0)EnumWindows((candidate,_)=>{GetWindowThreadProcessId(candidate,out uint pid);if(pid==process.Id&&IsWindowVisible(candidate)){window=candidate;return false;}return true;},0);
                if(window==0)continue;
                if(IsIconic(window))ShowWindowAsync(window,9);
                return SetForegroundWindow(window);
            }
            catch(InvalidOperationException){}
        }
        return false;
    }
    delegate bool WindowCallback(nint window,nint data);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,nint data);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsIconic(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindowAsync(nint window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetForegroundWindow(nint window);
    public static async Task AbortWithPermission(string gamePath)
    {
        try{await Task.Run(()=>Abort(gamePath));return;}
        catch(System.ComponentModel.Win32Exception error) when(error.NativeErrorCode==5){}
        catch(UnauthorizedAccessException){}
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WorkingDirectory=AppContext.BaseDirectory};
        start.ArgumentList.Add("--abort-game");start.ArgumentList.Add(Path.GetFullPath(gamePath));
        start.ArgumentList.Add(Process.GetCurrentProcess().SessionId.ToString());
        try
        {
            using var helper=Process.Start(start)??throw new IOException("Could not start the BFME2 stop helper.");
            await helper.WaitForExitAsync();
            if(helper.ExitCode!=0)throw new IOException("Windows could not stop BFME2. Close the game manually or try Abort again.");
        }
        catch(System.ComponentModel.Win32Exception error) when(error.NativeErrorCode==1223)
        {throw new OperationCanceledException("Windows permission was cancelled.",error);}
    }
    public static void Abort(string gamePath,int? session=null)
    {
        Validate(gamePath);
        int targetSession=session??Process.GetCurrentProcess().SessionId;
        string folder=Path.GetDirectoryName(Path.GetFullPath(gamePath))!;
        var allowed=new[]{Path.Combine(folder,"lotrbfme2.exe"),Path.Combine(folder,"game.dat")};
        foreach(var name in new[]{"game.dat","game","lotrbfme2"})
        foreach(var process in Process.GetProcessesByName(name))
        using(process)
        {
            try
            {
                if(process.HasExited||process.SessionId!=targetSession)continue;
                string? path=process.MainModule?.FileName;
                if(path!=null&&allowed.Contains(path,StringComparer.OrdinalIgnoreCase)){process.Kill();if(!process.WaitForExit(3000))throw new IOException("BFME2 is still stopping. Try Abort again.");return;}
            }
            catch(InvalidOperationException){} // The game may exit between enumeration and termination.
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


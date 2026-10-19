using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Fellowship;

public sealed partial class BrowserWindow
{
    Grid? dashboardRooms;
    readonly Grid dashboardPreferences=new(){Visibility=Visibility.Collapsed};
    readonly Dictionary<string,FrameworkElement> preferenceTabs=new();
    readonly StackPanel dashboardSocial=new(){Margin=new(26)};
    readonly TextBlock profileName=UI.Text("",14,bold:true);
    readonly TextBlock dashboardNetwork=UI.Text("Connecting…",11,UI.Muted);
    readonly List<Button> availabilityButtons=new();
    internal Button CreateAvailabilityButton()
    {
        var button=UI.Button(Settings.ManualAfk?"Set available":"Set myself AFK",()=>{});
        button.ToolTip="Manual AFK stays on until you choose Set available. Otherwise idle detection is automatic.";
        button.Click+=async(_,_)=>{Settings.ManualAfk=!Settings.ManualAfk;Settings.Save();RenderDashboard();try{await Peer.SetHostGameRunning(GameLauncher.IsGameActive());await Refresh();}catch(Exception error){System.Diagnostics.Debug.WriteLine(error);}};
        availabilityButtons.Add(button);button.Unloaded+=(_,_)=>availabilityButtons.Remove(button);return button;
    }
    Button? currentRoomLink;
    readonly TextBlock currentRoomName=UI.Text("",13,UI.Green,true);
    readonly TextBlock currentRoomPlayers=UI.Text("",11,UI.Green);
    string dashboardPage="Game rooms";
    readonly Dictionary<string,Button> navigation=new();
    void BuildDashboard(Grid roomBody)
    {
        dashboardRooms=roomBody;
        var root=(DockPanel)((Border)Content).Child;
        var menu=(Menu)root.Children[1];root.Children.Remove(menu);
        var menuBar=new Grid{Background=UI.Brush("#151A21")};menuBar.ColumnDefinitions.Add(new());menuBar.ColumnDefinitions.Add(new(){Width=UI.Auto});menuBar.Children.Add(menu);
        playerSearch.Width=250;playerSearch.Height=30;playerSearch.Margin=new(0);
        var searchBox=new Grid{Margin=new(12,5,20,5)};searchBox.Children.Add(playerSearch);
        var placeholder=UI.Text("Search players…",11,UI.Muted);placeholder.Margin=new(11,0,0,0);placeholder.IsHitTestVisible=false;searchBox.Children.Add(placeholder);
        playerSearch.TextChanged+=(_,_)=>placeholder.Visibility=playerSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn(searchBox,1);menuBar.Children.Add(searchBox);DockPanel.SetDock(menuBar,Dock.Top);root.Children.Insert(1,menuBar);
        root.Children.Remove(roomBody);
        var dashboard=new Grid();dashboard.ColumnDefinitions.Add(new(){Width=new GridLength(190)});dashboard.ColumnDefinitions.Add(new());root.Children.Add(dashboard);
        var side=new Grid{Margin=new(12,18,12,16)};UI.Rows(side,UI.Auto,UI.Star,UI.Auto);
        var sidebar=new Border{Background=UI.Brush("#151A21"),BorderBrush=UI.Brush("#2C313B"),BorderThickness=new(0,0,1,0),Child=side};dashboard.Children.Add(sidebar);
        var heading=UI.Text("WORKSPACE",10,UI.Muted,true);heading.Margin=new(12,0,0,12);
        var links=UI.Stack(heading);
        Button Nav(string label,Action action)
        {
            var button=UI.Button(label,action);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Height=38;button.Margin=new(0,0,0,6);button.Padding=new(12,6,12,6);button.Cursor=Cursors.Hand;
            links.Children.Add(button);return button;
        }
        foreach(var page in new[]{"Game rooms","Messages","Friends"}){var name=page;navigation[name]=Nav(name,()=>SelectDashboard(name));}
        currentRoomLink=Nav("▶  Current room",OpenCurrentRoom);currentRoomLink.Margin=new(0,0,0,6);currentRoomLink.Background=UI.Brush("#151A21");currentRoomLink.BorderBrush=UI.Brush("#151A21");currentRoomLink.Tag="CurrentRoom";currentRoomLink.Height=56;
        currentRoomName.TextWrapping=TextWrapping.NoWrap;currentRoomName.TextTrimming=TextTrimming.CharacterEllipsis;currentRoomPlayers.Margin=new(0,3,0,0);currentRoomPlayers.Foreground=UI.Brush(UI.Muted);
        var roomContent=new Grid();roomContent.ColumnDefinitions.Add(new(){Width=new GridLength(26)});roomContent.ColumnDefinitions.Add(new());
        var play=new System.Windows.Shapes.Path{Data=System.Windows.Media.Geometry.Parse("M 1,1 L 11,7 L 1,13 Z"),Fill=UI.Brush(UI.Green),Width=12,Height=14,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};roomContent.Children.Add(play);
        var roomLabels=UI.Stack(currentRoomName,currentRoomPlayers);Grid.SetColumn(roomLabels,1);roomContent.Children.Add(roomLabels);currentRoomLink.HorizontalContentAlignment=HorizontalAlignment.Stretch;currentRoomLink.Content=roomContent;
        var tools=UI.Text("TOOLS",10,UI.Muted,true);tools.Margin=new(12,22,0,12);links.Children.Add(tools);
        navigation["Game preferences"]=Nav("Game preferences",()=>SelectDashboard("Game preferences"));
        navigation["Settings"]=Nav("Settings",OpenSettings);Nav("Help & guide",UI.OpenGuide);
        UI.At(side,links,0);
        var profile=UI.Button("",()=>CreateProfileEditor(this).Show());profile.Style=(Style)Application.Current.FindResource("PlayerRowButton");profile.Height=double.NaN;profile.Margin=new(0,0,0,14);profile.Padding=new(8);profile.ToolTip="Edit your profile";
        profile.Content=UI.Row(UI.Avatar(Settings.Name,32),UI.Stack(profileName,UI.Text("Edit profile",10,UI.Muted)));
        var availability=CreateAvailabilityButton();availability.Margin=new(0,0,0,12);
        var bottom=UI.Stack(profile,availability,new Border{Margin=new(10,0,0,0),Child=dashboardNetwork});UI.At(side,bottom,2);
        Grid.SetColumn(roomBody,1);dashboard.Children.Add(roomBody);
        var socialScroll=UI.Scroll(dashboardSocial);Grid.SetColumn(socialScroll,1);dashboard.Children.Add(socialScroll);
        socialScroll.Visibility=Visibility.Collapsed;dashboardSocial.Tag=socialScroll;
        Grid.SetColumn(dashboardPreferences,1);dashboard.Children.Add(dashboardPreferences);
        SelectDashboard("Game rooms");
    }
    void SelectDashboard(string page)
    {
        dashboardPage=page;if(dashboardRooms==null)return;
        dashboardRooms.Visibility=page=="Game rooms"?Visibility.Visible:Visibility.Collapsed;
        if(dashboardSocial.Tag is FrameworkElement social)social.Visibility=page is "Friends" or "Messages"?Visibility.Visible:Visibility.Collapsed;
        bool preferences=page is "Settings" or "Game preferences";
        dashboardPreferences.Visibility=preferences?Visibility.Visible:Visibility.Collapsed;
        if(preferences)
        {
            if(!preferenceTabs.TryGetValue(page,out var content))
            {
                var frame=new Grid();UI.Rows(frame,UI.Star,UI.Auto);
                var feedbackText=UI.Text("",12,UI.Green,true);
                var feedback=UI.Card(feedbackText,"#1D352D",12);feedback.Margin=new(24,0,24,12);feedback.Visibility=Visibility.Collapsed;
                void Saved(){feedbackText.Text=page=="Settings"?"✓ Settings saved":"✓ Game preferences saved";feedback.Visibility=Visibility.Visible;RenderDashboard();}
                var editor=page=="Settings"?CreateSettingsContent(this,Saved,()=>{preferenceTabs.Remove("Settings");preferenceTabs.Remove("Game preferences");SelectDashboard("Settings");},message=>{feedbackText.Text=message;feedbackText.Foreground=UI.Brush(message.StartsWith("✓")?UI.Green:UI.Accent);feedback.Visibility=Visibility.Visible;}):GamePreferencesWindow.CreateContent(Settings,Saved);
                editor.HorizontalAlignment=HorizontalAlignment.Stretch;
                UI.At(frame,editor,0);UI.At(frame,feedback,1);content=frame;preferenceTabs[page]=content;
            }
            dashboardPreferences.Children.Clear();dashboardPreferences.Children.Add(content);
        }
        foreach(var pair in navigation){bool selected=pair.Key==page;pair.Value.Tag=selected?"SelectedNavigation":null;pair.Value.Background=UI.Brush("#151A21");pair.Value.BorderBrush=UI.Brush("#151A21");if(selected)pair.Value.SetResourceReference(Control.ForegroundProperty,"InteractionText");else pair.Value.Foreground=UI.Brush("#EFF1F5");}
        RenderDashboard();
    }
    void RenderDashboard()
    {
        foreach(var button in availabilityButtons){button.Content=Settings.ManualAfk?"Set available":"Set myself AFK";button.Foreground=UI.Brush(Settings.ManualAfk?IdlePresence.AwayColor:UI.Green);}
        if(dashboardRooms==null)return;
        profileName.Text=Settings.Name;profileName.TextTrimming=TextTrimming.CharacterEllipsis;profileName.MaxWidth=105;
        dashboardNetwork.Text=online?"● VPN discovery active":"○ Not connected";dashboardNetwork.Foreground=UI.Brush(online?UI.Green:UI.Muted);
        if(currentRoomLink!=null){currentRoomLink.Visibility=Peer.Current==null?Visibility.Collapsed:Visibility.Visible;currentRoomName.Text=Peer.Current?.Title??"Current room";currentRoomPlayers.Text=Peer.Current is {} active?$"{active.Players.Count}/{active.Capacity} players":"";currentRoomLink.ToolTip=Peer.Current?.Title;}
        if(dashboardPage is "Game rooms" or "Settings" or "Game preferences")return;
        dashboardSocial.Children.Clear();var title=UI.Text(dashboardPage,25,bold:true);title.Margin=new(0,0,0,22);dashboardSocial.Children.Add(title);
        if(dashboardPage=="Friends")
        {
            var manage=UI.Button("Manage friends",OpenFriends);manage.HorizontalAlignment=HorizontalAlignment.Left;manage.Margin=new(0,0,0,18);dashboardSocial.Children.Add(manage);
        }
        var contacts=dashboardPage=="Friends"?Settings.Friends:Settings.Friends.Concat(chats.Select(c=>new Friend(c.Value.ContactName,c.Key))).DistinctBy(f=>f.Address).ToList();
        if(contacts.Count==0)dashboardSocial.Children.Add(UI.Text(dashboardPage=="Friends"?"No friends added yet.":"Find a player using the search above to start a conversation.",13,UI.Muted));
        foreach(var friend in contacts)
        {
            var presence=Presence.Find(friend.Address,DateTimeOffset.UtcNow);
            string state=presence==null?"Offline":presence.Reconnecting?"Reconnecting":presence.IsAfk?"AFK":presence.GameRunning?"In BFME2":presence.Room!=null?"In a room":"Online";
            var button=UI.Button("",()=>{if(!Settings.IsBlocked(friend.Address))OpenChat(friend.Address,friend.Name);});button.Style=(Style)Application.Current.FindResource("PlayerRowButton");button.Height=double.NaN;button.Padding=new(12);button.Margin=new(0,0,0,8);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var name=UI.Text(friend.Name,13,presence?.IsAfk==true?UI.Muted:"#EFF1F5",true);var details=UI.Text((presence?.IsAfk==true||state=="Online"?friend.Address:state)+(Settings.IsBlocked(friend.Address)?" · Blocked":""),11,presence==null?UI.Muted:presence.IsAfk?IdlePresence.AwayColor:UI.Green);details.Margin=new(0,4,0,0);
            button.Content=UI.Row(UI.Avatar(friend.Name,34),UI.Stack(UI.Row(name,presence==null?UI.Text("",10):IdlePresence.Badge(presence.IsAfk)),details));button.ContextMenu=PlayerMenu(this,friend.Name,friend.Address);dashboardSocial.Children.Add(button);
        }
    }
}

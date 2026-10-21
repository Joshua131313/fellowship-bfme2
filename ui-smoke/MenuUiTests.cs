using Fellowship;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class MenuUiTests
{
    public static void Run()
    {
        var placements=MenuPlacement.Beside(new Point(100,50),new Size(220,300));
        if(placements.Length!=2||placements[0].Point.X<=100||placements[1].Point.X+220>=100||placements[0].PrimaryAxis!=PopupPrimaryAxis.Vertical)throw new Exception("Player menus do not prefer the right of the cursor with a left fallback");
        var menu=MenuPlacement.AtCursor(new Window());
        if(menu.Placement!=PlacementMode.Custom||menu.PlacementTarget is not Window)throw new Exception("Player menu cursor positioning was not configured");
        L.Apply("ar");if(menu.FlowDirection!=FlowDirection.RightToLeft)throw new Exception("Player menu text does not follow Arabic direction");L.Apply("en");
        var menuItem=new MenuItem{Header="Server"};menuItem.ApplyTemplate();
        if(menuItem.Cursor!=Cursors.Hand)throw new Exception("Menu options do not use the pointer cursor");

        var settings=Settings.InMemory();settings.Name="Frodo";
        if(settings.NewHostedRoom().Title!="Frodo's room")throw new Exception("First room title default changed");
        settings.LastRoomTitle="1v1 pro";
        var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        if(restored.NewHostedRoom().Title!="1v1 pro")throw new Exception("Last created room title did not survive a restart");
        restored.LastHostedSetup=new("Rhûn","maps\\rhun.map",6,4000,200,false,false,1,2);
        if(restored.NewHostedRoom().Title!="1v1 pro")throw new Exception("Restoring match settings overwrote the room title");

        var picker=new RoomPresetPicker(settings){Width=560,SelectedIndex=1};
        picker.Measure(new Size(560,34));picker.Arrange(new Rect(0,0,560,34));picker.UpdateLayout();picker.ApplyTemplate();
        var content=(ContentPresenter)picker.Template.FindName("SelectedContent",picker);
        if(content.HorizontalAlignment!=HorizontalAlignment.Left||content.TranslatePoint(new Point(),picker).X>14)throw new Exception("Preset caption is centered");
        picker.Reload();picker.UpdateLayout();
        if(picker.SelectedIndex!=1||content.HorizontalAlignment!=HorizontalAlignment.Left)throw new Exception("Preset refresh changed caption alignment or selection");
        var image=new RenderTargetBitmap(560,40,96,96,PixelFormats.Pbgra32);image.Render(picker);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create("dist/preview-preset-alignment.png");encoder.Save(file);
        Console.WriteLine("PASS right-of-cursor player menus, pointer cursors, preset alignment after refresh, and saved room title with/without previous match settings");

        const string address="10.211.0.2";
        var inviteSettings=Settings.InMemory();inviteSettings.Name="Frodo";inviteSettings.Friends=[new("Aragorn",address)];inviteSettings.MessageSounds=false;
        var browser=new BrowserWindow(inviteSettings);typeof(BrowserWindow).GetField("closingRoom",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(browser,true);browser.Peer.Host(new(){Title="Invite eligibility"},[]);
        var friends=new FriendsWindow(browser);
        try
        {
            var shared=browser.PlayerMenu(browser,"Aragorn",address).Items.OfType<MenuItem>().Single(i=>i.Header as string=="Invite to room");
            var list=(ListBox)typeof(FriendsWindow).GetField("list",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(friends)!;
            list.SelectedItem=list.Items.OfType<ListBoxItem>().Single(i=>i.Tag is Friend);
            var selected=list.ContextMenu!.Items.OfType<MenuItem>().Single(i=>i.Header as string=="Invite to room");
            if(shared.Command?.CanExecute(null)!=true||selected.Command?.CanExecute(null)!=true)throw new Exception("An eligible friend outside the room cannot be invited");
            var member=new Player{Id="guest",Name="Aragorn",Address=address};browser.Peer.Current!.Players.Add(member);
            var regular=browser.PlayerMenu(browser,member.Name,address,member);var friendMenu=((ListBoxItem)list.SelectedItem).ContextMenu!;
            foreach(var ordinary in new[]{regular,friendMenu})
            {
                ordinary.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                if(ordinary.Items.OfType<MenuItem>().Count(i=>i.Header as string is "Show game room info" or "Open room")!=2)throw new Exception("Ordinary player/friend menus lost room navigation actions");
            }
            var roomWindow=new RoomWindow(browser){AllowClose=true};
            try
            {
                var roster=(ListBox)typeof(RoomWindow).GetField("players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(roomWindow)!;
                var rosterMenu=roster.Items.OfType<ListBoxItem>().Single(i=>i.Tag is Player p&&p.Address==address).ContextMenu!;rosterMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                var labels=rosterMenu.Items.OfType<MenuItem>().Select(i=>i.Header as string).ToArray();
                if(labels.Any(label=>label is "Show game room info" or "Open room" or "Join game room")||!labels.Contains("Player info")||!labels.Contains("Send message")||!labels.Contains("Transfer host")||!labels.Contains("Kick from room"))throw new Exception("Room roster menu did not omit redundant room actions while retaining player/host controls");
            }
            finally{roomWindow.Close();}
            if(shared.Command!.CanExecute(null)||selected.Command!.CanExecute(null))throw new Exception("Existing room members can still be invited from a player or friends menu");
            member.Reconnecting=true;
            if(shared.Command.CanExecute(null)||selected.Command.CanExecute(null))throw new Exception("Reserved reconnecting members can still be invited");
            if(!ToolTipService.GetShowOnDisabled(shared)||!ToolTipService.GetShowOnDisabled(selected)||shared.ToolTip==null||selected.ToolTip==null)throw new Exception("Disabled invitation menus lack a visible explanation");
            browser.Peer.Current.Players.Remove(member);
            if(!shared.Command.CanExecute(null)||!selected.Command.CanExecute(null))throw new Exception("Invitation eligibility did not update after the player left");
            list.SelectedItem=null;if(selected.Command.CanExecute(null))throw new Exception("Friends invite command ignored its current selection");
            browser.Peer.Leave();if(shared.Command.CanExecute(null))throw new Exception("Invitation remained available after leaving the room");
        }
        finally{friends.Close();browser.Peer.Dispose();}
        Console.WriteLine("PASS live invite eligibility excludes existing and reserved room members in shared player and friends menus");
        Console.WriteLine("PASS room roster omits redundant room navigation while other player/friend menus retain it");
    }
}

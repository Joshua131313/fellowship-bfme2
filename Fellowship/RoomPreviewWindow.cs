using System.Windows;
using System.Windows.Controls;
namespace Fellowship;

public sealed class RoomPreviewWindow : Window
{
    public RoomPreviewWindow(Window? owner,Room room,string gamePath)
    {
        if(owner?.IsLoaded==true)Owner=owner;
        var body=UI.Shell(this,"Room preview",680,590);
        string? lastSnapshot=null;
        void Render(Room current,bool unavailable=false)
        {
        var snapshot=System.Text.Json.JsonSerializer.Serialize(current)+unavailable;
        if(snapshot==lastSnapshot)return;lastSnapshot=snapshot;
        room=current;body.Children.Clear();
        var content=new StackPanel{Margin=new(24)};
        content.Children.Add(UI.Text(room.Title,24,bold:true));
        var subtitle=UI.Text($"Hosted by {room.Host}  ·  {room.Players.Count} / {room.Capacity} players",12,UI.Muted);subtitle.Margin=new(0,6,0,16);content.Children.Add(subtitle);
        content.Children.Add(UI.Row(UI.Badge(room.FriendsOnly?"FRIENDS ONLY":"PUBLIC ON VPN"),UI.Text("   BFME2 1.00",11,UI.Muted)));
        var columns=new Grid{Margin=new(0,18,0,18)};columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new(){Width=new GridLength(20)});columns.ColumnDefinitions.Add(new());
        var map=new MapPreview{PreviewHeight=190,Margin=new(0,8,0,0)};map.ShowMap(gamePath,room.MapPath);map.ShowSelections(room,"",false);
        columns.Children.Add(UI.Card(UI.Stack(UI.Text(room.Map,16,bold:true),map)));
        var rules=UI.Stack(UI.Text("MATCH RULES",10,UI.Muted,true));
        var details=new MatchSummary{Margin=new(0,10,0,0)};details.Show(room,false);rules.Children.Add(details);
        var card=UI.Card(rules);Grid.SetColumn(card,2);columns.Children.Add(card);content.Children.Add(columns);
        content.Children.Add(UI.Text("PLAYERS",10,UI.Muted,true));
        var roster=new StackPanel{Margin=new(0,8,0,0)};
        foreach(var player in room.Players)
        {
            var row=new DockPanel{Margin=new(0,0,0,6),Background=System.Windows.Media.Brushes.Transparent};
            if(!unavailable&&owner is BrowserWindow browser)row.ContextMenu=browser.PlayerMenu(this,player.Name,player.Address,player);
            var state=UI.Text(player.Ready?"Ready":"Not ready",11,player.Ready?UI.Green:UI.Muted);DockPanel.SetDock(state,Dock.Right);row.Children.Add(state);
            row.Children.Add(UI.Avatar(player.Name,28));row.Children.Add(UI.Text(player.Name+(player.Id=="host"?"  ·  Host":"")+(IdlePresence.IsAfk(player.IdleSeconds,player.ManualAfk)?" · AFK":""),13,IdlePresence.IsAfk(player.IdleSeconds,player.ManualAfk)?UI.Muted:"#EFF1F5"));roster.Children.Add(row);
        }
        content.Children.Add(roster);
        if(owner is BrowserWindow host)
        {
            var join=UI.AsyncButton("Join room",()=>host.JoinPreview(room.Id),true,()=>!unavailable&&room.Players.Count<room.Capacity);
            UI.Require(join,()=>!unavailable&&(host.Peer.Current?.Id==room.Id||room.Players.Count<room.Capacity),()=>unavailable?"This room is no longer available.":"This room is full.");
            join.HorizontalAlignment=HorizontalAlignment.Right;join.Margin=new(0,18,0,0);content.Children.Add(join);
        }
        var foot=UI.Text(unavailable?"Room is no longer available · showing last known details":"Updates automatically",11,UI.Muted);foot.Margin=new(0,16,0,0);content.Children.Add(foot);
        body.Children.Add(UI.Scroll(content));
        }
        Render(room);
        if(owner is BrowserWindow app)
        {
            var roomId=room.Id;
            void Refresh(){var latest=app.PreviewRoom(roomId);Render(latest??room,latest==null);}
            app.RoomPreviewsChanged+=Refresh;
            Closed+=(_,_)=>app.RoomPreviewsChanged-=Refresh;
        }
    }
}


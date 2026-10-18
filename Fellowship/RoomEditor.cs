using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Fellowship;
public sealed class RoomEditor : Window
{
    public Room? Result { get; private set; }
    public RoomEditor(Window owner,Settings settings,Room initial)
    {
        Owner=owner;var body=UI.Shell(this,"Room settings",560,740);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};body.Children.Add(UI.Scroll(panel));
        panel.Children.Add(UI.Text("Set up your battle",24,"#F1F3F8",true));
        var name=UI.Field(panel,"ROOM NAME",initial.Title,80);
        ComboBox Choice(string title,object[] values,object selected)
        {
            var label=UI.Text(title,10,UI.Muted,true);label.Margin=new(0,16,0,7);panel.Children.Add(label);
            var combo=new ComboBox{ItemsSource=values,SelectedItem=selected};panel.Children.Add(combo);return combo;
        }
        var maps=GameInstallation.Maps(settings.GamePath);
        var map=Choice("MAP · FROM YOUR BFME2 INSTALLATION",maps.Cast<object>().ToArray(),maps.FirstOrDefault(m=>m.Path==initial.MapPath)??maps[0]);
        var preview=new MapPreview();panel.Children.Add(preview);
        void UpdatePreview(){if(map.SelectedItem is GameMap selected)preview.ShowMap(settings.GamePath,selected.Path);}
        map.SelectionChanged+=(_,_)=>UpdatePreview();UpdatePreview();
        var capacity=Choice("PLAYER LIMIT",Enumerable.Range(2,7).Cast<object>().ToArray(),initial.Capacity);
        var resources=Choice("STARTING RESOURCES",MatchRules.ResourceValues.Cast<object>().ToArray(),initial.Match.Resources);
        var factors=MatchRules.CommandValues.Select(n=>new Factor(n)).ToArray();
        var command=Choice("COMMAND POINTS",factors.Cast<object>().ToArray(),factors.First(f=>f.Value==initial.Match.CommandPoints));
        var custom=new CheckBox{Content="Allow Custom Heroes",IsChecked=initial.Match.CustomHeroes,Margin=new(0,18,0,12)};
        var ring=new CheckBox{Content="Allow Ring Heroes",IsChecked=initial.Match.RingHeroes,Margin=new(0,0,0,18)};
        panel.Children.Add(custom);panel.Children.Add(ring);
        var privacy=Choice("VISIBILITY",new object[]{"Public · everyone on this VPN","Private · host's friends only"},initial.FriendsOnly?"Private · host's friends only":"Public · everyone on this VPN");
        var hint=UI.Text("Private rooms are visible and joinable only from VPN addresses saved in the host's friends list.",11,UI.Muted);hint.Margin=new(0,10,0,16);panel.Children.Add(hint);
        panel.Children.Add(UI.Button("Save room",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)){UI.Error("Enter a room name.");return;}
            Result=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(initial))!;
            Result.Title=name.Text.Trim();Result.Capacity=(int)capacity.SelectedItem;Result.FriendsOnly=privacy.SelectedIndex==1;
            var selected=(GameMap)map.SelectedItem;Result.Map=selected.Name;Result.MapPath=selected.Path;
            Result.Match=new(){Resources=(int)resources.SelectedItem,CommandPoints=((Factor)command.SelectedItem).Value,CustomHeroes=custom.IsChecked==true,RingHeroes=ring.IsChecked==true};
            Result.Rules=Result.Match.Summary;DialogResult=true;
        },true));
    }
    sealed record Factor(int Value){public override string ToString()=>$"{Value/100.0:0.##}×";}
}

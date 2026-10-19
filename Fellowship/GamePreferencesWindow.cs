using System.Windows;
using System.Windows.Controls;
namespace Fellowship;

public sealed class GamePreferencesWindow : Window
{
    public GamePreferencesWindow(Window owner,Settings settings)
    {
        if(owner.IsLoaded)Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=UI.Shell(this,"Game preferences",480,660);MinWidth=420;MinHeight=540;
        var panel=new StackPanel{Margin=new(24)};body.Children.Add(UI.Scroll(panel));
        panel.Children.Add(UI.Text("Game preferences",21,bold:true));
        var hint=UI.Text("Applied when joining, if your choices are available.",11,UI.Muted);hint.Margin=new(0,8,0,18);panel.Children.Add(hint);
        panel.Children.Add(UI.Text("PREFERRED COLOR",10,UI.Muted,true));
        var color=new ComboBox{ItemsSource=PlayerColors.All,SelectedItem=PlayerColors.Get(settings.PreferredColor),Margin=new(0,6,0,16)};panel.Children.Add(color);
        panel.Children.Add(UI.Text("MAP",10,UI.Muted,true));
        var maps=GameInstallation.Maps(settings.GamePath);
        var map=new SearchableMapBox{ItemsSource=maps,Margin=new(0,6,0,0)};panel.Children.Add(map);
        var preview=new MapPreview{PreviewHeight=240};panel.Children.Add(preview);
        var selection=UI.Text("",12);panel.Children.Add(selection);
        var spots=new Dictionary<string,int>(settings.PreferredMapSpots,StringComparer.OrdinalIgnoreCase);
        Room? current=null;
        void Render()
        {
            if(current==null)return;
            int saved=spots.GetValueOrDefault(current.MapPath);
            current.Players=[new(){Id="preference",Name="Your preference",Spot=saved,Color=(color.SelectedItem as PlayerColor)?.Value??0}];
            preview.ShowSelections(current,"preference",true);
            selection.Text=saved>0?$"Preferred spot: {saved}":"No preferred spot — click a starting location.";
        }
        map.SelectionChanged+=(_,_)=>
        {
            if(map.SelectedItem is not GameMap selected)return;
            current=new(){Map=selected.Name,MapPath=selected.Path,Spots=GameMapData.Spots(settings.GamePath,selected.Path)};
            preview.ShowMap(settings.GamePath,selected.Path);Render();
        };
        color.SelectionChanged+=(_,_)=>Render();
        preview.SpotSelected+=spot=>{if(current==null)return;if(spot==0)spots.Remove(current.MapPath);else spots[current.MapPath]=spot;Render();};
        var clear=UI.Button("Clear this map’s preference",()=>{if(current!=null)spots.Remove(current.MapPath);Render();});clear.Margin=new(0,12,0,12);panel.Children.Add(clear);
        var save=UI.Button("Save preferences",()=>{settings.PreferredColor=(color.SelectedItem as PlayerColor)?.Value??0;settings.PreferredMapSpots=spots;settings.Save();Close();},true);save.Margin=new(0,0,0,0);panel.Children.Add(save);
        map.SelectedItem=maps.FirstOrDefault(m=>m.Name=="Rhûn")??maps.FirstOrDefault();
    }
}

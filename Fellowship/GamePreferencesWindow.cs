using System.Windows;
using System.Windows.Controls;
namespace Fellowship;

public sealed class GamePreferencesWindow : Window
{
    public GamePreferencesWindow(Window owner,Settings settings)
    {
        if(owner.IsLoaded)Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=UI.Shell(this,"Game preferences",480,660);MinWidth=420;MinHeight=540;
        body.Children.Add(CreateContent(settings,()=>{}));
    }
    public static Grid CreateContent(Settings settings,Action saved)
    {
        var body=new Grid();
        var panel=new StackPanel{Margin=new(24),HorizontalAlignment=HorizontalAlignment.Stretch};body.Children.Add(UI.Scroll(panel));
        panel.Children.Add(UI.LocalizedText("Game preferences",21,bold:true));
        var hint=UI.LocalizedText("Applied when joining, if your choices are available.",11,UI.Muted);hint.Margin=new(0,8,0,18);panel.Children.Add(hint);
        panel.Children.Add(UI.LocalizedText("PREFERRED COLOR",10,UI.Muted,true));
        var color=new ComboBox{ItemsSource=PlayerColors.All,SelectedItem=PlayerColors.Get(settings.PreferredColor),Margin=new(0,6,0,16)};L.Choices(color);panel.Children.Add(color);
        panel.Children.Add(UI.LocalizedText("MAP",10,UI.Muted,true));
        var maps=GameInstallation.Maps(settings.GamePath);
        var map=new SearchableMapBox{ItemsSource=maps,Margin=new(0,6,0,0)};panel.Children.Add(map);
        var preview=new MapPreview{PreviewHeight=240};panel.Children.Add(preview);
        var spots=new Dictionary<string,int>(settings.PreferredMapSpots,StringComparer.OrdinalIgnoreCase);
        Room? current=null;
        void Render()
        {
            if(current==null)return;
            int saved=spots.GetValueOrDefault(current.MapPath);
            current.Players=[new(){Id="preference",Name=settings.Name,Spot=saved,Color=(color.SelectedItem as PlayerColor)?.Value??0}];
            preview.ShowSelections(current,"preference",true);
            System.Windows.Automation.AutomationProperties.SetHelpText(preview,saved>0?$"Preferred spot: {saved}":"Click a starting location to choose your preference.");
        }
        map.SelectionChanged+=(_,_)=>
        {
            if(map.SelectedItem is not GameMap selected)return;
            current=new(){Map=selected.Name,MapPath=selected.Path,Spots=GameMapData.Spots(settings.GamePath,selected.Path)};
            preview.ShowMap(settings.GamePath,selected.Path);Render();
        };
        void Save(){settings.PreferredColor=(color.SelectedItem as PlayerColor)?.Value??0;settings.PreferredMapSpots=new(spots,StringComparer.OrdinalIgnoreCase);settings.Save();saved();}
        color.SelectionChanged+=(_,_)=>{Render();Save();};
        preview.SpotSelected+=spot=>{if(current==null)return;if(spot==0)spots.Remove(current.MapPath);else spots[current.MapPath]=spot;Render();Save();};
        var clear=UI.Button("Clear this map’s preference",()=>{if(current!=null)spots.Remove(current.MapPath);Render();Save();});clear.Margin=new(0,12,0,12);panel.Children.Add(clear);
        var items=panel.Children.Cast<UIElement>().ToArray();panel.Children.Clear();
        panel.Children.Add(items[0]);panel.Children.Add(items[1]);
        var layout=new Grid();layout.ColumnDefinitions.Add(new());layout.ColumnDefinitions.Add(new());
        layout.RowDefinitions.Add(new(){Height=UI.Auto});layout.RowDefinitions.Add(new(){Height=UI.Auto});
        var controls=new StackPanel();var mapPanel=new StackPanel();
        foreach(var item in items.Skip(2).Take(4))controls.Children.Add(item);
        mapPanel.Children.Add(preview);
        controls.Children.Add(clear);controls.Children.Add(UI.LocalizedText("Changes save automatically",11,UI.Muted));
        layout.Children.Add(controls);layout.Children.Add(mapPanel);panel.Children.Add(layout);
        void ArrangePreferences(double width){bool wide=width>=760;layout.ColumnDefinitions[1].Width=wide?UI.Star:new GridLength(0);Grid.SetColumn(mapPanel,wide?1:0);Grid.SetRow(mapPanel,wide?0:1);controls.Margin=new(0,0,wide?28:0,20);preview.PreviewHeight=wide?340:240;}
        body.SizeChanged+=(_,e)=>ArrangePreferences(e.NewSize.Width);ArrangePreferences(1000);
        map.SelectedItem=maps.FirstOrDefault(m=>m.Name=="Rhûn")??maps.FirstOrDefault();
        return body;
    }
}

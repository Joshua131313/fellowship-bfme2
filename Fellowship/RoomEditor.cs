using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Fellowship;
public sealed class RoomEditor : Window
{
    public Room? Result { get; private set; }
    public RoomEditor(Window owner,Settings settings,Room initial,Func<Room,Task>? create=null)
    {
        Owner=owner;var body=UI.Shell(this,"Create room",620,478);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var work=SystemParameters.WorkArea;
        Width=Math.Min(620,work.Width);Height=Math.Min(478,work.Height);
        MinWidth=Width;MinHeight=Height;
        // Keep the entire form visible on smaller/high-DPI desktops as well.
        var panel=new Grid{Width=576,Height=384};
        body.Children.Add(new Viewbox{Margin=new(20),Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Child=panel});
        foreach(double height in new[]{48d,232,38,16,50})panel.RowDefinitions.Add(new(){Height=new(height)});
        var heading=UI.Text("Create a room",21,"#F1F3F8",true);
        panel.Children.Add(heading);
        var setup=new Grid();setup.ColumnDefinitions.Add(new(){Width=new(368)});setup.ColumnDefinitions.Add(new(){Width=new(20)});setup.ColumnDefinitions.Add(new(){Width=new(188)});Grid.SetRow(setup,1);panel.Children.Add(setup);
        var form=new Grid();for(int i=0;i<4;i++)form.RowDefinitions.Add(new(){Height=new(58)});setup.Children.Add(form);
        StackPanel Field(Panel parent,string title)
        {
            var field=new StackPanel();parent.Children.Add(field);
            var label=UI.Text(title,9,UI.Muted,true);label.Margin=new(0,0,0,5);field.Children.Add(label);return field;
        }
        Grid Pair(int row)
        {
            var pair=new Grid();pair.ColumnDefinitions.Add(new());pair.ColumnDefinitions.Add(new(){Width=new(12)});pair.ColumnDefinitions.Add(new());Grid.SetRow(pair,row);form.Children.Add(pair);return pair;
        }
        var nameField=Field(form,"ROOM NAME");Grid.SetRow(nameField,0);
        var name=new TextBox{Text=initial.Title,MaxLength=80,Height=34,FontSize=12,Padding=new(9,4,9,4),AcceptsReturn=false,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden};nameField.Children.Add(name);
        System.Windows.Automation.AutomationProperties.SetName(name,"Room name");
        ComboBox Choice(Panel parent,string title,object[] values,object selected,int column=0,bool searchable=false)
        {
            var field=Field(parent,title);Grid.SetColumn(field,column);
            ComboBox combo=searchable?new SearchableMapBox():new ComboBox();
            combo.ItemsSource=values;combo.SelectedItem=selected;combo.Height=34;combo.FontSize=12;combo.Padding=new(9,5,28,5);
            combo.ItemContainerStyle=new Style(typeof(ComboBoxItem),(Style)Application.Current.FindResource(typeof(ComboBoxItem)));
            combo.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));
            combo.ItemContainerStyle.Setters.Add(new Setter(Control.FontSizeProperty,12d));
            System.Windows.Automation.AutomationProperties.SetName(combo,title);field.Children.Add(combo);return combo;
        }
        var mapRow=new Grid();Grid.SetRow(mapRow,1);form.Children.Add(mapRow);
        var maps=GameInstallation.Maps(settings.GamePath);
        var map=Choice(mapRow,"MAP",maps.Cast<object>().ToArray(),maps.FirstOrDefault(m=>m.Path==initial.MapPath)??maps[0],searchable:true);
        var limits=Pair(2);
        var capacityField=Field(limits,"ROOM LIMIT (2–64)");var capacity=new TextBox{Text=initial.Capacity.ToString(),MaxLength=2,ToolTip="Fellowship room capacity. BFME2 matches still support at most 8 players."};capacityField.Children.Add(capacity);
        var resources=Choice(limits,"STARTING RESOURCES",MatchRules.ResourceValues.Cast<object>().ToArray(),initial.Match.Resources,2);
        var options=Pair(3);
        var factors=MatchRules.CommandValues.Select(n=>new Factor(n)).ToArray();
        var command=Choice(options,"COMMAND POINTS",factors.Cast<object>().ToArray(),factors.First(f=>f.Value==initial.Match.CommandPoints));
        var privacy=Choice(options,"VISIBILITY",new object[]{"Public on VPN","Friends only"},initial.FriendsOnly?"Friends only":"Public on VPN",2);
        var preview=new MapPreview{PreviewHeight=166,Padding=new(6),Margin=new(0,5,0,8)};
        var mapName=UI.Text("",12,"#EDF1F8",true);
        var previewPanel=UI.Stack(UI.Text("MAP PREVIEW",9,UI.Muted,true),preview,mapName);
        Grid.SetColumn(previewPanel,2);setup.Children.Add(previewPanel);
        void UpdatePreview(){if(map.SelectedItem is GameMap selected){preview.ShowMap(settings.GamePath,selected.Path);mapName.Text=selected.Name;map.ToolTip=selected.Name;}}
        map.SelectionChanged+=(_,_)=>UpdatePreview();UpdatePreview();
        var custom=new CheckBox{Content="Custom Heroes",IsChecked=initial.Match.CustomHeroes,FontSize=12,Margin=new(0,0,26,0)};
        var ring=new CheckBox{Content="Ring Heroes",IsChecked=initial.Match.RingHeroes,FontSize=12};
        var heroes=UI.Row(custom,ring);Grid.SetRow(heroes,2);panel.Children.Add(heroes);
        void UpdateVisibility()=>privacy.ToolTip=privacy.SelectedIndex==1?"Only your saved friends can see and join.":"Everyone on your VPN can see and join.";
        privacy.SelectionChanged+=(_,_)=>UpdateVisibility();UpdateVisibility();
        var footer=new Grid{Margin=new(0,8,0,0)};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=new(100)});footer.ColumnDefinitions.Add(new(){Width=new(10)});footer.ColumnDefinitions.Add(new(){Width=new(146)});Grid.SetRow(footer,4);panel.Children.Add(footer);
        var cancel=UI.Button("Cancel",()=>DialogResult=false);cancel.IsCancel=true;cancel.Height=34;cancel.Padding=new(12,5,12,5);cancel.Margin=new(0);Grid.SetColumn(cancel,1);footer.Children.Add(cancel);
        bool creating=false;
        Closing+=(_,e)=>{if(creating)e.Cancel=true;};
        var progress=new ProgressBar{Height=4,Width=160,IsIndeterminate=true,Foreground=UI.Brush("#6789F5"),Background=UI.Brush("#263042"),Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left};footer.Children.Add(progress);
        var save=UI.AsyncButton("Create room",async ()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)){name.Focus();UI.Error("Enter a room name.");return;}
            Result=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(initial))!;
            Result.Title=name.Text.Trim();Result.Capacity=int.Parse(capacity.Text);Result.FriendsOnly=privacy.SelectedIndex==1;
            var selected=(GameMap)map.SelectedItem;Result.Map=selected.Name;Result.MapPath=selected.Path;
            Result.Match=new(){Resources=(int)resources.SelectedItem,CommandPoints=((Factor)command.SelectedItem).Value,CustomHeroes=custom.IsChecked==true,RingHeroes=ring.IsChecked==true};
            Result.Rules=Result.Match.Summary;
            creating=true;setup.IsEnabled=false;heroes.IsEnabled=false;cancel.IsEnabled=false;
            heading.Text="Creating room…";progress.Visibility=Visibility.Visible;
            try
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                if(create!=null)await create(Result);
                creating=false;DialogResult=true;
            }
            finally
            {
                creating=false;setup.IsEnabled=true;heroes.IsEnabled=true;cancel.IsEnabled=true;
                heading.Text="Create a room";progress.Visibility=Visibility.Collapsed;
            }
        },true);UI.Require(save,()=>!creating&&!string.IsNullOrWhiteSpace(name.Text)&&map.SelectedItem is GameMap&&int.TryParse(capacity.Text,out var limit)&&limit is >=2 and <=64&&resources.SelectedItem!=null&&command.SelectedItem!=null&&privacy.SelectedIndex>=0,()=>string.IsNullOrWhiteSpace(name.Text)?"Enter a room name.":"Select a map, player limit, resources, command points and privacy.");save.IsDefault=true;save.Height=34;save.Padding=new(12,5,12,5);save.Margin=new(0);Grid.SetColumn(save,3);footer.Children.Add(save);
    }
    sealed record Factor(int Value){public override string ToString()=>$"{Value/100.0:0.##}×";}
}

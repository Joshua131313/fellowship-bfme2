using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace Fellowship;

public sealed partial class BrowserWindow
{
    sealed record NetworkChoice(string Name,string Address)
    {
        public string Label=>Name.Contains("ZeroTier",StringComparison.OrdinalIgnoreCase)?"ZeroTier":Name;
        public string Description=>Name+"\n"+Address;
        public bool Radmin=>Name.Contains("Radmin",StringComparison.OrdinalIgnoreCase);
    }
    // Menu headers normally only open a submenu. Here clicking the header means all groups.
    sealed class NetworkMenuItem : MenuItem
    {
        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            var point=e.GetPosition(this);
            if(HasItems&&point.X>=0&&point.Y>=0&&point.X<ActualWidth&&point.Y<ActualHeight)
            {e.Handled=true;RaiseEvent(new RoutedEventArgs(ClickEvent,this));return;}
            base.OnPreviewMouseLeftButtonUp(e);
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if(HasItems&&e.Key==Key.Enter&&ReferenceEquals(Keyboard.FocusedElement,this))
            {e.Handled=true;RaiseEvent(new RoutedEventArgs(ClickEvent,this));return;}
            base.OnPreviewKeyDown(e);
        }
    }
    readonly ContextMenu networkMenu=new(){Placement=PlacementMode.Bottom};
    bool switchingNetwork;
    event Action? NetworkChanged;
    List<NetworkChoice> NetworkChoices()
    {
        var choices=PeerService.Interfaces().Select(i=>new NetworkChoice(i.Name,i.Address)).ToList();
        if(!choices.Any(i=>i.Address==Settings.BindAddress)&&Settings.BindAddress.Length>0)
            choices.Insert(0,new(Settings.NetworkName+" (unavailable)",Settings.BindAddress));
        if(Settings.BindAddress.Length==0)choices.Insert(0,new("Radmin VPN (unavailable)",""));
        return choices;
    }
    void LoadNetworkChoices()
    {
        var selected=NetworkChoices().FirstOrDefault(i=>i.Address==Settings.BindAddress);
        var caption=new Grid();caption.ColumnDefinitions.Add(new());caption.ColumnDefinitions.Add(new(){Width=UI.Auto});
        var text=UI.Text((selected?.Label??L.T("Select a network…"))+(selected?.Radmin==true&&Settings.RadminGroup.Length>0?" · "+Settings.RadminGroup:""),12);
        text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;caption.Children.Add(text);
        var arrow=new System.Windows.Shapes.Path{Data=System.Windows.Media.Geometry.Parse("M 0,0 L 5,5 L 10,0"),Stroke=UI.Brush("#A7B6CE"),StrokeThickness=1.6,StrokeStartLineCap=System.Windows.Media.PenLineCap.Round,StrokeEndLineCap=System.Windows.Media.PenLineCap.Round,StrokeLineJoin=System.Windows.Media.PenLineJoin.Round,Margin=new(12,0,2,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,1);caption.Children.Add(arrow);networkCaption.Content=caption;
        networkCaption.ToolTip=(selected?.Description??L.T("Select a network…"))+(selected?.Radmin==true?"\n"+(Settings.RadminGroup.Length>0?Settings.RadminGroup:L.T("All Radmin groups")):"");
    }
    void PopulateNetworkMenu()
    {
        networkMenu.Items.Clear();
        foreach(var choice in NetworkChoices())
        {
            var item=new NetworkMenuItem{Header=choice.Label,Tag=choice,ToolTip=choice.Description,Style=(Style)Application.Current.FindResource(typeof(MenuItem)),IsChecked=choice.Address==Settings.BindAddress&&(!choice.Radmin||Settings.RadminGroup.Length==0)};
            item.Click+=async(_,e)=>{if(!ReferenceEquals(e.OriginalSource,item))return;networkMenu.IsOpen=false;await SelectServer(choice,"");};
            if(choice.Radmin)
            {
                var placeholder=new MenuItem{IsEnabled=false};
                MenuItem? refresh=null;
                refresh=UI.MenuAction("Refresh Radmin groups",async()=>
                {
                    refresh!.IsEnabled=false;L.Bind(refresh,HeaderedItemsControl.HeaderProperty,"Reading Radmin groups…");
                    try{await ReadRadminMenuGroups();Groups();LoadNetworkChoices();}
                    finally{refresh.IsEnabled=true;L.Bind(refresh,HeaderedItemsControl.HeaderProperty,"Refresh Radmin groups");}
                });
                refresh.StaysOpenOnClick=true;
                item.Items.Add(new Separator());item.Items.Add(refresh);
                void Groups()
                {
                    var groups=Settings.RadminKnownGroups.Append(Settings.RadminGroup).Where(n=>n.Length>0).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToArray();
                    foreach(var stale in item.Items.OfType<MenuItem>().Where(option=>option.Tag is string name&&!groups.Contains(name,StringComparer.Ordinal)).ToArray())item.Items.Remove(stale);
                    for(int index=0;index<groups.Length;index++)
                    {
                        var group=groups[index];
                        if(item.Items.OfType<MenuItem>().FirstOrDefault(option=>option.Tag as string==group) is {} existing)
                        {existing.IsChecked=choice.Address==Settings.BindAddress&&Settings.RadminGroup==group;continue;}
                        var option=new MenuItem{Tag=group,ToolTip=group,IsChecked=choice.Address==Settings.BindAddress&&Settings.RadminGroup==group};
                        option.SetBinding(HeaderedItemsControl.HeaderProperty,new System.Windows.Data.Binding{Source=group});
                        option.Click+=async(_,e)=>{e.Handled=true;networkMenu.IsOpen=false;await SelectServer(choice,group);};item.Items.Insert(index,option);
                    }
                    if(groups.Length==0){L.Bind(placeholder,HeaderedItemsControl.HeaderProperty,readingGroups?"Reading Radmin groups…":"Open Radmin VPN to read its groups.");if(!item.Items.Contains(placeholder))item.Items.Insert(0,placeholder);}
                    else item.Items.Remove(placeholder);
                }
                Groups();MenuPlacement.Submenu(item);item.SubmenuOpened+=async(_,e)=>{if(!ReferenceEquals(e.OriginalSource,item)||Settings.RadminKnownGroups.Length>0)return;await ReadRadminMenuGroups();if(item.IsSubmenuOpen)Groups();};
            }
            networkMenu.Items.Add(item);
        }
    }
    async Task ReadRadminMenuGroups()
    {
        if(UsingRadmin){await RefreshRadminGroups(true);return;}
        if(readingGroups||!RadminNetworks.CanSpawn)return;
        readingGroups=true;
        try{var snapshot=await RadminNetworks.Refresh("");if(snapshot.Groups.Length>0){Settings.RadminKnownGroups=snapshot.Groups;Settings.Save();}}
        catch(Exception error){System.Diagnostics.Debug.WriteLine("Radmin menu: "+error.Message);}
        finally{readingGroups=false;UpdateGroupPickers();}
    }
    void ConfigureNetworkPicker()
    {
        networkMenu.PlacementTarget=networkCaption;networkMenu.MinWidth=220;
        networkMenu.SetBinding(FrameworkElement.FlowDirectionProperty,new System.Windows.Data.Binding(nameof(L.Direction)){Source=L.Current});
        networkCaption.ContextMenu=networkMenu;LoadNetworkChoices();
        networkCaption.Click+=(_,_)=>{PopulateNetworkMenu();networkMenu.IsOpen=true;};
        networkCaption.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down||e.Key==Key.F4){e.Handled=true;PopulateNetworkMenu();networkMenu.IsOpen=true;}};
        networkMenu.Opened+=(_,_)=>{if(networkMenu.Items.Count==0)PopulateNetworkMenu();};
    }
    async Task SelectServer(NetworkChoice chosen,string group)
    {
        if(switchingNetwork||changingGroup)return;
        switchingNetwork=true;networkCaption.IsEnabled=false;
        try
        {
            if(chosen.Address!=Settings.BindAddress)await ChangeNetwork(chosen.Name,chosen.Address);
            if(chosen.Radmin&&group!=Settings.RadminGroup)await SelectRadminGroup(group);
            LoadNetworkChoices();
            if(chosen.Radmin)await RefreshRadminGroups(true);
            while(refreshing)await Task.Delay(50);
            await Refresh();
        }
        catch(Exception error){L.Bind(status,TextBlock.TextProperty,"Network switch failed · "+error.Message);status.Foreground=UI.Brush("#E5B77A");}
        finally{switchingNetwork=false;networkCaption.IsEnabled=true;LoadNetworkChoices();}
    }
    async Task ChangeNetwork(string name,string target)
    {
        if(Peer.Address!=target)
        {
            await CloseRoom();Peer.Start(target);online=true;
            foreach(var ip in peerQueries.Keys.ToArray())queryVersions[ip]=queryVersions.GetValueOrDefault(ip)+1;
            Presence.Clear();activityAlerts=new();discovered.Clear();discoveredAddresses.Clear();address.Text="";initialDiscovery=true;
        }
        connectionFailure=null;networkWarningAddress=null;
        Settings.NetworkName=name;Settings.BindAddress=target;Settings.Save();
        LoadNetworkChoices();NetworkChanged?.Invoke();RenderRooms();RenderActivePlayers();friendsWindow?.Render();
    }
}

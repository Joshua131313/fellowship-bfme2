using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Fellowship;
public sealed partial class BrowserWindow
{
    readonly List<(FrameworkElement Panel,ComboBox Choice,TextBlock Status)> groupPickers=[];
    readonly DispatcherTimer radminTimer=new(){Interval=TimeSpan.FromSeconds(25)};
    bool loadingGroups,readingGroups,changingGroup;
    string groupReadError="";
    bool UsingRadmin=>Settings.NetworkName.Contains("Radmin",StringComparison.OrdinalIgnoreCase);
    FrameworkElement RadminGroupSelector(bool compact=false)
    {
        var label=UI.LocalizedText("RADMIN GROUP",10,UI.Muted,true);
        var choice=new ComboBox{MinWidth=160,Height=34,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};
        System.Windows.Automation.AutomationProperties.SetName(choice,"Radmin group");
        L.Bind(choice,FrameworkElement.ToolTipProperty,"Limit Fellowship to one Radmin group. Changing groups leaves your current room.");
        var refresh=UI.AsyncButton("↻",async()=>await RefreshRadminGroups(true));refresh.Width=34;refresh.Padding=new(0);refresh.Margin=new(8,0,0,0);L.Bind(refresh,FrameworkElement.ToolTipProperty,"Refresh Radmin groups");
        var field=new Grid();field.ColumnDefinitions.Add(new());field.ColumnDefinitions.Add(new(){Width=UI.Auto});field.Children.Add(choice);Grid.SetColumn(refresh,1);field.Children.Add(refresh);
        var status=UI.LocalizedText("",10,UI.Muted);status.TextWrapping=TextWrapping.Wrap;
        FrameworkElement panel;
        if(compact)
        {
            var row=new Grid{Margin=new(22,0,22,14)};row.ColumnDefinitions.Add(new(){Width=UI.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(240)});row.ColumnDefinitions.Add(new());
            label.Margin=new(0,0,12,0);row.Children.Add(label);Grid.SetColumn(field,1);row.Children.Add(field);status.Margin=new(14,0,0,0);Grid.SetColumn(status,2);row.Children.Add(status);panel=row;
        }
        else
        {
            label.Margin=new(0,14,0,7);status.Margin=new(0,7,0,0);panel=UI.Stack(label,field,status);
        }
        groupPickers.Add((panel,choice,status));UpdateGroupPickers();
        choice.DropDownOpened+=async(_,_)=>{if(Settings.RadminKnownGroups.Length==0)await RefreshRadminGroups(true);};
        choice.SelectionChanged+=async(_,_)=>
        {
            if(loadingGroups||changingGroup||choice.SelectedItem is not ComboBoxItem{Tag:string selected}||selected==Settings.RadminGroup)return;
            changingGroup=true;
            try
            {
                while(readingGroups)await Task.Delay(50);
                await CloseRoom();
                Settings.RadminGroup=selected;Settings.RadminMembership=RadminMembership.Empty;Settings.Save();groupReadError="";
                ResetGroupPresence();UpdateGroupPickers();
                await RefreshRadminGroups(true);await Refresh();
            }
            catch(Exception error){groupReadError=error.Message;}
            finally{changingGroup=false;UpdateGroupPickers();}
        };
        return panel;
    }
    void ConfigureRadminGroups()
    {
        NetworkChanged+=()=>{UpdateGroupPickers();if(RadminNetworks.CanSpawn)_=RefreshRadminGroups(true);};
        radminTimer.Tick+=async(_,_)=>await RefreshRadminGroups();
        Closed+=(_,_)=>radminTimer.Stop();
        if(RadminNetworks.CanSpawn)
        {
            radminTimer.Start();Dispatcher.BeginInvoke(new Action(async()=>await RefreshRadminGroups(true)));
        }
    }
    void UpdateGroupPickers()
    {
        loadingGroups=true;
        try
        {
            foreach(var (panel,choice,status) in groupPickers)
            {
                panel.Visibility=UsingRadmin?Visibility.Visible:Visibility.Collapsed;choice.IsEnabled=!changingGroup;
                if(!choice.IsDropDownOpen)
                {
                    choice.Items.Clear();choice.Items.Add(new ComboBoxItem{Tag="",Content=UI.LocalizedText("All Radmin groups",12)});
                    foreach(var name in Settings.RadminKnownGroups.Append(Settings.RadminGroup).Where(n=>n.Length>0).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase))choice.Items.Add(new ComboBoxItem{Tag=name,Content=UI.Text(name,12),ToolTip=name});
                    choice.SelectedItem=choice.Items.OfType<ComboBoxItem>().First(i=>(string)i.Tag==Settings.RadminGroup);
                }
                L.Bind(status,TextBlock.TextProperty,readingGroups?"Reading Radmin groups…":groupReadError.Length>0?groupReadError:Settings.RadminGroup.Length==0?"Choose a group to limit Fellowship. Other Radmin connections stay active.":"Only this group's members are used by Fellowship. Other Radmin connections stay active.");
            }
        }
        finally{loadingGroups=false;}
    }
    void ResetGroupPresence()
    {
        foreach(var ip in peerQueries.Keys.ToArray())queryVersions[ip]=queryVersions.GetValueOrDefault(ip)+1;
        Presence.Clear();activityAlerts=new();discovered.Clear();discoveredAddresses.Clear();address.Text="";initialDiscovery=true;
        RenderRooms();RenderActivePlayers();friendsWindow?.Render();
    }
    async Task RefreshRadminGroups(bool force=false)
    {
        if(readingGroups||!UsingRadmin||!RadminNetworks.CanSpawn)return;
        readingGroups=true;var selected=Settings.RadminGroup;var bind=Settings.BindAddress;UpdateGroupPickers();
        try
        {
            var snapshot=await RadminNetworks.Refresh(selected);
            if(selected!=Settings.RadminGroup||bind!=Settings.BindAddress)return;
            if(snapshot.Groups.Length>0&&!Settings.RadminKnownGroups.SequenceEqual(snapshot.Groups)){Settings.RadminKnownGroups=snapshot.Groups;Settings.Save();}
            if(!snapshot.Complete)groupReadError=snapshot.Error.Length>0?snapshot.Error:"Open Radmin VPN to read its groups.";
            else if(snapshot.LocalAddress!=Settings.BindAddress)groupReadError="The Radmin address does not match the selected network interface.";
            else
            {
                groupReadError="";Settings.RadminMembership=new(selected,snapshot.Members,DateTimeOffset.UtcNow);
                Presence.Keep(Peer.AllowsPeer);discoveredAddresses.RemoveWhere(ip=>!Peer.AllowsPeer(ip));Peer.RecheckNetworkScope();UpdateDiscoveredRooms();
                if(online)await Peer.Discover(presence:true);
            }
        }
        catch(Exception error){System.Diagnostics.Debug.WriteLine("Radmin groups: "+error.Message);groupReadError=error.Message;}
        finally{readingGroups=false;UpdateGroupPickers();}
    }
}

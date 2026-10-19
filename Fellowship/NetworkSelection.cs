using System.Windows;
using System.Windows.Controls;
namespace Fellowship;

public sealed partial class BrowserWindow
{
    sealed record NetworkChoice(string Name,string Address);
    bool loadingNetworks,switchingNetwork;
    event Action? NetworkChanged;
    void LoadNetworkChoices()
    {
        loadingNetworks=true;
        try
        {
            var choices=PeerService.Interfaces().Select(i=>new NetworkChoice(i.Name,i.Address)).ToList();
            if(!choices.Any(i=>i.Address==Settings.BindAddress)&&Settings.BindAddress.Length>0)
                choices.Insert(0,new(Settings.NetworkName+" (unavailable)",Settings.BindAddress));
            networkCaption.ItemsSource=choices;
            networkCaption.SelectedItem=choices.FirstOrDefault(i=>i.Address==Settings.BindAddress);
        }
        finally{loadingNetworks=false;}
    }
    void ConfigureNetworkPicker()
    {
        LoadNetworkChoices();
        networkCaption.DropDownOpened+=(_,_)=>LoadNetworkChoices();
        networkCaption.SelectionChanged+=async(_,_)=>
        {
            if(loadingNetworks||switchingNetwork||networkCaption.SelectedItem is not NetworkChoice chosen||chosen.Address==Settings.BindAddress)return;
            switchingNetwork=true;networkCaption.IsEnabled=false;
            status.Text=$"Connecting to {chosen.Name}…";status.Foreground=UI.Brush(UI.Accent);
            try
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                await ChangeNetwork(chosen.Name,chosen.Address);
                status.Text=$"Refreshing rooms and friends on {chosen.Name}…";
                while(refreshing)await Task.Delay(50);
                await Task.WhenAll(peerQueries.Values.ToArray());await Refresh();
                status.Text=$"● Connected to {chosen.Name} · {chosen.Address} · Rooms and friends refreshed";status.Foreground=UI.Brush(UI.Green);
            }
            catch(Exception error){status.Text="Network switch failed · "+error.Message;status.Foreground=UI.Brush("#E5B77A");UI.Error(error.Message);}
            finally{switchingNetwork=false;networkCaption.IsEnabled=true;LoadNetworkChoices();}
        };
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

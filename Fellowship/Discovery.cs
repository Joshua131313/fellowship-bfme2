using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
namespace Fellowship;
public sealed partial class PeerService
{
    public const int DiscoveryPort=28743;
    UdpClient? discovery;
    public event Action<string>? Discovered;
    const string Probe="FELLOWSHIP_DISCOVER_1";
    const string Reply="FELLOWSHIP_PEER_1";
    void StartDiscovery()
    {
        try
        {
            discovery=new UdpClient(new IPEndPoint(IPAddress.Parse(Address),DiscoveryPort)){EnableBroadcast=true};
            _=DiscoverLoop();
        }
        catch(SocketException){discovery?.Dispose();discovery=null;Notice?.Invoke("Automatic discovery unavailable. Direct-IP joining still works (UDP 28743).");}
    }
    async Task DiscoverLoop()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var packet=await discovery!.ReceiveAsync(stop.Token);
                if(packet.Buffer.Length>128||packet.RemoteEndPoint.Address.ToString()==Address)continue;
                var remote=packet.RemoteEndPoint.Address.ToString();
                var text=Encoding.ASCII.GetString(packet.Buffer);
                if(text==Probe)
                {
                    bool respond;lock(gate)respond=IsHost&&Current!=null&&CanSee(remote);
                    if(respond)await discovery.SendAsync(Encoding.ASCII.GetBytes(Reply),packet.RemoteEndPoint,stop.Token);
                }
                else if(text==Reply)Discovered?.Invoke(remote);
            }
        }
        catch(Exception e) when(e is SocketException or OperationCanceledException or ObjectDisposedException){}
    }
    public async Task Discover(string? target=null)
    {
        if(discovery==null)return;
        var destinations=new HashSet<string>();
        if(target!=null)destinations.Add(target);
        else
        {
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
            foreach(var ip in adapter.GetIPProperties().UnicastAddresses)
            {
                if(ip.Address.ToString()!=Address||ip.IPv4Mask==null)continue;
                var bytes=ip.Address.GetAddressBytes();var mask=ip.IPv4Mask.GetAddressBytes();
                if(bytes.Length!=4)continue;
                destinations.Add(new IPAddress(bytes.Select((b,i)=>(byte)(b|~mask[i])).ToArray()).ToString());
            }
            foreach(var friend in settings.Friends)destinations.Add(friend.Address);
        }
        foreach(var destination in destinations)
            try{await discovery.SendAsync(Encoding.ASCII.GetBytes(Probe),new IPEndPoint(IPAddress.Parse(destination),DiscoveryPort),stop.Token);}
            catch(Exception e) when(e is SocketException or OperationCanceledException or ObjectDisposedException or FormatException){}
    }
}

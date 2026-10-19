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
    const string PresenceProbe="FELLOWSHIP_PRESENCE_1";
    const string Reply="FELLOWSHIP_PEER_1";
    void StartDiscovery()
    {
        try
        {
            discovery=new UdpClient(new IPEndPoint(IPAddress.Parse(Address),DiscoveryPort)){EnableBroadcast=true};
            var adapter=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()==Address));
            if(OperatingSystem.IsWindows()&&adapter?.GetIPProperties().GetIPv4Properties() is {} ipv4)
                discovery.Client.SetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31 /* IP_UNICAST_IF */,IPAddress.HostToNetworkOrder(ipv4.Index));
            _=DiscoverLoop(discovery,Address,networkStop!.Token);
        }
        catch(SocketException){discovery?.Dispose();discovery=null;Notice?.Invoke("Automatic discovery unavailable. Direct-IP joining still works (UDP 28743).");}
    }
    async Task DiscoverLoop(UdpClient socket,string localAddress,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try
            {
                var packet=await socket.ReceiveAsync(token);
                if(token.IsCancellationRequested||packet.Buffer.Length>128||packet.RemoteEndPoint.Address.ToString()==localAddress)continue;
                var remote=packet.RemoteEndPoint.Address.ToString();
                if(settings.IsBlocked(remote)&&!settings.Friends.Any(f=>f.Address==remote))continue;
                var text=Encoding.ASCII.GetString(packet.Buffer);
                if(text==PresenceProbe)
                {
                    Discovered?.Invoke(remote);
                    await socket.SendAsync(Encoding.ASCII.GetBytes(Reply),packet.RemoteEndPoint,token);
                }
                else if(text==Probe)
                {
                    bool respond;lock(gate)respond=IsHost&&Current!=null&&CanSee(remote);
                    if(respond)await socket.SendAsync(Encoding.ASCII.GetBytes(Reply),packet.RemoteEndPoint,token);
                }
                else if(text==Reply)Discovered?.Invoke(remote);
            }
            catch(OperationCanceledException){break;}
            catch(ObjectDisposedException){break;}
            catch(SocketException)
            {
                // Windows reports ICMP errors (including an offline peer's closed
                // port) on ReceiveAsync. They do not invalidate the UDP socket.
                // Keep listening instead of losing all discovery until restart.
                try{await Task.Delay(100,token);}
                catch(OperationCanceledException){break;}
            }
        }
    }
    public async Task Discover(string? target=null,bool presence=false)
    {
        var socket=discovery;var source=Address;if(socket==null)return;
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
                destinations.Add(IPAddress.Broadcast.ToString());
                destinations.Add(new IPAddress(bytes.Select((b,i)=>(byte)(b|~mask[i])).ToArray()).ToString());
            }
            foreach(var friend in settings.Friends)destinations.Add(friend.Address);
        }
        foreach(var destination in destinations.Where(d=>!settings.IsBlocked(d)))
            try{await socket.SendAsync(Encoding.ASCII.GetBytes(presence?PresenceProbe:Probe),new IPEndPoint(IPAddress.Parse(destination),DiscoveryPort),stop.Token);}
            catch(Exception e) when(e is SocketException or OperationCanceledException or ObjectDisposedException or FormatException){}
    }
}

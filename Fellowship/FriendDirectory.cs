using System.Net;
using System.Net.Sockets;
namespace Fellowship;

public static class FriendDirectory
{
    // Call only with a name returned by this address's own peer query. Room
    // snapshots and chat sender labels cannot rename a different saved contact.
    // The directory saves this change together with its existing presence data.
    public static bool RememberOnline(Settings settings,string address,string? name)
    {
        if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||settings.IsBlocked(address)||!settings.AllowsNetworkPeer(address))return false;
        string nickname=name?.Trim()??"";
        if(nickname.Length is <1 or >40||nickname.Any(char.IsControl))return false;
        bool changed=false;
        for(int i=0;i<settings.Friends.Count;i++)
        {
            var friend=settings.Friends[i];
            if(friend.Address!=address||string.Equals(friend.Name,nickname,StringComparison.Ordinal))continue;
            settings.Friends[i]=friend with{Name=nickname};changed=true;
        }
        return changed;
    }
}

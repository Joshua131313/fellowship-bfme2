using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Fellowship;

public static class RoomIsolation
{
    public sealed record Policy(bool Enabled,string[] Members,string Broadcast);
    public static Policy For(Room room,string local)
    {
        var members=room.Players.Select(p=>p.Address).Append(local).Distinct().Order().ToArray();
        string broadcast="255.255.255.255";
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
        foreach(var address in nic.GetIPProperties().UnicastAddresses)
            if(address.Address.ToString()==local&&address.Address.AddressFamily==AddressFamily.InterNetwork)
            {
                var ip=address.Address.GetAddressBytes();var mask=address.IPv4Mask.GetAddressBytes();
                broadcast=new IPAddress(ip.Select((b,i)=>(byte)(b|~mask[i])).ToArray()).ToString();
            }
        return new(true,members,broadcast);
    }
    public static byte[] Request(int pid,Policy policy)
    {
        if(policy.Members.Length>64)throw new InvalidDataException("Room isolation supports up to 64 room members.");
        byte[] Address(string text)
        {
            if(!IPAddress.TryParse(text,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||text=="0.0.0.0")throw new InvalidDataException("Invalid isolation address.");
            return ip.GetAddressBytes();
        }
        var snapshot=new NativeLobby.Snapshot(0,"maps\\isolation.map","",[new(0,6,-1,-1,true)]);
        var bytes=NativePlacement.RequestBytes(pid,snapshot,0,0);
        using var stream=new MemoryStream(bytes);using var writer=new BinaryWriter(stream);
        stream.Position=568;writer.Write(2);stream.Position=1104;writer.Write(policy.Enabled?1:0);writer.Write(policy.Members.Length);
        foreach(var member in policy.Members){if(member=="255.255.255.255")throw new InvalidDataException("A member cannot be a broadcast address.");writer.Write(Address(member));}
        stream.Position=1368;writer.Write(Address(policy.Broadcast));return bytes;
    }
    public static async Task<int> Apply(string helper,int pid,Policy policy,CancellationToken token)
    {
        string path=Path.Combine(Settings.Folder,"native","isolation-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            File.WriteAllBytes(path,Request(pid,policy));
            var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(NativePlacement.PhysicalPath(path));
            using var child=Process.Start(start)??throw new IOException("Could not start isolation bridge.");
            await child.WaitForExitAsync(token);return BitConverter.ToInt32(File.ReadAllBytes(path),32);
        }
        finally{File.Delete(path);}
    }
}

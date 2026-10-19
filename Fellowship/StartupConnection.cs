using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
namespace Fellowship;

public static class StartupConnection
{
    public static string? Warning(Settings settings)
    {
        var messages=new List<string>();
        try
        {
            var adapters=NetworkInterface.GetAllNetworkInterfaces();
            if(!string.IsNullOrWhiteSpace(settings.BindAddress)&&!adapters.Any(n=>n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()==settings.BindAddress)))
                messages.Add($"{settings.NetworkName} ({settings.BindAddress}) is not connected or its address has changed. Connect to your VPN network, then select its current interface in Settings.");
        }
        catch(NetworkInformationException){messages.Add($"Fellowship could not read your network adapters. Check {settings.NetworkName} and refresh.");}

        object? manager=null;
        try
        {
            var type=Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if(type!=null){manager=Activator.CreateInstance(type);if(manager!=null&&!((dynamic)manager).IsConnectedToInternet)messages.Insert(0,"Windows reports no internet connection. Connect to the internet to use online updates and reach your VPN friends.");}
        }
        catch(COMException){}catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){}
        finally{if(manager!=null&&Marshal.IsComObject(manager))Marshal.ReleaseComObject(manager);}
        return messages.Count==0?null:string.Join("\n\n",messages);
    }
}

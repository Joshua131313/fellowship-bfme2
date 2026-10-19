using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
namespace Fellowship;

public static class StartupConnection
{
    public static string? Warning()
    {
        var messages=new List<string>();
        try
        {
            var adapters=NetworkInterface.GetAllNetworkInterfaces();
            if(!adapters.Any(n=>(n.Name+" "+n.Description).Contains("Radmin",StringComparison.OrdinalIgnoreCase)&&n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)))
                messages.Add("Radmin VPN is not connected or its adapter is unavailable. Open Radmin VPN and connect to your network, then refresh Fellowship.");
        }
        catch(NetworkInformationException){messages.Add("Fellowship could not read your network adapters. Check Radmin VPN and refresh.");}
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

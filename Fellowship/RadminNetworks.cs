using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace Fellowship;

public sealed record RadminSnapshot(string[] Groups,string Selected,string[] Members,string LocalAddress,bool Complete,string Error="");
public sealed record RadminMembership(string Group,string[] Members,DateTimeOffset ReadAt)
{
    public static readonly RadminMembership Empty=new("",[],DateTimeOffset.MinValue);
    public bool Allows(string group,string address)=>Group==group&&DateTimeOffset.UtcNow-ReadAt<TimeSpan.FromMinutes(2)&&Members.Contains(address,StringComparer.Ordinal);
}
public static class RadminNetworks
{
    public static bool CanSpawn=>System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name=="Fellowship";
    static bool Ipv4(string text)=>IPAddress.TryParse(text,out var ip)&&ip.AddressFamily==AddressFamily.InterNetwork&&!ip.Equals(IPAddress.Any)&&!ip.Equals(IPAddress.Broadcast);
    static AutomationElement[] Children(AutomationElement element)
    {
        var cache=new CacheRequest();cache.Add(AutomationElement.NameProperty);cache.Add(AutomationElement.AutomationIdProperty);
        using(cache.Activate())return element.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().ToArray();
    }
    static string Id(AutomationElement element)=>element.Cached.AutomationId;
    static string Name(AutomationElement element)=>element.Cached.Name;
    static AutomationElement? FindMainWindow()
    {
        var session=Process.GetCurrentProcess().SessionId;
        var pids=Process.GetProcessesByName("RvRvpnGui").Where(p=>p.SessionId==session).Select(p=>p.Id).ToHashSet();
        AutomationElement? root=null;
        EnumWindows((handle,_)=>
        {
            GetWindowThreadProcessId(handle,out var pid);if(!pids.Contains((int)pid))return true;
            var title=new StringBuilder(256);GetWindowText(handle,title,title.Capacity);
            if(title.ToString()!="Radmin VPN")return true;
            var candidate=AutomationElement.FromHandle(handle);
            if(candidate.Current.AutomationId!="MainWindow")return true;
            root=candidate;return false;
        },IntPtr.Zero);
        return root;
    }
    public static RadminSnapshot Read(string selected)
    {
        // Read membership only. Restore expansion and never invoke network
        // connection controls; group selection filters Fellowship traffic only.
        var root=FindMainWindow();
        if(root==null)return new([],selected,[],"",false,"Open Radmin VPN to read its groups.");
        var elements=Children(root);
        var groups=elements.Where(e=>Id(e).EndsWith(".CNetworkWidget",StringComparison.Ordinal)).Select(e=>
        {
            var nodes=Children(e);var label=nodes.FirstOrDefault(n=>Id(n).EndsWith(".NetworkName",StringComparison.Ordinal));var name=label==null?"":Name(label);
            return (Name:name,Element:e,Nodes:nodes);
        }).Where(g=>g.Name.Length>0).ToArray();
        var names=groups.Select(g=>g.Name).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToArray();
        var localLabel=elements.FirstOrDefault(e=>Id(e).EndsWith(".userInfoWidget.LIP",StringComparison.Ordinal));var local=localLabel==null?"":Name(localLabel);
        if(selected.Length==0)return new(names,"",[],local,names.Length>0);
        var matches=groups.Where(g=>g.Name==selected).ToArray();
        if(matches.Length!=1)return new(names,selected,[],local,false,"The selected Radmin group is unavailable.");
        var group=matches[0];
        var triangle=group.Nodes.FirstOrDefault(n=>Id(n).EndsWith(".NetworkWidget.Triang",StringComparison.Ordinal));
        if(triangle==null||!triangle.TryGetCurrentPattern(TogglePattern.Pattern,out var pattern))return new(names,selected,[],local,false,"Radmin group members could not be read.");
        var toggle=(TogglePattern)pattern;bool expanded=toggle.Current.ToggleState==ToggleState.On;
        try
        {
            if(!expanded)((InvokePattern)triangle.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); // Expand the view only, then restore it.
            string[] previous=[];int stable=0;
            for(int attempt=0;attempt<12;attempt++)
            {
                Thread.Sleep(150);
                var nodes=Children(group.Element);
                var addresses=nodes.Where(n=>Id(n).EndsWith(".AddressWidget",StringComparison.Ordinal)).Select(n=>Name(n).Trim()).Where(Ipv4).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
                bool hasContainer=nodes.Any(n=>Id(n).EndsWith(".NodesContainer",StringComparison.Ordinal));
                stable=previous.SequenceEqual(addresses)?stable+1:0;previous=addresses;
                if(stable>=(hasContainer?2:5)&&toggle.Current.ToggleState==ToggleState.On&&Ipv4(local))return new(names,selected,addresses,local,true);
            }
            return new(names,selected,[],local,false,"Radmin group members could not be read.");
        }
        finally
        {
            if(!expanded&&toggle.Current.ToggleState==ToggleState.On)((InvokePattern)triangle.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
    }
    public static async Task<RadminSnapshot> Refresh(string selected)
    {
        // Isolate the external accessibility provider from the dashboard. A hung
        // provider is stopped rather than freezing Fellowship's UI or networking.
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--radmin-groups");start.ArgumentList.Add(selected);
        using var process=Process.Start(start)??throw new IOException("Radmin group members could not be read.");
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}
        catch(TimeoutException){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("Radmin group reading timed out. Try refreshing again.");}
        await errors;
        if(process.ExitCode!=0)throw new IOException("Radmin group members could not be read.");
        return JsonSerializer.Deserialize<RadminSnapshot>(await output)??throw new IOException("Radmin group members could not be read.");
    }
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
    delegate bool EnumWindow(IntPtr handle,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr handle,StringBuilder text,int count);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
}

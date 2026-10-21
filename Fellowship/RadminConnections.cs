using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace Fellowship;

public sealed record RadminConnectionState(string Group,bool Enabled);
public sealed record RadminConnectionResult(bool Complete,RadminConnectionState[] States,string Error="");

public static partial class RadminNetworks
{
    // Only these exact connection commands may be invoked. Never infer an action
    // from its menu position: Leave/Delete are adjacent in Radmin's native menu.
    // Captions come from QCommand Enable/Disable network entries in the Radmin
    // client's 91 Qt translation catalogs, including the five Fellowship languages.
    static readonly Lazy<Dictionary<string,bool>> connectionCommands=new(()=>
    {using var source=typeof(RadminNetworks).Assembly.GetManifestResourceStream("Fellowship.RadminConnectionCommands")!;return JsonSerializer.Deserialize<Dictionary<string,bool>>(source)!;});
    public static bool? ConnectionCommand(string caption)=>connectionCommands.Value.TryGetValue(caption.Replace("&","").Trim().ToLowerInvariant(),out var enabled)?enabled:null;
    static AutomationElement[] ConnectionMenus(int pid)
    {
        var menus=new List<AutomationElement>();
        EnumWindows((handle,_)=>
        {
            GetWindowThreadProcessId(handle,out var owner);
            if(owner!=pid||!IsWindowVisible(handle))return true;
            var nativeClass=new System.Text.StringBuilder(128);GetClassName(handle,nativeClass,nativeClass.Capacity);
            if(!nativeClass.ToString().Contains("QWindowPopup",StringComparison.Ordinal))return true;
            try{var candidate=AutomationElement.FromHandle(handle);if(candidate.Current.ClassName=="QMenu")menus.Add(candidate);}catch(ElementNotAvailableException){}return true;
        },IntPtr.Zero);return menus.ToArray();
    }
    static void DismissConnectionMenu(AutomationElement menu)
    {
        var handle=(IntPtr)menu.Current.NativeWindowHandle;
        int pid=menu.Current.ProcessId;
        PostMessage(handle,0x100,(IntPtr)0x1b,IntPtr.Zero);PostMessage(handle,0x101,(IntPtr)0x1b,IntPtr.Zero);
        for(int i=0;i<20&&ConnectionMenus(pid).Any();i++)Thread.Sleep(50);
    }
    sealed class ConnectionView:IDisposable
    {
        public readonly AutomationElement Root;
        public readonly int Pid;
        readonly Dictionary<string,bool> expansion=new(StringComparer.Ordinal);
        RangeValuePattern? scroll;
        double oldScroll;
        readonly IntPtr handle;
        readonly bool wasVisible,wasMinimized;
        public ConnectionView()
        {
            Root=FindMainWindow()??throw new IOException("Open Radmin VPN to change its group connections.");Pid=Root.Current.ProcessId;
            if(ConnectionMenus(Pid).Length>0)throw new IOException("Close the open Radmin menu and try selecting the group again.");
            handle=(IntPtr)Root.Current.NativeWindowHandle;wasVisible=IsWindowVisible(handle);wasMinimized=IsIconic(handle);
        }
        public void Prepare()
        {
            if(!wasVisible||wasMinimized){ShowWindow(handle,wasMinimized?9:4);Thread.Sleep(150);}
            var controls=Children(Root);
            scroll=controls.FirstOrDefault(e=>e.Current.ControlType==ControlType.ScrollBar&&e.TryGetCurrentPattern(RangeValuePattern.Pattern,out _))?.GetCurrentPattern(RangeValuePattern.Pattern) as RangeValuePattern;
            oldScroll=scroll?.Current.Value??0;
            // Native Qt context menus target visible widgets. Collapse the tree
            // temporarily to expose headers; restore expansion and scrolling later.
            foreach(var group in controls.Where(e=>Id(e).EndsWith(".CNetworkWidget",StringComparison.Ordinal)))
            {
                var nodes=Children(group);var name=nodes.FirstOrDefault(e=>Id(e).EndsWith(".NetworkName",StringComparison.Ordinal));
                var triangle=nodes.FirstOrDefault(e=>Id(e).EndsWith(".NetworkWidget.Triang",StringComparison.Ordinal));
                if(name!=null&&triangle?.TryGetCurrentPattern(TogglePattern.Pattern,out var toggle)==true)expansion[Name(name)]=((TogglePattern)toggle).Current.ToggleState==ToggleState.On;
            }
            Flatten();
            if(scroll!=null&&!scroll.Current.IsReadOnly)scroll.SetValue(scroll.Current.Minimum);
            Thread.Sleep(100);
        }
        public AutomationElement Command(string group,out bool enabled)
        {
            if(ConnectionMenus(Pid).Length>0)throw new IOException("Close the open Radmin menu and try selecting the group again.");
            // Enabling a network may expand it automatically and move all later
            // headers. Re-establish the layout before choosing any native target.
            Flatten();
            if(scroll!=null&&!scroll.Current.IsReadOnly)scroll.SetValue(scroll.Current.Minimum);
            Thread.Sleep(60);
            var labels=Children(Root).Where(e=>Id(e).EndsWith(".NetworkName",StringComparison.Ordinal)&&Name(e)==group).ToArray();
            if(labels.Length!=1)throw new IOException("The selected Radmin group is unavailable.");
            var label=labels[0];
            for(int i=0;label.Current.IsOffscreen&&i<100&&scroll!=null;i++)
            {
                var state=scroll.Current;var next=Math.Min(state.Maximum,state.Value+Math.Max(state.LargeChange,40));
                if(next==state.Value)break;scroll.SetValue(next);Thread.Sleep(30);
            }
            var bounds=label.Current.BoundingRectangle;
            if(label.Current.IsOffscreen||bounds.IsEmpty||bounds.Width<1||bounds.Height<1)throw new IOException("Open Radmin VPN to change its group connections.");
            int x=(int)(bounds.Left+bounds.Width/2),y=(int)(bounds.Top+bounds.Height/2);
            if(x<short.MinValue||x>short.MaxValue||y<short.MinValue||y>short.MaxValue)throw new IOException("Radmin group controls are unavailable.");
            var handle=(IntPtr)Root.Current.NativeWindowHandle;
            var point=new NativePoint{x=x,y=y};
            if(!ScreenToClient(handle,ref point))throw new IOException("Radmin group controls are unavailable.");
            var position=(IntPtr)((point.y<<16)|(point.x&0xffff));
            if(!PostMessage(handle,0x204,(IntPtr)2,position)||!PostMessage(handle,0x205,IntPtr.Zero,position))throw new IOException("Radmin group controls are unavailable.");
            AutomationElement? menu=null;
            for(int i=0;i<30;i++){Thread.Sleep(50);var menus=ConnectionMenus(Pid);if(menus.Length==1){menu=menus[0];break;}if(menus.Length>1)break;}
            if(menu==null)throw new IOException("Radmin group controls are unavailable.");
            var commands=Children(menu).Where(e=>e.Current.ControlType==ControlType.MenuItem&&ConnectionCommand(Name(e))!=null).ToArray();
            if(commands.Length!=1)
            {DismissConnectionMenu(menu);throw new IOException("Radmin connection commands could not be recognized. Update Radmin VPN and try again.");}
            enabled=ConnectionCommand(Name(commands[0]))!.Value;return commands[0];
        }
        public bool Enabled(string group)
        {
            Command(group,out bool enabled);foreach(var menu in ConnectionMenus(Pid))DismissConnectionMenu(menu);return enabled;
        }
        public void Set(string group,bool enabled)
        {
            var command=Command(group,out bool current);
            if(current==enabled){foreach(var menu in ConnectionMenus(Pid))DismissConnectionMenu(menu);return;}
            if(!command.Current.IsEnabled)throw new IOException("Radmin could not change the group connection.");
            ((InvokePattern)command.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            // Qt's accessibility Invoke triggers QAction but does not dismiss
            // its QMenu. Close that menu explicitly before verifying the state.
            foreach(var menu in ConnectionMenus(Pid))DismissConnectionMenu(menu);
            for(int attempt=0;attempt<8;attempt++)
            {if(Enabled(group)==enabled)return;Thread.Sleep(150);}
            throw new IOException("Radmin could not change the group connection.");
        }
        public void Dispose()
        {
            foreach(var menu in ConnectionMenus(Pid))try{DismissConnectionMenu(menu);}catch(ElementNotAvailableException){}
            foreach(var group in Children(Root).Where(e=>Id(e).EndsWith(".CNetworkWidget",StringComparison.Ordinal)))
            {
                var nodes=Children(group);var name=nodes.FirstOrDefault(e=>Id(e).EndsWith(".NetworkName",StringComparison.Ordinal));
                var triangle=nodes.FirstOrDefault(e=>Id(e).EndsWith(".NetworkWidget.Triang",StringComparison.Ordinal));
                try{if(name!=null&&triangle!=null&&expansion.TryGetValue(Name(name),out var original)&&(((TogglePattern)triangle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState==ToggleState.On)!=original)((InvokePattern)triangle.GetCurrentPattern(InvokePattern.Pattern)).Invoke();}catch(ElementNotAvailableException){}
            }
            if(scroll!=null)try{scroll.SetValue(Math.Clamp(oldScroll,scroll.Current.Minimum,scroll.Current.Maximum));}catch(ElementNotAvailableException){}
            if(!wasVisible)ShowWindow(handle,0);else if(wasMinimized)ShowWindow(handle,6);
        }
        void Flatten()
        {
            bool changed=false;
            foreach(var triangle in Children(Root).Where(e=>Id(e).EndsWith(".NetworkWidget.Triang",StringComparison.Ordinal)))
                if(triangle.TryGetCurrentPattern(TogglePattern.Pattern,out var toggle)&&((TogglePattern)toggle).Current.ToggleState==ToggleState.On)
                {((InvokePattern)triangle.GetCurrentPattern(InvokePattern.Pattern)).Invoke();changed=true;}
            if(changed)Thread.Sleep(150);
        }
    }
    public static RadminConnectionResult SetConnections(string selected,bool inspectOnly=false)
    {
        try
        {
            using var view=new ConnectionView();view.Prepare();
            var names=Children(view.Root).Where(e=>Id(e).EndsWith(".NetworkName",StringComparison.Ordinal)).Select(Name).ToArray();
            if(inspectOnly)return new(true,names.Select(n=>new RadminConnectionState(n,view.Enabled(n))).ToArray());
            return ApplyConnections(selected,names,view.Enabled,view.Set);
        }
        catch(Exception error){Console.Error.WriteLine(error);return new(false,[],error is IOException?error.Message:"Radmin could not change the group connection.");}
    }
    public static RadminConnectionResult ApplyConnections(string selected,string[] names,Func<string,bool> read,Action<string,bool> set)
    {
        if(names.Length==0||names.Distinct(StringComparer.Ordinal).Count()!=names.Length||(selected.Length>0&&!names.Contains(selected,StringComparer.Ordinal)))
            throw new IOException("The selected Radmin group is unavailable.");
        // Read every state before changing anything. Connect the target first.
        var original=names.Select(n=>new RadminConnectionState(n,read(n))).ToArray();
        var changed=new List<RadminConnectionState>();
        try
        {
            foreach(var state in original.OrderBy(s=>s.Group==selected?0:1))
            {
                bool desired=selected.Length==0||state.Group==selected;if(state.Enabled==desired)continue;
                changed.Add(state);set(state.Group,desired);
                if(read(state.Group)!=desired)throw new IOException("Radmin could not change the group connection.");
            }
            var final=names.Select(n=>new RadminConnectionState(n,read(n))).ToArray();
            if(final.Any(s=>s.Enabled!=(selected.Length==0||s.Group==selected)))throw new IOException("Radmin could not change the group connection.");
            return new(true,final);
        }
        catch(Exception cause)
        {
            bool restored=true;
            foreach(var previous in changed.AsEnumerable().Reverse())try{set(previous.Group,previous.Enabled);if(read(previous.Group)!=previous.Enabled)restored=false;}catch{restored=false;}
            throw new IOException(restored?"Radmin could not switch groups. Previous connections were restored.":"Radmin could not finish switching groups. Check the enabled networks in Radmin VPN and retry.",cause);
        }
    }
    public static async Task<RadminConnectionResult> ChangeConnections(string selected)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--radmin-connect");start.ArgumentList.Add(selected);
        using var process=Process.Start(start)??throw new IOException("Radmin could not change the group connection.");
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));}
        catch(TimeoutException){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("Radmin connection switching timed out. Check Radmin VPN and retry.");}
        await errors;
        if(process.ExitCode!=0)throw new IOException("Radmin could not change the group connection.");
        var result=JsonSerializer.Deserialize<RadminConnectionResult>(await output)??throw new IOException("Radmin could not change the group connection.");
        if(!result.Complete)throw new IOException(result.Error);return result;
    }
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr handle,int command);
    [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int x,y;}
    [DllImport("user32.dll")]static extern bool ScreenToClient(IntPtr handle,ref NativePoint point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr handle,System.Text.StringBuilder text,int count);
}

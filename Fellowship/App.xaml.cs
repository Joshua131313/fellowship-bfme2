using System.Windows;
using System.Threading;
using System.Security.Principal;
namespace Fellowship;
public partial class App : Application
{
    void SelectMouseWheel(object sender,System.Windows.Input.MouseWheelEventArgs e)
    {
        if(sender is not System.Windows.Controls.ComboBox {IsDropDownOpen:false} box)return;
        e.Handled=true;
        // A closed select never consumes wheel input as a value change.
        // Forward it to the enclosing page; an open dropdown keeps normal scrolling.
        for(DependencyObject? parent=System.Windows.Media.VisualTreeHelper.GetParent(box);parent!=null;parent=System.Windows.Media.VisualTreeHelper.GetParent(parent))
        {
            if(parent is System.Windows.Controls.ScrollViewer scroll)
            {
                scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=System.Windows.Input.Mouse.MouseWheelEvent,Source=scroll});
                break;
            }
        }
    }
    Mutex? instanceMutex;
    EventWaitHandle? activateEvent;
    RegisteredWaitHandle? activationWait;
    TrayIcon? tray;
    public bool IsQuitting { get; private set; }
    public void Quit()
    {
        IsQuitting=true;
        tray?.Dispose();tray=null;
        Shutdown();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==5&&e.Args[0]=="--native-sync"&&int.TryParse(e.Args[2],out var syncGame)&&int.TryParse(e.Args[3],out var syncParent))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await LivePlacement.Run(e.Args[1],syncGame,syncParent,e.Args[4]));return;
        }
        if(e.Args.Length is 5 or 6&&(e.Args[0] is "--native-apply" or "--native-wait")&&int.TryParse(e.Args[4],out var placementParent))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await NativePlacement.Run(e.Args[1],e.Args[2],e.Args[3],e.Args[0]=="--native-wait",placementParent,e.Args.Length==6?e.Args[5]:null));return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--install-update"&&int.TryParse(e.Args[2],out var updatePid))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Installation.RunUpdate(e.Args[1],updatePid));return;
        }
        if(e.Args.Length is >=2 and <=4&&e.Args[0]=="--host-preset")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await HostPreset.Run(e.Args[1],e.Args.Length>=3?e.Args[2]:null));return;
        }
        if(e.Args.Any(a=>a.EndsWith("-navigation")||a.EndsWith("-placement"))){Shutdown(1);return;}
        if(e.Args.Length==2&&e.Args[0]=="--wait-for"&&int.TryParse(e.Args[1],out var oldPid))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{using var previous=System.Diagnostics.Process.GetProcessById(oldPid);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await previous.WaitForExitAsync(timeout.Token);}
            catch(ArgumentException){}
            catch(OperationCanceledException){MessageBox.Show("The previous Fellowship is still running. Quit it and reopen the updated version.");Shutdown();return;}
        }
        var identity=WindowsIdentity.GetCurrent().User!.Value;
        instanceMutex=new Mutex(false,@"Local\Fellowship.UI."+identity,out bool first);
        activateEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\Fellowship.Activate."+identity);
        if(!first){activateEvent.Set();Shutdown();return;}
        var main = new BrowserWindow();
        MainWindow = main;
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        tray=new TrayIcon(main);
        if(!e.Args.Contains("--startup"))main.Show();else main.EnsureOnline(true);
        activationWait=ThreadPool.RegisterWaitForSingleObject(activateEvent,(_,_)=>Dispatcher.BeginInvoke(()=>
        {
            if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;
            main.Show();main.Activate();
        }),null,Timeout.Infinite,false);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activationWait?.Unregister(null);
        tray?.Dispose();
        activateEvent?.Dispose();instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

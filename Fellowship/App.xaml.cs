using System.Windows;
using System.Threading;
using System.Security.Principal;
namespace Fellowship;
public partial class App : Application
{
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
        if(e.Args.Length is 2 or 3&&e.Args[0] is "--host-preset" or "--host-navigation" or "--guest-navigation")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await HostPreset.Run(e.Args[1],e.Args.Length==3?e.Args[2]:null,e.Args[0]!="--host-preset",e.Args[0]!="--guest-navigation"));
            return;
        }
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
        main.Show();
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

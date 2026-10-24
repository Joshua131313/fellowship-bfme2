using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Fellowship;

public static class StartupUpdates
{
    public enum Outcome { OpenFellowship,HandedOff,Exit }
    public sealed record Hooks(
        Func<CancellationToken,Task<AppUpdates.Release?>> Check,
        Func<AppUpdates.Release,IProgress<(long Received,long? Total)>,CancellationToken,Task<string>> Download,
        Func<AppUpdates.Release,string,bool,Task> Handoff,
        Func<string,CancellationToken,Task<bool>> Retry,
        Action<string,string,double?> Report);
    sealed class Progress(Action<(long Received,long? Total)> report):IProgress<(long Received,long? Total)>
    {public void Report((long Received,long? Total) value)=>report(value);}

    // No dashboard or room is created until this returns OpenFellowship.
    public static async Task<Outcome> Coordinate(Hooks hooks,Func<bool> tray,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            AppUpdates.Release? release;
            hooks.Report("Checking for updates…","Fellowship will open when the update is ready.",null);
            try{release=await hooks.Check(token);}
            catch{return Outcome.OpenFellowship;} // Offline startup stays usable.
            if(release==null||token.IsCancellationRequested)return Outcome.OpenFellowship;
            bool receiving=true;
            try
            {
                token.ThrowIfCancellationRequested();
                hooks.Report("Downloading update…","Fellowship will update automatically before opening.",null);
                var progress=new Progress(value=>
                {
                    if(!receiving||token.IsCancellationRequested)return;
                    double? percent=value.Total is >0?100.0*value.Received/value.Total.Value:null;
                    bool complete=value.Total is >0&&value.Received>=value.Total;
                    string text=value.Total is >0?$"{value.Received/1048576.0:0.0} / {value.Total.Value/1048576.0:0.0} MB · {percent:0}%":$"{value.Received/1048576.0:0.0} MB downloaded";
                    hooks.Report(complete?"Verifying download…":"Downloading update…",complete?"Checking the package before installation.":text,complete?null:percent);
                });
                var package=await hooks.Download(release,progress,token);
                receiving=false;token.ThrowIfCancellationRequested();
                hooks.Report("Preparing installation…","Fellowship will reopen when installation finishes.",null);
                await hooks.Handoff(release,package,tray());
                return Outcome.HandedOff;
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){return Outcome.OpenFellowship;}
            catch(Exception error)
            {
                receiving=false;
                if(token.IsCancellationRequested)return Outcome.OpenFellowship;
                try{if(!await hooks.Retry(error.Message,token))return Outcome.OpenFellowship;}
                catch(OperationCanceledException) when(token.IsCancellationRequested){return Outcome.OpenFellowship;}
            }
            finally{receiving=false;}
        }
        return Outcome.OpenFellowship;
    }

    public static string[] RelaunchArguments(bool tray,int? previousPid=null)
    {
        if(previousPid is <=0)throw new ArgumentOutOfRangeException(nameof(previousPid));
        var args=new List<string>();
        if(previousPid.HasValue){args.Add("--wait-for");args.Add(previousPid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        args.Add("--skip-startup-update");if(tray)args.Add("--startup");return args.ToArray();
    }

    public static async Task<Outcome> Run(bool background,Func<bool> tray,Action<Window?> activationTarget,Action? requestForeground=null)
    {
        using var lifetime=new CancellationTokenSource();
        var window=new StartupUpdateWindow{ShowActivated=!background};bool finished=false,handingOff=false,live=true,closed=false,closedByUser=false;
        activationTarget(window);
        window.Closing+=(_,e)=>{if(handingOff&&!finished)e.Cancel=true;};
        window.Closed+=(_,_)=>{closed=true;if(!finished){closedByUser=true;lifetime.Cancel();}};
        window.AllowCancel(()=>{requestForeground?.Invoke();lifetime.Cancel();},"Open Fellowship");
        void Show(){if(!window.IsVisible)window.Show();}
        void Report(string title,string detail,double? percent)
        {
            void Update(){if(live&&!lifetime.IsCancellationRequested)window.Report(title,detail,percent);}
            if(window.Dispatcher.CheckAccess())Update();else window.Dispatcher.BeginInvoke(Update);
        }
        async Task<bool> Retry(string error,CancellationToken token)
        {
            handingOff=false;AppUpdates.SetInstalling(false);
            if(token.IsCancellationRequested)return false;
            Show();var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration=token.Register(()=>decision.TrySetResult(false));
            window.Failure(error,()=>{requestForeground?.Invoke();decision.TrySetResult(true);},()=>{requestForeground?.Invoke();decision.TrySetResult(false);});
            return await decision.Task;
        }
        if(!background)Show();
        var hooks=new Hooks(
            async token=>
            {
                using var check=CancellationTokenSource.CreateLinkedTokenSource(token);check.CancelAfter(TimeSpan.FromSeconds(6));
                return await AppUpdates.Check(check.Token,portableOnly:!Installation.IsInstalled);
            },
            async (release,progress,token)=>
            {
                Show();AppUpdates.SetInstalling(true);
                if(!release.Installer&&await AppUpdates.CachedPortable(release,token:token) is {} cached)return cached;
                return await AppUpdates.Download(release,progress:progress,token:token,keepPortableArchive:!release.Installer);
            },
            (release,package,launchInTray)=>
            {
                handingOff=true;
                if(release.Installer)Installation.StartUpdate(package,release.Version,launchInTray);
                else
                {
                    var start=new ProcessStartInfo(package){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(package)!};
                    foreach(string arg in RelaunchArguments(launchInTray,Environment.ProcessId))start.ArgumentList.Add(arg);
                    _=Process.Start(start)??throw new IOException("Could not start the updated Fellowship.");
                }
                finished=true;live=false;window.Finish();((App)Application.Current).Quit();return Task.CompletedTask;
            },Retry,Report);
        Outcome result=Outcome.OpenFellowship;
        try
        {
            result=await Coordinate(hooks,tray,lifetime.Token);
            if(closedByUser){((App)Application.Current).Quit();return Outcome.Exit;}
            return result;
        }
        finally
        {
            live=false;finished=true;activationTarget(null);
            if(result!=Outcome.HandedOff)AppUpdates.SetInstalling(false);
            if(!closed)window.Finish();
        }
    }
}

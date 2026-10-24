#nullable enable
using Fellowship;
using System.IO;
using System.Net.Http;
using System.Text.Json;

static class StartupUpdateTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static AppUpdates.Release Release()=>new(new Version(99,0,0),"https://github.com/Joshua131313/fellowship-bfme2/releases/download/v99.0.0/Fellowship-99.0.0-win-x64.msi",new string('a',64),true);
    sealed class Attempt
    {
        public int Checks,Downloads,Handoffs,CompletedHandoffs,Retries;
        public bool Tray=true;
        public List<bool> CapturedTray=[];
        public List<(string Title,string Detail,double? Percent)> Reports=[];
        public Func<int,CancellationToken,Task<AppUpdates.Release?>> Read=(_,_)=>Task.FromResult<AppUpdates.Release?>(Release());
        public Func<int,IProgress<(long Received,long? Total)>,CancellationToken,Task<string>> Fetch=(_,progress,_)=>
        {progress.Report((512,1024));return Task.FromResult("verified-package.msi");};
        public Func<int,Task> Launch=_=>Task.CompletedTask;
        public Func<int,Task<bool>> Again=_=>Task.FromResult(false);
        public StartupUpdates.Hooks Hooks()=>new(
            token=>Read(++Checks,token),
            (release,progress,token)=>{Check(release.Version==Release().Version,"Startup downloaded an unexpected release");return Fetch(++Downloads,progress,token);},
            async (release,path,tray)=>
            {
                Check(release.Version==Release().Version&&path=="verified-package.msi","Startup handed off the wrong verified package");
                Handoffs++;CapturedTray.Add(tray);await Launch(Handoffs);CompletedHandoffs++;
            },
            (_,_)=>Again(++Retries),
            (title,detail,percent)=>Reports.Add((title,detail,percent)));
        public Task<StartupUpdates.Outcome> Run(CancellationToken token=default)=>StartupUpdates.Coordinate(Hooks(),()=>Tray,token);
    }
    public static async Task Run()
    {
        await LatestAndOffline();
        await AutomaticAndRetry();
        await CancellationAndLateProgress();
        PackageAndRelaunch();
        Console.WriteLine("PASS automatic startup updates, graceful failures, retry, cancellation, late progress and relaunch intent");
    }
    static async Task LatestAndOffline()
    {
        var current=new Attempt{Read=(_,_)=>Task.FromResult<AppUpdates.Release?>(null)};
        Check(await current.Run()==StartupUpdates.Outcome.OpenFellowship,"Latest version did not open Fellowship");
        Check(current.Checks==1&&current.Downloads==0&&current.Handoffs==0&&current.Retries==0,"Latest version attempted an unnecessary update");
        var offline=new Attempt{Read=(_,_)=>Task.FromException<AppUpdates.Release?>(new HttpRequestException("offline"))};
        Check(await offline.Run()==StartupUpdates.Outcome.OpenFellowship,"Offline startup prevented Fellowship from opening");
        Check(offline.Checks==1&&offline.Downloads==0&&offline.Handoffs==0,"Offline check attempted an update");
    }
    static async Task AutomaticAndRetry()
    {
        var automatic=new Attempt();
        automatic.Fetch=(_,progress,_)=>{automatic.Tray=false;progress.Report((1024,1024));return Task.FromResult("verified-package.msi");};
        Check(await automatic.Run()==StartupUpdates.Outcome.HandedOff,"Available update required another confirmation or opened the old dashboard");
        Check(automatic.Checks==1&&automatic.Downloads==1&&automatic.Handoffs==1&&automatic.CompletedHandoffs==1&&automatic.Retries==0,"Automatic startup did not hand off exactly once");
        Check(automatic.CapturedTray.SequenceEqual(new[]{false}),"Update captured tray intent before a later explicit launch");
        var hidden=new Attempt{Tray=false};hidden.Fetch=(_,_,_)=>{hidden.Tray=true;return Task.FromResult("verified-package.msi");};
        Check(await hidden.Run()==StartupUpdates.Outcome.HandedOff&&hidden.CapturedTray.SequenceEqual(new[]{true}),"Update lost late tray intent");

        IProgress<(long Received,long? Total)>? firstProgress=null;
        var retry=new Attempt{Fetch=(attempt,progress,_)=>{if(attempt==1){firstProgress=progress;return Task.FromException<string>(new InvalidDataException("checksum"));}return Task.FromResult("verified-package.msi");},Again=_=>Task.FromResult(true)};
        Check(await retry.Run()==StartupUpdates.Outcome.HandedOff,"Retry did not complete the verified update");
        Check(retry.Checks==2&&retry.Downloads==2&&retry.Handoffs==1&&retry.CompletedHandoffs==1&&retry.Retries==1,"Download retry reused stale metadata or duplicated installation");
        int reports=retry.Reports.Count;firstProgress!.Report((1024,1024));Check(retry.Reports.Count==reports,"Progress from a failed attempt overwrote the completed update screen");
        var skip=new Attempt{Fetch=(_,_,_)=>Task.FromException<string>(new IOException("download interrupted"))};
        Check(await skip.Run()==StartupUpdates.Outcome.OpenFellowship&&skip.Handoffs==0&&skip.Retries==1,"Continue after a failed download did not preserve the existing app");
        var launchRetry=new Attempt{Launch=attempt=>attempt==1?Task.FromException(new IOException("worker could not start")):Task.CompletedTask,Again=_=>Task.FromResult(true)};
        Check(await launchRetry.Run()==StartupUpdates.Outcome.HandedOff,"Installer handoff failure could not be retried");
        Check(launchRetry.Checks==2&&launchRetry.Downloads==2&&launchRetry.Handoffs==2&&launchRetry.CompletedHandoffs==1&&launchRetry.Retries==1,"Failed handoff retried without a fresh check or handed off successfully twice");
    }
    static async Task CancellationAndLateProgress()
    {
        using var preCancelled=new CancellationTokenSource();preCancelled.Cancel();var skipped=new Attempt();
        Check(await skipped.Run(preCancelled.Token)==StartupUpdates.Outcome.OpenFellowship&&skipped.Checks==0&&skipped.Downloads==0&&skipped.Handoffs==0,"Already canceled startup still checked or installed updates");
        using var checkCancel=new CancellationTokenSource();
        var checking=new Attempt{Read=(_,_)=>{checkCancel.Cancel();return Task.FromResult<AppUpdates.Release?>(Release());}};
        Check(await checking.Run(checkCancel.Token)==StartupUpdates.Outcome.OpenFellowship,"Cancelling the check did not continue with the existing app");
        Check(checking.Downloads==0&&checking.Handoffs==0&&checking.Retries==0,"A late check result continued updating after cancellation");
        using var downloadCancel=new CancellationTokenSource();IProgress<(long Received,long? Total)>? stale=null;
        var downloading=new Attempt{Fetch=(_,progress,_)=>{stale=progress;downloadCancel.Cancel();return Task.FromResult("verified-package.msi");}};
        Check(await downloading.Run(downloadCancel.Token)==StartupUpdates.Outcome.OpenFellowship,"Cancelling the download did not continue with the existing app");
        Check(downloading.Handoffs==0&&downloading.Retries==0,"A late download result started installation after cancellation");
        int before=downloading.Reports.Count;stale!.Report((1024,1024));
        await Task.Delay(40);
        Check(downloading.Reports.Count==before,"Late download progress updated a completed startup screen");
        using var retryCancel=new CancellationTokenSource();
        var retrying=new Attempt{Fetch=(_,_,_)=>Task.FromException<string>(new IOException("download interrupted")),Again=_=>{retryCancel.Cancel();return Task.FromCanceled<bool>(retryCancel.Token);}};
        Check(await retrying.Run(retryCancel.Token)==StartupUpdates.Outcome.OpenFellowship&&retrying.Handoffs==0&&retrying.Checks==1&&retrying.Retries==1,"Cancelling a retry prompt blocked startup or started another update");
    }
    static void PackageAndRelaunch()
    {
        var installed=new Version(AppUpdates.VersionText);var next=new Version(installed.Major,installed.Minor,installed.Build+1);string tag="v"+next.ToString(3),prefix="Fellowship-"+next.ToString(3)+"-win-x64";
        object Asset(string extension,string? url=null,string? digest=null)=>new{name=prefix+extension,browser_download_url=url??"https://github.com/Joshua131313/fellowship-bfme2/releases/download/"+tag+"/"+prefix+extension,digest=digest??"sha256:"+new string('a',64)};
        string Json(params object[] assets)=>JsonSerializer.Serialize(new{draft=false,prerelease=false,tag_name=tag,assets});
        var both=Json(Asset(".msi"),Asset(".zip"));
        Check(AppUpdates.Parse(both) is {Installer:true},"Installed update stopped preferring MSI");
        Check(AppUpdates.Parse(both,portableOnly:true) is {Installer:false},"Portable startup update selected an installer");
        bool noPortable=false;try{AppUpdates.Parse(Json(Asset(".msi")),portableOnly:true);}catch(InvalidDataException){noPortable=true;}
        Check(noPortable,"Portable startup silently converted an installer-only release into an installed app");
        foreach(var invalid in new[]{Json(Asset(".zip","https://example.com/update.zip")),Json(Asset(".zip",digest:"sha256:"+new string('z',64)))})
        {
            bool rejected=false;try{AppUpdates.Parse(invalid,portableOnly:true);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Portable startup accepted an untrusted package URL or malformed digest");
        }
        Check(StartupUpdates.RelaunchArguments(false).SequenceEqual(new[]{"--skip-startup-update"}),"Normal updated launch could enter another startup update");
        Check(StartupUpdates.RelaunchArguments(true).SequenceEqual(new[]{"--skip-startup-update","--startup"}),"Updated startup launch lost its tray intent");
        Check(StartupUpdates.RelaunchArguments(false,123).SequenceEqual(new[]{"--wait-for","123","--skip-startup-update"}),"Portable relaunch did not wait for the old process and bypass one update check");
        Check(StartupUpdates.RelaunchArguments(true,123).SequenceEqual(new[]{"--wait-for","123","--skip-startup-update","--startup"}),"Portable relaunch lost the tray startup intent");
        foreach(int invalid in new[]{0,-1})
        {
            bool rejected=false;try{StartupUpdates.RelaunchArguments(false,invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Portable relaunch accepted an invalid previous process id");
        }
    }
}

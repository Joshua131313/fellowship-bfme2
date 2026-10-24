using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Fellowship;
public static class AppUpdates
{
    public static string VersionText=>Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    static readonly HttpClient Client=CreateClient();
    static HttpClient CreateClient(){var c=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};c.DefaultRequestHeaders.UserAgent.ParseAdd("Fellowship/"+VersionText);return c;}
    public sealed record Release(Version Version,string Url,string Digest,bool Installer=false);
    public static Release? Available { get; private set; }
    public static event Action? AvailabilityChanged;
    public static bool Installing { get; private set; }
    public static event Action? InstallationChanged;
    public static DateTimeOffset LastCheckAttempt {get;private set;}
    public static void DeferAutomaticCheck()=>LastCheckAttempt=DateTimeOffset.UtcNow;
    internal static void SetInstalling(bool value){Installing=value;InstallationChanged?.Invoke();}
    public static Release? Parse(string json,bool portableOnly=false)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        if(!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'),out var version)||version<=new Version(VersionText))return null;
        var prefix=$"Fellowship-{version.ToString(3)}-win-x64";
        foreach(var extension in portableOnly?new[]{".zip"}:new[]{".msi",".zip"})
        {
        var name=prefix+extension;
        foreach(var a in root.GetProperty("assets").EnumerateArray())
        {
            if(a.GetProperty("name").GetString()!=name)continue;
            string url=a.GetProperty("browser_download_url").GetString()??"",digest=a.GetProperty("digest").GetString()??"";
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/Joshua131313/fellowship-bfme2/releases/download/",StringComparison.Ordinal)||!digest.StartsWith("sha256:")||digest.Length!=71||!digest[7..].All(Uri.IsHexDigit))throw new InvalidDataException("The release does not have a verified update package.");
            if(extension==".zip"&&Installation.IsInstalled)throw new InvalidDataException("The latest release has no installer yet. Your installed version is unchanged.");
            return new(version,url,digest[7..],extension==".msi");
        }
        }
        throw new InvalidDataException("The latest release has no Windows update package yet.");
    }
    public static async Task<Release?> Check(CancellationToken cancellation=default,bool portableOnly=false)
    {
        LastCheckAttempt=DateTimeOffset.UtcNow;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/Joshua131313/fellowship-bfme2/releases/latest");
        request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{NoCache=true};
        using var response=await Client.SendAsync(request,timeout.Token);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)throw new IOException("No published release is available yet.");
        response.EnsureSuccessStatusCode();Available=Parse(await response.Content.ReadAsStringAsync(timeout.Token),portableOnly);AvailabilityChanged?.Invoke();return Available;
    }
    public static async Task<string> Download(Release release,string? stagingRoot=null,IProgress<(long Received,long? Total)>? progress=null,CancellationToken token=default,bool keepPortableArchive=false)
    {
        Installation.EnsureUpdateSpace(stagingRoot??Settings.Folder,Installation.Executable,Path.GetTempPath(),Environment.SystemDirectory);
        using var downloadTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);downloadTimeout.CancelAfter(TimeSpan.FromMinutes(10));token=downloadTimeout.Token;
        var folder=Path.Combine(stagingRoot??Path.Combine(Settings.Folder,"versions"),release.Version.ToString(3),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var zip=Path.Combine(folder,release.Installer?"update.msi":"update.zip");
        using(var response=await Client.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,token))
        {
            response.EnsureSuccessStatusCode();using var source=await response.Content.ReadAsStreamAsync(token);using var target=File.Create(zip);
            var buffer=new byte[81920];int read;long total=0;long? expected=response.Content.Headers.ContentLength;var timer=Stopwatch.StartNew();
            if(expected>250_000_000)throw new InvalidDataException("Update package too large");progress?.Report((0,expected));
            while((read=await source.ReadAsync(buffer,token))>0){total+=read;if(total>250_000_000)throw new InvalidDataException("Update package too large");await target.WriteAsync(buffer.AsMemory(0,read),token);if(timer.ElapsedMilliseconds>=100){progress?.Report((total,expected));timer.Restart();}}
            if(expected.HasValue&&expected.Value!=total)throw new IOException("The download was incomplete. Please retry.");progress?.Report((total,expected));
        }
        using(var stream=File.OpenRead(zip))if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(release.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum failed. Your current version has not changed.");
        if(release.Installer)return zip;
        var exe=Path.Combine(folder,"Fellowship.exe");
        using(var archive=ZipFile.OpenRead(zip))
        {
        var entry=archive.GetEntry($"Fellowship-{release.Version.ToString(3)}/Fellowship.exe")??archive.GetEntry("Fellowship.exe")??throw new InvalidDataException("Update executable missing");
        if(entry.Length>350_000_000)throw new InvalidDataException("Update executable too large");
        entry.ExtractToFile(exe);
        }
        if(FileVersionInfo.GetVersionInfo(exe).FileVersion is not {} version||new Version(version)!=new Version(release.Version.Major,release.Version.Minor,release.Version.Build,0))throw new InvalidDataException("Update version mismatch");
        if(!keepPortableArchive)File.Delete(zip);return exe;
    }
    // Reuse only a ZIP matching the currently published release digest and an
    // executable matching that verified archive. Old portable shortcuts can
    // open the cached new app without downloading the same release each time.
    public static async Task<string?> CachedPortable(Release release,string? stagingRoot=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(release.Installer)return null;
        string root=Path.Combine(stagingRoot??Path.Combine(Settings.Folder,"versions"),release.Version.ToString(3));
        bool Linked(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
        if(!Directory.Exists(root)||Linked(root))return null;
        foreach(string folder in Directory.EnumerateDirectories(root).Take(64))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out _)||Linked(folder))continue;
                string zip=Path.Combine(folder,"update.zip"),exe=Path.Combine(folder,"Fellowship.exe");
                if(!File.Exists(zip)||!File.Exists(exe)||Linked(zip)||Linked(exe)||new FileInfo(zip).Length>250_000_000||new FileInfo(exe).Length>350_000_000)continue;
                using(var source=File.OpenRead(zip))
                    if(!Convert.ToHexString(await SHA256.HashDataAsync(source,token)).Equals(release.Digest,StringComparison.OrdinalIgnoreCase))continue;
                if(!Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion,out var version)||version.ToString(3)!=release.Version.ToString(3))continue;
                using var archive=ZipFile.OpenRead(zip);
                var entry=archive.GetEntry($"Fellowship-{release.Version.ToString(3)}/Fellowship.exe")??archive.GetEntry("Fellowship.exe");
                if(entry==null||entry.Length>350_000_000||entry.Length!=new FileInfo(exe).Length)continue;
                using var content=entry.Open();using var executable=File.OpenRead(exe);
                if(!(await SHA256.HashDataAsync(content,token)).SequenceEqual(await SHA256.HashDataAsync(executable,token)))continue;
                return exe;
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException){ }
        }
        return null;
    }
    public static async Task Install(Release release,Func<bool> inRoom,Action<string> status)
    {
        if(inRoom()){status("Leave your room before updating.");return;}
        if(Installing){status("An update is already downloading.");return;}
        Installing=true;InstallationChanged?.Invoke();
        bool handedOff=false;
        using var cancellation=new CancellationTokenSource();
        var window=new UpdateProgressWindow(release.Version.ToString(3));window.AllowCancel(()=>cancellation.Cancel());window.Show();
        try
        {
            status("Downloading update…");
            var progress=new Progress<(long Received,long? Total)>(value=>
            {
                double? percent=value.Total is >0?100.0*value.Received/value.Total.Value:null;
                string text=value.Total is >0?$"{value.Received/1048576.0:0.0} / {value.Total.Value/1048576.0:0.0} MB · {percent:0}%":$"{value.Received/1048576.0:0.0} MB downloaded";
                bool complete=value.Total is >0&&value.Received==value.Total;
                window.Report(complete?"Verifying download…":"Downloading update…",complete?"Checking the package before installation.":text,complete?null:percent);status(text);
            });
            var downloaded=await Download(release,progress:progress,token:cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            window.Report("Preparing installation…","Fellowship will close. The installer will keep you informed.");
            if(inRoom()){status("Leave your room, then retry the update.");window.Finish();return;}
            if(release.Installer)Installation.StartUpdate(downloaded,release.Version);
            else
            {
                var start=new ProcessStartInfo(downloaded){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(downloaded)!};
                foreach(string arg in StartupUpdates.RelaunchArguments(false,Environment.ProcessId))start.ArgumentList.Add(arg);
                _=Process.Start(start)??throw new IOException("Could not start the updated Fellowship.");
            }
            handedOff=true;window.Finish();((App)Application.Current).Quit();
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){status("Update cancelled. Your current version is unchanged.");window.Finish();}
        catch(Exception e){status("Update failed: "+e.Message);window.Failure(e.Message);}
        finally{if(!handedOff){Installing=false;InstallationChanged?.Invoke();}}
    }
    public static FrameworkElement Controls(Func<bool> inRoom)
    {
        var panel=new StackPanel{Margin=new(0,22,0,0)};panel.Children.Add(UI.LocalizedText("FELLOWSHIP "+VersionText,11,UI.Muted,true));
        var status=UI.LocalizedText("Updates from GitHub",11,UI.Muted);status.Margin=new(0,8,0,12);panel.Children.Add(status);
        Release? available=Available;
        var install=UI.AsyncButton("Download & restart",async()=>
        {
            if(available==null)return;
            await Install(available,inRoom,text=>L.Bind(status,System.Windows.Controls.TextBlock.TextProperty,text));
        },true);install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;
        install.Margin=new(0,8,0,0);
        UI.Require(install,()=>available!=null&&!inRoom(),()=>"Leave your room before downloading and restarting.");
        void UpdateAvailable(){panel.Visibility=Installing?Visibility.Collapsed:Visibility.Visible;available=Available;install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;if(available!=null)L.Bind(status,System.Windows.Controls.TextBlock.TextProperty,$"Version {available.Version.ToString(3)} is ready to download.");}
        panel.Loaded+=(_,_)=>{AvailabilityChanged+=UpdateAvailable;InstallationChanged+=UpdateAvailable;UpdateAvailable();};panel.Unloaded+=(_,_)=>{AvailabilityChanged-=UpdateAvailable;InstallationChanged-=UpdateAvailable;};
        panel.Children.Add(UI.AsyncButton("Check for updates",async()=>
        {
            try{L.Bind(status,System.Windows.Controls.TextBlock.TextProperty,"Checking GitHub…");available=await Check();install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;L.Bind(status,System.Windows.Controls.TextBlock.TextProperty,available==null?"You have the latest published version.":$"Version {available.Version} is available. Restarting closes Fellowship windows.");}
            catch(Exception e){L.Bind(status,System.Windows.Controls.TextBlock.TextProperty,"Could not check updates: "+e.Message);}
        }));panel.Children.Add(install);foreach(var button in panel.Children.OfType<Button>())button.HorizontalAlignment=HorizontalAlignment.Left;return panel;
    }
}

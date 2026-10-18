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
    public static Release? Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        if(!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'),out var version)||version<=new Version(VersionText))return null;
        var prefix=$"Fellowship-{version.ToString(3)}-win-x64";
        foreach(var extension in new[]{".msi",".zip"})
        {
        var name=prefix+extension;
        foreach(var a in root.GetProperty("assets").EnumerateArray())
        {
            if(a.GetProperty("name").GetString()!=name)continue;
            string url=a.GetProperty("browser_download_url").GetString()??"",digest=a.GetProperty("digest").GetString()??"";
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/Joshua131313/fellowship-bfme2/releases/download/",StringComparison.Ordinal)||!digest.StartsWith("sha256:")||digest.Length!=71)throw new InvalidDataException("The release does not have a verified update package.");
            if(extension==".zip"&&Installation.IsInstalled)throw new InvalidDataException("The latest release has no installer yet. Your installed version is unchanged.");
            return new(version,url,digest[7..],extension==".msi");
        }
        }
        throw new InvalidDataException("The latest release has no Windows update package yet.");
    }
    public static async Task<Release?> Check()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response=await Client.GetAsync("https://api.github.com/repos/Joshua131313/fellowship-bfme2/releases/latest",timeout.Token);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)throw new IOException("No published release is available yet.");
        response.EnsureSuccessStatusCode();Available=Parse(await response.Content.ReadAsStringAsync(timeout.Token));AvailabilityChanged?.Invoke();return Available;
    }
    public static async Task<string> Download(Release release,string? stagingRoot=null)
    {
        var folder=Path.Combine(stagingRoot??Path.Combine(Settings.Folder,"versions"),release.Version.ToString(3),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var zip=Path.Combine(folder,release.Installer?"update.msi":"update.zip");
        using(var response=await Client.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();using var source=await response.Content.ReadAsStreamAsync();using var target=File.Create(zip);
            var buffer=new byte[81920];int read;long total=0;
            while((read=await source.ReadAsync(buffer))>0){total+=read;if(total>250_000_000)throw new InvalidDataException("Update package too large");await target.WriteAsync(buffer.AsMemory(0,read));}
        }
        using(var stream=File.OpenRead(zip))if(!Convert.ToHexString(await SHA256.HashDataAsync(stream)).Equals(release.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum failed. Your current version has not changed.");
        if(release.Installer)return zip;
        var exe=Path.Combine(folder,"Fellowship.exe");
        using(var archive=ZipFile.OpenRead(zip))
        {
        var entry=archive.GetEntry($"Fellowship-{release.Version.ToString(3)}/Fellowship.exe")??archive.GetEntry("Fellowship.exe")??throw new InvalidDataException("Update executable missing");
        if(entry.Length>350_000_000)throw new InvalidDataException("Update executable too large");
        entry.ExtractToFile(exe);
        }
        if(FileVersionInfo.GetVersionInfo(exe).FileVersion is not {} version||new Version(version)!=new Version(release.Version.Major,release.Version.Minor,release.Version.Build,0))throw new InvalidDataException("Update version mismatch");
        File.Delete(zip);return exe;
    }
    public static async Task Install(Release release,Func<bool> inRoom,Action<string> status)
    {
        if(inRoom()){status("Leave your room before updating.");return;}
        if(Installing){status("An update is already downloading.");return;}
        Installing=true;
        try
        {
            status("Downloading and verifying…");var downloaded=await Download(release);
            if(inRoom()){status("Leave your room, then retry the update.");return;}
            if(release.Installer)Installation.StartUpdate(downloaded);
            else
            {
                var start=new ProcessStartInfo(downloaded){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(downloaded)!};start.ArgumentList.Add("--wait-for");start.ArgumentList.Add(Environment.ProcessId.ToString());Process.Start(start);
            }
            ((App)Application.Current).Quit();
        }
        catch(Exception e){status("Update failed: "+e.Message);}
        finally{Installing=false;}
    }
    public static FrameworkElement Controls(Func<bool> inRoom)
    {
        var panel=new StackPanel{Margin=new(0,22,0,0)};panel.Children.Add(UI.Text("FELLOWSHIP "+VersionText,11,UI.Muted,true));
        var status=UI.Text("Updates from GitHub",11,UI.Muted);status.Margin=new(0,8,0,8);panel.Children.Add(status);
        Release? available=Available;
        var install=UI.AsyncButton("Download & restart",async()=>
        {
            if(available==null)return;
            await Install(available,inRoom,text=>status.Text=text);
        },true);install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;
        install.Margin=new(0,8,0,0);
        UI.Require(install,()=>available!=null&&!inRoom(),()=>"Leave your room before downloading and restarting.");
        void UpdateAvailable(){available=Available;install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;if(available!=null)status.Text=$"Version {available.Version.ToString(3)} is ready to download.";}
        panel.Loaded+=(_,_)=>{AvailabilityChanged+=UpdateAvailable;UpdateAvailable();};panel.Unloaded+=(_,_)=>AvailabilityChanged-=UpdateAvailable;
        panel.Children.Add(UI.AsyncButton("Check for updates",async()=>
        {
            try{status.Text="Checking GitHub…";available=await Check();install.Visibility=available==null?Visibility.Collapsed:Visibility.Visible;status.Text=available==null?"You have the latest published version.":$"Version {available.Version} is available. Restarting closes Fellowship windows.";}
            catch(Exception e){status.Text="Could not check updates: "+e.Message;}
        }));panel.Children.Add(install);return panel;
    }
}

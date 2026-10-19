using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
namespace Fellowship;
public static class InstallerRecovery
{
    public const string UpgradeCode="{D124E6E7-6C01-44D3-A5AD-8077BF46EE40}";
    public sealed record Product(string Code,Version Version,string CachedPackage);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiEnumRelatedProducts(string upgrade,uint reserved,uint index,StringBuilder product);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiGetProductInfo(string product,string property,StringBuilder value,ref uint length);
    static string Info(string code,string property)
    {
        var value=new StringBuilder(4096);uint length=4096;var result=MsiGetProductInfo(code,property,value,ref length);
        if(result!=0)throw new IOException($"Cannot read installed package information ({result}).");return value.ToString();
    }
    public static List<Product> Registered()
    {
        var products=new List<Product>();
        for(uint i=0;;i++)
        {
            var code=new StringBuilder(39);uint result=MsiEnumRelatedProducts(UpgradeCode,0,i,code);
            if(result==259)break;if(result!=0)throw new IOException($"Cannot inspect installed Fellowship versions ({result}).");
            products.Add(new(code.ToString(),Version.Parse(Info(code.ToString(),"VersionString")),Info(code.ToString(),"LocalPackage")));
        }
        return products;
    }
    public static async Task<AppUpdates.Release> Release(Version version)
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Fellowship-Repair/"+AppUpdates.VersionText);
        string json=await client.GetStringAsync("https://api.github.com/repos/Joshua131313/fellowship-bfme2/releases/tags/v"+version.ToString(3));
        return ParseRelease(json,version);
    }
    public static AppUpdates.Release ParseRelease(string json,Version version)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var tag="v"+version.ToString(3);var name=$"Fellowship-{version.ToString(3)}-win-x64.msi";
        if(root.GetProperty("tag_name").GetString()!=tag||root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Recovery release identity mismatch.");
        foreach(var asset in root.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            var url=asset.GetProperty("browser_download_url").GetString();var digest=asset.GetProperty("digest").GetString()??"";
            if(url!=$"https://github.com/Joshua131313/fellowship-bfme2/releases/download/{tag}/{name}"||!digest.StartsWith("sha256:")||digest.Length!=71||!digest[7..].All(Uri.IsHexDigit))throw new InvalidDataException("Recovery package is not verified.");
            return new(version,url,digest[7..],true);
        }
        throw new IOException("The original installer is not available in the official release.");
    }
    public static void VerifyPackage(string package,Product expected)
    {
        object? installer=null,db=null;
        try
        {
            installer=Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer")!);db=((dynamic)installer!).OpenDatabase(package,0);
            string Read(string property)
            {
                object? view=null,record=null;
                try{view=((dynamic)db).OpenView("SELECT `Value` FROM `Property` WHERE `Property`='"+property+"'");((dynamic)view).Execute();record=((dynamic)view).Fetch();return record==null?"":(string)((dynamic)record).StringData[1];}
                finally{if(record!=null)Marshal.ReleaseComObject(record);if(view!=null)Marshal.ReleaseComObject(view);}
            }
            if(!Read("ProductCode").Equals(expected.Code,StringComparison.OrdinalIgnoreCase)||!Read("UpgradeCode").Equals(UpgradeCode,StringComparison.OrdinalIgnoreCase)||Read("ProductVersion")!=expected.Version.ToString(3))throw new InvalidDataException("Recovery installer does not match the registered Fellowship product.");
        }
        finally{if(db!=null)Marshal.ReleaseComObject(db);if(installer!=null)Marshal.ReleaseComObject(installer);}
    }
    public static bool MissingSource(int code,string log)=>code==1612||(code==1603&&log.Contains("1612")&&log.Contains("RemoveExistingProducts"));
    public static async Task Repair(Action<string,string> report,bool force=false)
    {
        foreach(var product in Registered())
        {
            if(!force&&File.Exists(product.CachedPackage))continue;
            report("Repairing Windows Installer…",$"Downloading the original Fellowship {product.Version.ToString(3)} installer.");
            var release=await Release(product.Version);
            // This durable source is outside disposable update staging and is never cleaned.
            var package=await AppUpdates.Download(release,Path.Combine(Settings.Folder,"installer-recovery"));
            VerifyPackage(package,product);
            report("Restoring installer files…","Your preferences and friends will be preserved.");
            var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false,CreateNoWindow=true};
            var log=Path.Combine(Path.GetDirectoryName(package)!,"repair.log");
            foreach(var arg in new[]{"/fv",package,"/qn","/norestart","/L*v",log})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("Cannot start installer repair.");await process.WaitForExitAsync();
            if(process.ExitCode is not (0 or 3010))throw new IOException($"Installer repair returned {process.ExitCode}. Details: {log}");
        }
    }
}

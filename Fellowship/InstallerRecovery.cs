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
    public sealed record Product(string Code,Version Version,string CachedPackage,uint Context=2);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiEnumRelatedProducts(string upgrade,uint reserved,uint index,StringBuilder product);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiGetProductInfo(string product,string property,StringBuilder value,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiSourceListSetInfo(string product,string? sid,uint context,uint options,string property,string value);
    public static string KeepSource(string package)
    {
        using var stream=File.OpenRead(package);
        var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        var folder=Path.Combine(Settings.Folder,"installer-packages",digest);Directory.CreateDirectory(folder);
        var destination=Path.Combine(folder,Path.GetFileName(package));
        bool matches=false;
        if(File.Exists(destination)){using var cached=File.OpenRead(destination);matches=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cached))==digest;}
        if(!matches)File.Copy(package,destination,true);
        return NativePlacement.PhysicalPath(destination);
    }
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
            products.Add(new(code.ToString(),Version.Parse(Info(code.ToString(),"VersionString")),Info(code.ToString(),"LocalPackage"),Info(code.ToString(),"AssignmentType")=="1"?4u:2u));
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
    public static void RegisterSource(string package,Product product)
    {
        package=NativePlacement.PhysicalPath(package);
        VerifyPackage(package,product);
        // Register the verified original source; do not run the old product's
        // repair actions (which can fail or overwrite a newer installation).
        var source=Path.GetDirectoryName(package)!+Path.DirectorySeparatorChar;
        var result=MsiSourceListSetInfo(product.Code,null,product.Context,0,"PackageName",Path.GetFileName(package));
        if(result==0)result=MsiSourceListSetInfo(product.Code,null,product.Context,1,"LastUsedSource",source);
        var log=Path.Combine(Path.GetDirectoryName(package)!,"source-recovery.log");
        File.WriteAllText(log,$"Product: {product.Code}\nVersion: {product.Version}\nContext: {product.Context}\nVerified source: {package}\nSource registration result: {result}\n");
        if(result!=0)throw new IOException($"Installer source recovery returned {result}. Details: {log}");
    }
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
            RegisterSource(package,product);
        }
    }
}

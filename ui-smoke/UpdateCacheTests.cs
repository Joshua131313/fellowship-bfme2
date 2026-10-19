using Fellowship;
using System.IO;
static class UpdateCacheTests
{
    public static void Run()
    {
        string root=Path.GetFullPath("dist/cache-tests/"+Guid.NewGuid().ToString("N"));
        string Stage(string version){var p=Path.Combine(root,version,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
        var old=Stage("0.1.40");var current=Stage("0.1.45");var future=Stage("0.1.46");var running=Stage("0.1.39");
        foreach(var p in new[]{old,current,future,running}){File.WriteAllText(Path.Combine(p,"update.msi"),"package");File.WriteAllText(Path.Combine(p,"installer-worker.exe"),"worker");}
        File.WriteAllText(Path.Combine(old,"install.log"),"diagnostic");File.WriteAllText(Path.Combine(old,"settings.json"),"preserve");
        string custom=Path.Combine(root,"0.1.40","custom");Directory.CreateDirectory(custom);File.WriteAllText(Path.Combine(custom,"update.msi"),"preserve");
        long reclaimed=UpdateCache.Clean(root,new Version(0,1,45),Path.Combine(running,"installer-worker.exe"));
        if(reclaimed!=26||File.Exists(Path.Combine(old,"update.msi"))||Directory.Exists(current)||!File.Exists(Path.Combine(old,"install.log"))||!File.Exists(Path.Combine(old,"settings.json"))||!File.Exists(Path.Combine(future,"update.msi"))||!File.Exists(Path.Combine(running,"update.msi"))||!File.Exists(Path.Combine(custom,"update.msi")))throw new Exception("Update cache cleanup scope failed");
        if(UpdateCache.Clean(root,new Version(0,1,45),Path.Combine(running,"installer-worker.exe"))!=0)throw new Exception("Cleanup not idempotent");
        Console.WriteLine("PASS old update cleanup preserves logs, unknown files, future downloads and running worker");
    }
}

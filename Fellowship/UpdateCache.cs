using System.IO;
namespace Fellowship;

public static class UpdateCache
{
    // Only updater-owned payloads in version/GUID staging folders are eligible.
    // Logs, unknown files, newer downloads and running executable folders survive.
    public static long Clean(string root,Version installedVersion,string runningExecutable)
    {
        long reclaimed=0;
        try
        {
            root=Path.GetFullPath(root);
            if(!Directory.Exists(root)||Linked(root))return 0;
            string runningFolder=Path.GetDirectoryName(Path.GetFullPath(runningExecutable))!;
            foreach(string versionFolder in Directory.EnumerateDirectories(root))
            {
                if(Linked(versionFolder)||!Version.TryParse(Path.GetFileName(versionFolder),out var version)||version>installedVersion)continue;
                foreach(string staging in Directory.EnumerateDirectories(versionFolder))
                {
                    if(Linked(staging)||!Guid.TryParseExact(Path.GetFileName(staging),"N",out _)||string.Equals(Path.GetFullPath(staging),runningFolder,StringComparison.OrdinalIgnoreCase))continue;
                    foreach(string name in new[]{"update.msi","update.zip","installer-worker.exe","Fellowship.exe"})
                    {
                        string file=Path.Combine(staging,name);
                        try{if(File.Exists(file)&&!Linked(file)){long size=new FileInfo(file).Length;File.Delete(file);reclaimed+=size;}}
                        catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                    RemoveEmpty(staging);
                }
                RemoveEmpty(versionFolder);
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        return reclaimed;
    }
    static bool Linked(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    static void RemoveEmpty(string path)
    {try{if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path,false);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public static void CleanInstalledCache()
    {
        if(!Installation.IsInstalled)return;
        Clean(Path.Combine(Settings.Folder,"versions"),new Version(AppUpdates.VersionText),Environment.ProcessPath!);
    }
}

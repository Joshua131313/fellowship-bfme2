#nullable enable
using Fellowship;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

static class PortableUpdateCacheTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static readonly byte[] Executable=File.ReadAllBytes(typeof(AppUpdates).Assembly.Location);
    static readonly Version Current=new(AppUpdates.VersionText);
    sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"Fellowship-portable-cache-tests-"+Guid.NewGuid().ToString("N"));
        public (AppUpdates.Release Release,string Folder,string Exe,string Zip) Stage(Version? version=null,string? directory=null,string? entryPath=null,byte[]? archiveExe=null,byte[]? extractedExe=null)
        {
            version??=Current;string folder=Path.Combine(Root,version.ToString(3),directory??Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string zip=Path.Combine(folder,"update.zip"),exe=Path.Combine(folder,"Fellowship.exe");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            using(var content=archive.CreateEntry(entryPath??$"Fellowship-{version.ToString(3)}/Fellowship.exe",CompressionLevel.Fastest).Open())
                content.Write(archiveExe??Executable);
            File.WriteAllBytes(exe,extractedExe??Executable);
            var release=new AppUpdates.Release(version,$"https://github.com/Joshua131313/fellowship-bfme2/releases/download/v{version.ToString(3)}/Fellowship-{version.ToString(3)}-win-x64.zip",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))));
            return(release,folder,exe,zip);
        }
        public void Dispose()
        {
            string absolute=Path.GetFullPath(Root),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(!string.Equals(Path.GetDirectoryName(absolute),parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("Fellowship-portable-cache-tests-",StringComparison.Ordinal))throw new Exception("Portable test cleanup escaped its temporary directory");
            if(Directory.Exists(absolute))Directory.Delete(absolute,true);
        }
    }
    public static async Task Run()
    {
        await ValidAndCancellation();
        await TamperedAndIncomplete();
        await IdentityAndLayout();
        Console.WriteLine("PASS verified portable update cache reuse, tamper rejection, version/layout/staging scope and cancellation (no executable launch)");
    }
    static async Task ValidAndCancellation()
    {
        using var fixture=new Fixture();var cache=fixture.Stage();
        Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==cache.Exe,"A verified portable release was downloaded again instead of reusing its cache");
        Check(await AppUpdates.CachedPortable(cache.Release with{Digest=cache.Release.Digest.ToLowerInvariant()},fixture.Root)==cache.Exe,"Portable release digest casing prevented reuse");
        Check(await AppUpdates.CachedPortable(cache.Release with{Installer=true},fixture.Root)==null,"An installer release reused a portable executable");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        bool rejected=false;try{await AppUpdates.CachedPortable(cache.Release,fixture.Root,cancelled.Token);}catch(OperationCanceledException){rejected=true;}
        Check(rejected,"Portable cache lookup ignored cancellation");
        Check(File.ReadAllBytes(cache.Exe).SequenceEqual(Executable),"Portable cache lookup changed the executable");
    }
    static async Task TamperedAndIncomplete()
    {
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();var tampered=File.ReadAllBytes(cache.Zip);tampered[^1]^=1;File.WriteAllBytes(cache.Zip,tampered);
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache accepted a ZIP that does not match GitHub's release digest");
            Check(File.ReadAllBytes(cache.Zip).SequenceEqual(tampered),"A rejected portable ZIP was silently modified");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();var tampered=Executable.ToArray();tampered[^1]^=1;File.WriteAllBytes(cache.Exe,tampered);
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache accepted an executable different from the verified ZIP entry");
            Check(File.ReadAllBytes(cache.Exe).SequenceEqual(tampered),"A rejected portable executable was silently modified");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();File.Delete(cache.Zip);
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Legacy portable staging without its verified ZIP was trusted");
            Check(File.Exists(cache.Exe),"An incomplete portable cache removed the user's executable");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();File.Delete(cache.Exe);
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache returned a missing executable");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();File.WriteAllBytes(cache.Zip,[1,2,3,4]);
            var release=cache.Release with{Digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cache.Zip)))};
            Check(await AppUpdates.CachedPortable(release,fixture.Root)==null,"A checksum-valid malformed archive escaped cache validation");
        }
    }
    static async Task IdentityAndLayout()
    {
        using(var fixture=new Fixture())
        {
            var next=new Version(Current.Major,Current.Minor,Current.Build+1);var cache=fixture.Stage(next);
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache accepted an executable whose file version differs from the release");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage(entryPath:"unexpected/Fellowship.exe");
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache accepted an unrelated archive layout");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage(entryPath:"Fellowship.exe");
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==cache.Exe,"Supported root-level portable archive was rejected");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage(directory:"custom");
            Check(await AppUpdates.CachedPortable(cache.Release,fixture.Root)==null,"Portable cache trusted a non-updater-owned directory");
            Check(File.Exists(cache.Zip)&&File.Exists(cache.Exe),"Portable cache altered an unrelated staging directory");
        }
        using(var fixture=new Fixture())
        {
            var cache=fixture.Stage();var next=cache.Release with{Version=new Version(Current.Major,Current.Minor,Current.Build+1)};
            Check(await AppUpdates.CachedPortable(next,fixture.Root)==null,"Portable cache searched a different release's version folder");
        }
        using(var fixture=new Fixture())
        {
            var first=fixture.Stage();var bad=File.ReadAllBytes(first.Exe);bad[^1]^=1;File.WriteAllBytes(first.Exe,bad);
            var second=fixture.Stage();
            // Reuse the same archive bytes so both folders correspond to the one
            // published digest; the first candidate must not block a later match.
            File.Copy(second.Zip,first.Zip,true);
            Check(await AppUpdates.CachedPortable(second.Release,fixture.Root)==second.Exe,"A damaged staged candidate prevented a later verified portable cache from opening");
        }
    }
}

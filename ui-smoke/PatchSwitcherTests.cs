#nullable enable
using Fellowship;
using System.IO;
using System.Reflection;

static class PatchSwitcherTests
{
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Static;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object? Call(string name,params object?[] values)
    {
        try{return typeof(PatchSwitcher).GetMethod(name,Hidden)!.Invoke(null,values);}
        catch(TargetInvocationException error) when(error.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static string[] Args(string patch,string? complete=null,string? target=null)=>(string[])Call("Arguments",patch,complete,target)!;
    static void Reject(Action action,string expected)
    {
        try{action();}catch(InvalidOperationException error){Check(error.Message.Contains(expected),"Unexpected patch-switch guard: "+error.Message);return;}
        throw new Exception("Patch switch accepted an unsafe or unsupported target");
    }
    static void Big(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,[66,73,71,70,16,0,0,0,0,0,0,0,0,0,0,16]);
    }
    public static void Run()
    {
        Check(Args("1.00").SequenceEqual(new[]{"-switch","-1.00","-force"}),"1.00 CLI arguments differ from Ecth's supported syntax");
        Check(Args("1.06").SequenceEqual(new[]{"-switch","-1.06","-force"}),"1.06 CLI arguments differ from Ecth's supported syntax");
        string beta=GamePatches.Normalize("1.09v3.2 ß16");
        Check(Args("1.09u4 beta16",beta,beta).SequenceEqual(new[]{"-switch","-latest","-force"}),"Exact installed latest beta was not mapped to -latest");
        Check(Args("1.09v3.1","1.09v3.1","1.09v3.1")[1]=="-latest","Exact installed latest stable patch was not mapped to -latest");
        Reject(()=>Args("1.09v3.1",beta,beta),"cannot switch directly");
        Reject(()=>Args("1.09v3.2 ß15",beta,beta),"cannot switch directly");
        Reject(()=>Args("1.09v3.2 ß16",beta,"1.09u4 ß17"),"latest patch could not be verified");
        Reject(()=>Args("1.09v3.2 ß16",beta,null),"latest patch could not be verified");
        Reject(()=>Args("1.00 -force -latest",beta,beta),"cannot switch directly");

        string fixture=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-switcher-tests-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            string root=Path.Combine(fixture,"BFME2 Ecth's Patch Switcher"),game=Path.Combine(fixture,"game"),executable=Path.Combine(game,"lotrbfme2.exe");
            Directory.CreateDirectory(game);File.WriteAllBytes(executable,[]);
            foreach(string relative in new[]{"100","106","109","109v2",@"109v3\release",@"109v301\release",@"109v302\ß16"})Directory.CreateDirectory(Path.Combine(root,relative));
            Big(Path.Combine(root,"100","Maps.big"));Big(Path.Combine(root,"106","Maps.big"));
            Big(Path.Combine(root,@"109v302\ß16\##!__BT2DC-v1.09v3.02.big"));
            string? complete=GamePatches.LatestInstalled(executable,root),target=GamePatches.LatestSwitcherTarget(executable,root);
            Check(complete==beta&&target==beta,"Synthetic complete latest beta disagreed with the switcher's folder ordering");
            Check(Args(beta,complete,target)[1]=="-latest","Complete exact latest target was incorrectly denied");
            Directory.CreateDirectory(Path.Combine(root,@"109u4\ß17"));
            complete=GamePatches.LatestInstalled(executable,root);target=GamePatches.LatestSwitcherTarget(executable,root);
            Check(complete==beta&&target=="1.09u4 ß17","Incomplete newer folder did not differ from the complete installed target");
            Reject(()=>Args(beta,complete,target),"latest patch could not be verified");
            Directory.CreateDirectory(Path.Combine(root,@"109u4\ß999999"));
            Check(GamePatches.LatestSwitcherTarget(executable,root)==null,"Uncatalogued beta folder produced a guessed -latest target");
            Reject(()=>Args(beta,complete,GamePatches.LatestSwitcherTarget(executable,root)),"latest patch could not be verified");

            string expected=Path.Combine(root,"data","BFME2Switcher.exe"),impostor=Path.Combine(fixture,"other","BFME2Switcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);Directory.CreateDirectory(Path.GetDirectoryName(impostor)!);
            File.WriteAllBytes(expected,[]);File.WriteAllBytes(impostor,[]);
            Check((bool)Call("SameExecutable",expected,expected.ToUpperInvariant())!,"Same physical switcher path did not match case-insensitively");
            Check(!(bool)Call("SameExecutable",expected,impostor)!,"A different executable with the switcher name matched the installed process");
            string installed=GameInstallation.Find();
            if(File.Exists(installed))Check((bool)Call("SameExecutable",installed,NativePlacement.PhysicalPath(installed))!,"8.3 registered game path did not match its physical installation path");

            string overrideFile=Path.Combine(root,"custom_bfme2_path.ini"),otherGame=Path.Combine(fixture,"other-game");
            Directory.CreateDirectory(otherGame);string otherExecutable=Path.Combine(otherGame,"lotrbfme2.exe");File.WriteAllBytes(otherExecutable,[]);
            Check((string?)Call("GameTarget",root,executable)==executable,"Absent switcher override did not use the registered installation");
            File.WriteAllText(overrideFile,game);
            Check((bool)Call("SameExecutable",executable,(string?)Call("GameTarget",root,otherExecutable)??"")!,"Valid switcher override did not take precedence over the registry path");
            File.WriteAllText(overrideFile,otherGame);
            Check(!(bool)Call("SameExecutable",executable,(string?)Call("GameTarget",root,executable)??"")!,"Different switcher override fell back to the matching registry installation");
            foreach(string invalid in new[]{"",@"relative\game","\""+game+"\"",game+"\n",Path.Combine(fixture,"missing-game"),executable})
            {
                File.WriteAllText(overrideFile,invalid);
                Check(Call("GameTarget",root,executable)==null,"Malformed switcher override fell back to the registry: "+invalid);
            }
            File.WriteAllText(overrideFile,game);
            using(var locked=File.Open(overrideFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check(Call("GameTarget",root,executable)==null,"Unreadable switcher override fell back to the registry");
            File.Delete(overrideFile);
            Check((string?)Call("GameTarget",root,executable)==executable,"Removing the override did not restore registry fallback");
        }
        finally
        {
            string tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fixture)&&fixture.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase))Directory.Delete(fixture,true);
        }

        // Holding the service gate guarantees this call cannot reach process
        // discovery or execution, even if the user's switcher is open.
        var gate=(SemaphoreSlim)typeof(PatchSwitcher).GetField("gate",Hidden)!.GetValue(null)!;
        Check(!PatchSwitcher.IsSwitching,"Isolated switcher tests began with an active patch switch");
        Check(gate.Wait(0),"Patch switching was already active during the isolated guard test");
        try
        {
            Check(PatchSwitcher.IsSwitching,"Held patch-switch gate did not block launch/sync eligibility");
            Reject(()=>PatchSwitcher.Switch("unused","1.00").GetAwaiter().GetResult(),"already in progress");
        }
        finally{gate.Release();}
        Check(!PatchSwitcher.IsSwitching,"Released patch-switch gate left launch/sync blocked without a worker");
        Console.WriteLine("PASS native switcher arguments, exact-latest and old-beta safety, incomplete/unknown beta folders, physical process identity and serialized preflight without executing a patch switch");
    }
}

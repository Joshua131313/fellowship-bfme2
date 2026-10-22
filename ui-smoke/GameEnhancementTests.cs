#nullable enable
using Fellowship;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

static class GameEnhancementTests
{
    static readonly string[] Names=["d3d9.dll","dinput8.dll"];
    static readonly string[] Hashes=["73BA5238B1A530139EBEEE3DEE2B5E64C9AA6D9F43100D9307B0541D50BDC9EF","09C63CFD93D2A17B8C5D0D3652D390347B2B1045112BF5B7FCF5A15D34615551"];
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static byte[] Payload(string name)
    {
        using var stream=typeof(GameEnhancements).Assembly.GetManifestResourceStream("Fellowship.GameEnhancements."+name)??throw new Exception("Missing bundled paired enhancement: "+name);
        using var copy=new MemoryStream();stream.CopyTo(copy);return copy.ToArray();
    }
    static void Apply(string executable,bool enabled,Func<bool>? running=null,Action<string>? checkpoint=null)
    {
        var method=typeof(GameEnhancements).GetMethod("ApplyCore",BindingFlags.NonPublic|BindingFlags.Static)??throw new Exception("Missing isolated enhancement transaction test entry point");
        try{method.Invoke(null,[executable,enabled,running??(()=>false),checkpoint]);}
        catch(TargetInvocationException error) when(error.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Reject(Action action,string message)
    {
        try{action();}
        catch(Exception error) when(error is IOException or InvalidOperationException or UnauthorizedAccessException){return;}
        throw new Exception(message);
    }
    sealed class GameFixture:IDisposable
    {
        public string Root {get;}=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-enhancement-tests-"+Guid.NewGuid().ToString("N")));
        public string Executable=>Path.Combine(Root,"lotrbfme2.exe");
        public GameFixture()
        {
            Directory.CreateDirectory(Root);File.WriteAllBytes(Executable,[]);File.WriteAllBytes(Path.Combine(Root,"game.dat"),[77,90,0,0]);
        }
        public string FilePath(string name)=>Path.Combine(Root,name);
        public void Write(string name,byte[] bytes)=>File.WriteAllBytes(FilePath(name),bytes);
        public void AssertFiles(byte[]? first,byte[]? second)
        {
            byte[]?[] expected=[first,second];
            for(int i=0;i<Names.Length;i++)
            {
                string path=FilePath(Names[i]);
                Check(expected[i]==null?!File.Exists(path):File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(expected[i]!),"Enhancement pair has an unexpected active file: "+Names[i]);
            }
        }
        public void Dispose()
        {
            // Cleanup is confined to this exact, uniquely created test fixture.
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            Check(Root.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(Root).StartsWith("Fellowship-enhancement-tests-",StringComparison.Ordinal),"Unsafe enhancement fixture cleanup path");
            if(Directory.Exists(Root))Directory.Delete(Root,true);
        }
    }
    public static void Run()
    {
        var payloads=Names.Select(Payload).ToArray();
        for(int i=0;i<Names.Length;i++)Check(Convert.ToHexString(SHA256.HashData(payloads[i]))==Hashes[i],"Bundled enhancement differs from the supplied DLL: "+Names[i]);
        Pair(payloads);Originals(payloads);Partial(payloads);ForeignChanges(payloads);Guards(payloads);Failures(payloads);Recovery(payloads);CorruptRecovery(payloads);LockedFile();
        Console.WriteLine("PASS paired game enhancement payload hashes, exact filenames, enable/disable, original restoration, partial installations, foreign-file protection, transactional rollback/recovery and running-game/path guards (DLLs never executed)");
    }
    static void Pair(byte[][] payloads)
    {
        using var game=new GameFixture();
        Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Disabled,"Empty game installation did not show enhancements disabled");
        Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);
        Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Enabled,"Both verified enhancement DLLs did not show enabled");
        Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);
        Apply(game.Executable,false);game.AssertFiles(null,null);
        Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Disabled,"Removing both enhancement DLLs left an enabled state");
        Apply(game.Executable,false);game.AssertFiles(null,null);
    }
    static void Originals(byte[][] payloads)
    {
        foreach(int count in new[]{1,2})
        {
            using var game=new GameFixture();byte[] originalA=[1,7,13,21],originalB=[2,8,14,22];
            game.Write(Names[0],originalA);if(count==2)game.Write(Names[1],originalB);
            Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);
            Apply(game.Executable,true); // Repeated enabling must not replace the original backup.
            Apply(game.Executable,false);game.AssertFiles(originalA,count==2?originalB:null);
            Apply(game.Executable,false);game.AssertFiles(originalA,count==2?originalB:null);
        }
    }
    static void Partial(byte[][] payloads)
    {
        foreach(int which in new[]{0,1})
        foreach(bool repair in new[]{false,true})
        {
            using var game=new GameFixture();game.Write(Names[which],payloads[which]);
            Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Partial,"An unmanaged single enhancement DLL showed fully enabled");
            if(repair){Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);}
            Apply(game.Executable,false);game.AssertFiles(null,null);
        }
        foreach(bool repair in new[]{false,true})
        {
            using var game=new GameFixture();byte[] original=[25,26,27];game.Write(Names[1],original);
            Apply(game.Executable,true);File.Delete(game.FilePath(Names[0]));
            Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Partial,"A missing managed enhancement DLL showed fully enabled");
            if(repair){Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);}
            Apply(game.Executable,false);game.AssertFiles(null,original);
        }
        using(var game=new GameFixture())
        {
            byte[] original=[28,29,30];game.Write(Names[0],payloads[0]);game.Write(Names[1],original);
            Apply(game.Executable,true);game.AssertFiles(payloads[0],payloads[1]);
            Apply(game.Executable,false);game.AssertFiles(null,original);
        }
        using(var game=new GameFixture())
        {
            game.Write(Names[0],payloads[0]);game.Write(Names[1],payloads[1]);
            Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Enabled,"A manually installed matching pair was not recognized");
            Apply(game.Executable,false);game.AssertFiles(null,null);
        }
    }
    static void ForeignChanges(byte[][] payloads)
    {
        foreach(bool enabled in new[]{false,true})
        {
            using var game=new GameFixture();Apply(game.Executable,true);
            byte[] replacement=[9,12,15,18];game.Write(Names[1],replacement);
            Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.Conflict,"A changed installed DLL did not expose a conflict");
            Reject(()=>Apply(game.Executable,enabled),"Changing managed enhancements overwrote a third-party replacement");
            game.AssertFiles(payloads[0],replacement);
        }
    }
    static void Guards(byte[][] payloads)
    {
        using var game=new GameFixture();
        Reject(()=>Apply(game.Executable,true,()=>true),"Enhancements modified a running game");game.AssertFiles(null,null);
        Apply(game.Executable,true);
        Reject(()=>Apply(game.Executable,false,()=>true),"Enhancements were removed while the game was running");game.AssertFiles(payloads[0],payloads[1]);
        string wrong=game.FilePath("other.exe");File.WriteAllBytes(wrong,[]);
        Reject(()=>Apply(wrong,true),"A different executable name was accepted as the BFME2 installation");
        File.WriteAllBytes(game.FilePath("game.dat"),[0,0]);
        Reject(()=>Apply(game.Executable,false),"An invalid game.dat was accepted for enhancement changes");game.AssertFiles(payloads[0],payloads[1]);
        Check(GameEnhancements.Inspect(game.FilePath("missing.exe")).State==GameEnhancementState.Unavailable,"An unavailable game installation did not expose unavailable state");
    }
    static void Failures(byte[][] payloads)
    {
        foreach(bool enabled in new[]{true,false})
        foreach(string point in new[]{"before-d3d9.dll","after-d3d9.dll","before-dinput8.dll","after-dinput8.dll","before-commit"})
        {
            using var game=new GameFixture();byte[] originalA=[31,32,33],originalB=[41,42,43];
            game.Write(Names[0],originalA);game.Write(Names[1],originalB);
            if(!enabled)Apply(game.Executable,true);
            bool injected=false;
            Reject(()=>Apply(game.Executable,enabled,checkpoint:name=>
            {
                if(name==point&&!injected){injected=true;throw new IOException("Simulated enhancement transaction failure at "+name);}
            }),"An injected paired enhancement write failure was ignored");
            Check(injected,"Transaction checkpoint was never reached: "+point);
            game.AssertFiles(enabled?originalA:payloads[0],enabled?originalB:payloads[1]);
            Apply(game.Executable,enabled);
            game.AssertFiles(enabled?payloads[0]:originalA,enabled?payloads[1]:originalB);
            if(enabled){Apply(game.Executable,false);game.AssertFiles(originalA,originalB);}
        }
        using(var game=new GameFixture())
        {
            int reads=0;
            Reject(()=>Apply(game.Executable,true,()=>++reads>1),"A game started during enhancement staging was not rechecked before commit");
            game.AssertFiles(null,null);
        }
    }
    static void Recovery(byte[][] payloads)
    {
        foreach(bool enabled in new[]{true,false})
        {
            using var game=new GameFixture();byte[] originalA=[51,52,53],originalB=[61,62,63];
            game.Write(Names[0],originalA);game.Write(Names[1],originalB);
            if(!enabled)Apply(game.Executable,true);
            bool firstFailure=false,rollbackFailure=false;
            Reject(()=>Apply(game.Executable,enabled,checkpoint:name=>
            {
                if(name=="after-d3d9.dll"){firstFailure=true;throw new IOException("Simulated active-file failure");}
                if(name=="rollback-before-d3d9.dll"){rollbackFailure=true;throw new IOException("Simulated rollback obstruction");}
            }),"A failed enhancement rollback was silently accepted");
            Check(firstFailure&&rollbackFailure,"Durable recovery fixture did not exercise an interrupted rollback");
            Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.RecoveryRequired,"An incomplete rollback did not retain a recoverable journal");
            Apply(game.Executable,enabled);
            game.AssertFiles(enabled?payloads[0]:originalA,enabled?payloads[1]:originalB);
            if(enabled){Apply(game.Executable,false);game.AssertFiles(originalA,originalB);}
        }
    }
    static void CorruptRecovery(byte[][] payloads)
    {
        using var game=new GameFixture();game.Write(Names[0],[21,22]);game.Write(Names[1],[31,32]);
        Reject(()=>Apply(game.Executable,true,checkpoint:name=>
        {
            if(name=="after-dinput8.dll"||name=="rollback-before-d3d9.dll")throw new IOException("Simulated interrupted recovery");
        }),"Interrupted recovery was accepted");
        game.AssertFiles(payloads[0],payloads[1]);
        File.WriteAllBytes(Path.Combine(game.Root,".fellowship-enhancements","pending","before",Names[1]),[99]);
        Reject(()=>Apply(game.Executable,true),"Damaged staged rollback backup was accepted");
        game.AssertFiles(payloads[0],payloads[1]);
        Check(GameEnhancements.Inspect(game.Executable).State==GameEnhancementState.RecoveryRequired,"Damaged recovery data lost its journal");
    }
    static void LockedFile()
    {
        using var game=new GameFixture();byte[] original=[71,72,73];game.Write(Names[1],original);
        using(var locked=File.Open(game.FilePath(Names[1]),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            Reject(()=>Apply(game.Executable,true),"An enhancement pair was committed despite a locked destination");
            Check(!File.Exists(game.FilePath(Names[0])),"A locked second destination left the first enhancement active");
        }
        game.AssertFiles(null,original);
    }
}

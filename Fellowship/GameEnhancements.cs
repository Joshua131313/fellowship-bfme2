using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Fellowship;

public enum GameEnhancementState { Disabled,Enabled,Partial,Conflict,RecoveryRequired,Unavailable }
public sealed record GameEnhancementStatus(GameEnhancementState State,string? Detail=null);

// The two wrappers are one installation. Stage and journal both files before
// changing either; restore the previous pair if a write fails or was interrupted.
public static class GameEnhancements
{
    static readonly string[] names=["d3d9.dll","dinput8.dll"];
    static readonly string[] hashes=["73BA5238B1A530139EBEEE3DEE2B5E64C9AA6D9F43100D9307B0541D50BDC9EF","09C63CFD93D2A17B8C5D0D3652D390347B2B1045112BF5B7FCF5A15D34615551"];
    static readonly Lazy<byte[][]> payloads=new(()=>names.Select((name,i)=>
    {
        using var source=typeof(GameEnhancements).Assembly.GetManifestResourceStream("Fellowship.GameEnhancements."+name)??throw new IOException("The game enhancement files are missing from Fellowship.");
        using var data=new MemoryStream();source.CopyTo(data);var bytes=data.ToArray();
        if(Convert.ToHexString(SHA256.HashData(bytes))!=hashes[i])throw new IOException("The game enhancement files could not be verified.");
        return bytes;
    }).ToArray());
    static readonly SemaphoreSlim gate=new(1,1);
    public static bool IsChanging=>gate.CurrentCount==0;
    sealed record Installation(string Id,string?[] Originals,string[] Installed);
    sealed record Journal(string?[] Before,string?[] After,Installation? Previous,Installation? Next,bool Committed=false);
    const string Folder=".fellowship-enhancements";
    const string Changed="The game DLLs were changed outside Fellowship. Restore those files before changing this feature.";
    const string Running="Close BFME2 before changing delay fix and borderless mode.";

    public static GameEnhancementStatus Inspect(string executable)
    {
        try
        {
            _=payloads.Value;string root=GameRoot(executable),store=Path.Combine(root,Folder);CheckDirectory(store);
            if(File.Exists(Path.Combine(store,"pending.json")))return new(GameEnhancementState.RecoveryRequired,"An interrupted change needs recovery. Close BFME2 and try again.");
            var state=ReadState(store);var actual=names.Select(name=>HashFile(Path.Combine(root,name))).ToArray();
            if(state!=null)
            {
                VerifyOriginals(store,state);
                if(actual.Where((hash,i)=>hash!=null&&hash!=state.Installed[i]).Any())return new(GameEnhancementState.Conflict,Changed);
                if(actual.SequenceEqual(hashes))return new(GameEnhancementState.Enabled);
                return new(GameEnhancementState.Partial,"Both files must be installed together. Enable the feature to repair it, or disable it to restore the previous files.");
            }
            if(actual.SequenceEqual(hashes))return new(GameEnhancementState.Enabled);
            if(actual.Where((hash,i)=>hash==hashes[i]).Any())return new(GameEnhancementState.Partial,"Only part of the feature is installed. Enable it to install both files together.");
            return new(GameEnhancementState.Disabled,actual.Any(hash=>hash!=null)?"Existing DLLs will be backed up and restored when this feature is disabled.":null);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {return new(GameEnhancementState.Unavailable,error.Message);}
    }

    public static async Task SetEnabled(string executable,bool enabled)
    {
        if(!await gate.WaitAsync(0))throw new InvalidOperationException("A game enhancement change is already in progress.");
        try
        {
            try{await Task.Run(()=>ApplyCore(executable,enabled));return;}
            catch(UnauthorizedAccessException){}
            // Only a permissions failure uses elevation. A locked DLL, conflict
            // or a failed rollback must never silently start another mutation.
            string physical=NativePlacement.PhysicalPath(executable);
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WorkingDirectory=AppContext.BaseDirectory,WindowStyle=ProcessWindowStyle.Hidden};
            start.ArgumentList.Add("--game-enhancements");start.ArgumentList.Add(enabled?"enable":"disable");start.ArgumentList.Add(physical);
            try
            {
                using var helper=Process.Start(start)??throw new IOException("Could not start the game enhancement helper.");
                await helper.WaitForExitAsync();
                if(helper.ExitCode!=0)throw new IOException("The game enhancement change could not finish. Review the helper message and retry; recovery data is preserved if needed.");
            }
            catch(Win32Exception error) when(error.NativeErrorCode==1223){throw new OperationCanceledException("Windows permission was cancelled.",error);}
        }
        finally{gate.Release();}
    }

    public static void Apply(string executable,bool enabled)
    {
        if(!gate.Wait(0))throw new InvalidOperationException("A game enhancement change is already in progress.");
        try{ApplyCore(executable,enabled);}finally{gate.Release();}
    }

    // isRunning/checkpoint are fixture seams: production always uses the real
    // process guard, and never loads or executes either supplied DLL itself.
    internal static void ApplyCore(string executable,bool enabled,Func<bool>? isRunning=null,Action<string>? checkpoint=null)
    {
        string root=GameRoot(executable),store=Path.Combine(root,Folder);_=payloads.Value;
        void Guard(){if((isRunning??GameLauncher.IsRunning)())throw new InvalidOperationException(Running);if(PatchSwitcher.IsSwitching)throw new InvalidOperationException("Wait for patch switching to finish before changing this feature.");}
        Guard();EnsureDirectory(store);
        CheckFile(Path.Combine(store,"operation.lock"));
        using var operation=new FileStream(Path.Combine(store,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string pending=Path.Combine(store,"pending"),journalPath=Path.Combine(store,"pending.json");
        if(File.Exists(journalPath))Recover(root,store,ReadJournal(store),checkpoint);
        // A staging directory without a journal never changed active files.
        CleanStaging(store);
        var previous=ReadState(store);if(previous!=null)VerifyOriginals(store,previous);
        var before=names.Select(name=>HashFile(Path.Combine(root,name))).ToArray();
        if(previous!=null&&before.Where((hash,i)=>hash!=null&&hash!=previous.Installed[i]).Any())throw new InvalidOperationException(Changed);
        if(previous==null&&!enabled)
        {
            // Adopt only our exact wrapper files; unrelated DLLs stay untouched.
            if(!before.Where((hash,i)=>hash==hashes[i]).Any())return;
            previous=new(Guid.NewGuid().ToString("N"),before.Select((hash,i)=>hash==hashes[i]?null:hash).ToArray(),hashes);
            SaveOriginals(root,store,previous,before);
        }
        Installation? next;
        if(enabled)
        {
            next=previous??new(Guid.NewGuid().ToString("N"),before.Select((hash,i)=>hash==hashes[i]?null:hash).ToArray(),hashes);
            next=next with{Installed=hashes};
        }
        else next=null;
        var after=enabled?hashes.Cast<string?>().ToArray():previous!.Originals;
        EnsureDirectory(pending);EnsureDirectory(Path.Combine(pending,"before"));EnsureDirectory(Path.Combine(pending,"after"));
        for(int i=0;i<names.Length;i++)
        {
            if(before[i]!=null)CopyVerified(Path.Combine(root,names[i]),Path.Combine(pending,"before",names[i]),before[i]!);
            if(after[i]!=null)
            {
                string target=Path.Combine(pending,"after",names[i]);
                if(enabled)WriteDurable(target,payloads.Value[i]);
                else CopyVerified(Original(store,previous!,i),target,after[i]!);
            }
        }
        var journal=new Journal(before,after,ReadState(store),next);
        WriteJson(journalPath,journal);
        var locks=new FileStream?[names.Length];
        void ReleaseLocks(){for(int i=0;i<locks.Length;i++){locks[i]?.Dispose();locks[i]=null;}}
        try
        {
            // Preflight both files before changing either. Windows replacement
            // can reject an open destination even with delete sharing, so retain
            // each handle only until that file's own rename is ready.
            for(int i=0;i<names.Length;i++)if(File.Exists(Path.Combine(root,names[i])))locks[i]=new FileStream(Path.Combine(root,names[i]),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(enabled&&journal.Previous==null)SaveOriginals(root,store,next!,before);
            for(int i=0;i<names.Length;i++)
            {
                Guard();checkpoint?.Invoke("before-"+names[i]);
                if(HashFile(Path.Combine(root,names[i]))!=before[i])throw new InvalidOperationException(Changed);
                if(before[i]!=after[i])
                {
                    locks[i]?.Dispose();locks[i]=null;
                    if(HashFile(Path.Combine(root,names[i]))!=before[i])throw new InvalidOperationException(Changed);
                    Replace(root,pending,"after",i,after[i]);
                }
                checkpoint?.Invoke("after-"+names[i]);
            }
            Guard();checkpoint?.Invoke("before-commit");
            if(!names.Select(name=>HashFile(Path.Combine(root,name))).SequenceEqual(after))throw new IOException("The paired game enhancement files could not be verified.");
            WriteState(store,next);WriteJson(journalPath,journal with{Committed=true});
            checkpoint?.Invoke("after-commit");CleanStaging(store);
        }
        catch(Exception error)
        {
            ReleaseLocks();
            try{Recover(root,store,ReadJournal(store),checkpoint);}
            catch(Exception recovery)
            {throw new IOException("The game enhancement change was interrupted. Close BFME2 and retry to restore both files; the recovery data has been preserved.",new AggregateException(error,recovery));}
            throw;
        }
        finally{ReleaseLocks();}
    }

    static void Recover(string root,string store,Journal journal,Action<string>? checkpoint)
    {
        var expected=journal.Committed?journal.After:journal.Before;
        bool conflict=false;
        if(!journal.Committed)
        {
            string pending=Path.Combine(store,"pending"),before=Path.Combine(pending,"before");
            CheckDirectory(pending);CheckDirectory(before);
            // Validate every needed rollback copy before restoring either DLL.
            // A damaged second backup must not create a new partial pair.
            for(int i=0;i<names.Length;i++)
            {
                string? current=HashFile(Path.Combine(root,names[i]));
                if((current==journal.Before[i]||current==journal.After[i])&&current!=expected[i]&&expected[i]!=null&&
                   HashFile(Path.Combine(before,names[i]))!=expected[i])throw new IOException("Game enhancement recovery data could not be verified.");
            }
            for(int i=0;i<names.Length;i++)
            {
                checkpoint?.Invoke("rollback-before-"+names[i]);
                string? current=HashFile(Path.Combine(root,names[i]));
                if(current!=journal.Before[i]&&current!=journal.After[i]){conflict=true;continue;}
                if(current!=expected[i])Replace(root,Path.Combine(store,"pending"),"before",i,expected[i]);
            }
            // Restore every known file even if an unrelated writer changed its
            // partner; preserve that foreign file and the recovery journal.
            if(conflict)throw new InvalidOperationException(Changed);
            WriteState(store,journal.Previous);
        }
        if(!names.Select(name=>HashFile(Path.Combine(root,name))).SequenceEqual(expected))throw new IOException("The paired game enhancement files could not be restored.");
        CleanStaging(store);
    }
    static void Replace(string root,string staging,string phase,int index,string? expected)
    {
        string destination=Path.Combine(root,names[index]);CheckFile(destination);
        if(expected==null){File.Delete(destination);return;}
        CheckDirectory(staging);CheckDirectory(Path.Combine(staging,phase));
        string source=Path.Combine(staging,phase,names[index]);
        if(HashFile(source)!=expected)throw new IOException("Game enhancement recovery data could not be verified.");
        // Keep the staged copy until commit/recovery is durable.
        string temporary=Path.Combine(root,".fellowship-"+Guid.NewGuid().ToString("N")+"-"+names[index]+".tmp");CheckFile(temporary);
        try{CopyVerified(source,temporary,expected);File.Move(temporary,destination,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static void SaveOriginals(string root,string store,Installation state,string?[] before)
    {
        string originals=Path.Combine(store,"originals");EnsureDirectory(originals);EnsureDirectory(Path.Combine(originals,state.Id));
        for(int i=0;i<names.Length;i++)if(state.Originals[i]!=null)
        {
            if(before[i]!=state.Originals[i])throw new InvalidOperationException(Changed);
            CopyVerified(Path.Combine(root,names[i]),Original(store,state,i),state.Originals[i]!);
        }
    }
    static string Original(string store,Installation state,int index)
    {
        CheckDirectory(Path.Combine(store,"originals"));CheckDirectory(Path.Combine(store,"originals",state.Id));
        return Path.Combine(store,"originals",state.Id,names[index]);
    }
    static void VerifyOriginals(string store,Installation state)
    {
        for(int i=0;i<names.Length;i++)if(state.Originals[i]!=null&&HashFile(Original(store,state,i))!=state.Originals[i])throw new IOException("The original game DLL backup is missing or changed. Restore the backup before disabling this feature.");
    }
    static string GameRoot(string executable)
    {
        GameLauncher.Validate(executable);string physical=NativePlacement.PhysicalPath(executable);GameLauncher.Validate(physical);
        return Path.GetDirectoryName(physical)!;
    }
    static Installation? ReadState(string store)
    {
        string path=Path.Combine(store,"state.json");if(!File.Exists(path))return null;
        var state=ReadJson<Installation>(path);Validate(state);return state;
    }
    static Journal ReadJournal(string store)
    {
        var journal=ReadJson<Journal>(Path.Combine(store,"pending.json"));
        if(!Valid(journal.Before)||!Valid(journal.After))throw new IOException("Invalid game enhancement recovery data.");
        if(journal.Previous!=null)Validate(journal.Previous);if(journal.Next!=null)Validate(journal.Next);return journal;
    }
    static void Validate(Installation state)
    {
        if(!Guid.TryParseExact(state.Id,"N",out _)||!Valid(state.Originals)||state.Installed==null||state.Installed.Length!=2||state.Installed.Any(hash=>!ValidHash(hash)))throw new IOException("Invalid game enhancement backup data.");
    }
    static bool Valid(string?[]? values)=>values is {Length:2}&&values.All(hash=>hash==null||ValidHash(hash));
    static bool ValidHash(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    static T ReadJson<T>(string path)
    {
        CheckFile(path);if(new FileInfo(path).Length>32768)throw new IOException("Invalid game enhancement backup data.");
        try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new IOException("Invalid game enhancement backup data.");}
        catch(JsonException error){throw new IOException("Invalid game enhancement backup data.",error);}
    }
    static void WriteState(string store,Installation? state)
    {
        string path=Path.Combine(store,"state.json");CheckFile(path);if(state==null)File.Delete(path);else WriteJson(path,state);
    }
    static void WriteJson<T>(string path,T value)
    {
        string temporary=path+".tmp";CheckFile(path);CheckFile(temporary);
        try{WriteDurable(temporary,JsonSerializer.SerializeToUtf8Bytes(value));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static void WriteDurable(string path,byte[] bytes)
    {
        CheckFile(path);using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);
    }
    static void CopyVerified(string source,string destination,string expected)
    {
        CheckFile(source);CheckFile(destination);
        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
        using(var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
        if(HashFile(destination)!=expected)throw new IOException("The paired game enhancement files could not be verified.");
    }
    static string? HashFile(string path)
    {
        CheckFile(path);if(!File.Exists(path))return null;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);return Convert.ToHexString(SHA256.HashData(file));
    }
    static void CheckFile(string path)
    {
        FileAttributes? attributes=Attributes(path);
        if(attributes.HasValue&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked game enhancement files are not supported.");
        if(attributes.HasValue&&(attributes&FileAttributes.Directory)!=0)throw new IOException("A game enhancement file path is a directory.");
    }
    static void CheckDirectory(string path)
    {
        FileAttributes? attributes=Attributes(path);
        if(attributes.HasValue&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked game enhancement backup folders are not supported.");
        if(attributes.HasValue&&(attributes&FileAttributes.Directory)==0)throw new IOException("A game enhancement backup directory is a file.");
    }
    static FileAttributes? Attributes(string path)
    {
        try{return File.GetAttributes(path);}
        catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
    }
    static void EnsureDirectory(string path){CheckDirectory(path);Directory.CreateDirectory(path);CheckDirectory(path);}
    static void CleanStaging(string store)
    {
        string pending=Path.Combine(store,"pending");CheckDirectory(pending);
        foreach(string phase in new[]{"before","after"})
        {
            string folder=Path.Combine(pending,phase);CheckDirectory(folder);
            foreach(string name in names){string file=Path.Combine(folder,name);CheckFile(file);if(File.Exists(file))File.Delete(file);}
            if(Directory.Exists(folder))Directory.Delete(folder,false);
        }
        if(Directory.Exists(pending))Directory.Delete(pending,false);
        string journal=Path.Combine(store,"pending.json");CheckFile(journal);File.Delete(journal);
    }
}

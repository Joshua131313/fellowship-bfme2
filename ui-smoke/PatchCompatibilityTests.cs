#nullable enable
using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

static class PatchCompatibilityTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action,string message)
    {
        try{action();}catch(InvalidOperationException){return;}
        throw new Exception(message);
    }
    public static void Run()
    {
        foreach(string patch in new[]{"1.00","1.06","1.09","1.09v2","1.09v3","1.09v3.1"})
            Check(GamePatches.IsKnown(patch)&&GamePatches.Normalize(patch)==patch,"Known main patch label did not validate: "+patch);
        Check(GamePatches.Normalize(" 1.09 V3.01 ")=="1.09v3.1","Legacy 1.09v3.01 alias did not normalize to its current label");
        string beta=GamePatches.Normalize("1.09v3.2 ß16");
        foreach(string alias in new[]{"1.09 v3.02 beta 16","1.09v3.2 β16","1.09u4 beta16"})
            Check(GamePatches.IsKnown(alias)&&GamePatches.Normalize(alias)==beta,"Exact beta alias did not retain its build: "+alias);
        Check(GamePatches.IsKnown(beta)&&GamePatches.Normalize("1.09u4 ß17")!=beta,"Distinct beta builds were collapsed together");
        Check(!GamePatches.IsKnown("unknown-patch")&&!GamePatches.IsKnown("1.09 beta")&&!GamePatches.IsKnown(""),"Unknown or ambiguous patch label validated");

        string fixture=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-patch-compatibility-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            string executable=Path.Combine(fixture,"lotrbfme2.exe"),data=Path.Combine(fixture,"game.dat");
            File.WriteAllBytes(executable,[]);File.WriteAllBytes(data,[77,90,1,2,3,4]);
            GameLauncher.Validate(executable);
            Check(!HostPreset.CanSynchronize(executable,new Room()),"Unverified MZ game binary enabled native memory synchronization");
            Reject(()=>HostPreset.ValidateBinary(executable),"Native preset hash guard accepted an unrelated MZ binary");
            File.WriteAllBytes(data,[0,1,2,3]);
            Reject(()=>GameLauncher.Validate(executable),"Normal launch validation accepted a non-Windows game.dat header");
            File.WriteAllBytes(data,[77,90,1,2,3,4]);
            GameLauncher.Validate(executable);

            string missing=Path.Combine(fixture,"missing","lotrbfme2.exe");
            var unknown=PatchCompatibility.Warning(missing,"1.09 V3.01");
            Check(unknown?.Contains("1.09v3.1")==true&&unknown.Contains("could not be verified")&&unknown.Contains("You can join"),"Unknown installation did not receive a nonblocking, canonical patch warning");
            string savedLanguage=L.Code;
            try
            {
                foreach(string code in new[]{"fr","es","ar","tr"})
                {
                    L.Apply(code);
                    string translated=L.T(unknown!);
                    Check(translated!=unknown&&translated.Contains("1.09v3.1"),"Patch warning did not translate or lost its version in "+code);
                    Check(L.T("PRESET")!="PRESET"&&L.T("Map files for this patch are unavailable. Install it in Ecth's Patch Switcher to browse its maps.")!="Map files for this patch are unavailable. Install it in Ecth's Patch Switcher to browse its maps.","Patch selector text is missing in "+code);
                }
            }
            finally{L.Apply(savedLanguage);}
            Check(!HostPreset.CanSynchronize(missing,new Room())&&!HostPreset.CanSynchronize(missing,new Room{Patch="1.09"}),"Missing installation enabled native synchronization");
            Check(HostPreset.Matches(new Room())&&!HostPreset.Matches(new Room{Patch="1.09"}),"Native preset patch gate no longer distinguishes 1.00");
            Check(RhunSides.Enabled(new Room{Mode="3v3"})&&!RhunSides.Enabled(new Room{Mode="3v3",Patch="1.09"}),"Rhûn native memory feature was enabled for an unsupported patch");

            string installed=GameInstallation.Find();string active=GamePatches.Active(installed);
            if(active!="Unknown")
            {
                Check(PatchCompatibility.Warning(installed,active)==null,"Matching verified patch displayed a mismatch warning");
                string required=active=="1.09"?"1.00":"1.09";
                var mismatch=PatchCompatibility.Warning(installed,required);
                Check(mismatch?.Contains(required)==true&&mismatch.Contains("installed patch is "+active)&&mismatch.Contains("You can join"),"Known installed patch mismatch did not explain both versions and allow joining");
                Check(!HostPreset.CanSynchronize(installed,new Room{Patch="1.09"}),"Different room patch enabled the 1.00 memory adapter");
            }
            else Console.WriteLine("SKIP known-installed-patch comparison: no verified local game installation");

            if(Application.Current is App)
                CheckRoomWindow(File.Exists(installed)?installed:executable);
            Console.WriteLine("PASS patch labels and exact beta aliases, nonblocking mismatch/unknown warnings, manual launch header validation and native hash/patch safety gates");
        }
        finally
        {
            string tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fixture)&&fixture.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase))Directory.Delete(fixture,true);
        }
    }
    static void CheckRoomWindow(string executable)
    {
        var settings=Settings.InMemory();settings.GamePath=executable;settings.LiveGameSync=true;
        BrowserWindow? browser=null;RoomWindow? roomWindow=null;
        var application=(App)Application.Current;bool quitting=application.IsQuitting;
        string language=L.Code;
        try
        {
            L.Apply("en");browser=new BrowserWindow(settings);
            // The test never shows either window or pumps the Dispatcher. Block
            // constructor-queued update checks and timers before they can run.
            typeof(BrowserWindow).GetField("checkingUpdate",Hidden)!.SetValue(browser,true);
            foreach(string timer in new[]{"refreshTimer","updateTimer"})
                ((DispatcherTimer)typeof(BrowserWindow).GetField(timer,Hidden)!.GetValue(browser)!).Stop();
            string active=GamePatches.Active(executable),required=active=="1.09"?"1.06":"1.09";
            browser.Peer.Host(new Room{Title="Patch compatibility fixture",Patch=required,Mode="3v3"});
            roomWindow=new RoomWindow(browser){AllowClose=true};
            Check(typeof(RoomWindow).GetMethod("SyncCommand",Hidden)!.Invoke(roomWindow,null)==null,"Room window created a native sync command for another patch");
            var notice=(TextBlock)typeof(RoomWindow).GetField("patchNotice",Hidden)!.GetValue(roomWindow)!;
            Check(notice.Visibility==Visibility.Visible&&notice.Text.Contains(required),"Room window omitted its selected-patch compatibility notice");
        }
        finally
        {
            // Production dashboard closing minimizes to tray. Temporarily allow
            // real closure so this hidden fixture releases its subscriptions.
            typeof(App).GetProperty(nameof(App.IsQuitting))!.SetValue(application,true);
            try{roomWindow?.Close();browser?.Close();}
            finally{typeof(App).GetProperty(nameof(App.IsQuitting))!.SetValue(application,quitting);L.Apply(language);}
        }
    }
}

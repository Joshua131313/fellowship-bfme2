#nullable enable
using Fellowship;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;

static class RoomActivityTests
{
    public static void Run()
    {
        string language=L.Code;L.Apply("en");
        var settings=Settings.InMemory();settings.Name="Aragorn";settings.LiveGameSync=false;settings.MessageSounds=false;
        var browser=new BrowserWindow(settings);typeof(BrowserWindow).GetField("closingRoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(browser,true);browser.Peer.Host(new(){Title="Room activity",Capacity=8},[]);
        var room=browser.Peer.Current!;room.HostAddress="10.210.0.1";room.Players[0].Address=room.HostAddress;
        var window=new RoomWindow(browser){AllowClose=true};
        var transcript=(ChatTranscript)typeof(RoomWindow).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var observe=typeof(RoomWindow).GetMethod("ObserveRoomActivity",BindingFlags.Instance|BindingFlags.NonPublic)!;
        void Update(Room? snapshot,bool reconnecting=false)=>observe.Invoke(window,[snapshot,reconnecting]);
        int Count()=>transcript.Document.Blocks.Count;
        string[] Bodies()=>transcript.Document.Blocks.Cast<Section>().Select(s=>((Run)((Paragraph)s.Blocks.FirstBlock).Inlines.LastInline).Text).ToArray();
        void Check(bool value,string error){if(!value)throw new Exception(error);}
        try
        {
            Check(Count()==0,"Opening a room repeated existing membership as chat events");Update(room);room.Revision++;Update(room);
            Check(Count()==0,"An unchanged room snapshot produced a system notice");
            var ready=new Player{Id="guest-ready",Name="Ready",Address="10.210.0.2"};room.Players.Add(ready);Update(room);
            Check(Bodies().SequenceEqual(new[]{"Ready joined the room."}),"Joining player was not shown once with their raw nickname");
            ready.Team=2;ready.Spot=3;ready.Ready=true;ready.GameRunning=true;room.Revision++;Update(room);Update(room);
            Check(Count()==1,"Readiness, game state, selections or repeated snapshots produced activity noise");
            ready.Reconnecting=true;Update(room);ready.Reconnecting=false;Update(room);
            Check(Count()==1,"A reserved reconnecting member was reported as leaving or joining");

            // Promote changes both members' IDs, but their addresses identify
            // the same people. No membership event should be fabricated.
            room.Players[0].Id="old-host";ready.Id="host";room.Host=ready.Name;room.HostAddress=ready.Address;Update(room);
            Check(Bodies().Last()=="Room host is now Ready"&&Count()==2,"Host promotion did not produce exactly one role change");
            window.ShowNotice("Host is now Ready");window.ShowNotice("Room handed to Ready");Update(room);
            Check(Count()==2,"Legacy host-transfer notices duplicated the snapshot system message");
            ready.Name="HOST";room.Host=ready.Name;Update(room);Check(Count()==2,"Renaming the same host was mistaken for a host transfer");

            var guest=new Player{Id="guest-boromir",Name="Boromir",Address="10.210.0.3"};room.Players.Add(guest);Update(room);
            room.Players.Remove(guest);Update(room);Update(room);
            Check(Bodies().TakeLast(2).SequenceEqual(new[]{"Boromir joined the room.","Boromir left the room."}),"A member join/leave was omitted or duplicated");
            int beforeReconnect=Count();Update(room,true);
            var resumed=Copy(room);resumed.Players.RemoveAll(p=>p.Address=="10.210.0.1");resumed.Players.Add(new(){Id="during-outage",Name="During outage",Address="10.210.0.4"});Update(resumed,true);Update(resumed);Update(resumed);
            Check(Count()==beforeReconnect,"A reconnect snapshot fabricated leaves or joins while the UI was disconnected");

            // The old owner can see null during Leave()/ResumeRoom(). Preserve
            // the baseline and report only the confirmed departure/new role.
            Update(null);var handed=Copy(resumed);handed.Players.RemoveAll(p=>p.Id=="host");var successor=handed.Players.Single();successor.Id="host";handed.Host=successor.Name;handed.HostAddress=successor.Address;Update(handed);
            Check(Bodies().TakeLast(2).SequenceEqual(new[]{"HOST left the room.","Room host is now During outage"}),"Null handoff lost or duplicated the confirmed host departure/role change");
            int beforeNewRoom=Count();var different=Copy(handed);different.Id=Guid.NewGuid().ToString("N");different.Players.Add(new(){Id="existing",Name="Existing member",Address="10.210.0.5"});Update(different);Update(different);
            Check(Count()==beforeNewRoom,"Opening a different room replayed past membership");

            foreach(string code in new[]{"fr","es","ar","tr"})
            {
                L.Apply(code);Pump();var bodies=Bodies();
                Check(bodies[0]==string.Format(L.T("{0} joined the room."),"Ready"),"Language change translated a raw player nickname or left an old activity label");
                Check(bodies[1]==string.Format(L.T("Room host is now {0}"),"Ready"),"Host activity did not update language while preserving its original nickname");
            }
            transcript.SelectAll();Check(transcript.Selection.Text.Contains("Ready")&&transcript.Selection.Text.Contains("Boromir"),"System events broke selection across room messages");
        }
        finally{window.Close();browser.Peer.Dispose();L.Apply(language);}
        Console.WriteLine("PASS room system joins/leaves/host changes once, silent initial/reconnect snapshots, stable identities through handoff, raw nicknames and live translation");
    }
    static Room Copy(Room room)=>JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(room))!;
    static void Pump()
    {var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
}

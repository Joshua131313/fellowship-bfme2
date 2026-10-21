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
        room.RecordDeparture(new(){Id="historical-ready",Name="Ready",Address="10.210.0.2"},"left");
        var window=new RoomWindow(browser){AllowClose=true};
        var transcript=(ChatTranscript)typeof(RoomWindow).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var observe=typeof(RoomWindow).GetMethod("ObserveRoomActivity",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var expected=new List<(string Key,string Name)>();
        void Update(Room? snapshot,bool reconnecting=false)=>observe.Invoke(window,[snapshot,reconnecting]);
        string[] Bodies()=>transcript.Document.Blocks.Cast<Section>().Select(s=>((Run)((Paragraph)s.Blocks.FirstBlock).Inlines.LastInline).Text).ToArray();
        void Check(bool value,string error){if(!value)throw new Exception(error);}
        void Expect(string error,params (string Key,string Name)[] events)
        {
            expected.AddRange(events);var wanted=expected.Select(e=>string.Format(L.T(e.Key),e.Name)).ToArray();var actual=Bodies();
            Check(actual.SequenceEqual(wanted),error+"\nExpected: "+string.Join(" | ",wanted)+"\nActual: "+string.Join(" | ",actual));
        }
        Player Add(Room target,string id,string name,string address)
        {var player=new Player{Id=id,Name=name,Address=address};target.Players.Add(player);return player;}
        void Remove(Room target,Player player,string? reason)
        {if(reason!=null)target.RecordDeparture(player,reason);target.Players.Remove(player);}
        try
        {
            Expect("Opening a room replayed existing membership or historical departures");Update(room);room.Revision++;Update(room);
            Expect("An unchanged room snapshot produced a system notice");
            var ready=Add(room,"guest-ready","Ready","10.210.0.2");Update(room);
            Expect("Joining player was not shown once with their raw nickname",("{0} joined the room.","Ready"));
            ready.Team=2;ready.Spot=3;ready.Ready=true;ready.GameRunning=true;room.Revision++;Update(room);Update(room);
            Expect("Readiness, game state, selections or repeated snapshots produced activity noise");

            ready.Reconnecting=true;Update(room);ready.Reconnecting=false;Update(room);
            Expect("Planned migration reservations fabricated a disconnect or reconnect");
            ready.Reconnecting=true;ready.ConnectionLost=true;Update(room);Update(room);
            Expect("Genuine transport loss was not reported exactly once",("{0} disconnected from Fellowship.","Ready"));
            ready.Reconnecting=false;ready.ConnectionLost=false;Update(room);Update(room);
            Expect("A resumed transport was not reported exactly once",("{0} reconnected to the room.","Ready"));
            ready.Reconnecting=true;ready.ConnectionLost=true;Update(room);
            Expect("A second genuine outage was suppressed",("{0} disconnected from Fellowship.","Ready"));
            Remove(room,ready,"disconnected");Update(room);Update(room);
            Expect("Reservation expiry repeated an already announced disconnect as a leave");

            ready=Add(room,"ready-rejoin-1","Ready","10.210.0.2");Update(room);
            Expect("Rejoining after expiry did not produce a single join",("{0} joined the room.","Ready"));
            Remove(room,ready,null);Update(room);Update(room);
            Expect("A legacy removal reused an old same-address disconnect cause",("{0} is no longer in the room.","Ready"));
            ready=Add(room,"ready-rejoin-2","Ready","10.210.0.2");Update(room);
            Expect("The same address could not rejoin after a legacy removal",("{0} joined the room.","Ready"));
            Remove(room,ready,"left");Update(room);Update(room);
            Expect("A fresh explicit leave was not shown exactly once",("{0} left the room.","Ready"));
            ready=Add(room,"ready-rejoin-3","Ready","10.210.0.2");Update(room);
            Expect("A player could not rejoin after leaving",("{0} joined the room.","Ready"));
            Remove(room,ready,null);Update(room);
            Expect("A stale explicit leave was reused after a same-address rejoin",("{0} is no longer in the room.","Ready"));
            ready=Add(room,"ready-rejoin-4","Ready","10.210.0.2");Update(room);
            Expect("Repeated same-address rejoins fabricated duplicate events",("{0} joined the room.","Ready"));
            ready.Reconnecting=true;ready.ConnectionLost=true;Update(room);
            Expect("A loss before a voluntary leave was omitted",("{0} disconnected from Fellowship.","Ready"));
            Remove(room,ready,"left");Update(room);
            Expect("An announced disconnect suppressed a later explicit leave",("{0} left the room.","Ready"));

            var gimli=Add(room,"guest-gimli","Gimli","10.210.0.3");Update(room);
            Expect("The removed-player fixture did not join",("{0} joined the room.","Gimli"));
            gimli.Reconnecting=true;gimli.ConnectionLost=true;Update(room);
            Expect("The removed-player fixture did not disconnect",("{0} disconnected from Fellowship.","Gimli"));
            Remove(room,gimli,"removed");Update(room);Update(room);
            Expect("An announced disconnect suppressed a later explicit removal",("{0} was removed from the room.","Gimli"));
            var boromir=Add(room,"guest-boromir","Boromir","10.210.0.4");Update(room);
            Expect("The Fellowship-quit fixture did not join",("{0} joined the room.","Boromir"));
            Remove(room,boromir,"disconnected");Update(room);Update(room);
            Expect("A fresh Fellowship quit was incorrectly called a voluntary room leave",("{0} disconnected from Fellowship.","Boromir"));
            var faramir=Add(room,"guest-faramir","Faramir","10.210.0.5");Update(room);
            Expect("The unknown-removal fixture did not join",("{0} joined the room.","Faramir"));
            Remove(room,faramir,"unknown");Update(room);
            Expect("An unknown removal claimed a voluntary leave",("{0} is no longer in the room.","Faramir"));
            var merry=Add(room,"guest-merry","Merry","10.210.0.6");Update(room);
            Expect("The legacy-expiry fixture did not join",("{0} joined the room.","Merry"));
            merry.Reconnecting=true;merry.ConnectionLost=true;Update(room);
            Expect("The legacy-expiry fixture did not announce its outage",("{0} disconnected from Fellowship.","Merry"));
            Remove(room,merry,null);Update(room);Update(room);
            Expect("An unknown expiry repeated an already announced disconnect");

            // An explicit leave and fresh join may be coalesced into one UI snapshot.
            var pippin=Add(room,"pippin-before","Pippin","10.210.0.11");Update(room);
            Expect("The coalesced replacement fixture did not join",("{0} joined the room.","Pippin"));
            Remove(room,pippin,"left");pippin=Add(room,"pippin-after","Pippin","10.210.0.11");Update(room);Update(room);
            Expect("A coalesced same-address leave/rejoin disappeared or duplicated",("{0} left the room.","Pippin"),("{0} joined the room.","Pippin"));
            Remove(room,pippin,"left");Update(room);
            Expect("A coalesced rejoin reused the previous departure ID",("{0} left the room.","Pippin"));

            ready=Add(room,"ready-final","Ready","10.210.0.2");Update(room);
            Expect("The host-transfer fixture did not join",("{0} joined the room.","Ready"));
            // Promotion remaps IDs and reserves guest connections. Neither is
            // evidence that a member left or suffered a transport failure.
            room.Players[0].Id="old-host";room.Players[0].Reconnecting=true;ready.Id="host";room.Host=ready.Name;room.HostAddress=ready.Address;Update(room);
            Expect("Host promotion fabricated membership or transport events",("Room host is now {0}","Ready"));
            room.Players[0].Reconnecting=false;Update(room);window.ShowNotice("Host is now Ready");window.ShowNotice("Room handed to Ready");Update(room);
            Expect("Planned migration or legacy notices duplicated a host change");
            ready.Name="HOST";room.Host=ready.Name;Update(room);
            Expect("Renaming the same host was mistaken for a host transfer");

            Update(room,true);var resumed=Copy(room);var oldMember=resumed.Players.Single(p=>p.Address=="10.210.0.1");Remove(resumed,oldMember,"removed");
            Add(resumed,"during-outage","During outage","10.210.0.7");Update(resumed,true);Update(resumed);Update(resumed);
            Expect("Recovery fabricated membership notices for changes during a local outage");
            // Only fresh metadata establishes how the old host departed after
            // a null handoff snapshot. Other outage differences stay silent.
            Update(null);var handed=Copy(resumed);var departingHost=handed.Players.Single(p=>p.Id=="host");Remove(handed,departingHost,"disconnected");
            var successor=handed.Players.Single();successor.Id="host";handed.Host=successor.Name;handed.HostAddress=successor.Address;Update(handed);Update(handed);
            Expect("Host application exit was not distinguished during handoff",("{0} disconnected from Fellowship.","HOST"),("Room host is now {0}","During outage"));

            var legacy=Copy(handed);legacy.Id=Guid.NewGuid().ToString("N");legacy.Departures.Clear();legacy.Players[0].Name="Legacy host";
            var legacySuccessor=Add(legacy,"legacy-successor","Legacy successor","10.210.0.8");Update(legacy);Update(legacy);
            Expect("Opening a different room replayed past membership");
            Update(null);legacy.Players.RemoveAll(p=>p.Id=="host");legacySuccessor.Id="host";legacy.Host=legacySuccessor.Name;legacy.HostAddress=legacySuccessor.Address;Update(legacy);
            Expect("A legacy host disappearance claimed an explicit leave",("{0} is no longer in the room.","Legacy host"),("Room host is now {0}","Legacy successor"));

            var voluntary=Copy(legacy);voluntary.Id=Guid.NewGuid().ToString("N");voluntary.Departures.Clear();voluntary.Players[0].Name="Leaving host";
            var voluntarySuccessor=Add(voluntary,"next-host","Next host","10.210.0.9");Update(voluntary);Update(null);
            Remove(voluntary,voluntary.Players.Single(p=>p.Id=="host"),"left");voluntarySuccessor.Id="host";voluntary.Host=voluntarySuccessor.Name;voluntary.HostAddress=voluntarySuccessor.Address;Update(voluntary);
            Expect("An explicit host leave lost its cause during handoff",("{0} left the room.","Leaving host"),("Room host is now {0}","Next host"));

            var initialLoss=Copy(voluntary);initialLoss.Id=Guid.NewGuid().ToString("N");initialLoss.Departures.Clear();var unseen=Add(initialLoss,"initial-loss","Unseen loss","10.210.0.10");unseen.Reconnecting=true;unseen.ConnectionLost=true;Update(initialLoss);
            unseen.Reconnecting=false;unseen.ConnectionLost=false;Update(initialLoss);Update(initialLoss);
            Expect("Opening a room during a loss fabricated a reconnect without an announced disconnect");
            unseen.Reconnecting=true;unseen.ConnectionLost=true;Update(initialLoss);
            Expect("A subsequent observed loss was not announced",("{0} disconnected from Fellowship.","Unseen loss"));
            Update(initialLoss,true);unseen.Reconnecting=false;unseen.ConnectionLost=false;Update(initialLoss);Update(initialLoss);
            Expect("A local outage replayed a remote reconnect from a recovery snapshot");
            unseen.Reconnecting=true;unseen.ConnectionLost=true;Update(initialLoss);
            Expect("A locally paused tracker did not recover for future outages",("{0} disconnected from Fellowship.","Unseen loss"));
            unseen.Reconnecting=false;unseen.ConnectionLost=false;Update(initialLoss);
            Expect("A later observed recovery was suppressed",("{0} reconnected to the room.","Unseen loss"));

            foreach(string code in new[]{"fr","es","ar","tr"})
            {
                L.Apply(code);Pump();Expect("Language change translated a raw nickname or left an old activity label");
                foreach(string key in new[]{"{0} disconnected from Fellowship.","{0} reconnected to the room.","{0} was removed from the room.","{0} is no longer in the room."})
                    Check(L.T(key)!=key,"A new activity message lacks a translation for "+code+": "+key);
            }
            transcript.SelectAll();Check(transcript.Selection.Text.Contains("Ready")&&transcript.Selection.Text.Contains("Boromir"),"System events broke selection across room messages");
        }
        finally{window.Close();browser.Peer.Dispose();L.Apply(language);}
        Console.WriteLine("PASS room activity causes, immediate disconnect/resume, silent expiry/migration/recovery, fresh departure fences, stable host identities, raw names and live translation");
    }
    static Room Copy(Room room)=>JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(room))!;
    static void Pump()
    {var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
}

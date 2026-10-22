using Fellowship;

static class PersonalRhunSideTests
{
    const string Host="10.3.0.1",Local="10.3.0.2",Other="10.3.0.3",Outside="10.3.0.9";
    // Non-sequential spot numbering and slightly different Y values ensure the
    // planner pairs top/middle/bottom rather than assuming a number offset.
    static readonly MapSpot[] Spots=[new(1,.2,.9),new(2,.8,.52),new(3,.2,.1),new(4,.8,.91),new(5,.2,.5),new(6,.8,.12)];
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static NativeLobby.Slot Human(int row,string ip,int team,int spot,bool local=false)=>new(row,6,spot-1,team-1,local,Color:2,Faction:0,Address:ip);
    static NativeLobby.Snapshot Snapshot(int hostTeam,int team,int spot,string host=Host,uint identity=101,params NativeLobby.Slot[] others)=>
        new(identity,GameMap.Rhun.Path,host,[Human(0,host,hostTeam,0),Human(3,Local,team,spot,true),..others]);
    static LivePlacement.Command Command(int team,int spot)=>new("personal-side-room",GameMap.Rhun.Path,Local,new(team,spot,3),
        Members:Local+"|"+Other,Rhun3v3:true,AllowExternalHost:true);
    static NativeLobby.Snapshot Applied(NativeLobby.Snapshot snapshot,LivePlacement.Choice choice)=>snapshot with
        {Slots=snapshot.Slots.Select(slot=>slot.Local?slot with{Team=choice.Team-1,Spot=choice.Spot-1,Color=choice.Color-1}:slot).ToList()};

    public static void Run()
    {
        foreach(var (left,right) in new[]{(3,6),(5,2),(1,4)})
        {
            var planner=new PersonalRhunSides();var command=Command(2,left);var lobby=Snapshot(1,1,left);
            var rightSide=planner.Resolve(command,lobby,Spots);
            Check(rightSide.State==PersonalRhunSides.State.Ready&&rightSide.Choice==new LivePlacement.Choice(1,right,3),"Native hosting team was not mirrored right with its original native team label");
            lobby=Applied(lobby,rightSide.Choice);command=command with{Choice=rightSide.Choice};
            for(int i=0;i<4;i++)Check(planner.Resolve(command,lobby,Spots).Choice==rightSide.Choice,"Repeated snapshots toggled an already correct hosting side");
            var teammate=Snapshot(2,2,right,"10.3.0.8",102);
            var sameTeam=planner.Resolve(command,teammate,Spots);
            Check(sameTeam.Choice.Spot==right&&sameTeam.Choice.Team==2,"Same-team host or reset native team labels flipped the local spot");
            command=command with{Choice=sameTeam.Choice};
            var opponent=Snapshot(2,1,right,Outside,103);
            var leftSide=planner.Resolve(command,opponent,Spots);
            Check(leftSide.Choice.Spot==left&&leftSide.Choice.Team==1,"Opposing hosting team did not mirror the same vertical position left");
            opponent=Applied(opponent,leftSide.Choice);command=command with{Choice=leftSide.Choice};
            Check(planner.Resolve(command,opponent,Spots).Choice==leftSide.Choice,"Mirrored import echo reapplied the side swap");
        }
        RandomAndManual();TeamEdits();Colors();Occupancy();NativeRequests();ConfirmedWrites();History();HistoryValidation();HistoryLifecycle();Gates();
        Console.WriteLine("PASS personal Rhûn hosting-side normalization, confirmed cohort history, native team authority, vertical mirrors, same-team/replay stability, Random intent, explicit edits, reciprocal own clearing and outsider occupancy");
    }
    static void RandomAndManual()
    {
        var planner=new PersonalRhunSides();var command=Command(1,1);var lobby=Snapshot(1,2,0);
        var initial=planner.Resolve(command,lobby,Spots);
        Check(initial.Choice.Spot==1,"Initial native Random did not retain the selected Fellowship vertical position");
        lobby=Applied(lobby,initial.Choice);command=command with{Choice=initial.Choice};planner.Resolve(command,lobby,Spots);
        // Replayed native setup resets all spots to Random, even if its native
        // object address is reused. Positive lifecycle evidence starts a lobby.
        planner.NewLobby();var replay=Applied(lobby,initial.Choice with{Spot=0});
        Check(planner.Resolve(command with{Choice=command.Choice with{Spot=0}},replay,Spots).Choice.Spot==1,"Replay Random lost the remembered bottom position");
        planner=new();command=Command(2,5);lobby=Snapshot(1,2,5);planner.Resolve(command,lobby,Spots);
        var manualRandom=Applied(lobby,new(2,0,3));
        Check(planner.Resolve(command,manualRandom,Spots).State==PersonalRhunSides.State.Random,"Deliberate native Random was replaced by remembered position");
        Check(planner.Resolve(command with{Choice=new(2,0,3)},manualRandom,Spots).Choice.Spot==0,"Random import echo restored an unwanted spot");
        var newNative=Applied(manualRandom,new(2,4,3));
        var changed=planner.Resolve(command with{Choice=new(2,0,3)},newNative,Spots);
        Check(changed.Choice.Spot==1,"New explicit native bottom choice was not mirrored left or was lost to stale Random");
        newNative=Applied(newNative,changed.Choice);command=command with{Choice=changed.Choice};planner.Resolve(command,newNative,Spots);
        var fellowship=planner.Resolve(command with{Choice=command.Choice with{Spot=6}},newNative,Spots);
        Check(fellowship.Choice.Spot==3,"New Fellowship top selection was overwritten by old native vertical");
        var nativeWins=planner.Resolve(command with{Choice=command.Choice with{Spot=5}},Applied(newNative,new(2,2,3)),Spots);
        Check(nativeWins.Choice.Spot==5,"Concurrent explicit native vertical was overwritten by another request");
    }
    static void TeamEdits()
    {
        var planner=new PersonalRhunSides();var command=Command(2,3);var lobby=Snapshot(1,2,3);
        planner.Resolve(command,lobby,Spots);
        var requested=planner.Resolve(command with{Choice=command.Choice with{Team=1}},lobby,Spots);
        Check(requested.Choice.Team==1&&requested.Choice.Spot==6,"Explicit Fellowship team change did not mirror relative to its effective requested team");
        var confirmed=Applied(lobby,requested.Choice);
        Check(planner.Resolve(command with{Choice=command.Choice with{Team=1}},confirmed,Spots).Choice==requested.Choice,"Team/spot echo undid a requested team switch");
        planner=new();command=Command(2,5);lobby=Snapshot(1,2,5);planner.Resolve(command,lobby,Spots);
        var nativeEdit=planner.Resolve(command,Applied(lobby,new(1,5,3)),Spots);
        Check(nativeEdit.Choice.Team==1&&nativeEdit.Choice.Spot==2,"Native team change was overwritten by saved Fellowship team number");
        planner=new();lobby=Snapshot(1,0,0);command=Command(2,3);
        var waiting=planner.Resolve(command,lobby,Spots);
        Check(waiting.State==PersonalRhunSides.State.WaitingForTeams&&waiting.Choice.Team==2&&waiting.Choice.Spot==0,"Initial Random team guessed a side instead of establishing the requested team first");
        var settled=planner.Resolve(command,Applied(lobby,new(2,0,3)),Spots);
        Check(settled.State==PersonalRhunSides.State.Ready&&settled.Choice.Spot==3,"Established team did not release the pending side selection");
        planner=new();var unknown=planner.Resolve(Command(1,6),Snapshot(0,1,6),Spots);
        Check(unknown.State==PersonalRhunSides.State.WaitingForTeams&&unknown.Choice.Spot==6,"Unknown hosting team guessed a map side");
    }
    static void Occupancy()
    {
        var command=Command(1,3);var outsider=Human(5,Outside,2,6);
        var lobby=Snapshot(1,1,3,Host,101,outsider);var planner=new PersonalRhunSides();
        var blocked=planner.Resolve(command,lobby,Spots);
        Check(blocked.State==PersonalRhunSides.State.Blocked&&blocked.Choice.Spot==3&&blocked.TargetSpot==6,"Outside player's seat was stolen or caused an unrelated vertical allocation");
        var free=lobby with{Slots=lobby.Slots.Where(slot=>slot.Ip!=Outside).ToList()};
        Check(planner.Resolve(command,free,Spots).Choice.Spot==6,"Freed counterpart did not release the pending mirror");
        var ai=lobby with{Slots=lobby.Slots.Select(slot=>slot.Ip==Outside?slot with{State=3}:slot).ToList()};
        Check(new PersonalRhunSides().Resolve(command,ai,Spots).State==PersonalRhunSides.State.Blocked,"Occupied AI seat authorized a clear");
        var sameTeam=Snapshot(1,1,3,Host,101,Human(5,Other,1,6));
        Check(new PersonalRhunSides().Resolve(command,sameTeam,Spots).State==PersonalRhunSides.State.Blocked,"Same-team vertical conflict authorized an unrelated reciprocal clear");

        // Two Fellowship clients can each clear only their own reciprocal seat.
        lobby=Snapshot(1,1,3,Host,101,Human(5,Other,2,6));planner=new();
        var clear=planner.Resolve(command,lobby,Spots);
        Check(clear.State==PersonalRhunSides.State.ClearOwn&&clear.Choice.Spot==0&&clear.TargetSpot==6,"Known reciprocal seats could not safely release the local reservation");
        lobby=Applied(lobby,clear.Choice);
        var intermediate=planner.Resolve(command,lobby,Spots);
        Check(intermediate.State==PersonalRhunSides.State.Blocked&&intermediate.Choice.Spot==0&&intermediate.TargetSpot==6,"Native clear echo erased the pending vertical or restored the old seat");
        command=command with{Choice=clear.Choice};
        Check(planner.Resolve(command,lobby,Spots).TargetSpot==6,"Fellowship zero import was mistaken for deliberate Random");
        lobby=lobby with{Slots=lobby.Slots.Select(slot=>slot.Ip==Other?slot with{Spot=-1}:slot).ToList()};
        var claim=planner.Resolve(command,lobby,Spots);
        Check(claim.State==PersonalRhunSides.State.Ready&&claim.Choice.Spot==6,"Cleared counterpart did not retain the exact pending top target");
        lobby=Applied(lobby,claim.Choice);command=command with{Choice=claim.Choice};
        Check(planner.Resolve(command,lobby,Spots).Choice==claim.Choice,"Claim/import acknowledgement recreated the reciprocal clear");

        // A fallback import while blocked must not overwrite a newer vertical.
        planner=new();command=Command(1,3);lobby=Snapshot(1,1,3);planner.Resolve(command,lobby,Spots);
        lobby=lobby with{Slots=[..lobby.Slots,Human(5,Outside,2,4)]};
        var lower=planner.Resolve(command with{Choice=new(1,1,3)},lobby,Spots);
        Check(lower.State==PersonalRhunSides.State.Blocked&&lower.TargetSpot==4,"Blocked new bottom choice did not retain its intended mirror");
        command=command with{Choice=lower.Choice};planner.Resolve(command,lobby,Spots);
        lobby=lobby with{Slots=lobby.Slots.Where(slot=>slot.Ip!=Outside).ToList()};
        Check(planner.Resolve(command,lobby,Spots).Choice.Spot==4,"Blocked fallback echo replaced the pending bottom preference with the current top seat");
    }
    static void Colors()
    {
        var command=Command(2,3) with{Choice=new(2,3,5)};var lobby=Snapshot(1,2,3);var planner=new PersonalRhunSides();
        var saved=planner.Resolve(command,lobby,Spots);
        Check(saved.Choice.Color==5,"Initial saved Fellowship color was not applied");
        lobby=Applied(lobby,saved.Choice);planner.Resolve(command,lobby,Spots);
        // The user changes native color while a host-team change also requires
        // a side correction. The old raw command must not undo that color.
        lobby=lobby with{Slots=lobby.Slots.Select(slot=>slot.Local?slot with{Color=6}:slot.Row==0?slot with{Team=1}:slot).ToList()};
        var native=planner.Resolve(command,lobby,Spots);
        Check(native.Choice.Spot==6&&native.Choice.Color==7,"Automatic spot mirror rewrote a deliberate native color change");
        lobby=Applied(lobby,native.Choice);
        Check(planner.Resolve(command,lobby,Spots).Choice.Color==7,"Stale raw color overwrote the settled native color before import");
        command=command with{Choice=native.Choice};planner.Resolve(command,lobby,Spots);
        var edited=planner.Resolve(command with{Choice=command.Choice with{Color=4}},lobby,Spots);
        Check(edited.Choice.Color==4,"A later explicit Fellowship color edit was ignored");
    }
    static void NativeRequests()
    {
        var lobby=Snapshot(1,1,3);
        foreach(int target in new[]{0,6})
        {
            byte[] bytes=NativePlacement.GuardedSideRequestBytes(1234,lobby,2,target);
            Check(BitConverter.ToInt32(bytes,16)==lobby.Local.Row&&BitConverter.ToInt32(bytes,20)==1&&BitConverter.ToInt32(bytes,24)==target-1,"Personal native request changed a different row or lost its target team/spot");
            Check(BitConverter.ToInt32(bytes,568)==3&&BitConverter.ToInt32(bytes,1084)==lobby.Local.Team&&BitConverter.ToInt32(bytes,1088)==lobby.Local.Spot,"Personal native move omitted compare-before-write guards");
            Check(BitConverter.ToInt32(bytes,1100)==lobby.Local.Color,"Personal native move did not preserve native color");
        }
        var command=Command(1,3);var planner=new PersonalRhunSides();var move=planner.Resolve(command,lobby,Spots);
        var reconciler=new LivePlacement.Reconciler();
        // A rejected native write leaves the observed spot unchanged. It must
        // retry instead of treating that observation as a completed mirror.
        reconciler.Remember(move.Choice,command.Choice,retry:true);
        var retry=planner.Resolve(command,lobby,Spots);
        Check(retry.Choice.Spot==6&&reconciler.ShouldWrite(retry.Choice),"Rejected side move was treated as complete");
        reconciler.Remember(retry.Choice,retry.Choice);
        Check(!reconciler.ShouldWrite(retry.Choice),"Confirmed side move would be written repeatedly");
    }
    static void ConfirmedWrites()
    {
        var planner=new PersonalRhunSides();var command=Command(1,3);var lobby=Snapshot(1,1,3);
        planner.Resolve(command,lobby,Spots);
        var lower=planner.Resolve(command with{Choice=command.Choice with{Spot=1}},lobby,Spots);
        Check(lower.Choice.Spot==4,"Bottom edit did not choose the right counterpart");
        planner.RecordApplied(lower.Choice);
        // Native success is immediately followed by a deliberate undo, so the
        // next read has exactly the pre-write value rather than the target.
        var undo=planner.Resolve(command with{Choice=command.Choice with{Spot=1}},lobby,Spots);
        Check(undo.Choice.Spot==6,"Manual native undo before readback was missed after a confirmed spot write");

        planner=new();command=Command(2,3);lobby=Snapshot(1,2,3);planner.Resolve(command,lobby,Spots);
        var switchTeam=command with{Choice=command.Choice with{Team=1}};
        var applied=planner.Resolve(switchTeam,lobby,Spots);planner.RecordApplied(applied.Choice);
        var nativeUndo=planner.Resolve(switchTeam,lobby,Spots);
        Check(nativeUndo.Choice.Team==2&&nativeUndo.Choice.Spot==3,"Confirmed pending team survived a deliberate immediate native undo");
        planner=new();command=Command(2,3);lobby=Snapshot(1,2,3);planner.Resolve(command,lobby,Spots);
        var concurrent=planner.Resolve(command with{Choice=command.Choice with{Team=1}},Applied(lobby,new(0,3,3)),Spots);
        Check(concurrent.Choice.Team==0&&concurrent.State==PersonalRhunSides.State.WaitingForTeams,
            "Concurrent Fellowship team request overrode a deliberate native Random team edit");
        planner=new();planner.Resolve(command,lobby,Spots);
        concurrent=planner.Resolve(command with{Choice=command.Choice with{Team=0}},Applied(lobby,new(1,3,3)),Spots);
        Check(concurrent.Choice.Team==1&&concurrent.Choice.Spot==6,
            "Concurrent Fellowship Random request overrode a deliberate native hosting-team edit");
    }
    static NativeLobby.Snapshot Started()=>new(101,GameMap.Rhun.Path,Host,
        [Human(0,Host,1,6),Human(3,Local,1,2,true),Human(1,Other,1,4),
         Human(2,Outside,2,3),Human(4,"10.3.0.4",2,5),Human(5,"10.3.0.5",2,1)]);
    static LivePlacement.Command HistoryCommand(int team,int spot)=>Command(team,spot) with{Members=Local};
    static void History()
    {
        var command=HistoryCommand(2,0);var planner=new PersonalRhunSides();planner.RememberMatch(command,Started());
        var replay=Snapshot(0,0,0,Other,102);
        var same=planner.Resolve(command,replay,Spots);
        Check(same.State==PersonalRhunSides.State.Ready&&same.FromHistory&&same.Choice.Team==0&&same.Choice.Spot==2,
            "Confirmed teammate host with unset teams did not retain the remembered middle on the right without inventing labels");
        for(int i=0;i<3;i++)
        {
            planner.NewLobby();var repeated=planner.Resolve(command,Snapshot(0,0,0,Other,(uint)(103+i)),Spots);
            Check(repeated.FromHistory&&repeated.Choice.Spot==2,"Repeated same-team hosting or replay changed sides by game parity");
        }
        var opposite=planner.Resolve(command,Snapshot(0,0,0,Outside,110),Spots);
        Check(opposite.FromHistory&&opposite.Choice.Team==0&&opposite.Choice.Spot==5,"Confirmed opposing host did not mirror the remembered middle left");
        var newHost=planner.Resolve(HistoryCommand(0,2),Snapshot(0,0,2,"10.3.0.7",111),Spots);
        Check(newHost.State==PersonalRhunSides.State.WaitingForTeams&&!newHost.FromHistory,"An unknown new host was assigned a guessed historical team");

        planner=new();planner.RememberMatch(command,Started());
        var hostKnown=planner.Resolve(HistoryCommand(0,0),Snapshot(2,0,0,Other),Spots);
        Check(hostKnown.FromHistory&&hostKnown.Choice.Team==2&&hostKnown.Choice.Spot==2,"Known host label did not safely establish the missing local teammate label");
        planner=new();planner.RememberMatch(command,Started());
        var localKnown=planner.Resolve(HistoryCommand(1,0),Snapshot(0,2,0,Outside),Spots);
        Check(localKnown.FromHistory&&localKnown.Choice.Team==2&&localKnown.Choice.Spot==5,"Known local label was replaced while inferring an opposing host's relative side");
        planner=new();planner.RememberMatch(command,Started());
        var current=planner.Resolve(HistoryCommand(2,2),Snapshot(2,1,2,Other),Spots);
        Check(!current.FromHistory&&current.Choice.Team==1&&current.Choice.Spot==5,"Last match cohorts overrode current known native host/local teams");
        foreach(var unsupported in new[]{Snapshot(3,0,2,Other),Snapshot(0,3,2,Other),Snapshot(4,0,2,Other),Snapshot(0,4,2,Other)})
        {
            planner=new();planner.RememberMatch(command,Started());
            var thirdTeam=planner.Resolve(HistoryCommand(0,2),unsupported,Spots);
            Check(thirdTeam.State==PersonalRhunSides.State.WaitingForTeams&&!thirdTeam.FromHistory&&thirdTeam.Choice.Team==unsupported.Local.Team+1,
                "Explicit native Team 3/4 was treated as Random and renumbered using history");
        }
        planner=new();planner.RememberMatch(command,Started());
        var selected=planner.Resolve(HistoryCommand(0,0),Snapshot(0,0,1,Other),Spots);
        Check(selected.FromHistory&&selected.Choice.Spot==4,"Remembered match vertical overrode a new deliberate native bottom selection");

        planner=new();planner.RememberMatch(command,Started());
        var lobby=Snapshot(0,0,2,Other);var initial=planner.Resolve(HistoryCommand(0,2),lobby,Spots);
        lobby=Applied(lobby,initial.Choice);
        var explicitTeam=planner.Resolve(HistoryCommand(2,2),lobby,Spots);
        Check(explicitTeam.State==PersonalRhunSides.State.WaitingForTeams&&!explicitTeam.FromHistory&&explicitTeam.Choice.Team==2,
            "Historical relationship reinterpreted an explicit Fellowship team edit while the host label was unknown");
        planner=new();planner.RememberMatch(command,Started());lobby=Snapshot(0,1,2,Other);
        planner.Resolve(HistoryCommand(1,2),lobby,Spots);
        var randomTeam=planner.Resolve(HistoryCommand(1,2),Applied(lobby,new(0,2,3)),Spots);
        Check(randomTeam.State==PersonalRhunSides.State.WaitingForTeams&&randomTeam.Choice.Team==0&&!randomTeam.FromHistory,
            "A deliberate native Random team was overwritten with a historical cohort label");

        planner=new();planner.RememberMatch(command,Started());
        var contradictory=Snapshot(0,1,2,Host,101,Human(5,Other,2,4));
        var conflict=planner.Resolve(HistoryCommand(1,2),contradictory,Spots);
        Check(conflict.State==PersonalRhunSides.State.WaitingForTeams&&!conflict.FromHistory,"A former teammate's contradictory current team was ignored during inference");
        contradictory=Snapshot(0,1,2,Host,101,Human(5,Outside,1,4));
        Check(new HistoryFixture(command).Planner.Resolve(HistoryCommand(1,2),contradictory,Spots).State==PersonalRhunSides.State.WaitingForTeams,
            "Former opposing cohorts now sharing a known label did not defer historical inference");
    }
    sealed class HistoryFixture
    {
        public PersonalRhunSides Planner { get; }=new();
        public HistoryFixture(LivePlacement.Command command)=>Planner.RememberMatch(command,Started());
    }
    static void HistoryValidation()
    {
        var command=HistoryCommand(0,2);var started=Started();
        var invalid=new[]{
            started with{Slots=started.Slots.Take(5).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{Team=0}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{State=3}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{Address=""}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{Address=Outside}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{Address="not-an-ip"}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Row==5?slot with{Faction=-2}:slot).ToList()},
            started with{Slots=started.Slots.Select(slot=>slot.Local?slot with{Local=false}:slot).ToList()},
            started with{HostAddress="10.3.0.7"}
        };
        foreach(var bad in invalid)
        {
            var planner=new PersonalRhunSides();planner.RememberMatch(command,started);planner.RememberMatch(command,bad);
            var result=planner.Resolve(command,Snapshot(0,0,2,Other),Spots);
            Check(result.State==PersonalRhunSides.State.WaitingForTeams&&!result.FromHistory,"A newer incomplete, ambiguous or unbalanced match reused an older confirmed lineup");
        }
        var withObserver=started with{Slots=[..started.Slots,Human(7,"10.3.0.7",0,0) with{Faction=-2}]};
        var valid=new PersonalRhunSides();valid.RememberMatch(command,withObserver);
        Check(valid.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"An extra observer invalidated a complete six-player match");
    }
    static void HistoryLifecycle()
    {
        var command=HistoryCommand(0,2);var planner=new PersonalRhunSides();planner.RememberMatch(command,Started());
        var managed=command with{Members=string.Join('|',Started().Slots.Select(slot=>slot.Ip))};
        Check(planner.Resolve(managed,Started(),Spots).State==PersonalRhunSides.State.Disabled,"Managed match unexpectedly used personal mirroring");
        Check(planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"Managed mode reset discarded the confirmed lineup needed for later mixed hosting");
        planner.Resolve(command with{Room="other-room"},Snapshot(0,0,2,Other),Spots);
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"Historical teams leaked back across Fellowship rooms");
        planner.RememberMatch(command,Started());
        planner.Resolve(command with{Map=@"maps\other\other.map"},Snapshot(0,0,2,Other),Spots);
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"Historical teams leaked across maps");
        planner.RememberMatch(command,Started());planner.RememberMatch(command with{Rhun3v3=false},Started());
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"An unsupported newer match retained old Rhûn cohort inference");
        planner.RememberMatch(command,Started());planner.RememberMatch(command,null);
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"An unreadable newer match reused old cohort inference");
        planner.RememberMatch(command,Started());planner.Resolve(command with{Rhun3v3=false},Snapshot(0,0,2,Other),Spots);
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"Changing game mode retained historical team assumptions");
        planner.RememberMatch(command,Started());planner.Reset();
        Check(!planner.Resolve(command,Snapshot(0,0,2,Other),Spots).FromHistory,"Full planner reset retained historical cohort evidence");
        planner.RememberMatch(command,Started());
        var stranger=Snapshot(0,0,2,Other) with{Slots=Snapshot(0,0,2,Other).Slots.Select(slot=>slot.Local?slot with{Address="10.3.0.8"}:slot).ToList()};
        Check(!planner.Resolve(command,stranger,Spots).FromHistory,"A new local player's IP inherited another player's confirmed relationship");
    }
    static void Gates()
    {
        var command=Command(1,3);var lobby=Snapshot(1,1,3);
        Check(new PersonalRhunSides().Resolve(command with{Rhun3v3=false},lobby,Spots).State==PersonalRhunSides.State.Disabled,"Other room mode received personal Rhûn mirroring");
        Check(new PersonalRhunSides().Resolve(command with{Map=@"maps\other\other.map"},lobby,Spots).State==PersonalRhunSides.State.Disabled,"Another map received personal mirroring");
        Check(new PersonalRhunSides().Resolve(command with{AllowExternalHost=false},lobby,Spots).State==PersonalRhunSides.State.Disabled,"Legacy command opted into personal side writes");
        Check(new PersonalRhunSides().Resolve(command,lobby,Spots[..5]).State==PersonalRhunSides.State.Disabled,"Incomplete map geometry guessed a mirror");
        var managed=command with{Members=command.Members+"|"+Host};
        Check(new PersonalRhunSides().Resolve(managed,lobby,Spots).State==PersonalRhunSides.State.Disabled,"Personal planner displaced a fully managed shared Rhûn plan");
        var observer=lobby with{Slots=lobby.Slots.Select(slot=>slot.Local?slot with{Faction=-2,Spot=-1}:slot).ToList()};
        Check(new PersonalRhunSides().Resolve(command,observer,Spots).Choice.Spot==0,"Observer was reseated as an active player");
    }
}

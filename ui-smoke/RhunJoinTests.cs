using Fellowship;
using System.Text.Json;

static class RhunJoinTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static string Ip(int id)=>"10.0.0."+id;
    static NativeLobby.Slot P(int id,int team,int spot,bool observer=false)=>
        new(id,6,spot-1,team-1,false,Faction:observer?-2:-1,Address:Ip(id));
    static Room Room()=>new(){Mode="3v3",
        Spots=Enumerable.Range(0,6).Select(i=>new MapSpot(i+1,i<3?.2:.8,(i%3)*.4+.1)).ToList(),
        Players=Enumerable.Range(1,8).Select(i=>new Player{Address=Ip(i)}).ToList()};
    static NativeLobby.Snapshot S(Room room,params NativeLobby.Slot[] slots)=>S(room,1,slots);
    static NativeLobby.Snapshot S(Room room,int host,params NativeLobby.Slot[] slots)=>
        new(123,room.MapPath,Ip(host),slots.OrderByDescending(s=>s.Ip==Ip(host)).ThenBy(s=>s.Ip,StringComparer.Ordinal)
            .Select((s,i)=>s with{Row=i,Local=i==0}).ToList());
    static bool Apply(Room room,string session,NativeLobby.Snapshot snapshot,bool started=false)=>
        RhunSides.Apply(room,new(session,snapshot,started),snapshot.HostAddress);
    static NativeLobby.Snapshot Targets(Room room,NativeLobby.Snapshot snapshot,bool clear=false)=>snapshot with
    {Slots=snapshot.Slots.Select(s=>room.SidePlan!.Moves.TryGetValue(s.Ip,out var move)
        ?s with{Team=move.Team-1,Spot=clear?-1:move.Spot-1}:s).ToList()};
    static NativeLobby.Snapshot Complete(Room room,string session,NativeLobby.Snapshot snapshot)
    {
        var target=Targets(room,snapshot);
        for(int i=0;i<5;i++)Apply(room,session,target);
        Check(room.SidePlan!.Ready&&!room.SidePlan.Cancelled,"Target selections did not settle the shared plan");
        Check(room.SidePlan.Moves.Keys.All(room.SidePlan.Confirmed.Contains),"Settled players were not confirmed");
        return target;
    }
    static void Unique(Room room,string message)=>Check(room.SidePlan!.Moves.Values.Where(m=>m.Spot>0)
        .GroupBy(m=>m.Spot).All(g=>g.Count()==1),message);

    public static void Run()
    {
        // Assign before the roster is full, then add both teammates and opponents
        // without recreating the native lobby or changing established selections.
        var room=Room();var session=Guid.NewGuid().ToString("N");var native=S(room,P(1,1,1));
        Check(Apply(room,session,native),"Early one-player side plan was not created");
        Check(room.SidePlan!.Moves.Count==1&&room.SidePlan.Moves[Ip(1)].Spot==4,"Early host was not placed on the right");
        var clear=Targets(room,native,true);
        Check(Apply(room,session,clear)&&room.SidePlan.Ready,"One-player clear phase did not finish");
        native=Complete(room,session,clear);
        int revision=room.SidePlan.Revision;string key=room.SidePlan.Key;
        native=S(room,native.Slots[0],P(2,2,5),P(4,1,2));
        Check(Apply(room,session,native),"Ready early plan ignored later arrivals");
        Check(room.SidePlan.Session==session&&room.SidePlan.Revision>revision&&room.SidePlan.Key!=key,"Roster update did not create a new revision within the native session");
        Check(room.SidePlan.Moves.Count==3&&room.SidePlan.Moves[Ip(1)].Spot==4&&room.SidePlan.Moves[Ip(2)].Spot==5&&room.SidePlan.Moves[Ip(4)].Spot==2,"Later arrivals displaced explicit selections");
        native=Complete(room,session,native);Unique(room,"Early roster growth assigned duplicate positions");
        revision=room.SidePlan.Revision;key=room.SidePlan.Key;
        for(int i=0;i<4;i++)Check(!Apply(room,session,native),"Duplicate settled report changed the plan");
        Check(room.SidePlan.Revision==revision&&room.SidePlan.Key==key,"Duplicate reports created revision loops");

        // An arrival cannot replace a plan while its existing participants are
        // still clearing occupied seats. It joins once that phase has settled.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(4,2,4));
        Apply(room,session,native);revision=room.SidePlan!.Revision;key=room.SidePlan.Key;
        var joining=S(room,native.Slots.Concat(new[]{P(2,2,5)}).ToArray());
        Apply(room,session,joining);
        Check(room.SidePlan.Revision==revision&&room.SidePlan.Key==key&&!room.SidePlan.Moves.ContainsKey(Ip(2))&&!room.SidePlan.Cancelled,"Arrival interrupted a pending clear phase");
        clear=Targets(room,joining,true);
        for(int i=0;i<3&&!room.SidePlan.Moves.ContainsKey(Ip(2));i++)Apply(room,session,clear);
        Check(room.SidePlan.Moves.ContainsKey(Ip(2))&&room.SidePlan.Revision>revision,"Deferred arrival was never added after the clear phase");
        Check(room.SidePlan.Moves[Ip(1)].Spot==4&&room.SidePlan.Moves[Ip(4)].Spot==1,"Intermediate Random selections lost outstanding targets");
        Complete(room,session,clear);Unique(room,"Arrival during clearing collided with existing targets");

        // Joining the host's old native Team 1 before it becomes Team 2 means
        // joining that cohort, not silently becoming an opponent during clear.
        // A subsequent explicit native team change updates that intent.
        foreach(bool changeTeam in new[]{false,true})
        {
            room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(4,2,4));
            Apply(room,session,native);joining=S(room,native.Slots.Append(P(2,1,2)).ToArray());Apply(room,session,joining);
            Check(room.SidePlan!.Arrivals.TryGetValue(Ip(2),out var arrival)&&arrival.NativeTeam==1&&arrival.Team==2,"Pending arrival did not retain its choice of the host's old team");
            if(changeTeam)
            {
                joining=joining with{Slots=joining.Slots.Select(s=>s.Ip==Ip(2)?s with{Team=1}:s).ToList()};
                Apply(room,session,joining);
                Check(room.SidePlan.Arrivals[Ip(2)].NativeTeam==2&&room.SidePlan.Arrivals[Ip(2)].Team==1,"Explicit native team change did not update pending cohort intent");
            }
            clear=Targets(room,joining,true);
            for(int i=0;i<3&&!room.SidePlan.Moves.ContainsKey(Ip(2));i++)Apply(room,session,clear);
            Check(room.SidePlan.Moves.TryGetValue(Ip(2),out var joined)&&joined.Team==(changeTeam?1:2)&&joined.Spot==(changeTeam?2:5),"Host clear reinterpreted the arrival's previously selected team");
            Complete(room,session,clear);Unique(room,"Pending cohort intent produced colliding positions");
        }

        // Ready means that clearing is complete, not that everybody has claimed
        // a seat yet. A new revision must carry those unconfirmed targets.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(4,2,4));
        Apply(room,session,native);clear=Targets(room,native,true);Apply(room,session,clear);
        Check(room.SidePlan!.Ready&&!room.SidePlan.Confirmed.Contains(Ip(1)),"Clear phase was mistaken for a confirmed seat");
        revision=room.SidePlan.Revision;joining=S(room,clear.Slots.Concat(new[]{P(2,2,5)}).ToArray());
        Check(Apply(room,session,joining)&&room.SidePlan.Revision>revision,"Arrival during the claim phase did not revise the plan");
        Check(room.SidePlan.Moves[Ip(1)].Spot==4&&room.SidePlan.Moves[Ip(4)].Spot==1&&room.SidePlan.Moves[Ip(2)].Spot==5,"Claim-phase arrival overwrote carried or explicit positions");
        Complete(room,session,joining);

        // A helper can receive a Ready revision before claiming its target.
        // Explicit native edits at that point must end coordination gracefully,
        // rather than leaving it waiting forever for an obsolete nonzero target.
        foreach(bool changeTeam in new[]{false,true})
        {
            room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(4,2,4));
            Apply(room,session,native);clear=Targets(room,native,true);Apply(room,session,clear);
            joining=S(room,clear.Slots.Append(P(2,2,5)).ToArray());Apply(room,session,joining);Apply(room,session,joining);
            Check(room.SidePlan!.Ready&&room.SidePlan.Revision>0&&!room.SidePlan.Confirmed.Contains(Ip(1)),"Ready manual-edit fixture did not retain an unconfirmed participant");
            var readyEdit=joining with{Slots=joining.Slots.Select(s=>s.Ip==Ip(1)
                ?changeTeam?s with{Team=0,Spot=-1}:s with{Spot=5}:s).ToList()};
            Check(Apply(room,session,readyEdit)&&room.SidePlan.Cancelled,"Ready revision kept waiting after an explicit native spot or team edit");
            var local=room.Players.Single(p=>p.Address==Ip(1));
            Check(local.Team==(changeTeam?1:2)&&local.Spot==(changeTeam?0:6),"Ready cancellation did not retain the explicit native choice");
            var unchanged=JsonSerializer.Serialize(room);
            Check(!Apply(room,session,readyEdit)&&JsonSerializer.Serialize(room)==unchanged,"Repeated cancelled report changed the native choices or plan");
        }

        // A participant becoming an observer releases only their seat. Remaining
        // participants can finish rather than cancelling the whole cohort.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(2,1,2),P(4,2,4));
        Apply(room,session,native);revision=room.SidePlan!.Revision;
        var observing=native with{Slots=native.Slots.Select(s=>s.Ip==Ip(2)?s with{Faction=-2,Spot=-1}:s).ToList()};
        Check(Apply(room,session,observing),"Observer transition did not update the pending roster");
        Check(room.SidePlan.Revision>revision&&!room.SidePlan.Cancelled&&!room.SidePlan.Moves.ContainsKey(Ip(2)),"Observer cancelled the remaining pending assignments");
        Check(room.SidePlan.Moves[Ip(1)].Spot==4&&room.SidePlan.Moves[Ip(4)].Spot==1&&room.Players.Single(p=>p.Address==Ip(2)).Spot==0,"Observer pruning changed another target or retained its seat");
        Complete(room,session,observing);

        // A confirmed player may have selected another seat or Random since
        // confirmation. A departure's new revision must use that current choice,
        // rather than waiting for or restoring the obsolete target.
        foreach(int selected in new[]{0,5})
        foreach(bool observer in new[]{false,true})
        {
            room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,2,4),P(2,2,5),P(4,1,1));
            Apply(room,session,native);native=Complete(room,session,native);revision=room.SidePlan!.Revision;
            var departed=native with{Slots=native.Slots.Where(s=>observer||s.Ip!=Ip(2))
                .Select(s=>s.Ip==Ip(1)?s with{Spot=selected-1}:
                    s.Ip==Ip(2)?s with{Faction=-2,Spot=-1}:s).ToList()};
            Check(Apply(room,session,departed)&&room.SidePlan.Revision>revision,"Departure did not revise the completed layout");
            Check(!room.SidePlan.Cancelled&&!room.SidePlan.Moves.ContainsKey(Ip(2))&&room.SidePlan.Moves[Ip(1)].Spot==selected,"Departure restored an obsolete confirmed seat or overrode manual Random");
            Check(room.Players.Single(p=>p.Address==Ip(1)).Spot==selected,"Departure published an obsolete player choice");
            Complete(room,session,departed);
        }

        // During an unfinished clear phase, departure plus a separate explicit
        // edit cancels stale writes and imports the native state together.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,1,1),P(2,1,2),P(4,2,4));
        Apply(room,session,native);revision=room.SidePlan!.Revision;
        var departureAndEdit=S(room,P(1,2,6),P(2,1,0,true),P(4,2,4));
        Check(Apply(room,session,departureAndEdit)&&room.SidePlan.Revision>revision&&room.SidePlan.Cancelled,"Departure hid a concurrent manual edit during clear");
        Check(room.Players.Single(p=>p.Address==Ip(1)).Spot==6&&room.Players.Single(p=>p.Address==Ip(4)).Spot==4&&room.Players.Single(p=>p.Address==Ip(2)).Spot==0,"Pending departure restored stale targets instead of keeping native choices");

        // A continuing player may voluntarily yield their old seat and choose
        // another; the substitute's explicit choice beats historical ownership.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,2,4),P(2,2,5),P(4,1,1),P(5,1,2));
        Apply(room,session,native);native=Complete(room,session,native);
        room.LastRhunHost=Ip(1);room.LastRhunLineup=native.Slots.ToDictionary(s=>s.Ip,s=>new LivePlacement.Choice(s.Team+1,s.Spot+1));
        joining=S(room,native.Slots.Select(s=>s.Ip==Ip(2)?s with{Spot=5}:s).Append(P(3,2,5)).ToArray());
        Check(Apply(room,session,joining),"Explicit substitute choice did not update the roster");
        // A Ready layout first cancels/imports the deliberate edit; the next
        // stable report can then include the arrival in a fresh revision.
        if(room.SidePlan!.Cancelled)Check(Apply(room,session,joining),"Cancelled layout did not reconsider the newly active substitute");
        Check(room.SidePlan!.Moves[Ip(2)].Spot==6&&room.SidePlan.Moves[Ip(3)].Spot==5,"Remembered ownership overrode a voluntary seat exchange");
        Complete(room,session,joining);Unique(room,"Voluntary substitute exchange created duplicate positions");

        // Once a player has confirmed their placement, a later explicit Random
        // selection remains Random when another participant joins.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,2,4),P(2,2,5),P(4,1,1));
        Apply(room,session,native);native=Complete(room,session,native);
        room.LastRhunLineup[Ip(2)]=new(2,5);
        var random=native with{Slots=native.Slots.Select(s=>s.Ip==Ip(2)?s with{Spot=-1}:s).ToList()};
        Apply(room,session,random);joining=S(room,random.Slots.Append(P(3,2,6)).ToArray());
        Check(Apply(room,session,joining),"Arrival after manual Random did not revise the roster");
        Check(room.SidePlan!.Moves[Ip(2)].Spot==0&&room.Players.Single(p=>p.Address==Ip(2)).Spot==0,"Roster growth restored a seat that its player deliberately cleared");
        Check(room.SidePlan.Moves[Ip(3)].Spot==6,"Manual Random disrupted the newcomer's explicit selection");
        Complete(room,session,joining);

        // A returning player can use an available seat, but cannot reclaim their
        // old seat from a substitute who now explicitly occupies it.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,2,4),P(2,2,5),P(4,1,1));
        Apply(room,session,native);native=Complete(room,session,native);room.LastRhunLineup[Ip(2)]=new(2,5);
        observing=native with{Slots=native.Slots.Select(s=>s.Ip==Ip(2)?s with{Faction=-2,Spot=-1}:s).ToList()};
        Apply(room,session,observing);
        joining=S(room,observing.Slots.Append(P(3,2,5)).ToArray());Apply(room,session,joining);native=Complete(room,session,joining);
        var returning=native with{Slots=native.Slots.Select(s=>s.Ip==Ip(2)?s with{Faction=-1,Spot=-1,Team=1}:s).ToList()};
        Check(Apply(room,session,returning),"Returning active player did not rejoin the side plan");
        Check(room.SidePlan!.Moves[Ip(3)].Spot==5&&room.SidePlan.Moves[Ip(2)].Spot!=5,"Returnee displaced the substitute from an explicitly selected seat");
        Complete(room,session,returning);Unique(room,"Returning player collided with an occupied seat");

        // Invalid overcrowding must not partially mutate the existing shared
        // roster, and no roster changes can revise a plan after match start.
        room=Room();session=Guid.NewGuid().ToString("N");native=S(room,P(1,2,4),P(2,2,5),P(4,1,1));
        Apply(room,session,native);native=Complete(room,session,native);
        var before=JsonSerializer.Serialize(room);var previousPlan=room.SidePlan;
        var overcrowded=S(room,native.Slots.Append(P(3,2,6)).Append(P(7,2,0)).ToArray());
        Check(!Apply(room,session,overcrowded)&&ReferenceEquals(previousPlan,room.SidePlan)&&JsonSerializer.Serialize(room)==before,"Four-player side partially changed the valid plan");
        Check(Apply(room,session,native,true)&&room.SidePlan!.Started,"Match start was not recorded");
        before=JsonSerializer.Serialize(room);previousPlan=room.SidePlan;
        Check(!Apply(room,session,S(room,native.Slots.Append(P(3,2,6)).ToArray()))&&ReferenceEquals(previousPlan,room.SidePlan)&&JsonSerializer.Serialize(room)==before,"Late roster report revised a started match");

        // Native team labels may reset when a teammate hosts. A new substitute
        // on that same native team inherits its anchored historical cohort.
        room=Room();session=Guid.NewGuid().ToString("N");room.LastRhunHost=Ip(1);
        foreach(int id in Enumerable.Range(1,6))room.LastRhunLineup[Ip(id)]=new(id<=3?2:1,id<=3?id+3:id-3);
        native=S(room,2,P(2,1,0),P(1,1,0),P(7,1,2),P(4,2,0));
        Check(Apply(room,session,native),"Reset-label teammate-host lobby was not planned");
        Check(room.SidePlan!.Moves[Ip(7)].Team==2&&room.SidePlan.Moves[Ip(7)].Spot==5,"New teammate was misclassified by reset native labels");
        Check(room.SidePlan.Moves[Ip(4)].Team==1,"Remembered opposing cohort was placed on the host side");
        Unique(room,"Historical cohort inference collided with an explicit newcomer");

        // Cancelling a pending layout imports the native choices together: old
        // target reservations must not block either player's deliberate edit.
        room=Room();session=Guid.NewGuid().ToString("N");
        room.Players.Single(p=>p.Address==Ip(2)).Spot=5;
        room.Players.Single(p=>p.Address==Ip(5)).Spot=2;
        native=S(room,P(1,1,1),P(4,2,4));Apply(room,session,native);
        var edited=S(room,P(1,2,5),P(4,1,2));
        Check(Apply(room,session,edited)&&room.SidePlan!.Cancelled,"Explicit edits did not cancel the pending side layout");
        Check(room.Players.Single(p=>p.Address==Ip(1)).Spot==5&&room.Players.Single(p=>p.Address==Ip(4)).Spot==2,"Cancelled targets overrode native choices during atomic import");
        foreach(var (id,spot) in new[]{(2,5),(5,2)})
        {
            var absent=room.Players.Single(p=>p.Address==Ip(id));
            Check(absent.Spot==0&&absent.LastMapSpots.GetValueOrDefault(room.MapPath)==spot,"Absent room reservation blocked an explicit native choice or lost its memory");
        }

        // A room participant who has not joined BFME2 cannot reserve a native
        // seat against an active player; unrelated remembered choices remain.
        room=Room();session=Guid.NewGuid().ToString("N");
        room.Players.Single(p=>p.Address==Ip(2)).Spot=4;
        room.Players.Single(p=>p.Address==Ip(5)).Spot=6;
        Check(Apply(room,session,S(room,P(1,1,1))),"Absent-reservation fixture did not produce an early plan");
        var released=room.Players.Single(p=>p.Address==Ip(2));
        Check(released.Spot==0&&released.LastMapSpots.GetValueOrDefault(room.MapPath)==4,"Active assignment retained a conflicting absent reservation");
        Check(room.Players.Single(p=>p.Address==Ip(5)).Spot==6,"Active assignment cleared an unrelated absent choice");

        // Valid native bounds still include spots that do not exist on Rhun.
        // Reject those and duplicate native choices without changing the room.
        room=Room();session=Guid.NewGuid().ToString("N");before=JsonSerializer.Serialize(room);
        Check(!Apply(room,session,S(room,P(1,1,7)))&&JsonSerializer.Serialize(room)==before,"Out-of-map native spot partially changed the room");
        Check(!Apply(room,session,S(room,P(1,1,1),P(4,2,1)))&&JsonSerializer.Serialize(room)==before,"Duplicate native spots were accepted");
        room.LastRhunHost=Ip(1);room.LastRhunLineup[Ip(2)]=new(2,5);room.LastRhunLineup[Ip(4)]=new(1,1);
        before=JsonSerializer.Serialize(room);
        Check(!Apply(room,session,S(room,2,P(2,1,0),P(4,1,0),P(7,1,0)))&&JsonSerializer.Serialize(room)==before,"Ambiguous newcomer cohort was guessed instead of deferred");

        // Revision metadata must remain within the real helper's 4 KiB frame,
        // including an eight-slot lobby and the maximum room membership list.
        room=Room();session=Guid.NewGuid().ToString("N");
        var boundedPlan=new SidePlan{Session=session,Revision=int.MaxValue,Host=Ip(1),Ready=true,
            Moves=Enumerable.Range(1,6).ToDictionary(Ip,id=>new SideMove(id<=3?2:1,id,id<=3?2:1,id)),
            Confirmed=Enumerable.Range(1,6).Select(Ip).ToHashSet(),
            Arrivals=new(){[Ip(7)]=new(1,2),[Ip(8)]=new(2,1)}};
        var members=string.Join("|",Enumerable.Range(1,64).Select(id=>"10.255.255."+id));
        var command=new LivePlacement.Command(Guid.NewGuid().ToString("N"),room.MapPath,Ip(1),new(2,4,10),
            NativeSetup.From(room),members,Rhun3v3:true,Sides:boundedPlan);
        var allSlots=S(room,Enumerable.Range(1,8).Select(id=>P(id,id<=3?2:1,id<=6?id:0,id>6)).ToArray());
        Check(JsonSerializer.SerializeToUtf8Bytes(command).Length<4096,"Revised side plan and maximum room members exceed helper IPC bound");
        Check(JsonSerializer.SerializeToUtf8Bytes(new LivePlacement.Reply("Layout",Lobby:new(session,allSlots))).Length<4096,"Eight-slot native report exceeds helper IPC bound");

        NoReturningPriority();
        Console.WriteLine("PASS Rhun early assignment and roster revisions: pending/ready joins, observer pruning, voluntary substitute choices, manual Random, no returning ownership, idempotence, overcrowding, match-start freeze, atomic cancellation and absent reservations");
    }

    static void NoReturningPriority()
    {
        var room=Room();var rotation=new LobbyRotation();var time=TimeSpan.Zero;
        void Settle(NativeLobby.Snapshot snapshot)
        {for(int i=0;i<7;i++){rotation.Observe(snapshot,time);time+=TimeSpan.FromMilliseconds(400);}}
        var occupied=S(room,7,P(7,1,1),P(8,1,2));Settle(occupied);
        var observers=occupied with{Slots=occupied.Slots.Select(s=>s with{Faction=-2,Spot=-1}).ToList()};Settle(observers);
        var waiting=S(room,7,P(7,1,0),P(8,1,0,true),P(1,1,0));Settle(waiting);
        var newcomer=waiting with{Slots=waiting.Slots.Select(s=>s with{Local=s.Ip==Ip(1)}).ToList()};
        Check(rotation.Replacement(newcomer)?.Spot==1,"Returning ownership took priority over the stable order of unassigned players");
        var returning=waiting with{Slots=waiting.Slots.Select(s=>s with{Local=s.Ip==Ip(7)}).ToList()};
        Check(rotation.Replacement(returning)?.Spot==2,"Random returnee reserved its old seat instead of another free seat");
    }
}

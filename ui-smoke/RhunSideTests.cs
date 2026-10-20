using Fellowship;

static class RhunSideTests
{
    public static void Run()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        var room=new Room{Mode="3v3",Spots=Enumerable.Range(0,6).Select(i=>new MapSpot(i+1,i<3?.2:.8,(i%3)*.4+.1)).ToList(),
            Players=Enumerable.Range(1,6).Select(i=>new Player{Address="10.0.0."+i,Team=i<=3?1:2,Spot=i}).ToList()};
        NativeLobby.Snapshot Snapshot(string host)=>new(123,room.MapPath,host,
            room.Players.OrderByDescending(p=>p.Address==host).Select((p,i)=>new NativeLobby.Slot(i,6,p.Spot-1,p.Team-1,i==0,Address:p.Address)).ToList());
        string first="10.0.0.1",second="10.0.0.4";
        var initial=Snapshot(first);var report=new LobbyReport(Guid.NewGuid().ToString("N"),initial);
        Check(!RhunSides.Apply(room,report,second),"Accepted a report from someone other than the native host");
        Check(RhunSides.Apply(room,report,first),"First side plan missing");
        Check(room.Players.First().Team==2&&room.Players.First().Spot==4,"First host was not assigned to the right");
        Check(room.SidePlan!.Moves.Values.Select(m=>m.Spot).Distinct().Count()==6,"Side plan collided");
        Check(!RhunSides.Apply(room,report,first),"Unchanged report recreated plan");
        var cleared=initial with{Slots=initial.Slots.Select(s=>s with{Team=room.SidePlan.Moves[s.Ip].Team-1,Spot=-1}).ToList()};
        Check(RhunSides.Apply(room,report with{Lobby=cleared},first)&&room.SidePlan.Ready,"Clear phase did not release assignment phase");
        var game1=Snapshot(first);
        Check(RhunSides.Apply(room,report with{Lobby=game1,Started=true},first),"Match start did not capture hosting history");
        Check(!RhunSides.Apply(room,report with{Lobby=game1,Started=true},first),"Repeated start recaptured history");
        var before=room.Players.ToDictionary(p=>p.Address,p=>p.Spot);
        foreach(string teammate in new[]{"10.0.0.2","10.0.0.3"})
        {
            // Native team numbers can reset in a recreated lobby. Membership and
            // positions from the previous match still identify the hosting team.
            var sameTeamLobby=Snapshot(teammate);
            sameTeamLobby=sameTeamLobby with{Slots=sameTeamLobby.Slots.Select(s=>s with{Team=1-s.Team,Spot=-1}).ToList()};
            var sameTeam=new LobbyReport(Guid.NewGuid().ToString("N"),sameTeamLobby);
            Check(RhunSides.Apply(room,sameTeam,teammate),"Same-team host did not create a plan");
            foreach(var player in room.Players)
                Check(player.Spot==before[player.Address],"Changing host within the same team moved a player");
            var stablePlan=room.SidePlan;
            Check(!RhunSides.Apply(room,sameTeam,teammate)&&ReferenceEquals(stablePlan,room.SidePlan),"Repeated same-team report changed the plan");
            Check(RhunSides.Apply(room,sameTeam with{Lobby=Snapshot(teammate),Started=true},teammate),"Same-team match history was not recorded");
        }
        Console.WriteLine("PASS consecutive same-team BFME2 host changes preserve all spots despite reset native team numbers and positions");
        var next=new LobbyReport(Guid.NewGuid().ToString("N"),Snapshot(second));
        Check(RhunSides.Apply(room,next,second),"Next host did not create a plan");
        foreach(var player in room.Players)
        {
            var old=room.Spots.Single(s=>s.Number==before[player.Address]);
            var spot=room.Spots.Single(s=>s.Number==player.Spot);
            Check(Math.Abs(old.Y-spot.Y)<.001&&old.X!=spot.X,"Host change did not preserve vertical position");
            Check((player.Team==2)==(spot.X>.5),"Team number and map side disagree");
        }
        var nextPlan=room.SidePlan;
        Check(!RhunSides.Apply(room,next,second)&&ReferenceEquals(nextPlan,room.SidePlan),"Duplicate reports flipped the sides again");
        var edited=next.Lobby with{Slots=next.Lobby.Slots.Select(s=>s.Ip==first?s with{Team=3}:s).ToList()};
        Check(RhunSides.Apply(room,next with{Lobby=edited},second)&&room.SidePlan!.Cancelled,"Concurrent manual edit was overwritten");
        room.Mode="2v2";Check(!RhunSides.Apply(room,new(Guid.NewGuid().ToString("N"),Snapshot(first)),first),"2v2 received Rhun rules");
        room.Mode="Custom";Check(!RhunSides.Enabled(room),"Custom mode received Rhun rules");
        room.Mode="3v3";room.MapPath="maps\\other.map";Check(!RhunSides.Enabled(room),"Other map received Rhun rules");
        var payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new LivePlacement.Reply("Layout",Lobby:report));
        Check(payload.Length<4096,"Lobby report exceeds helper IPC bound");
        var clearRequest=NativePlacement.SideClearRequestBytes(1,initial,new(1,1,2,4));
        Check(BitConverter.ToInt32(clearRequest,568)==3&&BitConverter.ToInt32(clearRequest,1084)==0&&BitConverter.ToInt32(clearRequest,1088)==0,"Guarded side-clear ABI incorrect");
        Console.WriteLine("PASS Rhun 3v3 host/cohort history, vertical mirroring, shared two-phase plan, no repeated flips, manual cancellation, mode/map gating and IPC bounds");
    }
}

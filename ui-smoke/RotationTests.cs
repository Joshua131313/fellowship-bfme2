using Fellowship;
using System.IO;

static class RotationTests
{
    public static void Run()
    {
        NativeLobby.Slot P(string id,int team,int spot,bool local=false,int faction=-1,int row=0)=>
            new(row,6,spot,team,local,3,faction,id);
        NativeLobby.Snapshot S(params NativeLobby.Slot[] players)=>new(123,"maps\\test.map","host",players.ToList());
        var time=TimeSpan.Zero;
        bool Settle(LobbyRotation rotation,NativeLobby.Snapshot snapshot)
        {
            bool settled=false;
            for(int i=0;i<7;i++){settled=rotation.Observe(snapshot,time);time+=TimeSpan.FromMilliseconds(400);}
            return settled;
        }
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        foreach(int count in new[]{2,3,4})
        {
            var rotation=new LobbyRotation();
            var players=Enumerable.Range(0,count*2).Select(i=>P("p"+i,i/count,i,local:i==0,row:i)).ToArray();
            Check(Settle(rotation,S(players)),"Initial roster did not settle");
            Check(rotation.Replacement(S(players))==null,"Moved an assigned player");
            // Player 0 observes; the replacement is initially an observer too.
            players[0]=players[0] with{Faction=-2,Spot=-1,Local=false};
            var waiting=players.Append(P("replacement",0,-1,true,-2,7)).ToArray();
            Settle(rotation,S(waiting));
            Check(rotation.Replacement(S(waiting))==null,"Assigned an observer");
            waiting[^1]=waiting[^1] with{Faction=-1};
            Check(!rotation.Observe(S(waiting),time),"Skipped transition stability");
            Settle(rotation,S(waiting));
            Check(rotation.Replacement(S(waiting))?.Spot==1,"Replacement did not inherit native team spot");
            var wrong=waiting.ToArray();wrong[^1]=wrong[^1] with{Team=-1};Settle(rotation,S(wrong));
            Check(rotation.Replacement(S(wrong))==null,"Guessed a random team");
            wrong[^1]=wrong[^1] with{Team=1};Settle(rotation,S(wrong));
            Check(rotation.Replacement(S(wrong))==null,"Stole the opposing team's seat");
        }
        var connecting=new LobbyRotation();var joining=S(P("joining",0,-1,true,-2));Settle(connecting,joining);
        Check(!connecting.WasActive(joining.Local),"Initial observer would clear saved preferences");
        var r=new LobbyRotation();
        var baseline=S(P("a",0,0,true),P("b",0,1,row:1),P("c",1,2,row:2));
        Settle(r,baseline);
        var manual=baseline with{Slots=[P("a",0,-1,true),baseline.Slots[1],baseline.Slots[2]]};
        Settle(r,manual);Check(r.Replacement(manual)==null,"Undid manual Random");
        var replaced=S(P("a",0,-1,false,-2),P("b",0,-1,false,-2,1),P("c",1,2,row:2),P("d",0,-1,true,row:3),P("e",0,-1,row:4));
        Settle(r,replaced);Check(r.Replacement(replaced)?.Spot==1,"First replacement allocation failed");
        var other=replaced with{Slots=replaced.Slots.Select(p=>p with{Local=p.Address=="e"}).ToList()};
        Check(r.Replacement(other)?.Spot==2,"Concurrent replacements collided");
        var explicitSpot=replaced with{Slots=replaced.Slots.Select(p=>p.Local?p with{Spot=5}:p).ToList()};
        Settle(r,explicitSpot);Check(r.Replacement(explicitSpot)==null,"Overwrote native spot selection");
        var changedMap=replaced with{Map="maps\\different.map"};
        Settle(r,changedMap);Check(r.Replacement(changedMap)==null,"Leaked positions between maps");

        r=new();Settle(r,baseline);
        // A brief initial observer state cannot release anybody's existing spot.
        var transient=S(P("a",0,-1,true,-2),baseline.Slots[1],baseline.Slots[2]);
        r.Observe(transient,time);time+=TimeSpan.FromMilliseconds(400);
        Settle(r,baseline);Check(r.Replacement(baseline)==null,"Temporary observer moved an active player");
        r.Suspend();Check(!r.Observe(baseline,time),"Read interruption bypassed stability");
        Settle(r,baseline);
        var recreated=S(P("a",0,-1,true),P("d",0,-1,row:1)) with{Identity=456};
        Settle(r,recreated);Check(r.Replacement(recreated)?.Spot==1,"Returning player lost prior seat after lobby recreation");
        r.NewSession();var reused=recreated with{Slots=[P("a",0,-1,true)]};
        Settle(r,reused);Check(r.Replacement(reused)?.Spot==1,"Reused native lobby identity lost rotation state");
        var bytes=NativePlacement.RotationRequestBytes(12,recreated,1);
        Check(BitConverter.ToInt32(bytes,568)==2&&BitConverter.ToInt32(bytes,20)==0&&BitConverter.ToInt32(bytes,24)==0,"Automatic placement bridge mode failed");
        Check(new NativeLobby.Slot(0,6,-1,0,true,Faction:-2).Observer,"Observer faction decoding failed");
        Check(!new NativeLobby.Slot(0,6,-1,0,true,Faction:-1).Observer,"Random faction mistaken for observer");
        Console.WriteLine("PASS automatic native team rotations: 2/3/4-player teams, observers, transient joins, manual choices, multiple replacements, random teams, map/session changes");
    }
}

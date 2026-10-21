using Fellowship;
using System.IO;

static class InviteMembershipTests
{
    static Settings SettingsFor(string name){var settings=Settings.InMemory();settings.Name=name;return settings;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Rejected(Func<Task> action,string expected,string description)
    {
        try{await action().WaitAsync(TimeSpan.FromSeconds(2));}
        catch(InvalidOperationException error)when(error.Message==expected){Console.WriteLine("PASS "+description);return;}
        throw new Exception("FAIL "+description);
    }
    public static async Task Run()
    {
        var inviterSettings=SettingsFor("Invitation owner");var otherSettings=SettingsFor("Other room owner");var idleSettings=SettingsFor("Idle friend");
        using var inviter=new PeerService(inviterSettings);using var other=new PeerService(otherSettings);using var idle=new PeerService(idleSettings);
        inviter.Start("127.0.0.241");other.Start("127.0.0.244");idle.Start("127.0.0.245");
        const string connected="127.0.0.242",reconnecting="127.0.0.243";
        inviterSettings.Friends=[new("Self",inviter.Address),new("Connected",connected),new("Reserved",reconnecting),new("Other room owner",other.Address),new("Idle friend",idle.Address)];
        otherSettings.Friends=[new("Invitation owner",inviter.Address)];idleSettings.Friends=[new("Invitation owner",inviter.Address)];
        inviter.Host(new(){Title="Current invitation room"},[]);other.Host(new(){Title="Other room"},[]);
        inviter.Current!.Players.Add(new(){Id="connected",Name="Connected",Address=connected});
        inviter.Current.Players.Add(new(){Id="reserved",Name="Reserved",Address=reconnecting,Reconnecting=true});
        const string memberError="This player is already in your room.";
        // Neither synthetic member has a listener. Only the central preflight
        // can return this exact error before any attempted TCP connection.
        await Rejected(()=>inviter.Invite(connected),memberError,"connected room member invitation is rejected before network access");
        await Rejected(()=>inviter.Invite(reconnecting),memberError,"reconnecting reserved member cannot be invited again");
        await Rejected(()=>inviter.Invite(inviter.Address),memberError,"local room member cannot invite itself");
        await Rejected(()=>inviter.Invite("127.0.0.246"),"Choose an unblocked friend.","nonfriend invitation still requires an existing friendship");

        Room? otherInvitation=null,idleInvitation=null;other.Invitation+=(_,_,room)=>otherInvitation=room;idle.Invitation+=(_,_,room)=>idleInvitation=room;
        string otherRoom=other.Current!.Id,currentRoom=inviter.Current.Id;
        await inviter.Invite(other.Address);
        Check(otherInvitation?.Id==currentRoom&&otherInvitation.HostAddress==inviter.Address&&other.Current?.Id==otherRoom,"Friend in another room was blocked or moved without accepting the invitation");
        await inviter.Invite(idle.Address);
        Check(idleInvitation?.Id==currentRoom&&idle.Current==null,"Nonmember idle friend was blocked or joined without accepting");
        Console.WriteLine("PASS other-room and idle friends remain eligible for invitations without changing their rooms");

        inviterSettings.BlockedUsers=[new("Other room owner",other.Address)];otherInvitation=null;
        await Rejected(()=>inviter.Invite(other.Address),"Choose an unblocked friend.","existing blocked-friend invitation guard is preserved");
        Check(otherInvitation==null,"Blocked friend received an invitation");
        inviterSettings.BlockedUsers=[];inviterSettings.RadminGroup="Chosen group";inviterSettings.RadminMembership=new("Chosen group",[other.Address],DateTimeOffset.UtcNow);idleInvitation=null;
        bool scopeRejected=false;
        try{await inviter.Invite(idle.Address);}catch(IOException error)when(error.Message=="This player is outside the selected Radmin group."){scopeRejected=true;}
        Check(scopeRejected&&idleInvitation==null,"Invitation bypassed the selected Radmin group restriction");
        inviterSettings.RadminGroup="";inviter.Leave();
        await Rejected(()=>inviter.Invite(idle.Address),"Join or host a room first.","invitation still requires an active current room");
        Console.WriteLine("PASS invitation membership preflight preserves friend, block, network scope and active-room requirements");
    }
}

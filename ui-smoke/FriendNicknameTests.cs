#nullable enable
using Fellowship;
using System.Reflection;
using System.Text.Json;

static class FriendNicknameTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static DateTimeOffset LastSave(PeerDirectory directory)=>(DateTimeOffset)typeof(PeerDirectory).GetField("lastSave",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(directory)!;
    static string Name(Settings settings,string address)=>settings.Friends.Single(friend=>friend.Address==address).Name;
    public static void Run()
    {
        const string address="127.0.0.231",other="127.0.0.232",host="127.0.0.233";
        var settings=Settings.InMemory();settings.Friends=[new("guliyn yasin",address),new("Other friend",other)];
        var directory=new PeerDirectory(settings);var now=new DateTimeOffset(2026,10,21,18,0,0,TimeSpan.Zero);
        directory.Success(address,"Doom",null,now);
        Check(Name(settings,address)=="Doom"&&directory.Find(address,now)?.Name=="Doom","Authoritative online nickname did not replace the saved friend name");
        Check(settings.Friends[0].Address==address&&Name(settings,other)=="Other friend","Nickname update reordered contacts or changed another address");
        Check(LastSave(directory)==now,"Nickname change did not request immediate persistence");
        directory.Success(address,"Doom",null,now.AddSeconds(5));
        Check(LastSave(directory)==now,"Repeated unchanged nickname forced an extra settings save");
        directory.Success(address,"DOOM",null,now.AddSeconds(6));
        Check(Name(settings,address)=="DOOM"&&LastSave(directory)==now.AddSeconds(6),"Case-only nickname change did not persist immediately");
        directory.Failure(address);var offline=now+PeerDirectory.Grace+TimeSpan.FromSeconds(7);
        Check(directory.Find(address,offline)==null&&Name(settings,address)=="DOOM","Offline friend reverted to the original saved contact name");
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;var restarted=new PeerDirectory(restored);
        Check(restarted.Find(address,offline)==null&&Name(restored,address)=="DOOM","Restarted offline friend lost its most recently confirmed nickname");

        // QueryPeer resolves a room through its host, but keeps the queried
        // player's own Name separate. Third-party roster names are never used.
        directory.Success(host,"Actual host",new(){HostAddress=host,Players=[new(){Address=address,Name="Spoofed roster name"}]},offline);
        Check(Name(settings,address)=="DOOM"&&settings.Friends.Count==2,"A room's third-party player name renamed a saved friend");
        Check(!FriendDirectory.RememberOnline(settings,"127.0.0.234","Stranger")&&settings.Friends.Count==2,"Unknown online player was added as a friend");
        foreach(string? invalid in new string?[]{null,"","   ","Bad\nName",new string('x',41)})
            Check(!FriendDirectory.RememberOnline(settings,address,invalid)&&Name(settings,address)=="DOOM","Invalid online nickname overwrote the saved contact");
        Check(!FriendDirectory.RememberOnline(settings,"not-an-address","Name")&&!FriendDirectory.RememberOnline(settings,"::1","Name"),"Invalid or unsupported source address was accepted");
        settings.BlockedUsers=[new("Blocked",address)];
        Check(!FriendDirectory.RememberOnline(settings,address,"Blocked update")&&Name(settings,address)=="DOOM","Blocked peer changed the saved friend identity");
        settings.BlockedUsers=[];settings.RadminGroup="Chosen group";settings.RadminMembership=new("Chosen group",[address],DateTimeOffset.UtcNow);
        Check(!FriendDirectory.RememberOnline(settings,other,"Outside group")&&Name(settings,other)=="Other friend","Out-of-scope peer changed a saved friend identity");
        Check(FriendDirectory.RememberOnline(settings,address,"Ready")&&Name(settings,address)=="Ready","Allowed same-address nickname was not retained verbatim");
        string language=L.Code;
        try{foreach(string code in new[]{"fr","ar"}){L.Apply(code);Check(Name(settings,address)=="Ready","Saved nickname was localized instead of retained verbatim");}}
        finally{L.Apply(language);}
        Console.WriteLine("PASS latest confirmed friend nickname persists offline and across restart, saves only on changes, and preserves source, block and network-scope guards");
    }
}

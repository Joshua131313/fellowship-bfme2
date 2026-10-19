using System.IO;
using System.Text.Json;
namespace Fellowship;
public sealed record Friend(string Name, string Address);
public sealed class Settings
{
    bool persist=true;
    public static Settings InMemory()=>new(){persist=false};
    public sealed record SavedSetup(string Map,string MapPath,int Capacity,int Resources,int CommandPoints,bool CustomHeroes,bool RingHeroes,int Team,int Spot,int Color=0);
    public List<RoomPreset> RoomPresets { get; set; } = [];
    public SavedSetup? LastHostedSetup { get; set; }
    public Room NewHostedRoom()=>LastHostedSetup is {} saved ? new Room
    {
        Map=saved.Map,MapPath=saved.MapPath,Capacity=saved.Capacity,
        Match=new(){Resources=saved.Resources,CommandPoints=saved.CommandPoints,CustomHeroes=saved.CustomHeroes,RingHeroes=saved.RingHeroes}
    } : new();
    public void RememberHost(Room room)
    {
        var host=room.Players.FirstOrDefault(p=>p.Id=="host");if(host==null)return;
        var saved=new SavedSetup(room.Map,room.MapPath,room.Capacity,room.Match.Resources,room.Match.CommandPoints,room.Match.CustomHeroes,room.Match.RingHeroes,host.Team,host.Spot,host.Color);
        if(LastHostedSetup==saved)return;
        LastHostedSetup=saved;Save();
    }
    public int LastSelectedColor { get; set; }
    public int PreferredColor { get; set; }
    public Dictionary<string,int> PreferredMapSpots { get; set; } = new();
    public int PreferredSpot(string map)=>PreferredMapSpots.FirstOrDefault(p=>p.Key.Equals(map,StringComparison.OrdinalIgnoreCase)).Value;
    public string Name { get; set; } = Environment.UserName;
    public string GamePath { get; set; } = "";
    public string BindAddress { get; set; } = "";
    public bool SkipRemoveFriendConfirmation { get; set; }
    public bool SkipBlockConfirmation { get; set; }
    public bool MessageSounds { get; set; } = true;
    public bool LiveGameSync { get; set; } = true;
    public bool MenuNavigation { get; set; }
    public List<Friend> Friends { get; set; } = [];
    public Friend[] BlockedUsers { get; set; } = [];
    public bool IsBlocked(string address)=>BlockedUsers.Any(p=>p.Address==address);
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fellowship");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        if(!persist)return;
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
public sealed record MapSpot(int Number,double X,double Y);
public sealed class Room
{
    public const string DefaultRules = "3v3 · 4000 starting resources · 2x command points · no Ring Heroes · no Custom Heroes";
    public int[] InitialTeams { get; set; } = [];
    public int NextTeam()=>InitialTeams.FirstOrDefault(team=>team>0&&Players.Count(p=>p.Team==team)<InitialTeams.Count(t=>t==team));
    public long Revision { get; set; }
    public static bool AcceptsUpdate(Room? current,Room? incoming)=>current==null||incoming==null||current.Id!=incoming.Id||incoming.Revision>=current.Revision;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Middle-earth gathering";
    public string Host { get; set; } = "";
    public string HostAddress { get; set; } = "";
    public bool Isolated { get; set; }
    public bool FriendsOnly { get; set; }
    public MatchRules Match { get; set; } = new();
    public string MapPath { get; set; } = @"maps\map wor rhun\map wor rhun.map";
    public string Map { get; set; } = "Rhûn";
    public string Patch { get; set; } = "1.00";
    public string Rules { get; set; } = DefaultRules;
    public int Capacity { get; set; } = 6;
    public string Phase { get; set; } = "Gathering";
    public List<MapSpot> Spots { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public void ApplyColorChoice(Player player,int preferred,int last)
    {
        player.Color=0;
        ApplyPreferences(player,preferred,0,MapPath);
        if(player.Color==0)ApplyPreferences(player,last,0,MapPath);
    }
    public void ApplyMapSpotChoices()
    {
        foreach(var player in Players){if(player.Color>0)player.LastSelectedColor=player.Color;player.Color=0;}
        foreach(var player in Players.AsEnumerable().Reverse())
        {
            ApplyColorChoice(player,player.PreferredColor,player.LastSelectedColor);
            ApplyPreferences(player,0,player.PreferredMapSpots.FirstOrDefault(p=>p.Key.Equals(MapPath,StringComparison.OrdinalIgnoreCase)).Value,MapPath);
            if(player.Spot==0)ApplyPreferences(player,0,player.LastMapSpots.FirstOrDefault(p=>p.Key.Equals(MapPath,StringComparison.OrdinalIgnoreCase)).Value,MapPath);
        }
    }
    public void ApplyPreferences(Player player,int color,int spot,string map)
    {
        if(color is >=1 and <=10&&!Players.Any(p=>p.Id!=player.Id&&p.Color==color))player.Color=color;
        if(MapPath.Equals(map,StringComparison.OrdinalIgnoreCase)&&Spots.Any(s=>s.Number==spot)&&!Players.Any(p=>p.Id!=player.Id&&p.Spot==spot))player.Spot=spot;
    }
    public override string ToString() => $"{Title}\n{Map}  ·  {Players.Count}/{Capacity} players  ·  Patch {Patch}  ·  {Phase}";
}
public sealed class Player
{
    public bool GameRunning { get; set; }
    public int PreferredColor { get; set; }
    public int LastSelectedColor { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public Dictionary<string,int> PreferredMapSpots { get; set; } = new();
    public bool CanHostTransfer { get; set; }
    public Dictionary<string,int> LastMapSpots { get; set; } = new();
    public int Spot { get; set; }
    public int Team { get; set; }
    public int Color { get; set; }
    public bool Ready { get; set; }
    public override string ToString() => $"{(Ready ? "● READY" : "○ WAITING")}    {Name}";
}
public sealed class Wire
{
    public bool GameRunning { get; set; }
    public int LastSelectedColor { get; set; }
    public bool SupportsIsolation { get; set; }
    public Dictionary<string,int> PreferredMapSpots { get; set; } = new();
    public bool CanHostTransfer { get; set; }
    public string Token { get; set; } = "";
    public Dictionary<string,string>? Tickets { get; set; }
    public int? Spot { get; set; }
    public int? Team { get; set; }
    public int? Color { get; set; }
    public string MapPath { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Version { get; set; } = "1.00";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public string Id { get; set; } = "";
    public bool Ready { get; set; }
    public Room? Room { get; set; }
}

public sealed class MatchRules
{
    public static readonly int[] ResourceValues = [500,750,1000,1200,1400,1600,1800,2000,2500,3000,4000];
    public static readonly int[] CommandValues = [33,50,100,200,400,800,10000];
    public int Resources { get; set; } = 4000;
    public int CommandPoints { get; set; } = 200;
    public bool CustomHeroes { get; set; }
    public bool RingHeroes { get; set; }
    public void Validate()
    {
        if(!ResourceValues.Contains(Resources)||!CommandValues.Contains(CommandPoints))throw new InvalidOperationException("Choose supported BFME2 1.00 rules.");
    }
    public string Summary => $"{Resources:N0} resources · {CommandPoints/100.0:0.##}× command points · Custom Heroes {(CustomHeroes?"on":"off")} · Ring Heroes {(RingHeroes?"on":"off")}";
}

public sealed record RoomPreset(string Name,string Map,string MapPath,int Capacity,int Resources,int CommandPoints,bool CustomHeroes,bool RingHeroes,int[] Teams,bool BuiltIn=false)
{
    public override string ToString()=>Name;
    public static List<RoomPreset> Defaults(IEnumerable<GameMap> maps)
    {
        RoomPreset Make(string name,string map,int count,int resources,int[] teams)
        {
            var installed=maps.FirstOrDefault(m=>m.Name.Equals(map,StringComparison.OrdinalIgnoreCase));
            return new(name,map,installed?.Path??"",count,resources,200,false,false,teams,true);
        }
        return [Make("3v3","Rhûn",6,4000,[1,1,1,2,2,2]),Make("2v2","Buckland",4,4000,[1,1,2,2]),Make("1v1 · 1,000 resources","Rohan",2,1000,[1,2]),Make("1v1 · 4,000 resources","Rohan",2,4000,[1,2]),Make("FFA","Gondor",8,4000,[0,0,0,0,0,0,0,0])];
    }
}

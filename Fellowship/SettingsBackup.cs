using System.IO;
using System.Text.Json;
using System.Net;
namespace Fellowship;
public static class SettingsBackup
{
    public sealed record Archive(int Format,Settings Preferences);
    public static string Export(Settings settings)=>JsonSerializer.Serialize(new Archive(1,settings),new JsonSerializerOptions{WriteIndented=true});
    public static Settings Parse(string json)
    {
        if(json.Length>2_000_000)throw new InvalidDataException("Backup is too large.");
        var archive=JsonSerializer.Deserialize<Archive>(json)??throw new InvalidDataException("Invalid backup.");
        if(archive.Format!=1||archive.Preferences is not {} s)throw new InvalidDataException("Unsupported backup format.");
        if(string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>40||s.PreferredColor is <0 or >10||s.LastSelectedColor is <0 or >10||s.PreferredMapSpots==null||s.Friends==null||s.BlockedUsers==null||s.RoomPresets==null)throw new InvalidDataException("Invalid preferences in backup.");
        if(s.PreferredMapSpots.Count>1000||s.PreferredMapSpots.Any(p=>p.Key.Length>512||p.Value is <0 or >8))throw new InvalidDataException("Invalid map preferences.");
        foreach(var friend in s.Friends.Concat(s.BlockedUsers))if(friend==null||string.IsNullOrWhiteSpace(friend.Name)||friend.Name.Length>40||!IPAddress.TryParse(friend.Address,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new InvalidDataException("Invalid friend or blocked-user entry.");
        foreach(var preset in s.RoomPresets)
        {
            if(preset==null||string.IsNullOrWhiteSpace(preset.Name)||preset.Name.Length>40||string.IsNullOrEmpty(preset.MapPath)||preset.MapPath.Length>512||preset.Capacity is <2 or >64||preset.Teams==null||preset.Teams.Length>64||preset.Teams.Any(t=>t is <0 or >4))throw new InvalidDataException("Invalid room preset.");
            new MatchRules{Resources=preset.Resources,CommandPoints=preset.CommandPoints}.Validate();
        }
        return s;
    }
    public static void Restore(Settings target,Settings source)
    {
        target.SkipRemoveFriendConfirmation=source.SkipRemoveFriendConfirmation;target.SkipBlockConfirmation=source.SkipBlockConfirmation;
        target.FriendActivitySounds=source.FriendActivitySounds;target.DesktopNotifications=source.DesktopNotifications;
        target.Name=source.Name;target.MessageSounds=source.MessageSounds;target.LiveGameSync=source.LiveGameSync;
        target.PreferredColor=source.PreferredColor;target.LastSelectedColor=source.LastSelectedColor;target.PreferredMapSpots=new(source.PreferredMapSpots);
        target.Friends=source.Friends.ToList();target.BlockedUsers=source.BlockedUsers.ToArray();target.RoomPresets=source.RoomPresets.Select(p=>p with{BuiltIn=false}).ToList();
        // Keep local installation/network paths and transient last-room state on this PC.
        target.Save();
    }
}

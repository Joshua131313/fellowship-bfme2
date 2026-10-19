using System.Runtime.InteropServices;
namespace Fellowship;
public static class IdlePresence
{
    public const int AfkSeconds=300;
    public const string AwayColor="#F2BD60";
    public static System.Windows.Controls.Border Badge(bool afk)=>new(){CornerRadius=new(5),Padding=new(7,3,7,3),Margin=new(6,0,0,0),Background=UI.Brush(afk?"#3A2C16":"#193C31"),Child=UI.Text(afk?"◷ AFK":"● Available",10,afk?AwayColor:UI.Green,true)};
    [StructLayout(LayoutKind.Sequential)]struct LastInput{public uint Size;public uint Tick;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput info);
    public static int? Seconds(){var info=new LastInput{Size=(uint)Marshal.SizeOf<LastInput>()};return GetLastInputInfo(ref info)?(int)Math.Min(unchecked((uint)Environment.TickCount-info.Tick)/1000,604800):null;}
    public static bool IsAfk(int? seconds,bool manual=false)=>manual||seconds>=AfkSeconds;
    public static int? Normalize(int? seconds)=>seconds is >=0 and <=604800?seconds:null;
    public static string Duration(int? seconds)=>seconds is not {} n?"Unavailable":n>=3600?$"{n/3600}h {n%3600/60}m":n>=60?$"{n/60}m {n%60}s":$"{n}s";
}
public sealed class PlayerActivity
{
    public DateTimeOffset? OnlineSince {get;set;}
    public DateTimeOffset? LastSeen {get;set;}
    public DateTimeOffset? OfflineAt {get;set;}
}

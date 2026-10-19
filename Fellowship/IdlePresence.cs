using System.Runtime.InteropServices;
namespace Fellowship;
public static class IdlePresence
{
    public const int AfkSeconds=300;
    [StructLayout(LayoutKind.Sequential)]struct LastInput{public uint Size;public uint Tick;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput info);
    public static int? Seconds(){var info=new LastInput{Size=(uint)Marshal.SizeOf<LastInput>()};return GetLastInputInfo(ref info)?(int)Math.Min(unchecked((uint)Environment.TickCount-info.Tick)/1000,604800):null;}
    public static bool IsAfk(int? seconds)=>seconds>=AfkSeconds;
    public static int? Normalize(int? seconds)=>seconds is >=0 and <=604800?seconds:null;
    public static string Duration(int? seconds)=>seconds is not {} n?"Unavailable":n>=3600?$"{n/3600}h {n%3600/60}m":n>=60?$"{n/60}m {n%60}s":$"{n}s";
}
public sealed class PlayerActivity
{
    public DateTimeOffset? OnlineSince {get;set;}
    public DateTimeOffset? LastSeen {get;set;}
    public DateTimeOffset? OfflineAt {get;set;}
}

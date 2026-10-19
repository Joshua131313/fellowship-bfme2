using System.IO;
using System.Media;
namespace Fellowship;

internal static class MessageAlert
{
    // Keep the resource stream alive for the player. Play is asynchronous.
    static readonly Lazy<SoundPlayer> Player = new(() =>
    {
        var stream = typeof(MessageAlert).Assembly.GetManifestResourceStream("Fellowship.Assets.message.wav")
            ?? throw new FileNotFoundException("The message alert resource is missing.");
        var player = new SoundPlayer(stream);
        player.Load();
        return player;
    });

    static readonly Lazy<SoundPlayer> Online=new(()=>Load("friend-online"));
    static readonly Lazy<SoundPlayer> Offline=new(()=>Load("friend-offline"));
    static SoundPlayer Load(string name)
    {
        var stream=typeof(MessageAlert).Assembly.GetManifestResourceStream("Fellowship.Assets."+name+".wav")??throw new FileNotFoundException(name);
        var player=new SoundPlayer(stream);player.Load();return player;
    }
    public static void PlayPresence(bool online)
    {
        try{(online?Online:Offline).Value.Play();}
        catch(Exception e) when(e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception){}
    }
    public static void Play()
    {
        // An unavailable audio device must not interrupt message delivery.
        try { Player.Value.Play(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { }
    }
}

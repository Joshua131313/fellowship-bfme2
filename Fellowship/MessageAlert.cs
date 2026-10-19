using System.IO;
using System.Media;
namespace Fellowship;
internal static class MessageAlert
{
    static Settings preferences=new();
    static readonly Dictionary<string,SoundPlayer> players=new();
    public static void Configure(Settings settings){preferences=settings;if(!settings.SoundsEnabled||settings.SoundVolume==0)foreach(var player in players.Values)player.Stop();}
    public static readonly (string Key,string Label)[] Choices=[("message","Private messages"),("sent","Messages sent"),("room-message","Room messages"),("invite","Invitations"),("room-join","Player joins room"),("room-leave","Player leaves room"),("friend-online","Friend online"),("friend-offline","Friend offline")];
    public static void PlayPresence(bool online)=>PlaySound(online?"friend-online":"friend-offline");
    public static void Effect(string name)=>PlaySound(name);
    public static void Play()=>PlaySound("message");
    public static void Preview(string name,int volume)=>PlaySound(name,volume);
    static void PlaySound(string name,int? previewVolume=null)
    {
        if(previewVolume==null&&(!preferences.SoundsEnabled||preferences.MutedSounds.Contains(name)))return;
        int volume=Math.Clamp(previewVolume??preferences.SoundVolume,0,100);if(volume==0)return;
        try
        {
            string key=name+":"+volume;
            if(!players.TryGetValue(key,out var player))
            {
                using var stream=typeof(MessageAlert).Assembly.GetManifestResourceStream("Fellowship.Assets."+name+".wav")??throw new FileNotFoundException(name);
                using var data=new MemoryStream();stream.CopyTo(data);
                player=new SoundPlayer(new MemoryStream(Scale(data.ToArray(),volume)));player.Load();
                if(players.Count>=32){foreach(var old in players.Values)old.Dispose();players.Clear();}
                players[key]=player;
            }
            player.Play();
        }
        catch(Exception e) when(e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception){}
    }
    internal static byte[] Scale(byte[] wav,int volume)
    {
        int bits=0,format=0;
        for(int offset=12;offset+8<=wav.Length;)
        {
            int length=BitConverter.ToInt32(wav,offset+4);if(length<0||length>wav.Length-offset-8)throw new InvalidDataException("Invalid sound data.");
            string id=System.Text.Encoding.ASCII.GetString(wav,offset,4);int start=offset+8;
            if(id=="fmt "&&length>=16){format=BitConverter.ToUInt16(wav,start);bits=BitConverter.ToUInt16(wav,start+14);}
            if(id=="data")
            {
                if(format!=1||bits!=16)throw new InvalidDataException("Unsupported sound format.");
                for(int i=start;i+1<start+length;i+=2){short sample=(short)(BitConverter.ToInt16(wav,i)*Math.Clamp(volume,0,100)/100);wav[i]=(byte)(sample&255);wav[i+1]=(byte)(sample>>8);}
            }
            offset=start+length+(length&1);
        }
        return wav;
    }
}

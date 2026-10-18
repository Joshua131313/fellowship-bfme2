using System.IO;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
namespace Fellowship;
public static class GameInstallation
{
    public static string Find()
    {
        if(!OperatingSystem.IsWindows())return "";
        var candidates=new List<string>();
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
        try
        {
            using var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
            foreach(var keyName in new[]{@"SOFTWARE\Electronic Arts\Electronic Arts\The Battle for Middle-earth II",@"SOFTWARE\Electronic Arts\The Battle for Middle-earth II"})
            {
                using var key=root.OpenSubKey(keyName);
                foreach(var valueName in new[]{"InstallPath","Install Dir","InstallDir"})
                    if(key?.GetValue(valueName) is string directory)candidates.Add(Path.Combine(directory,"lotrbfme2.exe"));
            }
        }
        catch(System.Security.SecurityException){}
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
            candidates.Add(Path.Combine(folder,"Electronic Arts","The Battle for Middle-earth (tm) II","lotrbfme2.exe"));
        return candidates.FirstOrDefault(File.Exists)??"";
    }
    public static List<GameMap> Maps(string executable)
    {
        var maps=new List<GameMap>();
        foreach(var archive in new[]{"Maps.big","_WSMaps.big"})
        try
        {
            using var stream=File.OpenRead(Path.Combine(Path.GetDirectoryName(executable)!,archive));
            using var reader=new BinaryReader(stream,Encoding.ASCII);
            var magic=Encoding.ASCII.GetString(reader.ReadBytes(4));
            if(magic is not ("BIGF" or "BIG4"))continue;
            reader.ReadBytes(4);int count=BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));reader.ReadBytes(4);
            if(count<0||count>100000)throw new InvalidDataException();
            for(int i=0;i<count;i++)
            {
                reader.ReadBytes(8);var bytes=new List<byte>();byte b;
                while((b=reader.ReadByte())!=0){if(bytes.Count>512)throw new InvalidDataException();bytes.Add(b);}
                var path=Encoding.ASCII.GetString(bytes.ToArray()).ToLowerInvariant();
                if(!path.EndsWith(".map")||!(path.StartsWith(@"maps\map wor ")||path.StartsWith(@"maps\map mp ")))continue;
                var folder=path.Split('\\')[1];var name=folder[(folder.StartsWith("map wor ")?8:7)..];
                var title=CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
                if(name=="rhun")title="Rhûn";
                maps.Add(new(title,path));
            }
        }
        catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException){}
        if(maps.Count==0)maps.Add(GameMap.Rhun);
        return maps.DistinctBy(m=>m.Path).OrderBy(m=>m.Name).ToList();
    }
}
public sealed record GameMap(string Name,string Path)
{
    public static GameMap Rhun=>new("Rhûn",@"maps\map wor rhun\map wor rhun.map");
    public override string ToString()=>Name;
}

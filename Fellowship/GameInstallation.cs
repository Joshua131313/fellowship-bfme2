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
    public static List<GameMap> Maps(string executable,string? patch=null)
    {
        var maps=GameAssets.Maps(GamePatches.Archives(executable,patch));
        if(maps.Count==0&&(patch==null||GamePatches.Normalize(patch)=="1.00"))maps.Add(GameMap.Rhun);
        return maps;
    }
}
public sealed record GameMap(string Name,string Path)
{
    public static GameMap Rhun=>new("Rhûn",@"maps\map wor rhun\map wor rhun.map");
    public override string ToString()=>Name;
}

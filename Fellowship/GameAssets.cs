using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Fellowship;

// BIG indexes are small even when their payload is several gigabytes. Cache the
// index, and open individual payloads only when their metadata/art is requested.
public static class GameAssets
{
    sealed record Entry(string Name,uint Offset,uint Size);
    sealed record Index(long Length,long Modified,Entry[] Entries);
    static readonly Dictionary<string,Index> indexes=new(StringComparer.OrdinalIgnoreCase);
    static readonly Regex blocks=new(@"(?im)^MapCache\s+(\S+)\s*\r?\n([\s\S]*?)^END\s*$",RegexOptions.Compiled,TimeSpan.FromSeconds(1));
    static Entry[] Entries(string archive)
    {
        try
        {
            var file=new FileInfo(archive);
            lock(indexes)
                if(indexes.TryGetValue(archive,out var found)&&found.Length==file.Length&&found.Modified==file.LastWriteTimeUtc.Ticks)return found.Entries;
            using var stream=file.OpenRead();using var reader=new BinaryReader(stream,Encoding.ASCII);
            if(Encoding.ASCII.GetString(reader.ReadBytes(4)) is not ("BIGF" or "BIG4"))return [];
            reader.ReadBytes(4);int count=BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            uint dataOffset=BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            if(count is <0 or >100000||dataOffset<16||dataOffset>stream.Length)return [];
            var entries=new List<Entry>();
            for(int i=0;i<count;i++)
            {
                uint offset=BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4)),size=BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
                var name=new StringBuilder();byte b;
                while((b=reader.ReadByte())!=0){if(name.Length>=512)throw new InvalidDataException();name.Append((char)b);}
                if(stream.Position>dataOffset||offset<dataOffset||offset+(long)size>stream.Length)throw new InvalidDataException();
                entries.Add(new(name.ToString().Replace('/','\\').ToLowerInvariant(),offset,size));
            }
            var result=new Index(file.Length,file.LastWriteTimeUtc.Ticks,entries.ToArray());
            lock(indexes){if(indexes.Count>256)indexes.Clear();indexes[archive]=result;}
            return result.Entries;
        }
        catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or OverflowException){return [];}
    }
    static byte[]? Read(string archive,Entry entry)
    {
        if(entry.Size>16000000)return null;
        try
        {
            using var stream=File.OpenRead(archive);if(entry.Offset+(long)entry.Size>stream.Length)return null;
            stream.Position=entry.Offset;var bytes=new byte[entry.Size];stream.ReadExactly(bytes);return bytes;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return null;}
    }
    public static byte[]? ReadAsset(IEnumerable<string> archives,string wanted)
    {
        wanted=wanted.Replace('/','\\');
        foreach(var archive in archives)
            if(Entries(archive).FirstOrDefault(e=>e.Name.Equals(wanted,StringComparison.OrdinalIgnoreCase)) is {} entry&&Read(archive,entry) is {} bytes)return bytes;
        return null;
    }
    static IEnumerable<string> Caches(IEnumerable<string> archives)
    {
        foreach(var archive in archives)
            foreach(var entry in Entries(archive).Where(e=>e.Name==@"maps\mapcache.ini"))
                if(Read(archive,entry) is {} bytes)yield return Encoding.ASCII.GetString(bytes);
    }
    static string DecodePath(string path)=>Regex.Replace(path,@"_([0-9a-fA-F]{2})",m=>((char)Convert.ToByte(m.Groups[1].Value,16)).ToString()).Replace('/','\\');
    static Match[] Blocks(string cache)
    {try{return blocks.Matches(cache).Cast<Match>().ToArray();}catch(RegexMatchTimeoutException){return [];}}
    public static List<GameMap> Maps(IEnumerable<string> sources)
    {
        var archives=sources.ToArray();
        var paths=archives.SelectMany(Entries).Where(e=>e.Name.StartsWith("maps\\")&&e.Name.EndsWith(".map")).Select(e=>e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var maps=new List<GameMap>();
        foreach(var cache in Caches(archives))
        foreach(Match block in Blocks(cache))
        {
            string path=DecodePath(block.Groups[1].Value);
            if(!seen.Add(path)||!paths.Contains(path))continue;
            string metadata=block.Groups[2].Value;
            if(!Regex.IsMatch(metadata,@"(?im)^\s*isMultiplayer\s*=\s*yes\s*$"))continue;
            var count=Regex.Match(metadata,@"(?im)^\s*numPlayers\s*=\s*(\d+)");
            if(!count.Success||!int.TryParse(count.Groups[1].Value,out int players)||players is <2 or >8)continue;
            maps.Add(new(Title(path),path.ToLowerInvariant()));
        }
        // Some map packs omit a cache. Only recognize the game's established
        // multiplayer naming convention there; never expose campaign/library maps.
        foreach(string path in paths.Where(p=>!seen.Contains(p)&&(p.StartsWith(@"maps\map wor ")||p.StartsWith(@"maps\map mp "))))
            maps.Add(new(Title(path),path));
        return maps.OrderBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    static string Title(string path)
    {
        var parts=path.Split('\\');string name=parts.Length>1?parts[^2]:Path.GetFileNameWithoutExtension(path);
        foreach(string prefix in new[]{"map wor ","map mp "})if(name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){name=name[prefix.Length..];break;}
        return name.Equals("rhun",StringComparison.OrdinalIgnoreCase)?"Rhûn":CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_',' '));
    }
    public static List<MapSpot> Spots(IEnumerable<string> archives,string map)
    {
        foreach(var cache in Caches(archives))
        {
            if(!Blocks(cache).Any(b=>DecodePath(b.Groups[1].Value).Equals(map,StringComparison.OrdinalIgnoreCase)))continue;
            try{return GameMapData.ParseSpots(cache,map);}
            catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or FormatException or OverflowException or RegexMatchTimeoutException){return [];}
        }
        return [];
    }
}

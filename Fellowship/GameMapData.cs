using System.IO;
using System.Text;
using System.Buffers.Binary;
namespace Fellowship;
public static class GameMapData
{
    public static List<MapSpot> ParseSpots(string cache,string map)
    {
        var blocks=System.Text.RegularExpressions.Regex.Matches(cache,@"(?im)^MapCache\s+(\S+)\s*\r?\n([\s\S]*?)^END\s*$");
        foreach(System.Text.RegularExpressions.Match block in blocks)
        {
            var path=System.Text.RegularExpressions.Regex.Replace(block.Groups[1].Value,@"_([0-9a-fA-F]{2})",m=>((char)Convert.ToByte(m.Groups[1].Value,16)).ToString());
            if(!path.Equals(map,StringComparison.OrdinalIgnoreCase))continue;
            var text=block.Groups[2].Value;
            (double X,double Y) Vector(string key)
            {
                var m=System.Text.RegularExpressions.Regex.Match(text,@"(?im)^\s*"+key+@"\s*=\s*X:([-\d.]+)\s+Y:([-\d.]+)");
                if(!m.Success)throw new InvalidDataException("Missing map coordinates");
                return(double.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture),double.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture));
            }
            var min=Vector("extentMin");var max=Vector("extentMax");
            if(max.X<=min.X||max.Y<=min.Y)throw new InvalidDataException("Invalid map bounds");
            var count=System.Text.RegularExpressions.Regex.Match(text,@"(?im)^\s*numPlayers\s*=\s*(\d+)");
            int players=count.Success?Math.Min(8,int.Parse(count.Groups[1].Value)):0;
            var result=new List<MapSpot>();
            for(int i=1;i<=players;i++)
            {
                var pos=Vector("Player_"+i+"_Start");double x=(pos.X-min.X)/(max.X-min.X),y=1-(pos.Y-min.Y)/(max.Y-min.Y);
                if(x<0||x>1||y<0||y>1)throw new InvalidDataException("Map spot outside bounds");
                result.Add(new(i,x,y));
            }
            return result;
        }
        return [];
    }
    public static byte[]? ReadAsset(string executable,string wanted)
    {
        foreach(var archive in new[]{"_WSMaps.big","Maps.big"})
        try
        {
            using var stream=File.OpenRead(Path.Combine(Path.GetDirectoryName(executable)!,archive));
            using var r=new BinaryReader(stream,Encoding.ASCII);
            if(Encoding.ASCII.GetString(r.ReadBytes(4)) is not ("BIGF" or "BIG4"))continue;
            r.ReadBytes(4);int count=BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));r.ReadBytes(4);
            if(count<0||count>100000)continue;
            for(int i=0;i<count;i++)
            {
                uint offset=BinaryPrimitives.ReadUInt32BigEndian(r.ReadBytes(4)),size=BinaryPrimitives.ReadUInt32BigEndian(r.ReadBytes(4));
                var name=new StringBuilder();byte b;
                while((b=r.ReadByte())!=0){if(name.Length>512)throw new InvalidDataException();name.Append((char)b);}
                if(!string.Equals(name.ToString(),wanted,StringComparison.OrdinalIgnoreCase))continue;
                if(size>16000000||offset+(long)size>stream.Length)throw new InvalidDataException();
                stream.Position=offset;return r.ReadBytes((int)size);
            }
        }
        catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException or OverflowException){}
        return null;
    }
    public static List<MapSpot> Spots(string executable,string map)
    {
        try{var cache=ReadAsset(executable,@"maps\mapcache.ini");return cache==null?[]:ParseSpots(Encoding.ASCII.GetString(cache),map);}
        catch(Exception e) when(e is IOException or ArgumentException or FormatException or OverflowException){return [];}
    }
}

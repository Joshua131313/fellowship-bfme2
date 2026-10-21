using System.IO;
using System.Text;
using System.Buffers.Binary;
namespace Fellowship;
public static class GameMapData
{
    public static List<MapSpot> ParseSpots(string cache,string map)
    {
        var blocks=System.Text.RegularExpressions.Regex.Matches(cache,@"(?im)^MapCache\s+(\S+)\s*\r?\n([\s\S]*?)^END\s*$",System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromSeconds(1));
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
    public static byte[]? ReadAsset(string executable,string wanted,string? patch=null)=>GameAssets.ReadAsset(GamePatches.Archives(executable,patch),wanted);
    public static List<MapSpot> Spots(string executable,string map,string? patch=null)=>GameAssets.Spots(GamePatches.Archives(executable,patch),map);
}

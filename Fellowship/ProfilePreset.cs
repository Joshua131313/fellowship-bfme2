using System.Text;
namespace Fellowship;
public static class ProfilePreset
{
    public static string MapPreferences(string original,string map=@"maps\map wor rhun\map wor rhun.map")
    {
        var encoded=string.Concat(Encoding.ASCII.GetBytes(map).Select(b=>"_"+b.ToString("X2")));
        var lines=original.Replace("\r\n","\n").Split('\n').Where(l=>
        {
            var key=l.Split('=',2)[0].Trim();
            return !key.Equals("Rts:Map",StringComparison.OrdinalIgnoreCase)&&!key.Equals("Rts:UseSystemMapDir",StringComparison.OrdinalIgnoreCase);
        }).ToList();
        while(lines.Count>0&&lines[^1].Length==0)lines.RemoveAt(lines.Count-1);
        lines.Add("Rts:Map = "+encoded);
        lines.Add("Rts:UseSystemMapDir = yes");
        return string.Join("\r\n",lines)+"\r\n";
    }

}


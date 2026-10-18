using System.IO;
using System.Buffers.Binary;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Fellowship;
public sealed class MapPreview : Border
{
    readonly Image image=new(){Height=120,Stretch=Stretch.Uniform};
    readonly TextBlock fallback=UI.Text("Map preview unavailable",11,UI.Muted);
    string last="";
    public MapPreview(){CornerRadius=new(6);Background=UI.Brush("#10151D");Padding=new(8);Margin=new(0,12,0,8);Child=image;}
    public void ShowMap(string executable,string map)
    {
        var key=executable+"|"+map;if(last==key)return;last=key;
        image.Source=Load(executable,map);Child=image.Source==null?fallback:image;
    }
    public static BitmapSource? Load(string executable,string map,string suffix="_pic.tga")
    {
        foreach(var archive in new[]{"_WSMaps.big","Maps.big"})
        try
        {
            using var stream=File.OpenRead(Path.Combine(Path.GetDirectoryName(executable)!,archive));
            using var r=new BinaryReader(stream,Encoding.ASCII);
            if(Encoding.ASCII.GetString(r.ReadBytes(4)) is not ("BIGF" or "BIG4"))continue;
            r.ReadBytes(4);int count=BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));r.ReadBytes(4);
            if(count<0||count>100000)continue;
            string wanted=map[..^4]+suffix;
            for(int i=0;i<count;i++)
            {
                uint offset=BinaryPrimitives.ReadUInt32BigEndian(r.ReadBytes(4)),size=BinaryPrimitives.ReadUInt32BigEndian(r.ReadBytes(4));
                var name=new StringBuilder();byte b;
                while((b=r.ReadByte())!=0){if(name.Length>512)throw new InvalidDataException();name.Append((char)b);}
                if(!string.Equals(name.ToString(),wanted,StringComparison.OrdinalIgnoreCase))continue;
                if(size>16000000||offset+(long)size>stream.Length)throw new InvalidDataException();
                stream.Position=offset;return Decode(r.ReadBytes((int)size));
            }
        }
        catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException or OverflowException){}
        return null;
    }
    public static BitmapSource Decode(byte[] bytes)
    {
        using var r=new BinaryReader(new MemoryStream(bytes));
        var h=r.ReadBytes(18);if(h.Length!=18||h[1]!=0||h[2] is not (2 or 10)||h[16] is not (24 or 32))throw new InvalidDataException("Unsupported map preview");
        int w=h[12]|h[13]<<8, height=h[14]|h[15]<<8, channels=h[16]/8;
        if(w<1||height<1||w>2048||height>2048)throw new InvalidDataException();
        r.ReadBytes(h[0]);var pixels=new byte[w*height*4];int index=0;
        void Pixel(byte[] color){if(index>=w*height||color.Length!=channels)throw new InvalidDataException();int x=index%w,y=index/w;if((h[17]&32)==0)y=height-1-y;if((h[17]&16)!=0)x=w-1-x;int p=(y*w+x)*4;pixels[p]=color[0];pixels[p+1]=color[1];pixels[p+2]=color[2];pixels[p+3]=255;index++;}
        while(index<w*height)
        {
            int packet=h[2]==10?r.ReadByte():0, count=h[2]==10?(packet&127)+1:1;
            if((packet&128)!=0){var color=r.ReadBytes(channels);for(int n=0;n<count;n++)Pixel(color);}
            else for(int n=0;n<count;n++)Pixel(r.ReadBytes(channels));
        }
        var result=BitmapSource.Create(w,height,96,96,PixelFormats.Bgra32,null,pixels,w*4);result.Freeze();return result;
    }
}

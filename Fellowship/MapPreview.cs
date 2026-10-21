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
    readonly Image image=new(){Width=320,Height=320,Stretch=Stretch.Fill};
    readonly Canvas mapCanvas=new(){Width=320,Height=320};
    readonly Canvas selectionLayer=new(){Width=320,Height=320};
    readonly Viewbox mapView=new(){Height=220,Stretch=Stretch.Uniform};
    public double PreviewHeight { get=>mapView.Height; set=>mapView.Height=value; }
    public event Action<int>? SpotSelected;
    readonly TextBlock fallback=UI.LocalizedText("Map preview unavailable",11,UI.Muted);
    string last="";
    public MapPreview(){FlowDirection=FlowDirection.LeftToRight;CornerRadius=new(6);Background=System.Windows.Media.Brushes.Transparent;Padding=new(0);Margin=new(0,12,0,8);mapCanvas.Children.Add(image);mapCanvas.Children.Add(selectionLayer);mapView.Child=mapCanvas;Child=mapView;}
    public void ShowMap(string executable,string map,string? patch=null)
    {
        patch=patch??GamePatches.Active(executable);
        var key=executable+"|"+map+"|"+patch;if(last==key)return;last=key;
        image.Source=Load(executable,map,patch:patch);image.ToolTip="Starting locations — preview only";Child=image.Source==null?fallback:mapView;
    }
    public static string TeamColor(int team)=>team switch{1=>"#73ACFF",2=>"#ED8585",3=>"#C598F4",4=>"#72D6AB",_=>"#B3BCCB"};
    public void ShowSelections(Room room,string localId,bool enabled)
    {
        selectionLayer.Children.Clear();
        if(image.Source==null)return;
        foreach(var spot in room.Spots)
        {
            var occupant=room.Players.FirstOrDefault(p=>p.Spot==spot.Number);bool mine=occupant?.Id==localId;
            string label=occupant==null?$"Spot {spot.Number} · click to claim":$"Spot {spot.Number} · {occupant.Name} · {(occupant.Team==0?"No team":"Team "+occupant.Team)}"+(mine?" · click to release":"");
            var ring=new System.Windows.Shapes.Ellipse{Width=34,Height=34,Fill=occupant==null?Brushes.Transparent:(Brush)new BrushConverter().ConvertFromString(PlayerColors.Get(occupant.Color).Hex)!,Stroke=mine?Brushes.White:Brushes.Transparent,StrokeThickness=3,Opacity=occupant==null?1:0.9};
            var marker=new Grid();marker.Children.Add(ring);
            if(mine){var mineText=UI.LocalizedText("YOU",10,"#FFFFFF",true);mineText.HorizontalAlignment=HorizontalAlignment.Center;mineText.VerticalAlignment=VerticalAlignment.Center;mineText.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1};marker.Children.Add(mineText);}
            var button=new Button{Style=(Style)Application.Current.FindResource("MapSpotButton"),Content=marker,Width=42,Height=42,Padding=new(0),Margin=new(0),Background=Brushes.Transparent,BorderThickness=new(0),ToolTip=label,IsEnabled=enabled&&(occupant==null||mine)};
            System.Windows.Automation.AutomationProperties.SetName(button,label);
            button.Click+=(_,_)=>SpotSelected?.Invoke(mine?0:spot.Number);
            Canvas.SetLeft(button,spot.X*320-21);Canvas.SetTop(button,spot.Y*320-21);selectionLayer.Children.Add(button);
        }
    }
    public static List<MapSpot> ParseSpots(string cache,string map)=>GameMapData.ParseSpots(cache,map);
    public static BitmapSource? Load(string executable,string map,string suffix="_art.tga",string? patch=null)
    {
        try
        {
            var bytes=GameMapData.ReadAsset(executable,map[..^4]+suffix,patch);if(bytes==null)return null;
            var terrain=Decode(bytes);var spots=GameMapData.Spots(executable,map,patch);
            const int size=320;var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(new RadialGradientBrush(Color.FromRgb(222,205,157),Color.FromRgb(126,108,70)),null,new Rect(0,0,size,size));
                // BFME2 displays the overhead art with the texture V axis reversed.
                // Keep this separate from world-to-preview conversion for start spots.
                dc.PushTransform(new ScaleTransform(1,-1,0,size/2.0));
                dc.DrawImage(terrain,new Rect(0,0,size,size));
                dc.Pop();
                foreach(var spot in spots)
                {
                    var center=new Point(spot.X*size,spot.Y*size);
                    var shadowCenter=new Point(center.X+1.5,center.Y+3);
                    var shadow=new RadialGradientBrush();
                    shadow.GradientStops.Add(new GradientStop(Color.FromArgb(170,0,0,0),0.65));
                    shadow.GradientStops.Add(new GradientStop(Color.FromArgb(75,0,0,0),0.83));
                    shadow.GradientStops.Add(new GradientStop(Colors.Transparent,1));
                    dc.DrawEllipse(shadow,null,shadowCenter,25,25);
                    var gold=new LinearGradientBrush(Color.FromRgb(241,207,85),Color.FromRgb(150,112,11),90);
                    dc.DrawEllipse(Brushes.Black,new Pen(UI.Brush("#654B08"),6),center,18,18);
                    dc.DrawEllipse(Brushes.Black,new Pen(gold,4),center,18,18);
                }
            }
            var result=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or OverflowException or FormatException){return null;}
    }
    public static BitmapSource Decode(byte[] bytes)
    {
        using var r=new BinaryReader(new MemoryStream(bytes));
        var h=r.ReadBytes(18);if(h.Length!=18||h[1]!=0||h[2] is not (2 or 10)||h[16] is not (24 or 32))throw new InvalidDataException("Unsupported map preview");
        int w=h[12]|h[13]<<8, height=h[14]|h[15]<<8, channels=h[16]/8;
        if(w<1||height<1||w>2048||height>2048)throw new InvalidDataException();
        r.ReadBytes(h[0]);var pixels=new byte[w*height*4];int index=0;
        void Pixel(byte[] color){if(index>=w*height||color.Length!=channels)throw new InvalidDataException();int x=index%w,y=index/w;if((h[17]&32)==0)y=height-1-y;if((h[17]&16)!=0)x=w-1-x;int p=(y*w+x)*4;pixels[p]=color[0];pixels[p+1]=color[1];pixels[p+2]=color[2];pixels[p+3]=channels==4?color[3]:(byte)255;index++;}
        while(index<w*height)
        {
            int packet=h[2]==10?r.ReadByte():0, count=h[2]==10?(packet&127)+1:1;
            if((packet&128)!=0){var color=r.ReadBytes(channels);for(int n=0;n<count;n++)Pixel(color);}
            else for(int n=0;n<count;n++)Pixel(r.ReadBytes(channels));
        }
        var result=BitmapSource.Create(w,height,96,96,PixelFormats.Bgra32,null,pixels,w*4);result.Freeze();return result;
    }
}

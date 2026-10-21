using System.IO;
using System.Buffers.Binary;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        var captions=new List<(MapSpot Spot,Player Player,bool Mine)>();
        foreach(var spot in room.Spots)
        {
            var occupant=room.Players.FirstOrDefault(p=>p.Spot==spot.Number);bool mine=occupant?.Id==localId;
            var ring=new System.Windows.Shapes.Ellipse{Width=34,Height=34,Fill=occupant==null?Brushes.Transparent:(Brush)new BrushConverter().ConvertFromString(PlayerColors.Get(occupant.Color).Hex)!,Stroke=mine?Brushes.White:Brushes.Transparent,StrokeThickness=3,Opacity=occupant==null?1:0.9};
            var marker=new Grid();marker.Children.Add(ring);
            if(occupant==null)
            {
                var number=new TextBlock{Text=spot.Number.ToString(),FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1}};marker.Children.Add(number);
            }
            var button=new Button{Style=(Style)Application.Current.FindResource("MapSpotButton"),Content=marker,Width=42,Height=42,Padding=new(0),Margin=new(0),Background=Brushes.Transparent,BorderThickness=new(0),ToolTip=SpotTip(spot,occupant,mine,enabled),IsEnabled=enabled&&(occupant==null||mine)};
            ToolTipService.SetShowOnDisabled(button,true);
            System.Windows.Automation.AutomationProperties.SetName(button,SpotDescription(spot,occupant,mine,enabled));
            button.Click+=(_,_)=>{if(enabled&&(occupant==null||mine))SpotSelected?.Invoke(mine?0:spot.Number);};
            Canvas.SetLeft(button,spot.X*320-21);Canvas.SetTop(button,spot.Y*320-21);selectionLayer.Children.Add(button);
            if(occupant!=null)captions.Add((spot,occupant,mine));
        }
        // Keep every hit target centered on its native map coordinate. Captions
        // have their own clamped bounds so edge spots cannot cut off a name.
        var occupied=new List<Rect>();
        var markers=room.Spots.Select(spot=>new Rect(spot.X*320-21,spot.Y*320-21,42,42)).ToArray();
        foreach(var (spot,player,mine) in captions)
        {
            var text=new TextBlock{Text=player.Name,Foreground=Brushes.White,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,FlowDirection=L.Current.Direction};
            text.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            double width=Math.Clamp(text.DesiredSize.Width+16,58,108);
            var bounds=CaptionBounds(spot,width,occupied,markers);occupied.Add(bounds);
            var label=new Border{DataContext=player,Child=text,Width=bounds.Width,Height=bounds.Height,CornerRadius=new(4),Padding=new(6,1,6,1),Background=new SolidColorBrush(Color.FromArgb(238,16,24,39)),BorderBrush=mine?Brushes.White:(Brush)new BrushConverter().ConvertFromString(TeamColor(player.Team))!,BorderThickness=new(mine?1.5:1),ToolTip=SpotTip(spot,player,mine,enabled),Cursor=enabled&&mine?Cursors.Hand:Cursors.Arrow};
            ToolTipService.SetShowOnDisabled(label,true);
            System.Windows.Automation.AutomationProperties.SetName(label,SpotDescription(spot,player,mine,enabled));
            if(enabled&&mine)label.MouseLeftButtonUp+=(_,args)=>{SpotSelected?.Invoke(0);args.Handled=true;};
            Canvas.SetLeft(label,bounds.X);Canvas.SetTop(label,bounds.Y);selectionLayer.Children.Add(label);
        }
    }
    static string SpotDescription(MapSpot spot,Player? player,bool mine,bool enabled)
        =>string.Join("\n",player==null?SpotDetails(spot,player,mine,enabled):new[]{player.Name}.Concat(SpotDetails(spot,player,mine,enabled)));
    static List<string> SpotDetails(MapSpot spot,Player? player,bool mine,bool enabled)
    {
        var details=new List<string>();
        details.Add(L.T($"Spot {spot.Number}"));
        if(player!=null)
        {
            if(mine)details.Add(L.T("YOU"));
            details.Add(L.T(player.Team==0?"No team":$"Team {player.Team}"));
            details.Add(L.T("COLOR")+": "+L.T(PlayerColors.Get(player.Color).Name));
            details.Add(L.T(player.Ready?"Ready":"Not ready"));
            if(player.Reconnecting)details.Add(L.T("Reconnecting"));
            if(IdlePresence.IsAfk(player.IdleSeconds,player.ManualAfk))details.Add(L.T("AFK"));
        }
        if(enabled&&(player==null||mine))details.Add(L.T(player==null?"Click to claim this spot":"Click to release this spot"));
        return details;
    }
    static ToolTip SpotTip(MapSpot spot,Player? player,bool mine,bool enabled)
    {
        var content=new StackPanel{MaxWidth=300,FlowDirection=L.Current.Direction};
        // TextBlock content avoids the global string-tooltip translator treating
        // an English-looking nickname as a resource key.
        if(player!=null)content.Children.Add(new TextBlock{Text=player.Name,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,4)});
        foreach(string detail in SpotDetails(spot,player,mine,enabled))content.Children.Add(new TextBlock{Text=detail,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,2)});
        return new ToolTip{Content=content};
    }
    static Rect CaptionBounds(MapSpot spot,double width,List<Rect> placed,Rect[] markers)
    {
        const double height=23;
        double x=spot.X*320,y=spot.Y*320;
        var candidates=new[]{new Point(x-width/2,y+23),new Point(x-width/2,y-23-height),new Point(x+24,y-height/2),new Point(x-24-width,y-height/2),new Point(x-width,y+23),new Point(x,y+23)};
        Rect best=default;double bestScore=double.PositiveInfinity;
        foreach(var candidate in candidates)
        {
            var bounds=new Rect(Math.Clamp(candidate.X,2,318-width),Math.Clamp(candidate.Y,2,318-height),width,height);
            double score=0;
            foreach(var obstacle in placed.Concat(markers))
            {
                var intersection=Rect.Intersect(bounds,obstacle);if(!intersection.IsEmpty)score+=intersection.Width*intersection.Height;
            }
            if(score<bestScore){best=bounds;bestScore=score;if(score==0)break;}
        }
        return best;
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

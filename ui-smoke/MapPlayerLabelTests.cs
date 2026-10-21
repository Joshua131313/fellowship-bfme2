#nullable enable
using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class MapPlayerLabelTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Canvas Layer(MapPreview preview)=>(Canvas)typeof(MapPreview).GetField("selectionLayer",Hidden)!.GetValue(preview)!;
    static Image Image(MapPreview preview)=>(Image)typeof(MapPreview).GetField("image",Hidden)!.GetValue(preview)!;
    static List<MapSpot> SixSpots()=>Enumerable.Range(0,6).Select(i=>new MapSpot(i+1,i<3?.16:.84,.18+.32*(i%3))).ToList();
    static Room Fixture(List<MapSpot> spots)=>new(){Spots=spots,Players=spots.Select((spot,i)=>new Player{Id="map-"+i,Name=i switch{0=>"Aragorn",1=>"Ready",2=>"LegolasWithAVeryLongUnbrokenNickname",3=>"عبد الرحمن",4=>"Gimli",5=>"Boromir",_=>"Player "+(i+1)},Spot=spot.Number,Team=i<spots.Count/2?1:2,Color=i%10+1,Ready=i%2==0,ManualAfk=i==3,Reconnecting=i==2}).ToList()};
    static MapPreview Preview(BitmapSource? terrain=null)
    {
        var preview=new MapPreview{Width=320,Height=320,PreviewHeight=320,Margin=new(0)};
        Image(preview).Source=terrain??BitmapSource.Create(1,1,96,96,PixelFormats.Bgr32,null,new byte[]{100,136,164,0},4);return preview;
    }
    static void Layout(FrameworkElement element,double width=320,double height=320)
    {element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();}
    static string[] TipLines(FrameworkElement element)=>((StackPanel)((ToolTip)element.ToolTip).Content).Children.OfType<TextBlock>().Select(text=>text.Text).ToArray();
    static Rect Bounds(Border label)=>new(Canvas.GetLeft(label),Canvas.GetTop(label),label.Width,label.Height);
    static void NoLabelOverlap(Canvas layer)
    {
        var labels=layer.Children.OfType<Border>().ToArray();
        foreach(var label in labels)
        {
            var bounds=Bounds(label);Check(bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=320&&bounds.Bottom<=320,"Player name caption is cut off by the map edge");
        }
        for(int i=0;i<labels.Length;i++)for(int j=i+1;j<labels.Length;j++)
        {var overlap=Rect.Intersect(Bounds(labels[i]),Bounds(labels[j]));Check(overlap.IsEmpty||overlap.Width*overlap.Height<1,"Map player name captions overlap");}
    }
    public static void Run()
    {
        string language=L.Code;var room=Fixture(SixSpots());
        try
        {
            foreach(string code in new[]{"en","fr","ar"})
            {
                L.Apply(code);var preview=Preview();preview.ShowSelections(room,"map-0",true);Layout(preview);var layer=Layer(preview);
                var buttons=layer.Children.OfType<Button>().ToArray();var labels=layer.Children.OfType<Border>().ToArray();
                Check(buttons.Length==6&&labels.Length==6,"Occupied map spots do not display one player name each");
                for(int i=0;i<buttons.Length;i++)
                {
                    Check(Math.Abs(Canvas.GetLeft(buttons[i])+21-room.Spots[i].X*320)<.001&&Math.Abs(Canvas.GetTop(buttons[i])+21-room.Spots[i].Y*320)<.001,"Map name caption moved the native spot hit target");
                    Check(buttons[i].Width==42&&buttons[i].Height==42,"Map spot hit target changed size");
                    Check(ToolTipService.GetShowOnDisabled(buttons[i]),"Disabled occupied spot cannot show player details on hover");
                    var name=(TextBlock)labels[i].Child;
                    Check(name.Text==room.Players[i].Name&&name.TextTrimming==TextTrimming.CharacterEllipsis&&name.TextWrapping==TextWrapping.NoWrap,"Player name was translated, wrapped or lacks ellipsis");
                    Check(TipLines(labels[i])[0]==room.Players[i].Name&&TipLines(buttons[i])[0]==room.Players[i].Name,"Hover tooltip lost the exact full player name");
                }
                Check(!buttons[1].IsEnabled&&TipLines(labels[1]).Contains(L.T("Not ready")),"Other player's readonly map caption lost its readiness tooltip");
                Check(TipLines(labels[1]).Contains(L.T("Team 1"))&&TipLines(labels[1]).Contains(L.T("COLOR")+": "+L.T("Red")),"Map tooltip team or player color is not translated");
                Check(TipLines(labels[2]).Contains(L.T("Reconnecting"))&&TipLines(labels[3]).Contains(L.T("AFK")),"Player tooltip omits reconnecting or AFK status");
                Check(((TextBlock)labels[1].Child).Text=="Ready"&&TipLines(labels[1])[0]=="Ready","Resource-looking nickname was translated in the map caption or tooltip");
                Check(preview.FlowDirection==FlowDirection.LeftToRight,"Arabic mirrored the map or its native spot coordinates");
                NoLabelOverlap(layer);
                int selected=-1;preview.SpotSelected+=spot=>selected=spot;
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(selected==0,"Clicking own map marker does not release the spot");
                selected=-1;buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(selected==-1,"Readonly occupied map marker changed another player's spot");
                labels[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});Check(selected==0,"Clicking own player name does not release the spot");
                room.Players[5].Spot=0;preview.ShowSelections(room,"map-0",true);Layout(preview);
                var empty=Layer(preview).Children.OfType<Button>().Last();empty.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(selected==6,"Clicking empty numbered map marker does not claim the spot");
                Check(((Grid)empty.Content).Children.OfType<TextBlock>().Single().Text=="6","Empty map marker does not display its spot number");
                room.Players[5].Spot=6;preview.ShowSelections(room,"map-0",false);
                Check(Layer(preview).Children.OfType<Button>().All(button=>!button.IsEnabled&&ToolTipService.GetShowOnDisabled(button)),"Readonly map preview lost disabled hover tooltips");
            }
            L.Apply("en");var edgeRoom=Fixture([new(1,0,.04),new(2,1,.04),new(3,0,.96),new(4,1,.96)]);var edges=Preview();edges.ShowSelections(edgeRoom,"map-0",true);Layout(edges);NoLabelOverlap(Layer(edges));
            var eightRoom=Fixture(Enumerable.Range(0,8).Select(i=>new MapSpot(i+1,i<4?.16:.84,.1+.26*(i%4))).ToList());var eight=Preview();eight.ShowSelections(eightRoom,"map-0",true);Layout(eight);NoLabelOverlap(Layer(eight));
            RenderMaps();
        }
        finally{L.Apply(language);Appearance.Apply("Dark");}
        Console.WriteLine("PASS player names on occupied map spots, translated detail tooltips, raw nickname identity, exact coordinates, safe labels and claim/release controls");
    }
    static void RenderMaps()
    {
        string executable=GameInstallation.Find();BitmapSource? terrain=MapPreview.Load(executable,GameMap.Rhun.Path,patch:"1.00");
        var actual=GameMapData.Spots(executable,GameMap.Rhun.Path,"1.00");var room=Fixture(actual.Count==6?actual:SixSpots());
        foreach(var (language,theme) in new[]{("en","Light"),("en","Dark"),("ar","Light")})
        {
            L.Apply(language);Appearance.Apply(theme);var preview=Preview(terrain);preview.ShowSelections(room,"map-0",true);Layout(preview);NoLabelOverlap(Layer(preview));
            var image=new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32);image.Render(preview);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("dist");using var file=File.Create($"dist/preview-map-player-labels-{language}-{theme.ToLowerInvariant()}.png");png.Save(file);
        }
    }
}

using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class GameEnhancementUiTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"Fellowship-enhancement-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string executable=Path.Combine(folder,"lotrbfme2.exe");File.WriteAllBytes(executable,[]);File.WriteAllBytes(Path.Combine(folder,"game.dat"),[77,90]);
            string selected=executable;
            var control=new GameEnhancementControls(()=>selected);
            var toggle=control.Children.OfType<CheckBox>().Single();
            Check(toggle.IsChecked==false,"Enhancement setting was enabled without its two files");
            var core=typeof(GameEnhancements).GetMethod("ApplyCore",BindingFlags.NonPublic|BindingFlags.Static)!;
            core.Invoke(null,[executable,true,(Func<bool>)(()=>false),null]);control.Refresh();
            Check(toggle.IsChecked==true,"Enhancement switch did not reflect both installed files");
            selected="";control.Refresh();
            Check(toggle.IsChecked==false&&!toggle.IsEnabled,"Changing the selected game retained the previous installation's switch state");
            selected=executable;control.Refresh();
            Check(toggle.IsChecked==true,"Returning to a game installation lost its actual enhancement state");
            File.Delete(Path.Combine(folder,"d3d9.dll"));control.Refresh();
            Check(toggle.IsChecked==null&&GameEnhancements.Inspect(executable).State==GameEnhancementState.Partial,"Partial installation was presented as a complete on/off state");
            typeof(System.Windows.Controls.Primitives.ToggleButton).GetMethod("OnToggle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(toggle,null);
            Check(toggle.IsChecked==false,"A partially installed pair could not be switched off with one click");
            core.Invoke(null,[executable,true,(Func<bool>)(()=>false),null]);control.Refresh();
            foreach(var (theme,language) in new[]{("Light","en"),("Dark","en"),("Light","ar")})
            {
                L.Apply(language);Appearance.Apply(theme);control.Refresh();
                var card=UI.Card(control);card.Width=480;card.FlowDirection=language=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
                card.Measure(new Size(480,double.PositiveInfinity));card.Arrange(new Rect(0,0,480,card.DesiredSize.Height));card.UpdateLayout();
                Check(card.ActualHeight<280&&card.ActualHeight>70,"Paired enhancement control does not fit the compact installation card");
                foreach(var text in control.Children.OfType<TextBlock>())Check(text.ActualWidth<=448.5,"Enhancement status text overflows the settings card");
                var bitmap=new RenderTargetBitmap(480,(int)Math.Ceiling(card.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(card);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("dist");
                using(var output=File.Create($"dist/preview-game-enhancements-{theme.ToLowerInvariant()}-{language}.png"))encoder.Save(output);
                card.Child=null;
            }
            L.Apply("en");Appearance.Apply("Dark");
            Console.WriteLine("PASS paired enhancement Settings switch derives actual file state, follows game path edits, shows partial installs, and fits light/dark/Arabic layouts (no DLL execution)");
        }
        finally
        {
            string full=Path.GetFullPath(folder),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("Fellowship-enhancement-ui-",StringComparison.Ordinal))throw new Exception("Unsafe UI fixture cleanup path");
            Directory.Delete(full,true);
        }
    }
}

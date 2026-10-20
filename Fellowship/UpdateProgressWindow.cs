using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
namespace Fellowship;

public sealed class UpdateProgressWindow : Window
{
    readonly TextBlock stage=UI.LocalizedText("Preparing update…",15,bold:true);
    readonly TextBlock detail=UI.LocalizedText("",11,UI.Muted);
    readonly ProgressBar progress=new(){Height=7,Minimum=0,Maximum=100,IsIndeterminate=true,Foreground=UI.Brush("#6789F5"),Background=UI.Brush("#263042"),BorderThickness=new(0)};
    readonly StackPanel actions=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,20,0,0)};
    bool busy=true;
    public UpdateProgressWindow(string version)
    {
        progress.Style=(Style)Application.Current.FindResource("FellowshipProgress");
        var body=UI.Shell(this,"Fellowship update",460,260);MinHeight=260;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel{Margin=new(24)};body.Children.Add(panel);
        panel.Children.Add(UI.LocalizedText("FELLOWSHIP "+version,10,UI.Accent,true));stage.Margin=new(0,14,0,9);panel.Children.Add(stage);
        detail.TextWrapping=TextWrapping.Wrap;detail.Margin=new(0,0,0,18);panel.Children.Add(detail);panel.Children.Add(progress);panel.Children.Add(actions);
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    public void Report(string title,string text,double? percent=null)
    {
        stage.Text=title;detail.Text=text;progress.IsIndeterminate=percent==null;
        if(percent.HasValue){if(IsLoaded)progress.BeginAnimation(ProgressBar.ValueProperty,new DoubleAnimation(Math.Clamp(percent.Value,0,100),TimeSpan.FromMilliseconds(180)));else progress.Value=Math.Clamp(percent.Value,0,100);}
    }
    public void AllowCancel(Action cancel)
    {
        actions.Children.Clear();actions.Children.Add(UI.Button("Cancel",cancel));
    }
    public void Failure(string message,Action? retry=null,string? log=null)
    {
        busy=false;SizeToContent=SizeToContent.Height;Report("Update could not finish",message,0);actions.Children.Clear();
        if(log!=null)actions.Children.Add(UI.Button("Open log",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe"){ArgumentList={log},UseShellExecute=true})));
        if(retry!=null)actions.Children.Add(UI.Button("Retry",()=>{busy=true;actions.Children.Clear();retry();},true));
        actions.Children.Add(UI.Button("Close",Close));
    }
    public void Finish(){busy=false;Close();}
}

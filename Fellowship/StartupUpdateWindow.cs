using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShapePath=System.Windows.Shapes.Path;
namespace Fellowship;

// This window only presents startup progress. The startup coordinator owns
// cancellation, retries and the decision to open the installed application.
public sealed class StartupUpdateWindow : Window
{
    readonly TextBlock stage=UI.LocalizedText("Checking for updates…",19,bold:true);
    readonly TextBlock detail=UI.LocalizedText("Fellowship will open when the update is ready.",12,UI.Muted);
    readonly TextBlock percentage=UI.Text("",10,UI.Muted);
    readonly ProgressBar progress=new(){Height=6,Minimum=0,Maximum=100,IsIndeterminate=true,Foreground=UI.Brush("#6789F5"),Background=UI.Brush("#263042"),BorderThickness=new(0)};
    readonly WrapPanel actions=new(){HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,20,0,0),Visibility=Visibility.Collapsed};
    readonly ScaleTransform pulse=new(1,1);
    bool busy=true,animating;
    Action? cancel;
    string cancelLabel="Cancel";

    public StartupUpdateWindow()
    {
        ResizeMode=ResizeMode.NoResize;
        var body=UI.Shell(this,"Fellowship update",440,350);
        MinWidth=320;MinHeight=0;MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-32);
        MaxHeight=Math.Max(240,SystemParameters.WorkArea.Height-32);
        Width=Math.Min(440,MaxWidth);SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        progress.Style=(Style)Application.Current.FindResource("FellowshipProgress");
        AutomationProperties.SetName(this,L.T("Fellowship update"));
        AutomationProperties.SetName(progress,L.T("Update progress"));
        AutomationProperties.SetLiveSetting(stage,AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(detail,AutomationLiveSetting.Polite);

        var panel=new StackPanel{Margin=new(30,24,30,26)};
        var logo=new Grid{Width=64,Height=64,FlowDirection=FlowDirection.LeftToRight};
        logo.Children.Add(new ShapePath{Data=Geometry.Parse("M 32,4 L 60,32 L 32,60 L 4,32 Z"),Stroke=UI.Brush(UI.Accent),StrokeThickness=3.5,StrokeLineJoin=PenLineJoin.Miter});
        logo.Children.Add(new ShapePath{Data=Geometry.Parse("M 32,18 L 46,32 L 32,46 L 18,32 Z"),Fill=UI.Brush(UI.Accent)});
        var tile=new Border{Width=90,Height=90,CornerRadius=new(24),Background=UI.Brush("#24324E"),Child=logo,HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=pulse,RenderTransformOrigin=new(.5,.5),Margin=new(0,0,0,16)};
        AutomationProperties.SetName(tile,"Fellowship");
        panel.Children.Add(tile);
        var brand=UI.Text("FELLOWSHIP",11,UI.Accent,true);brand.HorizontalAlignment=HorizontalAlignment.Center;brand.Margin=new(0,0,0,13);panel.Children.Add(brand);
        stage.TextAlignment=TextAlignment.Center;stage.Margin=new(0,0,0,8);panel.Children.Add(stage);
        detail.TextAlignment=TextAlignment.Center;detail.TextWrapping=TextWrapping.Wrap;detail.MinHeight=34;panel.Children.Add(detail);
        var meter=new Grid{Margin=new(0,18,0,0)};
        meter.RowDefinitions.Add(new(){Height=GridLength.Auto});meter.RowDefinitions.Add(new(){Height=GridLength.Auto});
        meter.Children.Add(progress);percentage.FlowDirection=FlowDirection.LeftToRight;percentage.HorizontalAlignment=HorizontalAlignment.Center;percentage.Margin=new(0,7,0,0);percentage.Visibility=Visibility.Collapsed;Grid.SetRow(percentage,1);meter.Children.Add(percentage);
        panel.Children.Add(meter);panel.Children.Add(actions);
        body.Children.Add(new ScrollViewer{Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=Math.Max(190,MaxHeight-50)});
        Loaded+=(_,_)=>Animate();
        Closed+=(_,_)=>StopAnimation();
    }

    public void Report(string title,string text,double? percent=null)
    {
        var wasBusy=busy;busy=true;
        L.Bind(stage,TextBlock.TextProperty,title);L.Bind(detail,TextBlock.TextProperty,text);
        progress.Visibility=Visibility.Visible;progress.IsIndeterminate=percent==null;
        percentage.Visibility=percent.HasValue?Visibility.Visible:Visibility.Collapsed;
        if(percent.HasValue)
        {
            double number=double.IsFinite(percent.Value)?Math.Clamp(percent.Value,0,100):0;
            percentage.Text=number.ToString("0",System.Globalization.CultureInfo.CurrentCulture)+"%";
            if(IsLoaded&&SystemParameters.ClientAreaAnimation)progress.BeginAnimation(ProgressBar.ValueProperty,new DoubleAnimation(number,TimeSpan.FromMilliseconds(180)));
            else{progress.BeginAnimation(ProgressBar.ValueProperty,null);progress.Value=number;}
        }
        else progress.BeginAnimation(ProgressBar.ValueProperty,null);
        if(!wasBusy)ShowActiveActions();
        Animate();
    }

    public void AllowCancel(Action action,string label="Cancel")
    {
        cancel=action;cancelLabel=label;if(busy)ShowActiveActions();
    }

    public void Failure(string text,Action retry,Action continueWithoutUpdate)
    {
        busy=false;StopAnimation();
        L.Bind(stage,TextBlock.TextProperty,"Update could not finish");
        L.Bind(detail,TextBlock.TextProperty,text);
        progress.IsIndeterminate=false;progress.BeginAnimation(ProgressBar.ValueProperty,null);progress.Visibility=Visibility.Collapsed;percentage.Visibility=Visibility.Collapsed;
        actions.Children.Clear();actions.Children.Add(ActionButton("Retry",()=>{Report("Checking for updates…","Fellowship will open when the update is ready.");retry();},true));
        actions.Children.Add(ActionButton("Open Fellowship",continueWithoutUpdate));actions.Visibility=Visibility.Visible;
    }

    public void Finish(){StopAnimation();Close();}

    void ShowActiveActions()
    {
        actions.Children.Clear();actions.Visibility=cancel==null?Visibility.Collapsed:Visibility.Visible;
        if(cancel!=null)actions.Children.Add(ActionButton(cancelLabel,cancel));
    }

    static Button ActionButton(string label,Action action,bool primary=false)
    {
        var button=UI.Button(label,action,primary);button.MinWidth=118;button.MinHeight=36;button.Margin=new(4,0,4,0);
        AutomationProperties.SetName(button,L.T(label));return button;
    }

    void Animate()
    {
        if(!busy||!IsLoaded||animating||!SystemParameters.ClientAreaAnimation)return;
        animating=true;
        var animation=new DoubleAnimation(1,1.045,TimeSpan.FromSeconds(1.25)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        pulse.BeginAnimation(ScaleTransform.ScaleXProperty,animation);pulse.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
    }

    void StopAnimation()
    {
        pulse.BeginAnimation(ScaleTransform.ScaleXProperty,null);pulse.BeginAnimation(ScaleTransform.ScaleYProperty,null);animating=false;
    }
}

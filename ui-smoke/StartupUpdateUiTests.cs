#nullable enable
using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class StartupUpdateUiTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        string language=L.Code;
        try
        {
            foreach(string theme in new[]{"Light","Dark","Black"})
            foreach(string code in new[]{"en","ar"})
            {
                Appearance.Apply(theme);L.Apply(code);
                var window=new StartupUpdateWindow{ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000};
                int cancelled=0,retried=0,opened=0,closed=0;
                window.Closed+=(_,_)=>closed++;
                try
                {
                    window.Show();Settle(window);
                    var progress=Nodes(window).OfType<ProgressBar>().Single();
                    var pulse=(ScaleTransform)typeof(StartupUpdateWindow).GetField("pulse",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                    Check(progress.IsIndeterminate,"Startup checking state does not show ongoing activity");
                    Check(Nodes(window).OfType<TextBlock>().Any(t=>t.Text==L.T("Checking for updates…")),"Startup status does not follow the selected language");
                    Check(pulse.HasAnimatedProperties==SystemParameters.ClientAreaAnimation,"Startup brand animation ignores the Windows reduced-motion preference");
                    Check(window.FlowDirection==(code=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight),"Startup updater does not follow the app's reading direction");
                    Check(AutomationProperties.GetName(progress)==L.T("Update progress"),"Startup progress has no translated accessible name");
                    CheckLayout(window);Render(window,$"dist/preview-startup-update-checking-{theme.ToLowerInvariant()}-{code}.png");

                    window.AllowCancel(()=>cancelled++);Click(window,"Cancel");
                    Check(cancelled==1&&closed==0,"Startup cancellation was not delegated to the coordinator");
                    window.Report("Downloading update…","34.5 / 70.0 MB",49);Settle(window);
                    Check(!progress.IsIndeterminate&&progress.Visibility==Visibility.Visible,"Startup download does not switch to determinate progress");
                    Check(Nodes(window).OfType<TextBlock>().Any(t=>t.Text=="49%"&&t.IsVisible),"Startup download percentage is missing");
                    Check(Button(window,"Cancel")!=null,"Reporting download progress removed the cancellation action");
                    CheckLayout(window);Render(window,$"dist/preview-startup-update-downloading-{theme.ToLowerInvariant()}-{code}.png");
                    window.Report("Downloading update…","34.5 / 70.0 MB",double.NaN);Settle(window);
                    Check(Nodes(window).OfType<TextBlock>().Any(t=>t.Text=="0%"&&t.IsVisible),"Invalid download percentages were not safely normalized");
                    window.Report("Downloading update…","34.5 / 70.0 MB",125);Settle(window);
                    Check(Nodes(window).OfType<TextBlock>().Any(t=>t.Text=="100%"&&t.IsVisible),"Download percentages overflow the progress range");

                    window.Failure("Windows Installer could not open its log file. Retry to create a new log in the update folder.",()=>retried++,()=>opened++);Settle(window);
                    Check(!pulse.HasAnimatedProperties,"Failure screen keeps the busy brand animation running");
                    Check(progress.Visibility==Visibility.Collapsed&&Button(window,"Retry")!=null&&Button(window,"Open Fellowship")!=null&&Button(window,"Cancel")==null,"Startup failure lacks clear recovery actions");
                    CheckLayout(window);Render(window,$"dist/preview-startup-update-failure-{theme.ToLowerInvariant()}-{code}.png");
                    Click(window,"Retry");Settle(window);
                    Check(retried==1&&opened==0&&progress.IsIndeterminate&&progress.Visibility==Visibility.Visible,"Retry does not return to active startup progress");
                    Check(Button(window,"Retry")==null&&Button(window,"Open Fellowship")==null&&Button(window,"Cancel")!=null,"Retry retained stale failure actions");
                    Check(pulse.HasAnimatedProperties==SystemParameters.ClientAreaAnimation,"Retry lost the reduced-motion-aware activity state");
                    window.Failure("Update could not finish",()=>retried++,()=>opened++);Click(window,"Open Fellowship");
                    Check(opened==1&&closed==0,"Opening the installed app was not delegated to the startup coordinator");
                    window.Report("Preparing update…","Fellowship will open when the update is ready.");Settle(window);
                    Check(Button(window,"Retry")==null&&Button(window,"Open Fellowship")==null,"New startup progress retained failure actions");
                    window.Finish();
                    Check(closed==1&&cancelled==1&&!pulse.HasAnimatedProperties,"Finishing startup did not close cleanly without invoking cancellation");
                }
                finally{if(closed==0)window.Finish();}
            }
        }
        finally{L.Apply(language);Appearance.Apply("Dark");}
        Console.WriteLine("PASS automatic-startup screen checking/download/retry/open/cancel states, bounded percentages, Windows motion preference, compact light/dark/black/Arabic layouts and clean finish (no update installation)");
    }

    static Button? Button(Window window,string label)=>Nodes(window).OfType<Button>().SingleOrDefault(b=>b.Content is string text&&text==L.T(label)&&b.IsVisible);
    static void Click(Window window,string label)
    {
        var button=Button(window,label)??throw new Exception("Missing startup action: "+label);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
    static void CheckLayout(Window window)
    {
        var root=(FrameworkElement)window.Content;
        Check(window.ActualWidth<=window.MaxWidth+.5&&window.ActualHeight<=window.MaxHeight+.5,"Startup updater exceeds the work area");
        foreach(var button in Nodes(window).OfType<Button>().Where(b=>b.IsVisible&&b.Content is string))
        {
            var bounds=button.TransformToAncestor(root).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
            Check(bounds.Left>=-.5&&bounds.Right<=root.ActualWidth+.5&&bounds.Top>=-.5&&bounds.Bottom<=root.ActualHeight+.5,"Translated startup action is clipped");
        }
        var status=Nodes(window).OfType<TextBlock>().Where(t=>t.IsVisible&&AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Polite).ToArray();
        Check(status.Length==2&&status.All(t=>t.ActualWidth<=root.ActualWidth-40),"Startup status is not accessible or does not wrap within the compact layout");
    }
    static void Settle(Window window)
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);window.UpdateLayout();
    }
    static void Render(Window window,string path)
    {
        var root=(FrameworkElement)window.Content;Directory.CreateDirectory("dist");
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
    static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Nodes(child))yield return node;}
}

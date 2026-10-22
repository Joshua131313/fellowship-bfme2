using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Fellowship;

// The switch reflects the selected installation's files, not a saved preference.
public sealed class GameEnhancementControls : StackPanel
{
    readonly Func<string> executable;
    readonly CheckBox toggle=new(){Margin=new(0,12,0,0)};
    readonly TextBlock status=UI.LocalizedText("Disabled",11,UI.Muted);
    readonly TextBlock detail=UI.LocalizedText("Both DLLs are installed and removed together. Existing files are backed up and restored when disabled.",11,UI.Muted);
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
    GameEnhancementStatus current=new(GameEnhancementState.Unavailable);
    bool busy;

    public GameEnhancementControls(Func<string> executable)
    {
        this.executable=executable;
        System.Windows.Automation.AutomationProperties.SetName(this,"Delay fix and borderless settings");
        L.Bind(toggle,ContentControl.ContentProperty,"Delay fix + borderless");
        System.Windows.Automation.AutomationProperties.SetName(toggle,"Delay fix + borderless");
        toggle.VerticalContentAlignment=VerticalAlignment.Center;
        status.Margin=new(0,6,0,0);detail.Margin=new(0,5,0,0);
        status.TextWrapping=detail.TextWrapping=TextWrapping.Wrap;
        Children.Add(new Border{Height=1,Background=UI.Brush("#2C313B"),Margin=new(0,16,0,0)});
        Children.Add(toggle);Children.Add(status);Children.Add(detail);
        ToolTipService.SetShowOnDisabled(toggle,true);
        toggle.Click+=async(_,_)=>
        {
            if(busy)return;
            bool enabled=toggle.IsChecked==true;
            string target=this.executable().Trim();busy=true;Refresh();
            try{await GameEnhancements.SetEnabled(target,enabled);}
            catch(OperationCanceledException){ }
            catch(Exception error){UI.Error(error.Message);}
            finally{busy=false;Refresh();}
        };
        timer.Tick+=(_,_)=>Refresh();
        Loaded+=(_,_)=>{Refresh();timer.Start();};Unloaded+=(_,_)=>timer.Stop();
        Refresh();
    }

    public void Refresh()
    {
        if(busy||GameEnhancements.IsChanging)
        {
            toggle.IsEnabled=false;L.Bind(status,TextBlock.TextProperty,"Applying game enhancements…");return;
        }
        current=GameEnhancements.Inspect(executable().Trim());
        toggle.IsChecked=current.State is GameEnhancementState.Partial or GameEnhancementState.RecoveryRequired?null:current.State==GameEnhancementState.Enabled;
        bool running=GameLauncher.IsRunning(),switching=PatchSwitcher.IsSwitching;
        string label=current.State switch
        {
            GameEnhancementState.Enabled=>"Enabled",
            GameEnhancementState.Disabled=>"Disabled",
            GameEnhancementState.Partial=>"Repair needed — switch off, then on again.",
            GameEnhancementState.RecoveryRequired=>"Interrupted change — switch off to recover both files.",
            GameEnhancementState.Conflict=>"Files changed outside Fellowship",
            _=>"Select your BFME2 executable first."
        };
        L.Bind(status,TextBlock.TextProperty,label);
        toggle.IsEnabled=!running&&!switching&&current.State is not (GameEnhancementState.Unavailable or GameEnhancementState.Conflict);
        string? reason=running?"Close BFME2 to change this setting.":switching?"Wait for patch switching to finish.":
            current.State is GameEnhancementState.Unavailable or GameEnhancementState.Conflict?current.Detail??label:null;
        toggle.ToolTip=reason==null?null:L.T(reason);
        L.Bind(detail,TextBlock.TextProperty,reason??current.Detail??"Both DLLs are installed and removed together. Existing files are backed up and restored when disabled.");
    }
}

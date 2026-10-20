using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Fellowship;

public sealed class TrayIcon : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon tray;
    readonly System.Drawing.Icon icon;
    readonly BrowserWindow browser;
    ContextMenu? menu;
    readonly Queue<(ActivityAlerts.Alert Alert,Action Action)> notifications=new();
    Action? notificationAction;
    bool notificationVisible;
    public TrayIcon(BrowserWindow browser)
    {
        this.browser=browser;
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Fellowship;component/Assets/Fellowship.ico")).Stream;
        using var loaded=new System.Drawing.Icon(stream);
        icon=(System.Drawing.Icon)loaded.Clone();
        tray=new(){Icon=icon,Text=L.T("Fellowship · running in the background"),Visible=true};
        L.Current.PropertyChanged+=LanguageChanged;
        tray.BalloonTipClicked+=(_,_)=>
        {
            var action=notificationAction;notificationAction=null;
            Application.Current.Dispatcher.InvokeAsync(()=>{try{action?.Invoke();}catch(Exception error){UI.Error(error.Message);}finally{notificationVisible=false;ShowNextNotification();}});
        };
        tray.BalloonTipClosed+=(_,_)=>{notificationAction=null;notificationVisible=false;ShowNextNotification();};
        tray.MouseClick+=(_,e)=>Application.Current.Dispatcher.InvokeAsync(()=>
        {
            if(e.Button==System.Windows.Forms.MouseButtons.Left)UI.Reveal(browser);
            if(e.Button==System.Windows.Forms.MouseButtons.Right)ShowMenu();
        });
    }
    void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>tray.Text=L.T("Fellowship · running in the background");
    public void Notify(ActivityAlerts.Alert alert,Action action)
    {
        if(notifications.Count>=32)return;
        notifications.Enqueue((alert,action));if(!notificationVisible)ShowNextNotification();
    }
    void ShowNextNotification()
    {
        if(!notifications.TryDequeue(out var next))return;
        notificationAction=next.Action;notificationVisible=true;
        try{tray.ShowBalloonTip(5000,next.Alert.DisplayTitle,next.Alert.DisplayMessage,System.Windows.Forms.ToolTipIcon.Info);}
        catch(System.ComponentModel.Win32Exception){notificationAction=null;notificationVisible=false;}
    }
    void ShowMenu()
    {
        if(menu!=null)menu.IsOpen=false;
        menu=new ContextMenu{Placement=PlacementMode.MousePoint};
        menu.SetBinding(FrameworkElement.FlowDirectionProperty,new System.Windows.Data.Binding(nameof(L.Direction)){Source=L.Current});
        void Item(string label,Action action,bool enabled=true)
        {
            var item=new MenuItem{IsEnabled=enabled};L.Bind(item,HeaderedItemsControl.HeaderProperty,label);
            item.Click+=(_,_)=>action();menu.Items.Add(item);
        }
        Item("Game rooms",()=>UI.Reveal(browser));
        Item("Friends",browser.OpenFriends);
        Item("Current room",browser.OpenCurrentRoom,browser.Peer.Current!=null);
        menu.Items.Add(new Separator());
        Item("Quit Fellowship",()=>((App)Application.Current).Quit());
        menu.IsOpen=true;
    }
    public void Dispose(){L.Current.PropertyChanged-=LanguageChanged;if(menu!=null)menu.IsOpen=false;tray.Visible=false;tray.Dispose();icon.Dispose();}
}

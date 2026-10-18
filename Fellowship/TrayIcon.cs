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
    public TrayIcon(BrowserWindow browser)
    {
        this.browser=browser;
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Fellowship;component/Assets/Fellowship.ico")).Stream;
        using var loaded=new System.Drawing.Icon(stream);
        icon=(System.Drawing.Icon)loaded.Clone();
        tray=new(){Icon=icon,Text="Fellowship · running in the background",Visible=true};
        tray.MouseClick+=(_,e)=>Application.Current.Dispatcher.InvokeAsync(()=>
        {
            if(e.Button==System.Windows.Forms.MouseButtons.Left)UI.Reveal(browser);
            if(e.Button==System.Windows.Forms.MouseButtons.Right)ShowMenu();
        });
    }
    void ShowMenu()
    {
        if(menu!=null)menu.IsOpen=false;
        menu=new ContextMenu{Placement=PlacementMode.MousePoint};
        void Item(string label,Action action,bool enabled=true)
        {
            var item=new MenuItem{Header=label,IsEnabled=enabled};
            item.Click+=(_,_)=>action();menu.Items.Add(item);
        }
        Item("Game rooms",()=>UI.Reveal(browser));
        Item("Friends",browser.OpenFriends);
        Item("Current room",browser.OpenCurrentRoom,browser.Peer.Current!=null);
        menu.Items.Add(new Separator());
        Item("Quit Fellowship",()=>((App)Application.Current).Quit());
        menu.IsOpen=true;
    }
    public void Dispose(){if(menu!=null)menu.IsOpen=false;tray.Visible=false;tray.Dispose();icon.Dispose();}
}

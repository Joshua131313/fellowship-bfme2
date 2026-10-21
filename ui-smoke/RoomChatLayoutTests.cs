using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class RoomChatLayoutTests
{
    public static void Run()
    {
        string language=L.Code;L.Apply("en");
        var settings=Settings.InMemory();settings.LiveGameSync=false;settings.Name="Aragorn";
        var browser=new BrowserWindow(settings);typeof(BrowserWindow).GetField("closingRoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(browser,true);browser.Peer.Host(new Room{Title="Room chat layout",Patch="1.00",Capacity=8},[]);
        var room=browser.Peer.Current!;for(int i=1;i<8;i++)room.Players.Add(new(){Id="layout-"+i,Name="Player "+i,Address="10.200.0."+i,Team=i<4?1:2,Spot=i,Color=i,Ready=i%2==0,GameRunning=i==1,ManualAfk=i==2});
        var window=new RoomWindow(browser){AllowClose=true};
        try
        {
            var work=SystemParameters.WorkArea;
            if(window.Width>work.Width-31||window.Height>work.Height-31||window.MinWidth>window.Width||window.MinHeight>window.Height)throw new Exception("Room default or minimum size exceeds the available work area");
            var view=(FrameworkElement)window.Content;window.Content=null;
            var splitter=Nodes(view).OfType<GridSplitter>().Single(s=>s.ResizeDirection==GridResizeDirection.Rows);
            var split=(Grid)splitter.Parent;
            var columns=(Grid)split.Parent;var body=(Grid)columns.Parent;
            var footer=(Grid)body.Children.Cast<UIElement>().Single(e=>Grid.GetRow(e)==2);
            var title=(TextBlock)typeof(RoomWindow).GetField("title",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var warning=(Grid)typeof(RoomWindow).GetField("patchWarning",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var consent=(CheckBox)typeof(RoomWindow).GetField("consent",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var summary=(TextBlock)typeof(RoomWindow).GetField("readySummary",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(title.Text!=room.Title||window.Title!=room.Title+" · Fellowship"||title.FontSize>13||title.TextWrapping!=TextWrapping.NoWrap||title.TextTrimming!=TextTrimming.CharacterEllipsis)throw new Exception("Room title is not a compact raw titlebar caption");
            if(body.Children.Cast<UIElement>().Single(e=>Grid.GetRow(e)==0)!=warning||Nodes(body).OfType<TextBlock>().Any(t=>t.Text==room.Title||t.Text=="Battle room"))throw new Exception("The redundant room content header is still present");
            if(splitter.ResizeBehavior!=GridResizeBehavior.PreviousAndNext||!splitter.Focusable||splitter.KeyboardIncrement<=0)throw new Exception("Room roster/chat split cannot be resized by pointer and keyboard");
            var roster=(ListBox)typeof(RoomWindow).GetField("players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(roster.Items.Count!=8)throw new Exception("Room roster layout test is missing players");
            var transcript=(ChatTranscript)typeof(RoomWindow).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            transcript.Append("Gimli","Ready for another game?");transcript.Append("Aragorn","Ready. We have room to follow the conversation now.",true);
            foreach(var theme in new[]{"Light","Dark"})
            {
                Appearance.Apply(theme);Layout(view,window.Width,window.Height);
                if(transcript.ActualHeight<150||transcript.ActualWidth<300||split.RowDefinitions[2].ActualHeight<=split.RowDefinitions[0].ActualHeight)throw new Exception("Default room chat viewport is still too small");
                if(window.Height>=830&&transcript.ActualHeight<260)throw new Exception("The eight-player roster leaves too little of the default room height for chat");
                var scroll=VisualNodes(roster).OfType<ScrollViewer>().First();
                if(window.Height>=800&&scroll.ExtentHeight>scroll.ViewportHeight+.01)throw new Exception($"Eight compact room players still need scrolling: {scroll.ExtentHeight} > {scroll.ViewportHeight}; roster height {roster.ActualHeight}");
                foreach(var item in roster.Items.Cast<ListBoxItem>())if(item.ActualHeight>28.1)throw new Exception("Room player row is not compact: "+item.ActualHeight);
                var first=(ListBoxItem)roster.Items[1];var name=Nodes((DependencyObject)first.Content).OfType<TextBlock>().Single(t=>t.Text=="Player 1");
                if(name.ToolTip is not ToolTip{Content:StackPanel details})throw new Exception("Room player details are missing from the name tooltip");
                var tooltipText=Nodes(details).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                if(!tooltipText.Contains("Guest · IN BFME2")||!tooltipText.Contains("Spot 1 · Team 1")||!tooltipText.Contains("Blue")||!tooltipText.Contains("Not ready")||!tooltipText.Contains("Ping"))throw new Exception("Room player hover does not contain role, game, selections, color, ping and readiness");
                if(Nodes((DependencyObject)first.Content).OfType<TextBlock>().Any(t=>t.Text.Contains("Spot ")||t.Text.Contains("Team ")||t.Text.Contains("BFME2")))throw new Exception("Detailed room player state still occupies roster rows");
                VisibleActions(window,view);
                if(footer.ActualHeight>54||consent.Visibility!=Visibility.Collapsed)throw new Exception("Routine host footer still reserves helper rows");
                var warningVisibility=warning.Visibility;warning.Visibility=Visibility.Collapsed;Layout(view,window.Width,window.Height);
                if(body.RowDefinitions[0].ActualHeight>.1)throw new Exception("A hidden patch warning still reserves a room header row");
                warning.Visibility=warningVisibility;Layout(view,window.Width,window.Height);
                var png=new PngBitmapEncoder();var image=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);image.Render(view);png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("dist");using var file=File.Create("dist/preview-room-chat-"+theme.ToLowerInvariant()+".png");png.Save(file);
            }
            double defaultRoster=split.RowDefinitions[0].ActualHeight,defaultChat=transcript.ActualHeight;Layout(view,window.Width,window.Height+160);
            if(Math.Abs(split.RowDefinitions[0].ActualHeight-defaultRoster)>1||transcript.ActualHeight<defaultChat+140)throw new Exception("A taller room gives unnecessary height to the roster instead of expanding chat");Layout(view,window.Width,window.Height);
            var isHost=typeof(PeerService).GetProperty(nameof(PeerService.IsHost))!;var localId=typeof(PeerService).GetProperty(nameof(PeerService.LocalId))!;var reconnecting=typeof(PeerService).GetProperty(nameof(PeerService.IsReconnecting))!;var current=typeof(PeerService).GetProperty(nameof(PeerService.Current))!;
            isHost.SetValue(browser.Peer,false);localId.SetValue(browser.Peer,"layout-1");var self=room.Players.Single(p=>p.Id=="layout-1");
            foreach(bool ready in new[]{false,true})
            {
                self.Ready=ready;window.Refresh();Layout(view,window.Width,window.Height);
                if(consent.Visibility!=Visibility.Visible||consent.IsChecked!=ready||summary.Visibility!=Visibility.Collapsed||footer.ActualHeight>54)throw new Exception("Routine guest readiness still shows helper rows or hides launch consent");
                if(Nodes(footer).OfType<TextBlock>().Any(t=>t.Text is "Room stays open when you exit the game." or "Check Ready when you want to play." or "Ready for the host to launch"))throw new Exception("Routine guest footer subtexts remain visible");
                VisibleActions(window,view);
            }
            var guestImage=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);guestImage.Render(view);var guestPng=new PngBitmapEncoder();guestPng.Frames.Add(BitmapFrame.Create(guestImage));using(var file=File.Create("dist/preview-room-chat-guest.png"))guestPng.Save(file);
            room.Players[^1].Reconnecting=true;window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(summary.Visibility!=Visibility.Visible||summary.Text!="Waiting for disconnected players to reconnect.")throw new Exception("Reserved-player warning disappeared from the compact footer");VisibleActions(window,view);room.Players[^1].Reconnecting=false;
            reconnecting.SetValue(browser.Peer,true);window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(summary.Visibility!=Visibility.Visible||summary.Text!="Reconnecting to room…"||consent.IsEnabled)throw new Exception("Local reconnect warning or readiness guard disappeared from the compact footer");VisibleActions(window,view);reconnecting.SetValue(browser.Peer,false);
            var launching=typeof(RoomWindow).GetField("launching",BindingFlags.Instance|BindingFlags.NonPublic)!;launching.SetValue(window,true);window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(summary.Visibility!=Visibility.Visible||summary.Text!="Launching BFME2…")throw new Exception("Launch progress disappeared from the compact footer");VisibleActions(window,view);launching.SetValue(window,false);
            current.SetValue(browser.Peer,null);window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(summary.Visibility!=Visibility.Visible||summary.Text!="Return to Game rooms to reconnect.")throw new Exception("Disconnected room did not reveal its compact reconnect status");VisibleActions(window,view);current.SetValue(browser.Peer,room);window.Refresh();
            if(summary.Visibility!=Visibility.Collapsed)throw new Exception("Routine guest footer did not collapse again after connection recovery");

            split.RowDefinitions[0].Height=new(210);window.Refresh();Layout(view,window.MinWidth,560);VisibleActions(window,view);
            if(transcript.ActualHeight<50||split.RowDefinitions[0].Height.Value!=210)throw new Exception("A short window clipped chat or discarded the requested splitter height");
            Layout(view,window.Width,window.Height);window.Refresh();Layout(view,window.Width,window.Height);
            if(Math.Abs(split.RowDefinitions[0].ActualHeight-210)>.1)throw new Exception("Window resize or room refresh reset the user's roster/chat split");split.RowDefinitions[0].Height=new(256);
            L.Apply("fr");room.Title="Ready";window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(title.Text!="Ready"||window.Title!="Ready · Fellowship"||title.ToolTip is not ToolTip{Content:TextBlock rawTip}||rawTip.Text!="Ready")throw new Exception("Live room rename was localized or failed to update the titlebar, tooltip and taskbar title");
            L.Apply("ar");room.Title=string.Join(" ",Enumerable.Repeat("غرفة الأصدقاء مع عنوان طويل",6));window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(title.Text!=room.Title||title.FlowDirection!=FlowDirection.RightToLeft||title.TextTrimming!=TextTrimming.CharacterEllipsis)throw new Exception("Long Arabic room title lost raw text, direction or ellipsis");
            VisibleActions(window,view);if(summary.Visibility!=Visibility.Collapsed||footer.ActualHeight>54)throw new Exception("Arabic guest footer is no longer compact");
            var captionBounds=title.TransformToAncestor(view).TransformBounds(new Rect(0,0,title.ActualWidth,title.ActualHeight));
            var controls=Nodes(view).OfType<Button>().Where(b=>b.Style==(Style)Application.Current.FindResource("CaptionButton")).ToArray();
            foreach(var button in controls)
            {
                var bounds=button.TransformToAncestor(view).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
                if(bounds.Right>view.ActualWidth+.1||bounds.Left<captionBounds.Right-1)throw new Exception("Long room title overlaps or cuts off a titlebar control");
            }
            var rtlImage=new RenderTargetBitmap((int)window.MinWidth,(int)window.MinHeight,96,96,PixelFormats.Pbgra32);rtlImage.Render(view);var rtlPng=new PngBitmapEncoder();rtlPng.Frames.Add(BitmapFrame.Create(rtlImage));using(var file=File.Create("dist/preview-room-chat-title-rtl.png"))rtlPng.Save(file);
            L.Apply("en");room.Title="Room chat layout";isHost.SetValue(browser.Peer,true);localId.SetValue(browser.Peer,"host");window.Refresh();
            Layout(view,window.MinWidth,window.MinHeight);VisibleActions(window,view);
            if(transcript.ActualHeight<50)throw new Exception("Minimum room size cuts off the chat composer or transcript");
            window.Content=view;
        }
        finally{window.Close();browser.Peer.Dispose();Appearance.Apply("Dark");L.Apply(language);}
        Console.WriteLine("PASS compact host/guest footer, conditional connection/launch warnings, eight-row roster with remaining chat space, preserved split, live raw title and RTL control clearance");
    }
    static void VisibleActions(RoomWindow window,FrameworkElement view)
    {
        foreach(string name in new[]{"launchMine","launch","abort","requestReady","consent","readySummary"})
        {
            var button=(FrameworkElement)typeof(RoomWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(button.Visibility!=Visibility.Visible)continue;
            var bounds=button.TransformToAncestor(view).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
            if(bounds.Left< -1||bounds.Right>view.ActualWidth+1||bounds.Bottom>view.ActualHeight+1)
            {
                var trail=new List<string>();for(DependencyObject parent=button;parent is FrameworkElement element;parent=VisualTreeHelper.GetParent(parent))trail.Add($"{element.GetType().Name} {element.ActualWidth}×{element.ActualHeight} desired {element.DesiredSize} slot {System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(element)} offset {element.TranslatePoint(new Point(),view)}"+(element is Grid grid?$" columns {string.Join(',',grid.ColumnDefinitions.Select(c=>c.ActualWidth))}; rows {string.Join(',',grid.RowDefinitions.Select(r=>r.ActualHeight))}":""));
                throw new Exception($"Room bottom action or critical status is cut off: {name}; bounds {bounds}; view {view.ActualWidth}×{view.ActualHeight}; "+string.Join(" > ",trail));
            }
        }
    }
    static void Layout(FrameworkElement view,double width,double height)
    {
        // This detached tree is never IsVisible. WPF can retain Auto-column
        // measures across hidden control visibility/language transitions, so
        // each rendered fixture needs a fresh descendant measurement.
        foreach(var element in VisualNodes(view).OfType<UIElement>().Reverse())element.InvalidateMeasure();
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);view.UpdateLayout();
    }
    static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Nodes(child))yield return node;}
    static IEnumerable<DependencyObject> VisualNodes(DependencyObject root)
    {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in VisualNodes(VisualTreeHelper.GetChild(root,i)))yield return node;}
}

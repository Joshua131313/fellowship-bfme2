using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            var chatPanel=(RoomChatPanel)typeof(RoomWindow).GetField("chatPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var title=(TextBlock)typeof(RoomWindow).GetField("title",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var warning=(Grid)typeof(RoomWindow).GetField("patchWarning",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var body=(Grid)warning.Parent;
            var footer=(Grid)body.Children.Cast<UIElement>().Single(e=>Grid.GetRow(e)==2);
            var consent=(CheckBox)typeof(RoomWindow).GetField("consent",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var summary=(TextBlock)typeof(RoomWindow).GetField("readySummary",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(title.Text!=room.Title||window.Title!=room.Title+" · Fellowship"||title.FontSize>13||title.TextWrapping!=TextWrapping.NoWrap||title.TextTrimming!=TextTrimming.CharacterEllipsis)throw new Exception("Room title is not a compact raw titlebar caption");
            if(body.Children.Cast<UIElement>().Single(e=>Grid.GetRow(e)==0)!=warning||Nodes(body).OfType<TextBlock>().Any(t=>t.Text==room.Title||t.Text=="Battle room"))throw new Exception("The redundant room content header is still present");
            if(!chatPanel.PlayersExpanded||!chatPanel.PlayersButton.Focusable)throw new Exception("Floating room players are not expanded initially or cannot be toggled by keyboard");
            if(Nodes(chatPanel).OfType<GridSplitter>().Any())throw new Exception("The old vertical roster/chat split remains inside the full-height chat");
            var roster=(ListBox)typeof(RoomWindow).GetField("players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(roster.Items.Count!=8)throw new Exception("Room roster layout test is missing players");
            var transcript=(ChatTranscript)typeof(RoomWindow).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var composer=(FrameworkElement)Nodes(chatPanel).OfType<TextBox>().Single().Parent;
            if(!Nodes(chatPanel.PlayersCard).Contains(roster)||!Nodes(chatPanel).Contains(transcript))throw new Exception("Room players or transcript are detached from the floating chat panel");
            transcript.Append("Gimli","Ready for another game?");transcript.Append("Aragorn","Ready. We have room to follow the conversation now.",true);
            string longMessage=string.Join(" ",Enumerable.Repeat("This longer room message must wrap beside the players without losing selectable text.",12));
            transcript.Append("Boromir",longMessage);
            foreach(var theme in new[]{"Light","Dark"})
            {
                Appearance.Apply(theme);Layout(view,window.Width,window.Height);
                FloatingBounds(chatPanel,transcript,composer,view,false);
                if(transcript.ActualHeight<150||transcript.ActualWidth<180)throw new Exception("Default room chat viewport is still too small");
                if(window.Height>=800&&(transcript.ActualHeight<350||transcript.ActualHeight<chatPanel.PlayersCard.ActualHeight+60))throw new Exception("Chat does not use the full available height alongside the floating players card");
                var scroll=VisualNodes(roster).OfType<ScrollViewer>().First();
                if(window.Height>=800&&scroll.ExtentHeight>scroll.ViewportHeight+.01)throw new Exception($"Eight compact room players still need scrolling: {scroll.ExtentHeight} > {scroll.ViewportHeight}; roster height {roster.ActualHeight}");
                foreach(var item in roster.Items.Cast<ListBoxItem>())if(item.ActualHeight>28.1)throw new Exception("Room player row is not compact: "+item.ActualHeight);
                var first=(ListBoxItem)roster.Items[1];var name=Nodes((DependencyObject)first.Content).OfType<TextBlock>().Single(t=>t.Text=="Player 1");
                if(name.ToolTip is not ToolTip{Content:StackPanel details})throw new Exception("Room player details are missing from the name tooltip");
                var tooltipText=Nodes(details).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                if(!tooltipText.Contains("Guest · IN BFME2")||!tooltipText.Contains("Spot 1 · Team 1")||!tooltipText.Contains("Blue")||!tooltipText.Contains("Not ready")||!tooltipText.Contains("Ping"))throw new Exception("Room player hover does not contain role, game, selections, color, ping and readiness");
                if(Nodes((DependencyObject)first.Content).OfType<TextBlock>().Any(t=>t.Text.Contains("Spot ")||t.Text.Contains("Team ")||t.Text.Contains("BFME2")))throw new Exception("Detailed room player state still occupies roster rows");
                VisibleActions(window,view);
                LaunchRole(window,true);
                if(footer.ActualHeight>54||consent.Visibility!=Visibility.Collapsed)throw new Exception("Routine host footer still reserves helper rows");
                var warningVisibility=warning.Visibility;warning.Visibility=Visibility.Collapsed;Layout(view,window.Width,window.Height);
                if(body.RowDefinitions[0].ActualHeight>.1)throw new Exception("A hidden patch warning still reserves a room header row");
                warning.Visibility=warningVisibility;Layout(view,window.Width,window.Height);
                var png=new PngBitmapEncoder();var image=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);image.Render(view);png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("dist");using var file=File.Create("dist/preview-room-chat-"+theme.ToLowerInvariant()+".png");png.Save(file);
            }
            double defaultRoster=chatPanel.PlayersCard.ActualHeight,defaultChat=transcript.ActualHeight;Layout(view,window.Width,window.Height+160);
            if(Math.Abs(chatPanel.PlayersCard.ActualHeight-defaultRoster)>1||transcript.ActualHeight<defaultChat+140)throw new Exception("A taller room stretches the floating roster instead of expanding the full-height chat");Layout(view,window.Width,window.Height);
            var removedPlayers=room.Players.Skip(2).ToArray();room.Players.RemoveRange(2,removedPlayers.Length);window.Refresh();Layout(view,window.Width,window.Height);
            if(roster.Items.Count!=2||chatPanel.PlayersCard.ActualHeight>160||chatPanel.PlayersCard.ActualHeight>defaultRoster-80)throw new Exception("A two-player floating card reserves unused height instead of shrinking to its contents");
            FloatingBounds(chatPanel,transcript,composer,view,false);room.Players.AddRange(removedPlayers);window.Refresh();Layout(view,window.Width,window.Height);
            if(roster.Items.Count!=8)throw new Exception("Restoring eight players did not repopulate the floating roster");
            double expandedWidth=transcript.ActualWidth,expandedHeight=transcript.ActualHeight;
            transcript.SelectAll();string selection=transcript.Selection.Text;
            var document=transcript.Document;
            if(!selection.Contains(longMessage,StringComparison.Ordinal))throw new Exception("The long wrapped room message is missing from cross-message selection");
            chatPanel.TogglePlayers();Layout(view,window.Width,window.Height);
            if(chatPanel.PlayersExpanded||chatPanel.PlayersCard.Visibility==Visibility.Visible||transcript.ActualWidth<expandedWidth+120||Math.Abs(transcript.ActualHeight-expandedHeight)>1||transcript.Selection.Text!=selection||!ReferenceEquals(document,transcript.Document))throw new Exception("Collapsing players hides chat selection, changes its height or fails to reclaim roster width");
            chatPanel.PlayersButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,window.Width,window.Height);
            if(!chatPanel.PlayersExpanded||Math.Abs(transcript.ActualWidth-expandedWidth)>1||Math.Abs(transcript.ActualHeight-expandedHeight)>1||transcript.Selection.Text!=selection)throw new Exception("The Players button does not restore a consistent floating layout without replacing selected text");
            transcript.Selection.Select(transcript.Document.ContentEnd,transcript.Document.ContentEnd);transcript.ScrollToEnd();
            FloatingBounds(chatPanel,transcript,composer,view,false);
            var isHost=typeof(PeerService).GetProperty(nameof(PeerService.IsHost))!;var localId=typeof(PeerService).GetProperty(nameof(PeerService.LocalId))!;var reconnecting=typeof(PeerService).GetProperty(nameof(PeerService.IsReconnecting))!;var current=typeof(PeerService).GetProperty(nameof(PeerService.Current))!;
            isHost.SetValue(browser.Peer,false);localId.SetValue(browser.Peer,"layout-1");var self=room.Players.Single(p=>p.Id=="layout-1");
            foreach(bool ready in new[]{false,true})
            {
                self.Ready=ready;window.Refresh();Layout(view,window.Width,window.Height);
                if(consent.Visibility!=Visibility.Visible||consent.IsChecked!=ready||summary.Visibility!=Visibility.Collapsed||footer.ActualHeight>54)throw new Exception("Routine guest readiness still shows helper rows or hides launch consent");
                if(Nodes(footer).OfType<TextBlock>().Any(t=>t.Text is "Room stays open when you exit the game." or "Check Ready when you want to play." or "Ready for the host to launch"))throw new Exception("Routine guest footer subtexts remain visible");
                VisibleActions(window,view);
                LaunchRole(window,false);
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

            window.Refresh();Layout(view,window.MinWidth,560);VisibleActions(window,view);FloatingBounds(chatPanel,transcript,composer,view,false);
            if(transcript.ActualHeight<50)throw new Exception("A short window clips the floating chat viewport or composer");
            chatPanel.PlayersExpanded=false;window.Refresh();Layout(view,window.Width,window.Height);
            if(chatPanel.PlayersExpanded||chatPanel.PlayersCard.Visibility==Visibility.Visible)throw new Exception("Room refresh or resize resets the user's collapsed player list");
            chatPanel.PlayersExpanded=true;Layout(view,window.Width,window.Height);FloatingBounds(chatPanel,transcript,composer,view,false);
            L.Apply("fr");room.Title="Ready";window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(title.Text!="Ready"||window.Title!="Ready · Fellowship"||title.ToolTip is not ToolTip{Content:TextBlock rawTip}||rawTip.Text!="Ready")throw new Exception("Live room rename was localized or failed to update the titlebar, tooltip and taskbar title");
            L.Apply("ar");room.Title=string.Join(" ",Enumerable.Repeat("غرفة الأصدقاء مع عنوان طويل",6));window.Refresh();Layout(view,window.MinWidth,window.MinHeight);
            if(title.Text!=room.Title||title.FlowDirection!=FlowDirection.RightToLeft||title.TextTrimming!=TextTrimming.CharacterEllipsis)throw new Exception("Long Arabic room title lost raw text, direction or ellipsis");
            FloatingBounds(chatPanel,transcript,composer,view,true);
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
            LaunchRole(window,true);
            FloatingBounds(chatPanel,transcript,composer,view,false);
            if(transcript.ActualHeight<50)throw new Exception("Minimum room size cuts off the chat composer or transcript");
            window.Content=view;
        }
        finally{window.Close();browser.Peer.Dispose();Appearance.Apply("Dark");L.Apply(language);}
        Console.WriteLine("PASS full-height room chat, bounded floating roster, selection-safe toggle, role-specific primary launch action and F9 menu, compact footer, connection guards, raw title and RTL layout");
    }
    static void LaunchRole(RoomWindow window,bool host)
    {
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var local=(Button)typeof(RoomWindow).GetField("launchMine",hidden)!.GetValue(window)!;
        var all=(Button)typeof(RoomWindow).GetField("launch",hidden)!.GetValue(window)!;
        var menu=(MenuItem)typeof(RoomWindow).GetField("launchMenu",hidden)!.GetValue(window)!;
        string label=host?"Launch all":"Launch game";
        if(host)
        {
            if(local.Visibility!=Visibility.Collapsed||all.Visibility!=Visibility.Visible||all.Content as string!=label||all.Tag as string!="Primary")throw new Exception("Room host does not have exactly its primary Launch all action");
        }
        else if(local.Visibility!=Visibility.Visible||local.Content as string!=label||local.Tag as string!="Primary"||all.Visibility!=Visibility.Collapsed)throw new Exception("Room guest does not have exactly its primary Launch game action");
        if(menu.Header as string!=label||menu.Command==null)throw new Exception("Room launch menu does not follow the current host or guest role");
        if(menu.Resources["Shortcut"] is not KeyGesture{Key:Key.F9,Modifiers:ModifierKeys.None}||!window.InputBindings.OfType<KeyBinding>().Any(binding=>binding.Key==Key.F9&&binding.Modifiers==ModifierKeys.None&&ReferenceEquals(binding.Command,menu.Command)))throw new Exception("Role switch detached F9 from the current room launch action");
    }
    static void FloatingBounds(RoomChatPanel panel,ChatTranscript transcript,FrameworkElement composer,FrameworkElement view,bool rtl)
    {
        if(!panel.PlayersExpanded||panel.PlayersCard.Visibility!=Visibility.Visible)throw new Exception("Floating player bounds require the expanded card");
        // The panel's own coordinate system is mirrored in Arabic. Compare
        // physical positions in the neutral outer visual, as the user sees them.
        var bounds=Bounds(panel,view);var card=Bounds(panel.PlayersCard,view);var chat=Bounds(transcript,view);var input=Bounds(composer,view);
        if(card.Width<=0||card.Height<=0||card.Left<bounds.Left-.5||card.Right>bounds.Right+.5||card.Top<bounds.Top-.5||card.Bottom>input.Top+.5)throw new Exception($"Floating players cover the composer or escape the chat panel: card={card}, composer={input}, panel={bounds}");
        if(chat.IntersectsWith(card))throw new Exception("The expanded players card overlaps the transcript viewport containing the long wrapped message");
        if(chat.Top>card.Top+24||chat.Bottom>input.Top+.5||input.Top-chat.Bottom>24||input.Bottom>bounds.Bottom+.5)throw new Exception($"Room chat does not extend through the available body above its full-width composer: chat={chat}, card={card}, composer={input}");
        if(rtl?card.Right>chat.Left+.5:chat.Right>card.Left+.5)throw new Exception($"Floating players obscure selectable chat text instead of reserving space on the logical trailing side: chat={chat}, card={card}, RTL={rtl}");
        if(rtl?card.Left-bounds.Left>24:bounds.Right-card.Right>24)throw new Exception("The floating player card is not anchored to the logical trailing edge");
        if(input.Width<bounds.Width-32)throw new Exception("The floating roster narrows the room message composer");
    }
    static Rect Bounds(FrameworkElement element,FrameworkElement ancestor)=>element.TransformToAncestor(ancestor).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
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

using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class ChatUiTests
{
    static void Settle(FrameworkElement view,double width=480,double height=330)
    {
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        view.UpdateLayout();
    }
    public static void Run()
    {
        var chat=new ChatTranscript();
        chat.Append("Aragorn","Ready for another game?");chat.Append("You","Ready!",true);Settle(chat);
        var incoming=(Section)chat.Document.Blocks.FirstBlock;var outgoing=(Section)chat.Document.Blocks.LastBlock;
        if(incoming.Margin.Left!=0||outgoing.Margin.Left!=0||incoming.Margin.Bottom>4||outgoing.Margin.Bottom>4||incoming.Blocks.Count!=1||outgoing.Blocks.Count!=1||incoming.Padding.Left!=34||outgoing.Padding.Left!=34)throw new Exception("Chat rows are not compact single-paragraph rows with a leading avatar gutter");
        foreach(var section in new[]{incoming,outgoing})
        {
            var paragraph=(Paragraph)section.Blocks.FirstBlock;var header=paragraph.Inlines.OfType<Run>().ToArray();
            if(header.Length!=2||header[0].FontSize!=12||header[1].FontSize<10||header[1].FontSize>11||paragraph.FontSize!=13||paragraph.Inlines.OfType<LineBreak>().Count()!=1)throw new Exception("Chat name/time header and body do not use the compact two-line layout");
            var name=header[0].ContentStart.GetCharacterRect(LogicalDirection.Forward);var body=paragraph.Inlines.OfType<Span>().First().ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if(body.Top-name.Top<16||body.Top-name.Top>21)throw new Exception("Chat message body is not directly underneath the name/time header");
            if(paragraph.Inlines.OfType<InlineUIContainer>().Any())throw new Exception("Chat avatars interrupted the selectable text document");
        }
        var avatars=((IEnumerable<(Rect Bounds,string Initial,Brush Fill)>)typeof(ChatTranscript).GetField("avatars",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!).ToArray();
        if(avatars.Length!=2||avatars.Any(avatar=>avatar.Bounds.Width!=26||avatar.Bounds.Height!=26)||avatars[0].Initial!="A"||avatars[1].Initial!="Y")throw new Exception("Chat rows do not display compact leading player avatars");
        chat.SelectAll();if(!chat.Selection.Text.Contains("Aragorn")||!chat.Selection.Text.Contains("another game?")||!chat.Selection.Text.Contains("Ready!")||chat.Selection.Text.Contains('\uFFFC'))throw new Exception("Avatar layout interrupted continuous conversation selection");
        chat.Selection.Select(chat.Document.ContentStart,chat.Document.ContentStart);
        foreach(var theme in new[]{"Light","Dark","Black"})
        {
            Appearance.Apply(theme);Settle(chat);
            var image=new RenderTargetBitmap(480,330,96,96,PixelFormats.Pbgra32);image.Render(chat);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("dist");using var file=File.Create("dist/preview-chat-"+theme.ToLowerInvariant()+".png");encoder.Save(file);
        }
        for(int i=0;i<32;i++)chat.Append(i%2==0?"Aragorn":"You","Message "+i,i%2==1);
        Settle(chat);AssertBottom(chat,"initial overflow");
        chat.Append("Aragorn","A new message after the scrollbar appears");Settle(chat);AssertBottom(chat,"incoming message");
        chat.ScrollToHome();Settle(chat);var position=chat.VerticalOffset;
        chat.Append("Aragorn","Keep the earlier conversation in view");Settle(chat);
        if(Math.Abs(chat.VerticalOffset-position)>2)throw new Exception("New message interrupted reading earlier messages");
        chat.ScrollToEnd();Settle(chat);chat.Append("You",string.Join(" ",Enumerable.Repeat("A long wrapped message",35)),true);Settle(chat);AssertBottom(chat,"wrapped outgoing message");
        Settle(chat,340,260);AssertBottom(chat,"window resize");
        chat.AppendBlocked("Blocked player","Hidden secret");Settle(chat);chat.SelectAll();
        if(chat.Selection.Text.Contains("Hidden secret"))throw new Exception("Blocked message leaked into selectable conversation");
        Appearance.Apply("Dark");
        Console.WriteLine("PASS compact avatar/name/time/body chat rows, continuous selection, sticky scrolling after overflow/wrapping/resize, earlier-message position and blocked text privacy");
        SystemEvents();
    }
    static readonly (ChatTranscript.SystemEventKind Kind,string Text,object[] Arguments)[] activity=[
        (ChatTranscript.SystemEventKind.PlayerJoined,"{0} joined the room.",["Ready"]),
        (ChatTranscript.SystemEventKind.PlayerLeft,"{0} left the room.",["Boromir"]),
        (ChatTranscript.SystemEventKind.HostChanged,"Room host is now {0}",["Aragorn"]),
        (ChatTranscript.SystemEventKind.Disconnected,"Connection lost. Reconnecting to the room…",[]),
        (ChatTranscript.SystemEventKind.Reconnected,"Room connection restored.",[]),
        (ChatTranscript.SystemEventKind.Info,"Ready request sent to room participants.",[])
    ];
    static ChatTranscript ActivityChat(bool rtl=false)
    {
        var chat=new ChatTranscript{FlowDirection=rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight};chat.Append("Aragorn","Ready for another game?");
        foreach(var message in activity)chat.AppendSystem(message.Kind,message.Text,message.Arguments);return chat;
    }
    static void SystemEvents()
    {
        string language=L.Code;L.Apply("en");var chat=ActivityChat();Settle(chat);
        var sections=chat.Document.Blocks.OfType<Section>().Skip(1).ToArray();
        foreach(var section in sections)
        {
            if(section.Blocks.Count!=1||section.Blocks.FirstBlock is not Paragraph paragraph||paragraph.Inlines.Cast<Inline>().Any(i=>i is LineBreak or InlineUIContainer)||paragraph.Inlines.LastInline is not System.Windows.Documents.Run)throw new Exception("System activity is not one continuous selectable paragraph without a heading");
            var first=paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);var last=paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if(last.Bottom-first.Top>22)throw new Exception("Short system activity still consumes more than one compact line");
            if(paragraph.Inlines.FirstInline is not System.Windows.Documents.Run time||time.FontSize<10||time.FontSize>11||!time.Text.Contains(':'))throw new Exception("System activity lost its compact readable timestamp");
        }
        chat.SelectAll();if(chat.Selection.Text.Contains("SYSTEM")||chat.Selection.Text.Contains('\uFFFC')||!chat.Selection.Text.Contains("Ready joined the room.")||!chat.Selection.Text.Contains("Ready for another game?"))throw new Exception("Event decoration interrupted selection/copy or retained a SYSTEM heading");
        L.Apply("fr");Settle(chat);var joined=(Run)((Paragraph)sections[0].Blocks.FirstBlock).Inlines.LastInline;
        if(joined.Text!=string.Format(L.T("{0} joined the room."),"Ready"))throw new Exception("Event text did not update language while retaining the raw player name");
        try
        {
            foreach(string theme in new[]{"Light","Dark","Black"})foreach(bool rtl in new[]{false,true})RenderSystemEvents(theme,rtl);
            L.Apply("en");chat.Selection.Select(chat.Document.ContentStart,chat.Document.ContentStart);
            chat.AppendSystem(string.Join(" ",Enumerable.Repeat("A long diagnostic activity message",20)));Settle(chat,280,220);
            var wrapped=(Paragraph)((Section)chat.Document.Blocks.LastBlock).Blocks.FirstBlock;
            if(wrapped.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom-wrapped.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top<30)throw new Exception("Long system activity cannot wrap within the conversation");
            for(int i=0;i<30;i++)chat.AppendSystem("Ready request sent to room participants.");Settle(chat);AssertBottom(chat,"system activity overflow");
            chat.ScrollToHome();Settle(chat);double earlier=chat.VerticalOffset;chat.AppendSystem(ChatTranscript.SystemEventKind.PlayerLeft,"{0} left the room.","Boromir");Settle(chat);
            if(Math.Abs(chat.VerticalOffset-earlier)>2)throw new Exception("System activity interrupted reading earlier messages");
            chat.ScrollToEnd();Settle(chat);chat.AppendSystem(ChatTranscript.SystemEventKind.HostChanged,"Room host is now {0}","Ready");Settle(chat);AssertBottom(chat,"new system activity");
        }
        finally{L.Apply(language);Appearance.Apply("Dark");}
        Console.WriteLine("PASS compact icon/timestamp/activity rows, raw-name live translations, continuous selection, wrapping, sticky scroll and all theme/RTL renders");
    }
    static void RenderSystemEvents(string theme,bool rtl)
    {
        L.Apply(rtl?"ar":"en");Appearance.Apply(theme);var chat=ActivityChat(rtl);var host=new Border{Background=UI.Brush("#191D24"),Child=chat};Settle(host,520,360);
        var icons=((IEnumerable<(Rect Bounds,ChatTranscript.SystemEventKind Kind,Brush Fill,Brush Stroke)>)typeof(ChatTranscript).GetField("systemIcons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!).ToArray();
        if(icons.Length!=6||icons.Select(i=>i.Kind).Distinct().Count()!=6||icons.Any(i=>i.Bounds.Width!=20||i.Bounds.Height!=20))throw new Exception("Activity rows do not display all six compact event icon kinds");
        var avatars=((IEnumerable<(Rect Bounds,string Initial,Brush Fill)>)typeof(ChatTranscript).GetField("avatars",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!).ToArray();
        if(avatars.Length!=1||avatars[0].Initial!="A")throw new Exception("System rows changed normal player avatars");
        var transform=chat.TransformToAncestor(host);var actual=icons.Select(i=>transform.TransformBounds(i.Bounds)).ToArray();
        if(actual.Any(b=>rtl?b.Left<400:b.Right>120)||actual.Any(b=>Math.Abs(rtl?b.Right-actual[0].Right:b.Left-actual[0].Left)>.5))throw new Exception("System icons do not follow the logical leading gutter");
        foreach(var section in chat.Document.Blocks.OfType<Section>().Skip(1))
        {
            var paragraph=(Paragraph)section.Blocks.FirstBlock;var time=(System.Windows.Documents.Run)paragraph.Inlines.FirstInline;
            var start=transform.TransformBounds(time.ContentStart.GetCharacterRect(LogicalDirection.Forward));var end=transform.TransformBounds(time.ContentEnd.GetCharacterRect(LogicalDirection.Backward));var timeBounds=Rect.Union(start,end);
            var message=transform.TransformBounds(paragraph.Inlines.LastInline.ContentStart.GetCharacterRect(LogicalDirection.Forward));double gap=rtl?timeBounds.Left-message.Right:message.Left-timeBounds.Right;
            if(gap<4)throw new Exception($"{theme} {(rtl?"RTL":"LTR")} activity timestamp touches the message: gap {gap}");
        }
        var image=new RenderTargetBitmap(520,360,96,96,PixelFormats.Pbgra32);image.Render(host);var pixels=new byte[520*360*4];image.CopyPixels(pixels,520*4,0);
        foreach(var icon in icons)
        {
            var expected=((SolidColorBrush)icon.Fill).Color;var sample=transform.Transform(new Point(icon.Bounds.Left+2,icon.Bounds.Top+10));int at=((int)sample.Y*520+(int)sample.X)*4;
            if(pixels[at]!=expected.B||pixels[at+1]!=expected.G||pixels[at+2]!=expected.R||expected==((SolidColorBrush)host.Background).Color)throw new Exception($"{theme} activity icon plate is not visible");
            int marked=0;for(int y=4;y<17;y++)for(int x=4;x<17;x++)
            {
                var point=transform.Transform(new Point(icon.Bounds.Left+x,icon.Bounds.Top+y));int offset=((int)point.Y*520+(int)point.X)*4;
                if(pixels[offset]!=expected.B||pixels[offset+1]!=expected.G||pixels[offset+2]!=expected.R)marked++;
            }
            if(marked<8)throw new Exception($"{theme} event vector is missing for {icon.Kind}");
        }
        Directory.CreateDirectory("dist");var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create($"dist/preview-chat-system-{theme.ToLowerInvariant()}-{(rtl?"rtl":"ltr")}.png");png.Save(file);
    }
    static void AssertBottom(ChatTranscript chat,string stage)
    {
        if(chat.ExtentHeight<=chat.ViewportHeight||chat.ExtentHeight-chat.ViewportHeight-chat.VerticalOffset>3)
            throw new Exception($"Chat did not follow {stage}: {chat.VerticalOffset}/{chat.ExtentHeight}, viewport {chat.ViewportHeight}");
    }
}

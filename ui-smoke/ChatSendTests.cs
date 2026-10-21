using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class ChatSendTests
{
    public static void Run()
    {
        L.Apply("en");
        var transcript=new ChatTranscript();var requests=new List<(string Text,string Id,TaskCompletionSource<bool> Ack)>();int sounds=0;
        var outbox=new ChatOutbox(transcript,(text,id)=>
        {
            var ack=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);requests.Add((text,id,ack));return ack.Task;
        },()=>sounds++);
        var first=outbox.Send("You","Instant outgoing message");
        if(requests.Count!=1||first.Completion!.IsCompleted||!first.Sending||!Text(transcript).Contains(first.Text))throw new Exception("Outgoing message waited for network completion before appearing");
        var second=outbox.Send("You","Continue typing while delivery is pending");
        if(requests.Count!=2||first.Id==second.Id||transcript.Document.Blocks.Count!=2)throw new Exception("A pending send blocked another local message");
        requests[0].Ack.SetException(new IOException("Message was not acknowledged."));Wait(()=>first.Completion!.IsCompleted);
        if(first.Confirmed||first.Sending||!Text(transcript).Contains("Delivery not confirmed")||!Text(transcript).Contains("Retry"))throw new Exception("Failed delivery lost its message or retry action: "+first.Confirmed+"/"+first.Sending+" text="+Text(transcript)+" error="+first.Completion?.Exception);
        outbox.Retry(first);
        if(requests.Count!=3||requests[2].Id!=first.Id||transcript.Document.Blocks.Count!=2)throw new Exception("Retry changed identity or appended a second bubble");
        requests[2].Ack.SetResult(true);Wait(()=>first.Completion!.IsCompleted);
        if(!first.Confirmed||Text(transcript).Contains("Delivery not confirmed")||Text(transcript).Contains("Message sent"))throw new Exception("Successful retry kept a failure or redundant delivery row");
        if(!transcript.ConfirmOutgoing(first.Id,first.Text)||transcript.Document.Blocks.Count!=2)throw new Exception("Room echo did not correlate with the optimistic message");
        if(!transcript.ConfirmOutgoing(first.Id,first.Text)||transcript.Document.Blocks.Count!=2)throw new Exception("Repeated room echo duplicated the outgoing message");
        requests[1].Ack.SetResult(true);Wait(()=>second.Completion!.IsCompleted);
        var echoed=outbox.Send("You","Echo confirms delivery before the socket reports failure");
        transcript.ConfirmOutgoing(echoed.Id,echoed.Text);requests[3].Ack.SetException(new IOException("Connection closed."));Wait(()=>echoed.Completion!.IsCompleted);
        if(!echoed.Confirmed||Text(transcript).Contains("Delivery not confirmed"))throw new Exception("Late socket failure replaced an already confirmed room echo");
        if(sounds!=2)throw new Exception("Delivery completion played duplicate send sounds");
        outbox.Retry(first);if(requests.Count!=4)throw new Exception("A confirmed message could be retried");
        var legacy=outbox.Send("You","Legacy echo");requests[4].Ack.SetResult(true);Wait(()=>legacy.Completion!.IsCompleted);
        if(!transcript.ConfirmOutgoing("",legacy.Text))throw new Exception("Legacy echo after acknowledgement duplicated the local bubble");
        transcript.SelectAll();if(!transcript.Selection.Text.Contains(first.Text)||!transcript.Selection.Text.Contains(second.Text))throw new Exception("Optimistic bubble status broke cross-message text selection");
        for(int i=0;i<205;i++)transcript.Append("Friend","Later message "+i);
        if(!transcript.ConfirmOutgoing(first.Id))throw new Exception("Delayed echo resurrected an outgoing bubble trimmed from the transcript");
        Console.WriteLine("PASS immediate local sending, concurrent drafts, visible failure/retry with stable IDs, echo deduplication, acknowledgement races and cross-message selection");

        var scrolling=new ChatTranscript();for(int i=0;i<35;i++)scrolling.Append("Friend","Conversation line "+i);Settle(scrolling);
        scrolling.ScrollToHome();Settle(scrolling);double earlier=scrolling.VerticalOffset;
        scrolling.Append("Friend","Incoming while reading earlier messages");Settle(scrolling);
        if(Math.Abs(scrolling.VerticalOffset-earlier)>2)throw new Exception("Incoming message interrupted earlier reading");
        scrolling.ScrollToVerticalOffset(scrolling.ExtentHeight);Settle(scrolling);
        var wrap=string.Join(" ",Enumerable.Repeat("A newly sent wrapped message",35));
        var newest=scrolling.AppendOutgoing("You",wrap,"scroll-rearm");Settle(scrolling);Bottom(scrolling,"send after scrolling up and back down");
        scrolling.SetSent(newest);Settle(scrolling);Bottom(scrolling,"delivery status after a wrapped outgoing message");
        scrolling.ScrollToHome();Settle(scrolling);scrolling.SelectAll();
        scrolling.AppendOutgoing("You","An explicit send clears stale transcript selection","scroll-selection");Settle(scrolling);Bottom(scrolling,"explicit send while an earlier transcript selection remains");
        if(!scrolling.Selection.IsEmpty)throw new Exception("Stale conversation selection prevented following an explicit send");
        Console.WriteLine("PASS scroll-up/back-to-bottom rearming, immediate outgoing follow after wrapping/status layout, and stale selection clearing");

        var linked=new ChatTranscript();string linkBody="See https://example.com/maps?id=1, http://localhost:8080/lobby. (https://example.com/map_(two)) and www.example.org/path! Unsafe javascript:alert(1) file:///C:/test ftp://example.org remain text.";
        linked.Append("https://sender.invalid",linkBody);Settle(linked);
        var links=Links(linked).Where(l=>l.NavigateUri!=null).ToArray();
        if(links.Length!=4||links.Any(l=>l.NavigateUri!.Scheme is not ("http" or "https")||l.Cursor!=System.Windows.Input.Cursors.Hand))throw new Exception("Chat did not restrict clickable links to safe HTTP/HTTPS destinations");
        var destinations=links.Select(l=>l.NavigateUri!.AbsoluteUri).ToArray();
        if(!destinations.SequenceEqual(new[]{"https://example.com/maps?id=1","http://localhost:8080/lobby","https://example.com/map_(two)","https://www.example.org/path"}))throw new Exception("Chat link detection included surrounding punctuation or changed balanced URL parentheses");
        linked.SelectAll();if(!linked.Selection.Text.Contains(linkBody))throw new Exception("Clickable links changed the selectable message text");
        linked.AppendSystem("https://diagnostic.example");linked.AppendBlocked("Blocked player","Hidden https://hidden.example/map");
        if(Links(linked).Count(l=>l.NavigateUri!=null)!=4||Text(linked).Contains("hidden.example"))throw new Exception("System text or hidden blocked text unexpectedly exposed links");
        var blocked=(Paragraph)((Section)linked.Document.Blocks.LastBlock).Blocks.FirstBlock;var toggle=blocked.Inlines.OfType<Hyperlink>().Single();
        toggle.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        if(Links(linked).Count(l=>l.NavigateUri!=null)!=5||!Text(linked).Contains("Hidden https://hidden.example/map"))throw new Exception("Revealing a blocked message did not enable its link");
        toggle.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        if(Links(linked).Count(l=>l.NavigateUri!=null)!=4||Text(linked).Contains("hidden.example"))throw new Exception("Hiding a blocked message left its link exposed");
        string longUrl="https://example.com/"+new string('a',350);var linkedOutgoing=linked.AppendOutgoing("You",longUrl,"linked-outgoing");linked.SetSent(linkedOutgoing);Settle(linked);
        var longLink=Links(linked).Single(l=>l.NavigateUri?.AbsoluteUri==longUrl);
        if(longLink.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom-longLink.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top<30)throw new Exception("Long clickable chat links do not wrap in the compact conversation width");
        linked.SelectAll();if(!linked.Selection.Text.Contains(linkBody)||!linked.Selection.Text.Contains(longUrl))throw new Exception("Links broke continuous selection across messages");
        Console.WriteLine("PASS clickable HTTP/HTTPS/www links, punctuation and balanced parentheses, unchanged selection, safe schemes, blocked privacy and long-link wrapping (navigation not invoked)");

        foreach(string theme in new[]{"Light","Dark","Black"})
        foreach(bool rtl in new[]{false,true})RenderChatRows(theme,rtl);
        Appearance.Apply("Dark");
    }
    static IEnumerable<Hyperlink> Links(ChatTranscript chat)=>chat.Document.Blocks.OfType<Section>().SelectMany(s=>s.Blocks.OfType<Paragraph>()).SelectMany(p=>Inlines(p.Inlines)).OfType<Hyperlink>();
    static IEnumerable<Inline> Inlines(InlineCollection inlines)
    {foreach(var inline in inlines){yield return inline;if(inline is Span span)foreach(var nested in Inlines(span.Inlines))yield return nested;}}
    static string Text(ChatTranscript chat)=>new TextRange(chat.Document.ContentStart,chat.Document.ContentEnd).Text;
    static void Bottom(ChatTranscript chat,string stage)
    {
        if(chat.ExtentHeight-chat.ViewportHeight-chat.VerticalOffset>3)throw new Exception("Chat did not follow "+stage);
    }
    static void Wait(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(3);var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{if(condition()||DateTime.UtcNow>=deadline)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
        if(!condition())throw new Exception("Timed out waiting for the chat delivery UI");
    }
    static void Settle(FrameworkElement view)
    {
        view.Measure(new Size(480,330));view.Arrange(new Rect(0,0,480,330));view.UpdateLayout();
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);view.UpdateLayout();
    }
    static void RenderChatRows(string theme,bool rtl)
    {
        Appearance.Apply(theme);
        var chat=new ChatTranscript{FlowDirection=rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight};
        chat.Append("Aragorn","Ready for another game?");chat.Append("You","Ready!",true);
        var host=new Border{Background=UI.Brush("#191D24"),Child=chat};Settle(host);
        var bounds=((IEnumerable<(Rect Bounds,string Initial,Brush Fill)>)typeof(ChatTranscript).GetField("avatars",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!).ToArray();
        if(bounds.Length!=2||bounds.Any(b=>b.Bounds.Width!=26||b.Bounds.Height!=26))throw new Exception("Received and outgoing messages lack compact avatar bounds");
        var transform=chat.TransformToAncestor(host);var actual=bounds.Select(b=>transform.TransformBounds(b.Bounds)).ToArray();
        if(Math.Abs(rtl?actual[0].Right-actual[1].Right:actual[0].Left-actual[1].Left)>1||actual.Any(b=>rtl?b.Left<240:b.Right>240))throw new Exception("Chat avatars did not share the logical leading edge");
        foreach(var section in chat.Document.Blocks.OfType<Section>())
        {
            var paragraph=(Paragraph)section.Blocks.FirstBlock;var top=paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);var bottom=paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if(bottom.Bottom-top.Top>44)throw new Exception("Single-line body and name/time header consume too much vertical space");
        }
        var image=new RenderTargetBitmap(480,330,96,96,PixelFormats.Pbgra32);image.Render(host);var pixels=new byte[480*330*4];image.CopyPixels(pixels,480*4,0);
        Directory.CreateDirectory("dist");var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create($"dist/preview-chat-rows-{theme.ToLowerInvariant()}-{(rtl?"rtl":"ltr")}.png");png.Save(file);
        foreach(var avatar in bounds)
        {
            var expected=((SolidColorBrush)avatar.Fill).Color;var sample=transform.Transform(new Point(avatar.Bounds.Left+3,avatar.Bounds.Top+10));int x=(int)Math.Floor(sample.X),y=(int)Math.Floor(sample.Y),at=(y*480+x)*4;
            if(pixels[at]!=expected.B||pixels[at+1]!=expected.G||pixels[at+2]!=expected.R)throw new Exception($"{theme} {(rtl?"RTL":"LTR")} player avatar is not visibly rendered at {x},{y}");
            if(expected==((SolidColorBrush)host.Background).Color)throw new Exception("Player avatar disappears into the chat background");
        }
        // The body area remains the host surface; only avatars draw decoration.
        var clear=transform.Transform(new Point(450,bounds[0].Bounds.Top+25));int clearAt=((int)clear.Y*480+(int)clear.X)*4;var surface=((SolidColorBrush)host.Background).Color;
        if(pixels[clearAt]!=surface.B||pixels[clearAt+1]!=surface.G||pixels[clearAt+2]!=surface.R)throw new Exception("Chat message still paints a bubble across the conversation surface");
        Console.WriteLine($"PASS {theme} {(rtl?"RTL":"LTR")} compact avatar rows, name/time header, body underneath and bubble-free shared surface");
    }
}

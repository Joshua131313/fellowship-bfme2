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
    }
    static void AssertBottom(ChatTranscript chat,string stage)
    {
        if(chat.ExtentHeight<=chat.ViewportHeight||chat.ExtentHeight-chat.ViewportHeight-chat.VerticalOffset>3)
            throw new Exception($"Chat did not follow {stage}: {chat.VerticalOffset}/{chat.ExtentHeight}, viewport {chat.ViewportHeight}");
    }
}

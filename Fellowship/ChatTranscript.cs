using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Fellowship;

// One document keeps selection and copying continuous across compact chat rows.
public sealed class ChatTranscript : RichTextBox
{
    public sealed class OutgoingMessage
    {
        internal readonly Section Section;
        internal readonly Span Delivery=new(){FontSize=10};
        internal bool EchoSeen;
        public string Id {get;}
        public string Text {get;}
        public bool Sending {get;internal set;}
        public bool Confirmed {get;internal set;}
        public Task? Completion {get;internal set;}
        internal OutgoingMessage(string id,string text,Section section){Id=id;Text=text;Section=section;}
    }
    sealed record Bubble(Section Section,bool Mine,string Id="");
    readonly List<Bubble> bubbles=[];
    readonly Dictionary<string,OutgoingMessage> outgoing=new();
    readonly HashSet<string> knownOutgoing=[];
    readonly Queue<string> outgoingOrder=new();
    List<(Rect Bounds,Brush Fill)> backgrounds=[];
    ScrollViewer? viewer;
    bool following=true,scrollQueued,scrolling;
    double ContentWidth=>viewer?.ViewportWidth>0?viewer.ViewportWidth:ActualWidth-Padding.Left-Padding.Right;
    public ChatTranscript()
    {
        IsReadOnly=true;IsDocumentEnabled=true;BorderThickness=new(0);Background=Brushes.Transparent;
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Padding=new(10);Document=new FlowDocument{PagePadding=new(0),FontFamily=new("Segoe UI"),FontSize=13,ColumnWidth=double.PositiveInfinity};
        SetResourceReference(ForegroundProperty,"ThemeDEE3ED");SetResourceReference(SelectionBrushProperty,"InteractionSelected");
        AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,e)=>
        {
            viewer=e.OriginalSource as ScrollViewer??viewer;
            // Growing content is layout, not a request from the reader to stop following.
            if(!scrolling)
            {
                // Reaching the bottom is reader intent even when scrolling and
                // document/viewport layout changes arrive in the same event.
                if(VerticalOffset>=ExtentHeight-ViewportHeight-3)following=true;
                else if(Math.Abs(e.VerticalChange)>0.1&&Math.Abs(e.ExtentHeightChange)<0.1&&Math.Abs(e.ViewportHeightChange)<0.1)following=false;
            }
            if(Math.Abs(e.ViewportWidthChange)>0.1)LayoutBubbles();
            if(following&&(e.ExtentHeightChange!=0||e.ViewportHeightChange!=0))FollowLatest();
            UpdateBackgrounds();
        }));
        SizeChanged+=(_,_)=>{LayoutBubbles();if(following)FollowLatest();};
        LayoutUpdated+=(_,_)=>UpdateBackgrounds();
        var menu=new ContextMenu();Section? target=null;
        menu.Items.Add(UI.MenuAction("Copy selected text",()=>CopySelection(),()=>!Selection.IsEmpty));
        menu.Items.Add(UI.MenuAction("Copy message",()=>{if(target!=null)Copy(new TextRange(target.ContentStart,target.ContentEnd).Text.TrimEnd());},()=>target!=null));
        menu.Items.Add(UI.MenuAction("Select all",()=>{Focus();SelectAll();}));ContextMenu=menu;
        ContextMenuOpening+=(_,_)=>{var position=GetPositionFromPoint(Mouse.GetPosition(this),true);DependencyObject? node=position?.Parent;target=null;while(node!=null){if(node is Section section){target=section;break;}node=(node as FrameworkContentElement)?.Parent;}};
    }
    void Copy(string text){try{if(text.Length>0)Clipboard.SetText(text);}catch(System.Runtime.InteropServices.ExternalException){ToolTip="Clipboard is busy. Try copying again.";}}
    void CopySelection()=>Copy(Selection.Text);
    public void Append(string name,string text,bool mine=false)
    {
        if(mine)FollowOwnMessage();var section=CreateSection(name);((Paragraph)section.Blocks.FirstBlock).Inlines.Add(MessageBody(text));Add(section,mine);
    }
    public OutgoingMessage AppendOutgoing(string name,string text,string id)
    {
        if(outgoing.TryGetValue(id,out var existing))return existing;
        FollowOwnMessage();
        var section=CreateSection(name);((Paragraph)section.Blocks.FirstBlock).Inlines.Add(MessageBody(text));
        var message=new OutgoingMessage(id,text,section);outgoing[id]=message;
        knownOutgoing.Add(id);outgoingOrder.Enqueue(id);
        while(outgoingOrder.Count>1000)knownOutgoing.Remove(outgoingOrder.Dequeue());
        SetSending(message);Add(section,true,id);return message;
    }
    public bool ConfirmOutgoing(string id,string text="")
    {
        OutgoingMessage? message=null;
        if(id.Length>0){if(!knownOutgoing.Contains(id))return false;outgoing.TryGetValue(id,out message);}
        else message=outgoing.Values.FirstOrDefault(m=>!m.EchoSeen&&m.Text==text);
        if(message==null)return id.Length>0&&knownOutgoing.Contains(id);
        message.EchoSeen=true;SetSent(message);return true;
    }
    public void SetSending(OutgoingMessage message)
    {
        if(message.Confirmed)return;message.Sending=true;Delivery(message,"Sending…",UI.Muted);
    }
    public void SetSent(OutgoingMessage message)
    {
        message.Sending=false;message.Confirmed=true;
        if(message.Delivery.Parent is Paragraph line)line.Inlines.Remove(message.Delivery);
        LayoutBubbles();if(following)FollowLatest();
    }
    public void SetFailed(OutgoingMessage message,Exception error,Action retry)
    {
        message.Sending=false;if(message.Confirmed)return;
        Delivery(message,"Delivery not confirmed","#E5B77A");message.Delivery.ToolTip=L.ErrorMessage(error.Message);
        message.Delivery.Inlines.Add(new Run("  ·  "));
        var label=new Run();L.Bind(label,Run.TextProperty,"Retry");
        var link=new Hyperlink(label){Cursor=Cursors.Hand,Foreground=UI.Brush(UI.Accent)};
        link.Click+=(_,e)=>{e.Handled=true;retry();};message.Delivery.Inlines.Add(link);
        LayoutBubbles();if(following)FollowLatest();
    }
    void Delivery(OutgoingMessage message,string text,string color)
    {
        message.Delivery.Inlines.Clear();message.Delivery.ToolTip=null;message.Delivery.Foreground=UI.Brush(color);
        message.Delivery.Inlines.Add(new Run("  ·  "));var run=new Run();L.Bind(run,Run.TextProperty,text);message.Delivery.Inlines.Add(run);
        if(message.Delivery.Parent==null)((Paragraph)message.Section.Blocks.FirstBlock).Inlines.Add(message.Delivery);
        LayoutBubbles();if(following)FollowLatest();
    }
    sealed class SystemText(string format,object[] arguments):IValueConverter
    {
        public object Convert(object value,Type target,object parameter,CultureInfo culture)=>string.Format(CultureInfo.CurrentUICulture,L.T(format),arguments);
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
    public void AppendSystem(string text,params object[] arguments)
    {
        var section=CreateSection("");var line=(Paragraph)section.Blocks.FirstBlock;var header=line.Inlines.FirstInline;L.Bind(header,Run.TextProperty,"SYSTEM");header.Foreground=UI.Brush(UI.Muted);
        var run=Body("");if(arguments.Length==0)L.Bind(run,Run.TextProperty,text);else BindingOperations.SetBinding(run,Run.TextProperty,new Binding(nameof(L.Direction)){Mode=BindingMode.OneWay,Source=L.Current,Converter=new SystemText(text,arguments)});line.Inlines.Add(run);Add(section,false);
    }
    static Section CreateSection(string name)
    {
        var section=new Section{Margin=new(0,0,0,4),Padding=new(8,4,8,4)};
        var line=new Paragraph{Margin=new(0),LineHeight=18,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
        line.Inlines.Add(new Run(name){FontWeight=FontWeights.SemiBold,FontSize=12,Foreground=UI.Brush(UI.Accent)});
        line.Inlines.Add(new Run("  "+DateTime.Now.ToString("HH:mm")+"  "){FontSize=9,Foreground=UI.Brush(UI.Muted)});
        section.Blocks.Add(line);return section;
    }
    static Run Body(string text)=>new(text){Foreground=UI.Brush("#DEE3ED")};
    static readonly Regex webLinks=new(@"(?<![\p{L}\p{N}_@])(?:https?://|www\.)[^\s<>]+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
    static Span MessageBody(string text)
    {
        var body=new Span{Foreground=UI.Brush("#DEE3ED")};int position=0;
        foreach(Match match in webLinks.Matches(text))
        {
            var value=match.Value.TrimEnd('.',',',';',':','!','?','\'','"','’','”');
            foreach(var (open,close) in new[]{('(',')'),('[',']'),('{','}')})
                while(value.EndsWith(close)&&value.Count(c=>c==close)>value.Count(c=>c==open))value=value[..^1];
            if(!Uri.TryCreate(value.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?"https://"+value:value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||string.IsNullOrEmpty(uri.Host))continue;
            if(match.Index>position)body.Inlines.Add(Body(text[position..match.Index]));
            var link=new Hyperlink(new Run(value)){NavigateUri=uri,Cursor=Cursors.Hand,Foreground=UI.Brush(UI.Accent),TextDecorations=TextDecorations.Underline,ToolTip=new ToolTip{Content=uri.AbsoluteUri}};
            link.RequestNavigate+=(_,e)=>{e.Handled=true;try{Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch(Exception error){UI.Error(error.Message);}};
            body.Inlines.Add(link);position=match.Index+value.Length;
        }
        if(position<text.Length)body.Inlines.Add(Body(text[position..]));return body;
    }
    public void AppendBlocked(string name,string text)
    {
        var section=CreateSection(name);var line=(Paragraph)section.Blocks.FirstBlock;bool shown=false;Span? body=null;
        var label=new Run();L.Bind(label,Run.TextProperty,"Show blocked message");var toggle=new Hyperlink(label){Cursor=Cursors.Hand,Foreground=UI.Brush(UI.Accent)};
        toggle.Click+=(_,e)=>{e.Handled=true;shown=!shown;if(shown){if(body==null){body=new Span(new Run("  "));body.Inlines.Add(MessageBody(text));}line.Inlines.Add(body);}else line.Inlines.Remove(body!);L.Bind(label,Run.TextProperty,shown?"Hide message":"Show blocked message");LayoutBubbles();if(following)FollowLatest();};
        line.Inlines.Add(toggle);Add(section,false);
    }
    void Add(Section section,bool mine,string id="")
    {
        bubbles.Add(new(section,mine,id));Document.Blocks.Add(section);
        while(bubbles.Count>200){var old=bubbles[0];Document.Blocks.Remove(old.Section);if(old.Id.Length>0)outgoing.Remove(old.Id);bubbles.RemoveAt(0);}
        LayoutBubbles();if(following)FollowLatest();
    }
    void LayoutBubbles()
    {
        double width=ContentWidth;
        if(width<80)return;
        double cap=width,dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach(var bubble in bubbles)
        {
            double natural=80;
            foreach(var block in bubble.Section.Blocks)
            {
                if(block is Paragraph p)
                {
                    var text=new TextRange(p.ContentStart,p.ContentEnd).Text;
                    var measure=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection,new Typeface(Document.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),p.FontSize,Foreground,dpi);
                    natural=Math.Max(natural,Math.Ceiling(measure.WidthIncludingTrailingWhitespace)+16);
                }
                else if(block is BlockUIContainer container&&container.Child is FrameworkElement child)
                {child.Measure(new Size(Math.Max(1,cap-24),double.PositiveInfinity));natural=Math.Max(natural,child.DesiredSize.Width+24);}
            }
            double gap=Math.Max(0,width-Math.Min(cap,natural));
            // Every row starts on the same logical leading edge. WPF mirrors
            // this control's visual coordinate system for right-to-left UI.
            var margin=new Thickness(0,0,gap,4);
            if(bubble.Section.Margin!=margin)bubble.Section.Margin=margin;
        }
    }
    void FollowOwnMessage()
    {
        following=true;
        // Selection in the conversation must not leave an explicitly sent
        // message offscreen. Keep keyboard focus in the composer's text box.
        Selection.Select(Document.ContentEnd,Document.ContentEnd);
    }
    void FollowLatest()
    {
        if(scrollQueued)return;scrollQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            scrollQueued=false;
            if(!following||!Selection.IsEmpty||IsMouseCaptureWithin)return;
            scrolling=true;
            try{UpdateLayout();ScrollToEnd();viewer?.ScrollToEnd();UpdateLayout();}
            finally{scrolling=false;}
        }));
    }
    void UpdateBackgrounds()
    {
        var next=new List<(Rect,Brush)>();
        foreach(var bubble in bubbles)
        {
            var first=bubble.Section.ContentStart;var last=bubble.Section.ContentEnd;
            if(!first.HasValidLayout||!last.HasValidLayout)continue;
            var top=first.GetCharacterRect(LogicalDirection.Forward);var bottom=last.GetCharacterRect(LogicalDirection.Backward);
            if(top.IsEmpty||bottom.IsEmpty)continue;
            var section=bubble.Section;
            double width=ContentWidth-section.Margin.Left-section.Margin.Right;
            if(width<=0)continue;
            var bounds=new Rect(Padding.Left+section.Margin.Left,top.Top-4,width,Math.Max(1,bottom.Bottom-top.Top+8));
            next.Add((bounds,UI.Brush(bubble.Mine?"#263657":"#263042")));
        }
        if(!backgrounds.SequenceEqual(next)){backgrounds=next;InvalidateVisual();}
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        var border=new Pen(UI.Brush("#343F51"),1);
        foreach(var (bounds,fill) in backgrounds)drawingContext.DrawRoundedRectangle(fill,border,bounds,6,6);
        drawingContext.Pop();
    }
}

// The document is updated before network work begins. A retry reuses the same
// message identity and section, including after an acknowledgement was lost.
public sealed class ChatOutbox(ChatTranscript transcript,Func<string,string,Task> deliver,Action? sent=null,Action<Exception?>? state=null)
{
    public ChatTranscript.OutgoingMessage Send(string name,string text)
    {
        var message=transcript.AppendOutgoing(name,text,Guid.NewGuid().ToString("N"));Retry(message);return message;
    }
    public void Retry(ChatTranscript.OutgoingMessage message)
    {
        if(message.Confirmed||message.Completion is {IsCompleted:false})return;
        transcript.SetSending(message);message.Completion=Deliver(message);
    }
    async Task Deliver(ChatTranscript.OutgoingMessage message)
    {
        try{await deliver(message.Text,message.Id);await Update(()=>{transcript.SetSent(message);sent?.Invoke();state?.Invoke(null);});}
        catch(Exception error)
        {
            // An echo may confirm delivery just before a connection reports an
            // error. Never replace that confirmation with a retry prompt.
            await Update(()=>{transcript.SetFailed(message,error,()=>Retry(message));if(!message.Confirmed)state?.Invoke(error);});
        }
    }
    Task Update(Action action)
    {
        if(transcript.Dispatcher.HasShutdownStarted)return Task.CompletedTask;
        if(transcript.Dispatcher.CheckAccess()){action();return Task.CompletedTask;}
        return transcript.Dispatcher.InvokeAsync(action).Task;
    }
}

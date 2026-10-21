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
    public enum SystemEventKind {Info,PlayerJoined,PlayerLeft,HostChanged,Disconnected,Reconnected}
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
    sealed record ChatRow(Section Section,string Name,bool Mine,string Id="",SystemEventKind? System=null);
    readonly List<ChatRow> rows=[];
    readonly Dictionary<string,OutgoingMessage> outgoing=new();
    readonly HashSet<string> knownOutgoing=[];
    readonly Queue<string> outgoingOrder=new();
    List<(Rect Bounds,string Initial,Brush Fill)> avatars=[];
    List<(Rect Bounds,SystemEventKind Kind,Brush Fill,Brush Stroke)> systemIcons=[];
    ScrollViewer? viewer;
    bool following=true,scrollQueued,scrolling;
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
            if(Math.Abs(e.ViewportWidthChange)>0.1)LayoutRows();
            if(following&&(e.ExtentHeightChange!=0||e.ViewportHeightChange!=0))FollowLatest();
            UpdateAvatars();
        }));
        SizeChanged+=(_,_)=>{LayoutRows();if(following)FollowLatest();};
        LayoutUpdated+=(_,_)=>UpdateAvatars();
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
        if(mine)FollowOwnMessage();var section=CreateSection(name);((Paragraph)section.Blocks.FirstBlock).Inlines.Add(MessageBody(text));Add(section,name,mine);
    }
    public OutgoingMessage AppendOutgoing(string name,string text,string id)
    {
        if(outgoing.TryGetValue(id,out var existing))return existing;
        FollowOwnMessage();
        var section=CreateSection(name);((Paragraph)section.Blocks.FirstBlock).Inlines.Add(MessageBody(text));
        var message=new OutgoingMessage(id,text,section);outgoing[id]=message;
        knownOutgoing.Add(id);outgoingOrder.Enqueue(id);
        while(outgoingOrder.Count>1000)knownOutgoing.Remove(outgoingOrder.Dequeue());
        SetSending(message);Add(section,name,true,id);return message;
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
        LayoutRows();if(following)FollowLatest();
    }
    public void SetFailed(OutgoingMessage message,Exception error,Action retry)
    {
        message.Sending=false;if(message.Confirmed)return;
        Delivery(message,"Delivery not confirmed","#E5B77A");message.Delivery.ToolTip=L.ErrorMessage(error.Message);
        message.Delivery.Inlines.Add(new Run("  ·  "));
        var label=new Run();L.Bind(label,Run.TextProperty,"Retry");
        var link=new Hyperlink(label){Cursor=Cursors.Hand,Foreground=UI.Brush(UI.Accent)};
        link.Click+=(_,e)=>{e.Handled=true;retry();};message.Delivery.Inlines.Add(link);
        LayoutRows();if(following)FollowLatest();
    }
    void Delivery(OutgoingMessage message,string text,string color)
    {
        message.Delivery.Inlines.Clear();message.Delivery.ToolTip=null;message.Delivery.Foreground=UI.Brush(color);
        message.Delivery.Inlines.Add(new Run("  ·  "));var run=new Run();L.Bind(run,Run.TextProperty,text);message.Delivery.Inlines.Add(run);
        if(message.Delivery.Parent==null)((Paragraph)message.Section.Blocks.FirstBlock).Inlines.Add(message.Delivery);
        LayoutRows();if(following)FollowLatest();
    }
    sealed class SystemText(string format,object[] arguments):IValueConverter
    {
        public object Convert(object value,Type target,object parameter,CultureInfo culture)=>string.Format(CultureInfo.CurrentUICulture,L.T(format),arguments);
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
    public void AppendSystem(string text,params object[] arguments)=>AppendSystem(SystemEventKind.Info,text,arguments);
    public void AppendSystem(SystemEventKind kind,string text,params object[] arguments)
    {
        var section=new Section{Margin=new(0,0,0,4),Padding=new(34,2,0,2)};
        var line=new Paragraph{Margin=new(0),FontSize=12,LineHeight=18,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
        line.Inlines.Add(new Run(DateTime.Now.ToString("HH:mm")){FontSize=10,Foreground=UI.Brush(UI.Muted),FlowDirection=FlowDirection.LeftToRight});
        // Keep the gap outside the LTR time run: trailing spaces inside its
        // directional embedding can move away from Arabic message text.
        line.Inlines.Add(new Run("\u00A0\u00A0"){FontSize=12});
        var run=new Run{Foreground=UI.Brush("#B5C0D3")};if(arguments.Length==0)L.Bind(run,Run.TextProperty,text);else BindingOperations.SetBinding(run,Run.TextProperty,new Binding(nameof(L.Direction)){Mode=BindingMode.OneWay,Source=L.Current,Converter=new SystemText(text,arguments)});
        line.Inlines.Add(run);section.Blocks.Add(line);Add(section,"",false,system:kind);
    }
    static Section CreateSection(string name)
    {
        var section=new Section{Margin=new(0,0,0,4),Padding=new(34,2,0,2)};
        var line=new Paragraph{Margin=new(0),FontSize=13,LineHeight=18,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
        line.Inlines.Add(new Run(name){FontWeight=FontWeights.SemiBold,FontSize=12,Foreground=UI.Brush("#EFF1F5")});
        line.Inlines.Add(new Run("  "+DateTime.Now.ToString("HH:mm")){FontSize=10,Foreground=UI.Brush(UI.Muted)});
        line.Inlines.Add(new LineBreak());
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
        toggle.Click+=(_,e)=>{e.Handled=true;shown=!shown;if(shown){if(body==null){body=new Span(new Run("  "));body.Inlines.Add(MessageBody(text));}line.Inlines.Add(body);}else line.Inlines.Remove(body!);L.Bind(label,Run.TextProperty,shown?"Hide message":"Show blocked message");LayoutRows();if(following)FollowLatest();};
        line.Inlines.Add(toggle);Add(section,name,false);
    }
    void Add(Section section,string name,bool mine,string id="",SystemEventKind? system=null)
    {
        rows.Add(new(section,name,mine,id,system));Document.Blocks.Add(section);
        while(rows.Count>200){var old=rows[0];Document.Blocks.Remove(old.Section);if(old.Id.Length>0)outgoing.Remove(old.Id);rows.RemoveAt(0);}
        LayoutRows();if(following)FollowLatest();
    }
    void LayoutRows()
    {
        // One text paragraph per message retains continuous selection, including
        // links and delivery controls. Only its leading gutter is decorative.
        foreach(var row in rows)
        {
            var margin=new Thickness(0,0,0,4);if(row.Section.Margin!=margin)row.Section.Margin=margin;
        }
        UpdateAvatars();
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
    void UpdateAvatars()
    {
        var next=new List<(Rect,string,Brush)>();
        var events=new List<(Rect,SystemEventKind,Brush,Brush)>();
        foreach(var row in rows)
        {
            var first=row.Section.ContentStart;if(!first.HasValidLayout)continue;
            var top=first.GetCharacterRect(LogicalDirection.Forward);if(top.IsEmpty)continue;
            if(row.System is {} kind)
            {
                var body=((Paragraph)row.Section.Blocks.FirstBlock).Inlines.LastInline.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if(body.IsEmpty)continue;
                var (fill,stroke)=kind switch
                {
                    SystemEventKind.PlayerJoined or SystemEventKind.Reconnected=>("#193C31",UI.Green),
                    SystemEventKind.PlayerLeft or SystemEventKind.Disconnected=>("#3A2C16","#F2BD60"),
                    SystemEventKind.HostChanged=>("#26344D",UI.Accent),
                    _=>("#263042",UI.Muted)
                };
                events.Add((new Rect(Padding.Left+3,body.Top+(body.Height-20)/2,20,20),kind,UI.Brush(fill),UI.Brush(stroke)));continue;
            }
            string name=row.Name.Trim();string initial=name.Length==0?"?":StringInfo.GetNextTextElement(name).ToUpperInvariant();
            // WPF mirrors the control's drawing coordinates in right-to-left UI,
            // putting this same gutter on the logical leading side in Arabic.
            next.Add((new Rect(Padding.Left,top.Top+1,26,26),initial,UI.Brush("#2C3854")));
        }
        if(!avatars.SequenceEqual(next)||!systemIcons.SequenceEqual(events)){avatars=next;systemIcons=events;InvalidateVisual();}
    }
    static readonly Dictionary<SystemEventKind,Geometry> eventGeometry=new()
    {
        [SystemEventKind.PlayerJoined]=Geometry.Parse("M 8,2 L 12,2 L 12,12 L 8,12 M 2,7 L 9,7 M 6,4 L 9,7 L 6,10"),
        [SystemEventKind.PlayerLeft]=Geometry.Parse("M 6,2 L 2,2 L 2,12 L 6,12 M 5,7 L 12,7 M 9,4 L 12,7 L 9,10"),
        [SystemEventKind.HostChanged]=Geometry.Parse("M 2,4 L 5,7 L 7,2 L 9,7 L 12,4 L 11,11 L 3,11 Z M 4,13 L 10,13"),
        [SystemEventKind.Disconnected]=Geometry.Parse("M 3,2 L 3,5 M 7,2 L 7,5 M 1,5 L 9,5 L 9,7 A 4,4 0 0 1 5,11 L 5,13 M 10,2 L 13,5 M 13,2 L 10,5"),
        [SystemEventKind.Reconnected]=Geometry.Parse("M 6,9 L 5,10 A 3,3 0 0 1 1,6 L 4,3 A 3,3 0 0 1 8,3 M 6,5 L 9,2 A 3,3 0 0 1 13,6 L 10,9 A 3,3 0 0 1 6,9 M 5,8 L 9,4"),
        [SystemEventKind.Info]=new GeometryGroup{Children={new EllipseGeometry(new Point(7,7),5.5,5.5),Geometry.Parse("M 7,6 L 7,10 M 7,3.5 L 7,3.6")}}
    };
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach(var (bounds,initial,fill) in avatars)
        {
            drawingContext.DrawRoundedRectangle(fill,null,bounds,6,6);
            var letter=new FormattedText(initial,CultureInfo.CurrentUICulture,FlowDirection,new Typeface(Document.FontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),12,UI.Brush("#B9CCFF"),dpi);
            // FormattedText anchors right-to-left text at the origin's right
            // edge, while left-to-right text starts at its left edge.
            double center=FlowDirection==FlowDirection.RightToLeft?(bounds.Width+letter.Width)/2:(bounds.Width-letter.Width)/2;
            drawingContext.DrawText(letter,new Point(bounds.X+center,bounds.Y+(bounds.Height-letter.Height)/2));
        }
        foreach(var (bounds,kind,fill,stroke) in systemIcons)
        {
            drawingContext.DrawRoundedRectangle(fill,null,bounds,5,5);
            var pen=new Pen(stroke,1.4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            drawingContext.PushTransform(new TranslateTransform(bounds.Left+3,bounds.Top+3));drawingContext.DrawGeometry(null,pen,eventGeometry.GetValueOrDefault(kind,eventGeometry[SystemEventKind.Info]));drawingContext.Pop();
        }
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

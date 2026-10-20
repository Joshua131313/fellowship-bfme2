using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Fellowship;

// One document keeps selection and copying continuous across message bubbles.
public sealed class ChatTranscript : RichTextBox
{
    sealed record Bubble(Section Section,bool Mine);
    readonly List<Bubble> bubbles=[];
    List<(Rect Bounds,Brush Fill)> backgrounds=[];
    ScrollViewer? viewer;
    bool following=true,scrollQueued,scrolling;
    double ContentWidth=>viewer?.ViewportWidth>0?viewer.ViewportWidth:ActualWidth-Padding.Left-Padding.Right;
    public ChatTranscript()
    {
        IsReadOnly=true;IsDocumentEnabled=true;BorderThickness=new(0);Background=Brushes.Transparent;
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Padding=new(14);Document=new FlowDocument{PagePadding=new(0),FontFamily=new("Segoe UI"),FontSize=13,ColumnWidth=double.PositiveInfinity};
        SetResourceReference(ForegroundProperty,"ThemeDEE3ED");SetResourceReference(SelectionBrushProperty,"InteractionSelected");
        AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,e)=>
        {
            viewer=e.OriginalSource as ScrollViewer??viewer;
            // Growing content is layout, not a request from the reader to stop following.
            if(!scrolling&&Math.Abs(e.VerticalChange)>0.1&&Math.Abs(e.ExtentHeightChange)<0.1&&Math.Abs(e.ViewportHeightChange)<0.1)
                following=VerticalOffset>=ExtentHeight-ViewportHeight-3;
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
        var section=CreateSection(name);section.Blocks.Add(Body(text));Add(section,mine);
    }
    public void AppendSystem(string text)
    {
        var section=CreateSection("");var header=((Paragraph)section.Blocks.FirstBlock).Inlines.FirstInline;L.Bind(header,Run.TextProperty,"SYSTEM"+"  ·  "+DateTime.Now.ToString("HH:mm"));
        var run=new Run();L.Bind(run,Run.TextProperty,text);section.Blocks.Add(new Paragraph(run){Margin=new(0),Foreground=UI.Brush("#DEE3ED")});Add(section,false);
    }
    static Section CreateSection(string name)
    {
        var section=new Section{Margin=new(0,0,0,12),Padding=new(12,9,12,9)};
        section.Blocks.Add(new Paragraph(new Run(name+"  ·  "+DateTime.Now.ToString("HH:mm"))){FontSize=10,Foreground=UI.Brush(UI.Muted),Margin=new(0,0,0,4)});return section;
    }
    static Paragraph Body(string text)=>new(new Run(text)){Margin=new(0),Foreground=UI.Brush("#DEE3ED")};
    public void AppendBlocked(string name,string text)
    {
        var section=CreateSection(name+" · Blocked");var body=Body(text);bool shown=false;
        var toggle=UI.Button("Show blocked message",()=>{shown=!shown;if(shown)section.Blocks.Add(body);else section.Blocks.Remove(body);LayoutBubbles();if(following)FollowLatest();});
        toggle.Click+=(_,_)=>L.Bind(toggle,ContentControl.ContentProperty,shown?"Hide message":"Show blocked message");
        section.Blocks.Add(new BlockUIContainer(toggle));Add(section,false);
    }
    void Add(Section section,bool mine)
    {
        bubbles.Add(new(section,mine));Document.Blocks.Add(section);
        while(bubbles.Count>200){Document.Blocks.Remove(bubbles[0].Section);bubbles.RemoveAt(0);}
        LayoutBubbles();if(following)FollowLatest();
    }
    void LayoutBubbles()
    {
        double width=ContentWidth;
        if(width<80)return;
        double cap=Math.Min(420,width*.78),dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach(var bubble in bubbles)
        {
            double natural=80;
            foreach(var block in bubble.Section.Blocks)
            {
                if(block is Paragraph p)
                {
                    var text=new TextRange(p.ContentStart,p.ContentEnd).Text;
                    var measure=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection,new Typeface(Document.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),p.FontSize,Foreground,dpi);
                    natural=Math.Max(natural,Math.Ceiling(measure.WidthIncludingTrailingWhitespace)+40);
                }
                else if(block is BlockUIContainer container&&container.Child is FrameworkElement child)
                {child.Measure(new Size(Math.Max(1,cap-24),double.PositiveInfinity));natural=Math.Max(natural,child.DesiredSize.Width+24);}
            }
            double gap=Math.Max(0,width-Math.Min(cap,natural));
            var margin=bubble.Mine?new Thickness(gap,0,0,12):new Thickness(0,0,gap,12);
            if(bubble.Section.Margin!=margin)bubble.Section.Margin=margin;
        }
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
            var bounds=new Rect(Padding.Left+section.Margin.Left,top.Top-9,width,Math.Max(1,bottom.Bottom-top.Top+18));
            next.Add((bounds,UI.Brush(bubble.Mine?"#263657":"#1E232C")));
        }
        if(!backgrounds.SequenceEqual(next)){backgrounds=next;InvalidateVisual();}
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        foreach(var (bounds,fill) in backgrounds)drawingContext.DrawRoundedRectangle(fill,null,bounds,11,11);
        drawingContext.Pop();
    }
}

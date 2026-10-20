using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
namespace Fellowship;

public sealed class ChatTranscript : RichTextBox
{
    public ChatTranscript()
    {
        IsReadOnly=true;IsDocumentEnabled=true;BorderThickness=new(0);Background=System.Windows.Media.Brushes.Transparent;
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Padding=new(14);Document=new FlowDocument{PagePadding=new(0),FontFamily=new("Segoe UI"),FontSize=13};
        SetResourceReference(ForegroundProperty,"ThemeDEE3ED");SetResourceReference(SelectionBrushProperty,"InteractionSelected");
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
        var section=CreateSection(name,mine);section.Blocks.Add(Body(text));Document.Blocks.Add(section);Trim();
    }
    public void AppendSystem(string text)
    {
        var section=CreateSection("",false);var header=((Paragraph)section.Blocks.FirstBlock).Inlines.FirstInline;L.Bind(header,Run.TextProperty,"SYSTEM"+"  ·  "+DateTime.Now.ToString("HH:mm"));
        var run=new Run();L.Bind(run,Run.TextProperty,text);section.Blocks.Add(new Paragraph(run){Margin=new(0),Foreground=UI.Brush("#DEE3ED")});Document.Blocks.Add(section);Trim();
    }
    Section CreateSection(string name,bool mine)
    {
        var section=new Section{Margin=new(0,0,0,12),Padding=new(10),Background=UI.Brush(mine?"#263657":"#1E232C")};
        section.Blocks.Add(new Paragraph(new Run(name+"  ·  "+DateTime.Now.ToString("HH:mm"))){FontSize=10,Foreground=UI.Brush(UI.Muted),Margin=new(0,0,0,5)});return section;
    }
    static Paragraph Body(string text)=>new(new Run(text)){Margin=new(0),Foreground=UI.Brush("#DEE3ED")};
    public void AppendBlocked(string name,string text)
    {
        var section=CreateSection(name+" · Blocked",false);var body=Body(text);bool shown=false;
        var toggle=UI.Button("Show blocked message",()=>{shown=!shown;if(shown)section.Blocks.Add(body);else section.Blocks.Remove(body);});
        toggle.Click+=(_,_)=>L.Bind(toggle,ContentControl.ContentProperty,shown?"Hide message":"Show blocked message");
        section.Blocks.Add(new BlockUIContainer(toggle));Document.Blocks.Add(section);Trim();
    }
    void Trim(){while(Document.Blocks.Count>200)Document.Blocks.Remove(Document.Blocks.FirstBlock);if(Selection.IsEmpty)ScrollToEnd();}
}

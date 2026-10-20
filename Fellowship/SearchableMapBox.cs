using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace Fellowship;
public sealed class SearchableMapBox : ComboBox
{
    TextBox? search;
    TextBlock? empty;
    public SearchableMapBox(){IsTextSearchEnabled=false;SetResourceReference(StyleProperty,typeof(ComboBox));ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));}
    public static bool Matches(string name,string query)=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(name,query.Trim(),CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
    public override void OnApplyTemplate()
    {
        if(search!=null){search.TextChanged-=FilterChanged;search.PreviewKeyDown-=SearchKey;}
        base.OnApplyTemplate();if(Template.FindName("PART_NoMaps",this) is TextBlock emptyLabel)L.Bind(emptyLabel,TextBlock.TextProperty,"No maps match your search");
        search=GetTemplateChild("PART_MapSearch") as TextBox;empty=GetTemplateChild("PART_NoMaps") as TextBlock;
        if(search!=null){search.Visibility=Visibility.Visible;search.ToolTip="Search map names";System.Windows.Automation.AutomationProperties.SetName(search,"Search maps");search.TextChanged+=FilterChanged;search.PreviewKeyDown+=SearchKey;}
    }
    protected override void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>{if(!IsDropDownOpen||search==null)return;search.Clear();UpdateLayout();Filter();search.Focus();});
    }
    void FilterChanged(object sender,TextChangedEventArgs e)=>Filter();
    void Filter()
    {
        int count=0;
        foreach(var item in Items)
        {
            bool match=Matches(item.ToString()??"",search?.Text??"");if(match)count++;
            if(ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem container)container.Visibility=match?Visibility.Visible:Visibility.Collapsed;
        }
        if(empty!=null)empty.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void SearchKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){IsDropDownOpen=false;Focus();e.Handled=true;}
        if(e.Key==Key.Down)
        {
            foreach(var item in Items)if(ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem {Visibility:Visibility.Visible} container){container.Focus();break;}
            e.Handled=true;
        }
    }
}

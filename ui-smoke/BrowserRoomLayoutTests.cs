using Fellowship;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class BrowserRoomLayoutTests
{
    public static void Run()
    {
        string language=L.Code;L.Apply("en");var settings=Settings.InMemory();settings.Name="Frodo";
        var browser=new BrowserWindow(settings);var view=(FrameworkElement)browser.Content;browser.Content=null;
        var body=(Grid)typeof(BrowserWindow).GetField("dashboardRooms",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(browser)!;
        var search=(TextBox)typeof(BrowserWindow).GetField("search",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(browser)!;
        var host=Nodes(body).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Host room");
        var table=body.Children.Cast<FrameworkElement>().Single(e=>Grid.GetRow(e)==3);var footer=body.Children.Cast<FrameworkElement>().Single(e=>Grid.GetRow(e)==4);var status=body.Children.Cast<FrameworkElement>().Single(e=>Grid.GetRow(e)==5);
        try
        {
            Layout(view,browser.Width,browser.Height);
            if(body.RowDefinitions[1].ActualHeight>.1||Nodes(body).OfType<TextBlock>().Any(t=>t.Text=="THE BATTLE FOR MIDDLE-EARTH II"||t.Text=="Game rooms"&&t.FontSize>20))throw new Exception("Game rooms still reserves its removed page header");
            double expanded=table.ActualHeight;var oldHeader=new Border{Height=90};Grid.SetRow(oldHeader,1);body.Children.Add(oldHeader);Layout(view,browser.Width,browser.Height);
            if(Math.Abs(expanded-table.ActualHeight-90)>1)throw new Exception("The rooms table does not absorb the full removed header height");body.Children.Remove(oldHeader);
            foreach(string code in new[]{"en","fr","es","tr","ar"})
            {
                L.Apply(code);Layout(view,browser.MinWidth,browser.MinHeight);CheckLayout(view,body,search,host,table,footer,status,code=="ar");
            }
            L.Apply("en");Layout(view,browser.Width,browser.Height);CheckLayout(view,body,search,host,table,footer,status,false);double normal=table.ActualHeight;
            Layout(view,browser.Width,browser.Height+180);CheckLayout(view,body,search,host,table,footer,status,false);
            if(Math.Abs(table.ActualHeight-normal-180)>1)throw new Exception("A taller browser does not give all additional height to the room table");
            foreach(var (code,theme) in new[]{("en","Light"),("ar","Dark")})
            {
                L.Apply(code);Appearance.Apply(theme);Layout(view,browser.Width,browser.Height);CheckLayout(view,body,search,host,table,footer,status,code=="ar");
                var image=new RenderTargetBitmap((int)browser.Width,(int)browser.Height,96,96,PixelFormats.Pbgra32);image.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("dist");using var file=File.Create($"dist/preview-browser-rooms-compact-{code}.png");png.Save(file);
            }
            var select=typeof(BrowserWindow).GetMethod("SelectDashboard",BindingFlags.Instance|BindingFlags.NonPublic)!;select.Invoke(browser,["Online players"]);
            if(body.Visibility!=Visibility.Collapsed)throw new Exception("Other dashboard pages no longer hide the room browser");select.Invoke(browser,["Game rooms"]);
            if(body.Visibility!=Visibility.Visible||!Nodes(body).Contains(host)||!Nodes(body).Contains(search))throw new Exception("Returning to Game rooms lost its filter actions");
        }
        finally{browser.Content=view;browser.Peer.Dispose();L.Apply(language);Appearance.Apply("Dark");}
        Console.WriteLine("PASS header-free room browser, matching filter gaps, adjacent mirrored Host action, full table height growth, translated minimum-width filters and visible footer controls");
    }
    static void CheckLayout(FrameworkElement view,Grid body,TextBox search,Button host,FrameworkElement table,FrameworkElement footer,FrameworkElement status,bool rtl)
    {
        var input=Bounds(search,view);var action=Bounds(host,view);double gap=rtl?input.Left-action.Right:action.Left-input.Right;
        if(gap< -.5||gap>24||Math.Abs(input.Bottom-action.Bottom)>1||search.ActualWidth<120)throw new Exception($"Host room is not adjacent to the search input on its logical trailing side: input={input}, host={action}, RTL={rtl}");
        var tableBounds=Bounds(table,body);var footerBounds=Bounds(footer,body);var statusBounds=Bounds(status,body);
        var filter=body.Children.Cast<FrameworkElement>().Single(e=>Grid.GetRow(e)==2);var filterBounds=Bounds(filter,body);
        double beforeFilters=filterBounds.Top-body.RowDefinitions.Take(2).Sum(row=>row.ActualHeight),afterFilters=tableBounds.Top-filterBounds.Bottom;
        if(Math.Abs(beforeFilters-afterFilters)>.5||Math.Abs(beforeFilters-12)>.5)throw new Exception($"Room filter gaps do not match: above={beforeFilters}, below={afterFilters}");
        if(body.RowDefinitions[1].ActualHeight>.1||table.ActualHeight<180||tableBounds.Bottom>footerBounds.Top+.5||footerBounds.Bottom>statusBounds.Top+.5||statusBounds.Bottom>body.ActualHeight+.5)throw new Exception("Room table or bottom controls have unused header space or overlap");
        foreach(var button in Nodes(footer).OfType<Button>())
        {
            var bounds=Bounds(button,view);if(bounds.Left< -.5||bounds.Right>view.ActualWidth+.5||bounds.Bottom>view.ActualHeight+.5)throw new Exception("A browser bottom action is clipped: "+button.Content);
        }
    }
    static Rect Bounds(FrameworkElement element,FrameworkElement ancestor)=>element.TransformToAncestor(ancestor).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
    static void Layout(FrameworkElement view,double width,double height)
    {
        foreach(var element in VisualNodes(view).OfType<UIElement>().Reverse())element.InvalidateMeasure();
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);view.UpdateLayout();
    }
    static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Nodes(child))yield return node;}
    static IEnumerable<DependencyObject> VisualNodes(DependencyObject root)
    {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in VisualNodes(VisualTreeHelper.GetChild(root,i)))yield return node;}
}

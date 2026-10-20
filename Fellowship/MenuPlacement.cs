using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Fellowship;

public static class MenuPlacement
{
    public static CustomPopupPlacement[] Beside(Point cursor,Size menu,double gap=4)=>[
        new(new Point(cursor.X+gap,cursor.Y+gap/2),PopupPrimaryAxis.Vertical),
        new(new Point(cursor.X-menu.Width-gap,cursor.Y+gap/2),PopupPrimaryAxis.Vertical)];

    public static ContextMenu AtCursor(Window owner)
    {
        var menu=new ContextMenu{Placement=PlacementMode.Custom,PlacementTarget=owner};
        menu.SetBinding(FrameworkElement.FlowDirectionProperty,new System.Windows.Data.Binding(nameof(L.Direction)){Source=L.Current});
        Point? anchor=null;
        menu.Closed+=(_,_)=>anchor=null;
        menu.CustomPopupPlacementCallback=(size,target,offset)=>
        {
            // WPF custom placements use screen pixels; account for DPI and RTL transforms.
            if(anchor==null)
            {
                var local=Mouse.GetPosition(owner);
                anchor=PresentationSource.FromVisual(owner)!=null?(Point)(owner.PointToScreen(local)-owner.PointToScreen(new Point())):local;
            }
            return Beside(anchor.Value,size,4*VisualTreeHelper.GetDpi(owner).DpiScaleX);
        };
        return menu;
    }

    public static void Submenu(MenuItem item)
    {
        item.ApplyTemplate();
        if(item.Template.FindName("PART_Popup",item) is not Popup popup)return;
        popup.Placement=PlacementMode.Custom;
        popup.CustomPopupPlacementCallback=(size,target,offset)=>
        {
            double left=0,right=target.Width;
            if(PresentationSource.FromVisual(item)!=null)
            {
                var origin=item.PointToScreen(new Point());var edge=item.PointToScreen(new Point(item.ActualWidth,0));
                left=Math.Min(origin.X,edge.X)-origin.X;right=Math.Max(origin.X,edge.X)-origin.X;
            }
            var gap=4*VisualTreeHelper.GetDpi(item).DpiScaleX;
            var toRight=new CustomPopupPlacement(new Point(right+gap,0),PopupPrimaryAxis.Vertical);
            var toLeft=new CustomPopupPlacement(new Point(left-size.Width-gap,0),PopupPrimaryAxis.Vertical);
            return item.FlowDirection==FlowDirection.RightToLeft?[toLeft,toRight]:[toRight,toLeft];
        };
    }
}

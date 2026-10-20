using Fellowship;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class MenuUiTests
{
    public static void Run()
    {
        var placements=MenuPlacement.Beside(new Point(100,50),new Size(220,300));
        if(placements.Length!=2||placements[0].Point.X<=100||placements[1].Point.X+220>=100||placements[0].PrimaryAxis!=PopupPrimaryAxis.Vertical)throw new Exception("Player menus do not prefer the right of the cursor with a left fallback");
        var menu=MenuPlacement.AtCursor(new Window());
        if(menu.Placement!=PlacementMode.Custom||menu.PlacementTarget is not Window)throw new Exception("Player menu cursor positioning was not configured");
        L.Apply("ar");if(menu.FlowDirection!=FlowDirection.RightToLeft)throw new Exception("Player menu text does not follow Arabic direction");L.Apply("en");
        var menuItem=new MenuItem{Header="Server"};menuItem.ApplyTemplate();
        if(menuItem.Cursor!=Cursors.Hand)throw new Exception("Menu options do not use the pointer cursor");

        var settings=Settings.InMemory();settings.Name="Frodo";
        if(settings.NewHostedRoom().Title!="Frodo's room")throw new Exception("First room title default changed");
        settings.LastRoomTitle="1v1 pro";
        var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        if(restored.NewHostedRoom().Title!="1v1 pro")throw new Exception("Last created room title did not survive a restart");
        restored.LastHostedSetup=new("Rhûn","maps\\rhun.map",6,4000,200,false,false,1,2);
        if(restored.NewHostedRoom().Title!="1v1 pro")throw new Exception("Restoring match settings overwrote the room title");

        var picker=new RoomPresetPicker(settings){Width=560,SelectedIndex=1};
        picker.Measure(new Size(560,34));picker.Arrange(new Rect(0,0,560,34));picker.UpdateLayout();picker.ApplyTemplate();
        var content=(ContentPresenter)picker.Template.FindName("SelectedContent",picker);
        if(content.HorizontalAlignment!=HorizontalAlignment.Left||content.TranslatePoint(new Point(),picker).X>14)throw new Exception("Preset caption is centered");
        picker.Reload();picker.UpdateLayout();
        if(picker.SelectedIndex!=1||content.HorizontalAlignment!=HorizontalAlignment.Left)throw new Exception("Preset refresh changed caption alignment or selection");
        var image=new RenderTargetBitmap(560,40,96,96,PixelFormats.Pbgra32);image.Render(picker);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create("dist/preview-preset-alignment.png");encoder.Save(file);
        Console.WriteLine("PASS right-of-cursor player menus, pointer cursors, preset alignment after refresh, and saved room title with/without previous match settings");
    }
}

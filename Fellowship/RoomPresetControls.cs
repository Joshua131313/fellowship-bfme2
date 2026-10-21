using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Fellowship;
public sealed class RoomPresetPicker : ComboBox
{
    readonly Settings settings;
    string patch;
    bool refreshing;
    public event Action<RoomPreset>? Picked;
    public event Action? ModeChanged;
    public event Action? CustomPicked;
    public bool IsCustom=>SelectedItem is not ComboBoxItem{Tag:RoomPreset};
    public RoomPresetPicker(Settings settings,string patch="1.00")
    {
        this.settings=settings;this.patch=GamePatches.Normalize(patch);Style=(Style)Application.Current.FindResource(typeof(ComboBox));Height=34;ToolTip="Choose a saved room preset";
        DropDownOpened+=(_,_)=>Reload();
        SelectionChanged+=(_,_)=>{if(refreshing)return;if(SelectedItem is ComboBoxItem{Tag:RoomPreset preset})Picked?.Invoke(preset);else CustomPicked?.Invoke();ModeChanged?.Invoke();};
        Reload();
    }
    public void SetPatch(string selected)
    {
        selected=GamePatches.Normalize(selected);if(patch==selected)return;
        patch=selected;Reload();
    }
    public void Reload()
    {
        var previous=(SelectedItem as ComboBoxItem)?.Tag as RoomPreset;refreshing=true;
        Items.Clear();var custom=new ComboBoxItem{Padding=new(10,5,10,5),ContentTemplate=Caption(null)};L.Bind(custom,ContentControl.ContentProperty,"Custom preset");Items.Add(custom);
        var maps=GameInstallation.Maps(settings.GamePath,patch);
        foreach(var preset in RoomPreset.Defaults(maps,patch).Concat(settings.RoomPresets.Where(p=>GamePatches.Normalize(p.Patch)==patch)))
        {
            var item=new ComboBoxItem{Tag=preset,Content=preset.Name,ContentTemplate=Caption(preset),Padding=new(10,5,10,5),HorizontalContentAlignment=HorizontalAlignment.Stretch,IsEnabled=maps.Any(m=>m.Path.Equals(preset.MapPath,StringComparison.OrdinalIgnoreCase))};
            if(!item.IsEnabled){item.ToolTip="The preset map is not installed: "+preset.Map;ToolTipService.SetShowOnDisabled(item,true);}Items.Add(item);
        }
        SelectedItem=Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.IsEnabled&&i.Tag is RoomPreset p&&p.Name==previous?.Name&&p.BuiltIn==previous.BuiltIn&&(p.BuiltIn||GamePatches.Normalize(previous.Patch)==patch))??Items[0];refreshing=false;ModeChanged?.Invoke();
    }
    DataTemplate Caption(RoomPreset? preset)
    {
        // Data-backed captions render directly; UIElement snapshots can center or disappear.
        var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(DockPanel));
        if(preset is {BuiltIn:false})
        {
            var delete=new FrameworkElementFactory(typeof(Button));delete.SetValue(DockPanel.DockProperty,Dock.Right);delete.SetValue(ContentControl.ContentProperty,"×");delete.SetValue(WidthProperty,28d);delete.SetValue(HeightProperty,22d);delete.SetValue(Control.PaddingProperty,new Thickness(0));delete.SetValue(MarginProperty,new Thickness(12,0,0,0));delete.SetValue(ToolTipProperty,"Delete "+preset.Name);delete.SetValue(TagProperty,"Danger");
            delete.SetBinding(VisibilityProperty,new Binding(nameof(IsDropDownOpen)){Source=this,Converter=new BooleanToVisibilityConverter()});
            delete.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>{e.Handled=true;settings.RoomPresets.Remove(preset);settings.Save();Reload();}));row.AppendChild(delete);
        }
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding());text.SetValue(TextBlock.FontSizeProperty,12d);text.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);row.AppendChild(text);template.VisualTree=row;template.Seal();return template;
    }
    public static async Task<string?> PromptName(Window owner)
    {
        var dialog=new Window{Owner=owner,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=UI.Shell(dialog,"Save preset",380,210);dialog.MinHeight=210;
        var name=new TextBox{MaxLength=40,Margin=new(0,8,0,16)};
        var save=UI.Button("Save preset",()=>UI.FinishPopup(dialog,true),true);save.IsDefault=true;
        UI.Require(save,()=>!string.IsNullOrWhiteSpace(name.Text),()=>"Enter a preset name.");
        var cancel=UI.Button("Cancel",()=>UI.FinishPopup(dialog,false));cancel.IsCancel=true;
        body.Children.Add(new Border{Padding=new(20),Child=UI.Stack(UI.LocalizedText("Preset name",12),name,UI.Row(cancel,save))});
        dialog.Loaded+=(_,_)=>name.Focus();return await UI.ShowPopup(dialog)?name.Text.Trim():null;
    }
}

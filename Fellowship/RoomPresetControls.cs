using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Fellowship;
public sealed class RoomPresetPicker : ComboBox
{
    readonly Settings settings;
    bool refreshing;
    public event Action<RoomPreset>? Picked;
    public event Action? ModeChanged;
    public event Action? CustomPicked;
    public bool IsCustom=>SelectedItem is not ComboBoxItem{Tag:RoomPreset};
    public RoomPresetPicker(Settings settings)
    {
        this.settings=settings;Style=(Style)Application.Current.FindResource(typeof(ComboBox));Height=34;ToolTip="Choose a saved room preset";
        DropDownOpened+=(_,_)=>Reload();
        SelectionChanged+=(_,_)=>{if(refreshing)return;if(SelectedItem is ComboBoxItem{Tag:RoomPreset preset})Picked?.Invoke(preset);else CustomPicked?.Invoke();ModeChanged?.Invoke();};
        Reload();
    }
    public void Reload()
    {
        var previous=(SelectedItem as ComboBoxItem)?.Tag as RoomPreset;refreshing=true;
        Items.Clear();Items.Add(new ComboBoxItem{Content=UI.LocalizedText("Custom preset",12),Padding=new(10,5,10,5)});
        foreach(var preset in RoomPreset.Defaults(GameInstallation.Maps(settings.GamePath)).Concat(settings.RoomPresets))
        {
            var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(UI.Text(preset.Name,12));
            if(!preset.BuiltIn)
            {
                var delete=UI.Button("×",()=>{});delete.Width=28;delete.Height=22;delete.Padding=new(0);delete.Margin=new(12,0,0,0);delete.ToolTip="Delete "+preset.Name;delete.Tag="Danger";
                delete.SetBinding(VisibilityProperty,new Binding(nameof(IsDropDownOpen)){Source=this,Converter=new BooleanToVisibilityConverter()});
                delete.Click+=(_,e)=>{e.Handled=true;settings.RoomPresets.Remove(preset);settings.Save();Reload();};Grid.SetColumn(delete,1);row.Children.Add(delete);
            }
            Items.Add(new ComboBoxItem{Tag=preset,Content=row,Padding=new(10,5,10,5),HorizontalContentAlignment=HorizontalAlignment.Stretch});
        }
        SelectedItem=Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag is RoomPreset p&&p.Name==previous?.Name&&p.BuiltIn==previous.BuiltIn)??Items[0];refreshing=false;ModeChanged?.Invoke();
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

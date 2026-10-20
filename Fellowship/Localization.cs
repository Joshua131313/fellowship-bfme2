using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Fellowship;

// ResourceManager provides satellite-resource lookup and English fallback. Only UI
// resources are translated; player names, chat, addresses and protocol values stay intact.
public sealed class L : INotifyPropertyChanged
{
    public static L Current {get;}=new();
    static L()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.ToolTipOpeningEvent,new ToolTipEventHandler((sender,_)=>{
            if(sender is FrameworkElement element&&element.ToolTip is string text){var source=tips.GetOrCreateValue(element);if(text!=source.Translated)source.Original=text;source.Translated=T(source.Original);element.ToolTip=source.Translated;}
        }));
        EventManager.RegisterClassHandler(typeof(Button),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var button=(Button)sender;if(button.Content is string label&&label.Length>3){button.MinHeight=Math.Max(34,double.IsNaN(button.Height)?34:button.Height);button.Height=double.NaN;}
        }));
        EventManager.RegisterClassHandler(typeof(ToolTip),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var tip=(ToolTip)sender;if(tip.Content is string text&&!BindingOperations.IsDataBound(tip,ContentControl.ContentProperty))Bind(tip,ContentControl.ContentProperty,text);
        }));
        EventManager.RegisterClassHandler(typeof(ComboBox),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var box=(ComboBox)sender;box.MinHeight=Math.Max(34,double.IsNaN(box.Height)?34:box.Height);box.Height=double.NaN;
            if(box.ItemTemplate==null&&string.IsNullOrEmpty(box.DisplayMemberPath)&&box is not SearchableMapBox&&box is not RoomPresetPicker)Choices(box);
        }));
        EventManager.RegisterClassHandler(typeof(CheckBox),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var control=(CheckBox)sender;if(control.Content is string text&&!BindingOperations.IsDataBound(control,ContentControl.ContentProperty))Bind(control,ContentControl.ContentProperty,text);
        }));
        EventManager.RegisterClassHandler(typeof(MenuItem),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var control=(MenuItem)sender;if(control.Header is string text&&!BindingOperations.IsDataBound(control,HeaderedItemsControl.HeaderProperty))Bind(control,HeaderedItemsControl.HeaderProperty,text);
        }));
        EventManager.RegisterClassHandler(typeof(Expander),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            var control=(Expander)sender;if(control.Header is string text&&!BindingOperations.IsDataBound(control,HeaderedContentControl.HeaderProperty))Bind(control,HeaderedContentControl.HeaderProperty,text);
        }));
    }
    sealed class TipSource {public string Original="",Translated="";}
    static readonly ConditionalWeakTable<FrameworkElement,TipSource> tips=new();
    static readonly ResourceManager resources=new("Fellowship.Localization.Strings",typeof(L).Assembly){IgnoreCase=true};
    public sealed record Language(string Code,string Name){public override string ToString()=>Name;}
    public static readonly Language[] Languages=[new("en","English"),new("fr","Français"),new("es","Español"),new("ar","العربية"),new("tr","Türkçe")];
    public static string Code {get;private set;}="en";
    public event PropertyChangedEventHandler? PropertyChanged;
    public FlowDirection Direction=>Code=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
    public string this[string key]=>T(key);
    public static string T(string text)
    {
        if(Code=="en")return text;
        var culture=CultureInfo.GetCultureInfo(Code);
        var value=resources.GetString(text,culture);if(value!=null)return value;
        // Decorative prefixes and menu accelerators are not part of the resource key.
        var key=text.Replace("_","").Trim();
        foreach(var prefix in new[]{"＋  ","＋ ","▶  ","↻  ","● ","○ ","◷ "})if(key.StartsWith(prefix)){var rest=key[prefix.Length..];return prefix+T(rest);}
        if(key.EndsWith("  →"))return T(key[..^3])+"  →";
        if(key.EndsWith("  ↑"))return T(key[..^3])+"  ↑";
        if(key.EndsWith("…")&&resources.GetString(key[..^1],culture) is {} ellipsis)return ellipsis+"…";
        var direct=resources.GetString(key,culture);if(direct!=null)return direct;
        foreach(var separator in new[]{"\n"," · ","   ·   ","  |  ",": "})
        {
            var parts=text.Split(separator);if(parts.Length<2)continue;
            return string.Join(separator,parts.Select(T));
        }
        foreach(var template in templates.Value)
        {
            var match=template.Pattern.Match(text);if(!match.Success)continue;
            var format=resources.GetString(template.Key,culture);if(format==null)continue;
            return string.Format(culture,format,match.Groups.Cast<Group>().Skip(1).Select(g=>(object)g.Value).ToArray());
        }
        return text;
    }
    static readonly Lazy<(string Key,Regex Pattern)[]> templates=new(()=>resources.GetResourceSet(CultureInfo.InvariantCulture,true,true)!.Cast<System.Collections.DictionaryEntry>()
        .Select(e=>(string)e.Key).Where(k=>k.Contains("{0}"))
        .Select(k=>(k,new Regex("^"+Regex.Replace(Regex.Escape(k),@"\\\{\d+}","(.+?)")+"$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(20)))).ToArray());
    public static string ErrorMessage(string text)
    {
        var translated=T(text);return Code=="en"||translated!=text?translated:T("An unexpected error occurred. Please retry. Copy diagnostic details if you need support.");
    }
    public static void Apply(string code)
    {
        Code=Languages.Any(l=>l.Code==code)?code:"en";
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(Code);
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.CurrentUICulture;
        Current.PropertyChanged?.Invoke(Current,new(null));
    }
    sealed class Translation(string text,bool error=false):IValueConverter
    {
        public object Convert(object value,Type target,object parameter,CultureInfo culture)=>error?ErrorMessage(text):T(text);
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
    public static void Bind(DependencyObject target,DependencyProperty property,string text)=>BindingOperations.SetBinding(target,property,new Binding(nameof(Direction)){Mode=BindingMode.OneWay,Source=Current,Converter=new Translation(text)});
    public static void BindError(DependencyObject target,DependencyProperty property,string text)=>BindingOperations.SetBinding(target,property,new Binding(nameof(Direction)){Mode=BindingMode.OneWay,Source=Current,Converter=new Translation(text,true)});
    public static void Window(Window window)=>window.SetBinding(FrameworkElement.FlowDirectionProperty,new Binding(nameof(Direction)){Mode=BindingMode.OneWay,Source=Current});
    public static ComboBox LanguagePicker(Settings settings)
    {
        var box=new ComboBox{ItemsSource=Languages,DisplayMemberPath="Name",SelectedItem=Languages.FirstOrDefault(l=>l.Code==settings.Language)??Languages[0],MinWidth=180,FlowDirection=FlowDirection.LeftToRight};
        System.Windows.Automation.AutomationProperties.SetName(box,"Language");
        box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is Language selected){settings.Language=selected.Code;settings.Save();Apply(selected.Code);}};return box;
    }
    public sealed class DisplayConverter:IMultiValueConverter
    {
        public object Convert(object[] values,Type target,object parameter,CultureInfo culture)=>T(values.FirstOrDefault()?.ToString()??"");
        public object[] ConvertBack(object value,Type[] targets,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    public static MultiBinding Display(string path)
    {
        var binding=new MultiBinding{Mode=BindingMode.OneWay,Converter=new DisplayConverter()};binding.Bindings.Add(new Binding(path));binding.Bindings.Add(new Binding(nameof(Direction)){Mode=BindingMode.OneWay,Source=Current});return binding;
    }
    public static void Choices(ComboBox box)
    {
        var binding=new MultiBinding{Mode=BindingMode.OneWay,Converter=new DisplayConverter()};binding.Bindings.Add(new Binding());binding.Bindings.Add(new Binding(nameof(Direction)){Mode=BindingMode.OneWay,Source=Current});
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetBinding(TextBlock.TextProperty,binding);box.ItemTemplate=new DataTemplate{VisualTree=text};
    }
}

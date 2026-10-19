using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace Fellowship;
public static class Appearance
{
    sealed class Swatch:System.ComponentModel.INotifyPropertyChanged
    {
        Color color;
        public Color Color{get=>color;set{color=value;PropertyChanged?.Invoke(this,new(nameof(Color)));}}
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    static readonly Dictionary<string,(SolidColorBrush Brush,Swatch Source)> brushes=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string> light=new(StringComparer.OrdinalIgnoreCase)
    {
        ["#4B6599"]="#BFCEEB",
        ["#193C31"]="#ECF8F2",
        ["#32664F"]="#B8DECD",
        ["#101216"]="#F5F7FB",
        ["#10251D"]="#FFFFFF",
        ["#111419"]="#F5F7FB",
        ["#15181E"]="#FFFFFF",
        ["#151A21"]="#FFFFFF",
        ["#171A20"]="#FFFFFF",
        ["#171C24"]="#FFFFFF",
        ["#181C22"]="#FFFFFF",
        ["#191D24"]="#FFFFFF",
        ["#192333"]="#FFFFFF",
        ["#1B1F26"]="#FFFFFF",
        ["#1B222D"]="#FFFFFF",
        ["#1C2330"]="#FFFFFF",
        ["#1D222A"]="#FFFFFF",
        ["#1D352D"]="#DDF3E8",
        ["#1E232C"]="#FFFFFF",
        ["#20242B"]="#FFFFFF",
        ["#20242C"]="#FFFFFF",
        ["#202630"]="#E8EDF5",
        ["#202B3B"]="#E8EDF5",
        ["#232C39"]="#E8EDF5",
        ["#242D3E"]="#E8EDF5",
        ["#24324E"]="#DFE9FF",
        ["#252930"]="#E8EDF5",
        ["#252A33"]="#E8EDF5",
        ["#25483B"]="#DDF3E8",
        ["#263042"]="#E8EDF5",
        ["#263247"]="#E8EDF5",
        ["#26344D"]="#DFE9FF",
        ["#263657"]="#DFE9FF",
        ["#272F3C"]="#E8EDF5",
        ["#273750"]="#DFE9FF",
        ["#283142"]="#E8EDF5",
        ["#293347"]="#E8EDF5",
        ["#293448"]="#E8EDF5",
        ["#29354E"]="#DFE9FF",
        ["#293853"]="#E8EFFF",
        ["#293A58"]="#DFE9FF",
        ["#2C313B"]="#D7DFEA",
        ["#2C3854"]="#BAC6D8",
        ["#2D3B53"]="#E8EFFF",
        ["#303640"]="#E8EDF5",
        ["#303945"]="#E8EDF5",
        ["#303947"]="#E8EDF5",
        ["#343942"]="#C3CDDC",
        ["#343E4D"]="#BAC6D8",
        ["#343F50"]="#BAC6D8",
        ["#343F51"]="#BAC6D8",
        ["#34445F"]="#E8EFFF",
        ["#344969"]="#BAC6D8",
        ["#363B45"]="#BAC6D8",
        ["#363C48"]="#BAC6D8",
        ["#3A4659"]="#BAC6D8",
        ["#3B4658"]="#BAC6D8",
        ["#3B485D"]="#BAC6D8",
        ["#43516A"]="#BAC6D8",
        ["#445063"]="#BAC6D8",
        ["#456BDD"]="#456BDD",
        ["#49556B"]="#BAC6D8",
        ["#537BEF"]="#537BEF",
        ["#5B6F91"]="#536780",
        ["#5B719F"]="#536780",
        ["#6287F1"]="#6287F1",
        ["#637590"]="#536780",
        ["#6789F5"]="#6789F5",
        ["#688FFF"]="#688FFF",
        ["#698DFC"]="#698DFC",
        ["#7184A4"]="#536780",
        ["#7395FF"]="#3158B0",
        ["#74D6AB"]="#127754",
        ["#81A6FF"]="#3158B0",
        ["#83A5FF"]="#3158B0",
        ["#8BA9DC"]="#465974",
        ["#8DEAC2"]="#127754",
        ["#8E9BB1"]="#536780",
        ["#919AA9"]="#536780",
        ["#929BAA"]="#536780",
        ["#9EB8F6"]="#3158B0",
        ["#A7B6CE"]="#465974",
        ["#A9C1FF"]="#3158B0",
        ["#AABCE1"]="#465974",
        ["#AD3545"]="#AD3545",
        ["#B1C9FF"]="#3158B0",
        ["#B50D1B"]="#B50D1B",
        ["#B5A98D"]="#83550E",
        ["#B5C0D3"]="#465974",
        ["#B9CCFF"]="#3158B0",
        ["#BAC5D6"]="#465974",
        ["#BFD1FF"]="#3158B0",
        ["#D4DEEC"]="#182438",
        ["#D7E2F5"]="#182438",
        ["#DADEE7"]="#182438",
        ["#DCE2ED"]="#182438",
        ["#DCE4F1"]="#182438",
        ["#DEE3ED"]="#182438",
        ["#DFE6F2"]="#182438",
        ["#E4E9F2"]="#182438",
        ["#E5B77A"]="#83550E",
        ["#E5BC70"]="#83550E",
        ["#E5E8EE"]="#182438",
        ["#E5EBFF"]="#182438",
        ["#E6EAF2"]="#182438",
        ["#E7EBF3"]="#182438",
        ["#E81123"]="#E81123",
        ["#E8A6A6"]="#AC2F43",
        ["#E8EBF0"]="#182438",
        ["#E8EBF2"]="#182438",
        ["#E8EDF7"]="#182438",
        ["#EDF1F8"]="#182438",
        ["#EE8690"]="#AC2F43",
        ["#EFF1F5"]="#182438",
        ["#F0F2F7"]="#182438",
        ["#F1F3F8"]="#182438",
        ["#F2F4F9"]="#182438",
        ["#F3F4F8"]="#182438",
        ["#FFFFFF"]="#182438",
    };
    static string mode="Dark";
    static bool isLight;
    static bool watching;
    public static Brush Brush(string hex)
    {
        if(!light.ContainsKey(hex))return (Brush)new BrushConverter().ConvertFromString(hex)!;
        if(!brushes.TryGetValue(hex,out var entry))
        {
            var source=new Swatch{Color=(Color)ColorConverter.ConvertFromString(isLight?light[hex]:hex)};
            var brush=new SolidColorBrush();
            System.Windows.Data.BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new System.Windows.Data.Binding(nameof(Swatch.Color)){Source=source});
            entry=(brush,source);brushes[hex]=entry;
        }
        return entry.Brush;
    }
    public static void Apply(string value)
    {
        mode=value is "Light" or "System"?value:"Dark";
        bool systemLight=true;
        try{systemLight=Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))!=0;}catch{}
        isLight=mode=="Light"||mode=="System"&&systemLight;
        foreach(var pair in light)
        {
            var brush=(SolidColorBrush)Brush(pair.Key);
            brushes[pair.Key].Source.Color=(Color)ColorConverter.ConvertFromString(isLight?pair.Value:pair.Key);
            if(Application.Current!=null)Application.Current.Resources["Theme"+pair.Key[1..]]=brush;
        }
        if(!watching&&Application.Current!=null)
        {
            watching=true;
            SystemEvents.UserPreferenceChanged+=(_,_)=>{if(mode=="System")Application.Current?.Dispatcher.BeginInvoke(()=>Apply(mode));};
        }
    }
}

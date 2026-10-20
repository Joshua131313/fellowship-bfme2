using System.Windows;
using System.Windows.Controls;
namespace Fellowship;
public sealed class OnboardingWindow : Window
{
    public OnboardingWindow(Settings settings)
    {
        var body=UI.Shell(this,"Welcome to Fellowship",820,740);MinWidth=620;MinHeight=600;
        UI.Rows(body,UI.Star,UI.Auto);
        var page=new Grid();UI.At(body,page,0);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(24)};UI.At(body,footer,1);
        int step=0;
        // Choices already autosave. Finishing or dismissing this first-run window
        // completes onboarding; it must not reappear on the next launch.
        Closing+=(_,_)=>{settings.OnboardingCompleted=true;settings.Save();};
        void Finish(){Close();}
        var back=UI.Button("Back",()=>{});back.Margin=new(0,0,10,0);
        var next=UI.Button("Continue",()=>{},true);
        back.Click+=(_,_)=>{step=0;Render();};next.Click+=(_,_)=>{if(step==0){step=1;Render();}else Finish();};
        footer.Children.Add(back);footer.Children.Add(next);
        void Render()
        {
            page.Children.Clear();back.Visibility=step==0?Visibility.Collapsed:Visibility.Visible;L.Bind(next,ContentControl.ContentProperty,step==0?"Continue":"Get started");
            if(step==1){page.Children.Add(GamePreferencesWindow.CreateContent(settings,()=>{}));return;}
            var panel=new StackPanel{Margin=new(28)};page.Children.Add(UI.Scroll(panel));
            panel.Children.Add(UI.LocalizedText("Choose your defaults",24,bold:true));
            var hint=UI.LocalizedText("You can change these preferences later in Settings.",12,UI.Muted);hint.Margin=new(0,8,0,22);panel.Children.Add(hint);
            void Field(string label,FrameworkElement control){var title=UI.LocalizedText(label,12,bold:true);title.Margin=new(0,12,0,8);panel.Children.Add(title);panel.Children.Add(control);}
            Field("Language",L.LanguagePicker(settings));
            var theme=new ComboBox{ItemsSource=new[]{"Dark","Black","Light","System"},SelectedItem=settings.Appearance};L.Choices(theme);
            theme.SelectionChanged+=(_,_)=>{if(theme.SelectedItem is string value){settings.Appearance=value;settings.Save();Appearance.Apply(value);}};Field("Appearance",theme);
            var sounds=new CheckBox{IsChecked=settings.SoundsEnabled,Margin=new(0,22,0,10)};L.Bind(sounds,ContentControl.ContentProperty,"Enable app sounds");panel.Children.Add(sounds);
            var volume=new Slider{Minimum=0,Maximum=100,Value=settings.SoundVolume,TickFrequency=1,IsSnapToTickEnabled=true,Height=30};Field("Volume",volume);
            sounds.Checked+=(_,_)=>SaveSound();sounds.Unchecked+=(_,_)=>SaveSound();
            volume.ValueChanged+=(_,_)=>SaveSound();
            void SaveSound(){settings.SoundsEnabled=sounds.IsChecked==true;settings.SoundVolume=(int)volume.Value;settings.Save();MessageAlert.Configure(settings);}
        }
        Render();
    }
}

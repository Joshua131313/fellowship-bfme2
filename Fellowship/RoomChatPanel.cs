using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace Fellowship;

// A floating roster shares the transcript's height, rather than reserving a
// separate panel above it. Its column keeps readable text clear of the card;
// the composer spans both columns and never sits underneath the roster.
public sealed class RoomChatPanel : Grid
{
    readonly Grid conversation=new();
    readonly ColumnDefinition rosterColumn=new(){Width=GridLength.Auto};
    readonly TextBlock arrow=UI.Text("⌃",12,UI.Muted);
    readonly Button collapse;
    readonly ChatTranscript transcript;
    bool expanded=true;
    public Border PlayersCard {get;}
    public Button PlayersButton {get;}
    public bool PlayersExpanded
    {
        get=>expanded;
        set
        {
            if(expanded==value)return;
            expanded=value;UpdateExpanded();
        }
    }
    public RoomChatPanel(ChatTranscript transcript,ListBox players,TextBlock playerCount,TextBlock chatHeading,FrameworkElement composer)
    {
        this.transcript=transcript;
        UI.Rows(this,GridLength.Auto,UI.Star,GridLength.Auto);
        var heading=new Grid{Margin=new(14,8,12,6)};
        heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        chatHeading.Margin=new(0,0,8,0);heading.Children.Add(chatHeading);
        var count=UI.Text("",10,UI.Muted,true);count.SetBinding(TextBlock.TextProperty,new Binding(nameof(TextBlock.Text)){Source=playerCount});
        count.TextWrapping=TextWrapping.NoWrap;
        arrow.Margin=new(8,0,0,0);
        PlayersButton=UI.Button("",TogglePlayers);PlayersButton.Content=UI.Row(count,arrow);PlayersButton.Height=28;PlayersButton.MinHeight=28;PlayersButton.Padding=new(9,3,9,3);PlayersButton.Margin=new(0);PlayersButton.Background=Brushes.Transparent;
        Grid.SetColumn(PlayersButton,1);heading.Children.Add(PlayersButton);UI.At(this,heading,0);
        conversation.ColumnDefinitions.Add(new());conversation.ColumnDefinitions.Add(rosterColumn);
        conversation.Children.Add(transcript);

        var roster=new Grid();UI.Rows(roster,GridLength.Auto,UI.Star);
        var rosterHeading=new Grid{Margin=new(10,5,6,4)};
        rosterHeading.ColumnDefinitions.Add(new());rosterHeading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        playerCount.Margin=new(0,0,6,0);playerCount.TextWrapping=TextWrapping.NoWrap;rosterHeading.Children.Add(playerCount);
        collapse=UI.Button("−",()=>{PlayersExpanded=false;PlayersButton.Focus();});collapse.Height=24;collapse.MinHeight=24;collapse.Width=24;collapse.FontSize=16;collapse.Padding=new(0);collapse.Margin=new(0);collapse.Background=Brushes.Transparent;collapse.BorderThickness=new(0);
        L.Bind(collapse,FrameworkElement.ToolTipProperty,"Hide players");L.Bind(collapse,AutomationProperties.NameProperty,"Hide players");
        Grid.SetColumn(collapse,1);rosterHeading.Children.Add(collapse);UI.At(roster,rosterHeading,0);
        players.Margin=new(0,0,0,4);ScrollViewer.SetVerticalScrollBarVisibility(players,ScrollBarVisibility.Auto);UI.At(roster,players,1);
        PlayersCard=UI.Card(roster,"#191D24",0);PlayersCard.Width=246;PlayersCard.MaxHeight=280;PlayersCard.VerticalAlignment=VerticalAlignment.Top;PlayersCard.Margin=new(8,0,12,12);
        PlayersCard.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=12,ShadowDepth=3,Opacity=.16};
        Grid.SetColumn(PlayersCard,1);Panel.SetZIndex(PlayersCard,2);conversation.Children.Add(PlayersCard);UI.At(this,conversation,1);
        UI.At(this,composer,2);
        conversation.SizeChanged+=(_,_)=>UpdateBounds();players.SizeChanged+=(_,_)=>UpdateBounds();Loaded+=(_,_)=>UpdateBounds();
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key!=Key.Escape||!PlayersExpanded||players.ContextMenu?.IsOpen==true)return;
            // Give an open per-player context menu first chance to handle Esc.
            if(players.Items.Cast<object>().OfType<ListBoxItem>().Any(item=>item.ContextMenu?.IsOpen==true))return;
            PlayersExpanded=false;PlayersButton.Focus();e.Handled=true;
        };
        UpdateExpanded();
    }
    public void TogglePlayers()=>PlayersExpanded=!PlayersExpanded;
    void UpdateExpanded()
    {
        PlayersCard.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        arrow.Text=expanded?"⌃":"⌄";
        L.Bind(PlayersButton,FrameworkElement.ToolTipProperty,expanded?"Hide players":"Show players");
        L.Bind(PlayersButton,AutomationProperties.NameProperty,expanded?"Hide players":"Show players");
        UpdateBounds();
        // Changing the roster's visibility never replaces the document or its
        // selection. ChatTranscript retains its existing follow-latest policy.
    }
    void UpdateBounds()
    {
        double width=conversation.ActualWidth,height=conversation.ActualHeight;
        if(width>0)
        {
            double next=Math.Min(246,Math.Max(156,width*.46));
            if(Math.Abs(PlayersCard.Width-next)>.5)PlayersCard.Width=next;
        }
        if(height>0)
        {
            double maximum=Math.Max(0,height-PlayersCard.Margin.Top-PlayersCard.Margin.Bottom);
            if(Math.Abs(PlayersCard.MaxHeight-maximum)>.5)PlayersCard.MaxHeight=maximum;
        }
    }
}

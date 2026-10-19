using System.Windows;
using System.Windows.Controls;
namespace Fellowship;

public sealed class MatchSummary : Grid
{
    public void Show(Room room,bool includeMap=true)
    {
        Children.Clear();RowDefinitions.Clear();ColumnDefinitions.Clear();
        ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        void Detail(string label,string value)
        {
            int row=RowDefinitions.Count;RowDefinitions.Add(new(){Height=GridLength.Auto});
            var key=UI.Text(label,11,UI.Muted);key.Margin=new(0,5,14,5);
            var text=UI.Text(value,12,bold:true);text.Margin=new(0,5,0,5);text.HorizontalAlignment=HorizontalAlignment.Right;text.TextAlignment=TextAlignment.Right;
            SetRow(key,row);SetRow(text,row);SetColumn(text,1);Children.Add(key);Children.Add(text);
        }
        Detail("Mode",room.Mode);
        if(includeMap){Detail("Map",room.Map);Detail("Room limit",$"{room.Capacity} players");}
        Detail("Resources",room.Match.Resources.ToString("N0"));
        Detail("Command points",$"{room.Match.CommandPoints/100.0:0.##}×");
        Detail("Custom Heroes",room.Match.CustomHeroes?"On":"Off");
        Detail("Ring Heroes",room.Match.RingHeroes?"On":"Off");
        if(includeMap)Detail("Visibility",room.FriendsOnly?"Friends only":"Public on VPN");
    }
}

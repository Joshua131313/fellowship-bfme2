namespace Fellowship;

public sealed record PlayerColor(int Value,string Name,string Hex)
{
    public override string ToString()=>Name;
}

public static class PlayerColors
{
    // BFME2 1.00 INI.big / data/ini/multiplayer.ini, RGBColor in declaration order.
    // Fellowship 0 = Random; native color IDs are -1 (Random) and 0..9.
    public static readonly PlayerColor[] All = [
        new(0,"Random","#9AA5B5"),new(1,"Blue","#465B9C"),new(2,"Red","#9E382A"),
        new(3,"Gold","#AFBD4C"),new(4,"Green","#3E9864"),new(5,"Orange","#CE8745"),
        new(6,"Sky Blue","#7AA8CC"),new(7,"Purple","#9474B7"),new(8,"Pink","#CC9FBC"),
        new(9,"Gray","#646464"),new(10,"White","#FFFFFF")];
    public static PlayerColor Get(int value)=>All.FirstOrDefault(c=>c.Value==value)??All[0];
}

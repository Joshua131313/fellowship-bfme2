using Fellowship;

static class PatchPresetTests
{
    const string LebenninSix=@"maps\map et lebennin iv\map et lebennin iv.map";
    const string LebenninFour=@"maps\map et lebennin\map et lebennin.map";
    const string FordsTwo=@"maps\map mp fords of isen ii\map mp fords of isen ii.map";
    const string HillsEight=@"maps\map mp tournament hills\map mp tournament hills.map";
    public static void Run()
    {
        // Identical display names and similarly named variants must not select
        // the wrong player-count map. The known archive paths disambiguate them.
        GameMap[] maps=
        [
            GameMap.Rhun,new("Buckland",@"maps\map mp buckland\map mp buckland.map"),
            new("Rohan",@"maps\map wor rohan\map wor rohan.map"),new("Gondor",@"maps\map wor gondor\map wor gondor.map"),
            new("Lebennin",LebenninFour),new("Lebennin",LebenninSix),
            new("Fords of Isen II",@"maps\map mp fords of isen iv\map mp fords of isen iv.map"),
            new("Fords of Isen II",FordsTwo),new("Tournament Hills",HillsEight)
        ];
        var stock=RoomPreset.Defaults(maps,"1.00");
        if(stock.Count!=5||stock.Any(p=>p.Name=="4v4")||stock.Single(p=>p.Name=="3v3").MapPath!=GameMap.Rhun.Path||
           stock.Single(p=>p.Name=="2v2").Map!="Buckland"||stock.Where(p=>p.Mode=="1v1").Any(p=>p.Map!="Rohan"))throw new Exception("1.00 preset defaults changed");
        foreach(var patch in new[]{"1.09v3","1.09v3.1","1.09v3.2 ß16","1.09u4 ß17"})
        {
            var presets=RoomPreset.Defaults(maps,patch);
            Check(presets.Single(p=>p.Name=="3v3"),LebenninSix,6,[1,1,1,2,2,2]);
            Check(presets.Single(p=>p.Name=="2v2"),LebenninFour,4,[1,1,2,2]);
            var singles=presets.Where(p=>p.Mode=="1v1").ToArray();
            if(singles.Length!=2||!singles.Select(p=>p.Resources).Order().SequenceEqual(new[]{1000,4000}))throw new Exception("1v1 resource presets were lost");
            foreach(var preset in singles)Check(preset,FordsTwo,2,[1,2]);
            if(presets.Any(p=>p.Patch!=GamePatches.Normalize(patch)))throw new Exception("Patch-specific map preset has the wrong patch");
        }
        foreach(var patch in new[]{"1.06","1.09","1.09v2","v2 ß21.12","1.09v3","1.09v3.1","1.09v3.2 ß16","1.09u4 ß17"})
            Check(RoomPreset.Defaults(maps,patch).Single(p=>p.Name=="4v4"),HillsEight,8,[1,1,1,1,2,2,2,2]);
        foreach(var patch in new[]{"1.06","1.09","1.09v3","1.09v3.1","1.09u4 ß17"})
            if(RoomPreset.Defaults([],patch).Single(p=>p.Name=="4v4").MapPath.Length!=0)throw new Exception("Missing 4v4 map was advertised as installed");
        if(RoomPreset.Defaults(maps.Where(m=>m.Path!=LebenninSix),"1.09v3").Single(p=>p.Name=="3v3").MapPath.Length!=0)throw new Exception("Missing six-player Lebennin silently fell back to its four-player variant");
        if(RoomPreset.Defaults(maps.Where(m=>m.Path!=FordsTwo),"1.09v3").Where(p=>p.Mode=="1v1").Any(p=>p.MapPath.Length!=0))throw new Exception("Missing two-player Fords silently fell back to its four-player variant");
        Console.WriteLine("PASS patch-specific Lebennin/Fords map variants, unchanged 1.00 presets, 4v4 team layouts, and unavailable-map disabling");
    }
    static void Check(RoomPreset preset,string path,int capacity,int[] teams)
    {
        if(preset.MapPath!=path||preset.Capacity!=capacity||!preset.Teams.SequenceEqual(teams)||!preset.BuiltIn||preset.CommandPoints!=200||preset.CustomHeroes||preset.RingHeroes)
            throw new Exception("Incorrect "+preset.Patch+" "+preset.Name+" preset map or match settings");
    }
}

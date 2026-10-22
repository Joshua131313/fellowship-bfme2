using Fellowship;

static class PatchPresetTests
{
    const string LebenninSix=@"maps\map et lebennin iv\map et lebennin iv.map";
    const string LebenninFour=@"maps\map et lebennin\map et lebennin.map";
    const string FordsTwo=@"maps\map mp fords of isen ii\map mp fords of isen ii.map";
    const string ClassicFords=@"maps\map mp fords of isen\map mp fords of isen.map";
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
        foreach(var patch in new[]{"1.09","1.09v2","v2 ß21.12","1.09v3","1.09v3.1","1.09v3.2 ß16","1.09u4 ß17"})
        {
            var presets=RoomPreset.Defaults(maps,patch);
            Check(presets.Single(p=>p.Name=="3v3"),LebenninSix,6,[1,1,1,2,2,2]);
            Check(presets.Single(p=>p.Name=="2v2"),LebenninFour,4,[1,1,2,2]);
            var singles=presets.Where(p=>p.Mode=="1v1").ToArray();
            if(singles.Length!=2||!singles.Select(p=>p.Resources).Order().SequenceEqual(new[]{1000,4000}))throw new Exception("1v1 resource presets were lost");
            foreach(var preset in singles)Check(preset,FordsTwo,2,[1,2]);
            if(presets.Any(p=>p.Patch!=GamePatches.Normalize(patch)))throw new Exception("Patch-specific map preset has the wrong patch");
        }
        var classicMaps=maps.Append(new GameMap("Fords Of Isen",ClassicFords)).ToArray();
        foreach(var patch in new[]{"1.09","1.09v2","v2 ß21.12","1.09v3","1.09v3.1","1.09v3.2 ß16","1.09u4 ß17"})
            foreach(var preset in RoomPreset.Defaults(classicMaps,patch).Where(p=>p.Mode=="1v1"))Check(preset,ClassicFords,2,[1,2]);
        foreach(var patch in new[]{"1.00","1.06"})
        {
            var presets=RoomPreset.Defaults(classicMaps,patch);
            if(presets.Single(p=>p.Name=="3v3").MapPath!=GameMap.Rhun.Path||presets.Single(p=>p.Name=="2v2").Map!="Buckland"||presets.Where(p=>p.Mode=="1v1").Any(p=>p.Map!="Rohan"))throw new Exception(patch+" map defaults changed when 1.09 preferred maps were present");
        }
        var noLebennin=maps.Where(map=>map.Path!=LebenninSix&&map.Path!=LebenninFour).ToArray();
        foreach(var patch in new[]{"1.09","1.09v2","v2 ß21.12"})
        {
            var presets=RoomPreset.Defaults(noLebennin,patch);
            Check(presets.Single(p=>p.Name=="3v3"),GameMap.Rhun.Path,6,[1,1,1,2,2,2]);
            if(presets.Single(p=>p.Name=="2v2").Map!="Buckland"||presets.Single(p=>p.Name=="2v2").MapPath.Length==0)throw new Exception("Earlier 1.09 family lost its available stock fallback when Lebennin was absent");
            var unavailable=RoomPreset.Defaults(noLebennin.Where(map=>map.Path!=GameMap.Rhun.Path&&map.Name!="Buckland"),patch);
            if(unavailable.Where(p=>p.Mode is "3v3" or "2v2").Any(p=>p.MapPath.Length!=0))throw new Exception("Missing defaults borrowed maps outside the selected patch catalogue");
        }
        foreach(var patch in new[]{"1.06","1.09","1.09v2","v2 ß21.12","1.09v3","1.09v3.1","1.09v3.2 ß16","1.09u4 ß17"})
            Check(RoomPreset.Defaults(maps,patch).Single(p=>p.Name=="4v4"),HillsEight,8,[1,1,1,1,2,2,2,2]);
        foreach(var patch in new[]{"1.06","1.09","1.09v3","1.09v3.1","1.09u4 ß17"})
            if(RoomPreset.Defaults([],patch).Single(p=>p.Name=="4v4").MapPath.Length!=0)throw new Exception("Missing 4v4 map was advertised as installed");
        if(RoomPreset.Defaults(maps.Where(m=>m.Path!=LebenninSix),"1.09v3").Single(p=>p.Name=="3v3").MapPath.Length!=0)throw new Exception("Missing six-player Lebennin silently fell back to its four-player variant");
        if(RoomPreset.Defaults(maps.Where(m=>m.Path!=FordsTwo),"1.09v3").Where(p=>p.Mode=="1v1").Any(p=>p.MapPath.Length!=0))throw new Exception("Missing two-player Fords silently fell back to its four-player variant");
        Console.WriteLine("PASS all-1.09 Lebennin defaults, exact classic Fords preference/II fallback, unchanged 1.00/1.06 presets, earlier-patch stock fallbacks, 4v4 team layouts and unavailable-map disabling");
    }
    static void Check(RoomPreset preset,string path,int capacity,int[] teams)
    {
        if(preset.MapPath!=path||preset.Capacity!=capacity||!preset.Teams.SequenceEqual(teams)||!preset.BuiltIn||preset.CommandPoints!=200||preset.CustomHeroes||preset.RingHeroes)
            throw new Exception("Incorrect "+preset.Patch+" "+preset.Name+" preset map or match settings");
    }
}

using Fellowship;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

static class PatchRoomTests
{
    public static void Run()
    {
        var legacy=JsonSerializer.Deserialize<RoomPreset>("""{"Name":"Legacy","Map":"Rhûn","MapPath":"maps\\map wor rhun\\map wor rhun.map","Capacity":6,"Resources":4000,"CommandPoints":200,"CustomHeroes":false,"RingHeroes":false,"Teams":[1,1,1,2,2,2]}""")!;
        if(legacy.Patch!="1.00")throw new Exception("Existing saved presets did not default to patch 1.00");
        var patch=GamePatches.Normalize("1.09");
        if(RoomPreset.Defaults([GameMap.Rhun],patch).Any(p=>p.Patch!=patch))throw new Exception("Built-in presets lost their selected patch");
        if(GameInstallation.Maps("missing-game.exe",patch).Count!=0)throw new Exception("Missing patch archives invented a playable map catalog");
        var settings=Settings.InMemory();settings.GamePath=GameInstallation.Find();
        var original=legacy with{Name="Shared preset name"};var alternate=original with{Patch=patch};
        settings.RoomPresets.Add(original);settings.RoomPresets.Add(alternate);
        var picker=new RoomPresetPicker(settings);
        RoomPreset[] Saved()=>picker.Items.OfType<ComboBoxItem>().Select(i=>i.Tag).OfType<RoomPreset>().Where(p=>!p.BuiltIn).ToArray();
        if(Saved().Length!=1||Saved()[0]!=original)throw new Exception("Default picker mixed presets from different patches");
        picker.SelectedItem=picker.Items.OfType<ComboBoxItem>().Single(i=>i.Tag is RoomPreset p&&p==original);
        picker.SetPatch(patch);
        if(Saved().Length!=1||Saved()[0]!=alternate||!picker.IsCustom)throw new Exception("Changing patches silently selected a different saved preset with the same name");
        if(picker.Items.OfType<ComboBoxItem>().Select(i=>i.Tag).OfType<RoomPreset>().Any(p=>GamePatches.Normalize(p.Patch)!=patch))throw new Exception("Picker contains a preset for the wrong patch");
        picker.SetPatch("1.00");
        if(Saved().Single()!=original||settings.RoomPresets.Count!=2)throw new Exception("Changing patch modified stored presets");

        var editor=new RoomEditor(null,settings,new Room{Title="Patch room"});
        var nodes=Nodes(editor).ToArray();
        var patchChoice=nodes.OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="PATCH");
        var mapChoice=nodes.OfType<SearchableMapBox>().Single();
        var roomPresets=nodes.OfType<RoomPresetPicker>().Single();
        if(patchChoice.SelectedItem?.ToString()!=GamePatches.NewRoomPatch(settings.GamePath))throw new Exception("New room did not select the detected supported patch");
        roomPresets.SelectedIndex=0;
        var startingTeams=nodes.OfType<ComboBox>().Single(c=>c.Items.Cast<object>().Any(item=>item.ToString()=="Manual teams"));
        startingTeams.SelectedItem=startingTeams.Items.Cast<object>().Single(item=>item.ToString()=="Manual teams");
        var available=patchChoice.Items.OfType<string>().FirstOrDefault(p=>GamePatches.Normalize(p)==patch);
        if(available!=null)
        {
            string oldMap=(mapChoice.SelectedItem as GameMap)?.Path??"";
            patchChoice.SelectedItem=available;
            var expected=GameInstallation.Maps(settings.GamePath,patch);
            if(!mapChoice.Items.Cast<GameMap>().Select(m=>m.Path).SequenceEqual(expected.Select(m=>m.Path)))throw new Exception("Patch selector retained the previous map catalog");
            if(expected.Any(m=>m.Path.Equals(oldMap,StringComparison.OrdinalIgnoreCase))&&(mapChoice.SelectedItem as GameMap)?.Path!=oldMap)throw new Exception("Manual-team patch selection discarded a map available on both patches");
            if(roomPresets.Items.OfType<ComboBoxItem>().Select(i=>i.Tag).OfType<RoomPreset>().Any(p=>GamePatches.Normalize(p.Patch)!=patch))throw new Exception("Room editor retained presets for the previous patch");
            patchChoice.SelectedItem="1.00";
            if(!mapChoice.Items.Cast<GameMap>().Select(m=>m.Path).SequenceEqual(GameInstallation.Maps(settings.GamePath,"1.00").Select(m=>m.Path)))throw new Exception("Switching back retained the alternate patch map catalog");
            var v3Maps=GameInstallation.Maps(settings.GamePath,"1.09v3");
            var v3Preset=RoomPreset.Defaults(v3Maps,"1.09v3").Single(p=>p.Name=="3v3");
            if(v3Preset.MapPath.Length>0)
            {
                roomPresets.SelectedItem=roomPresets.Items.OfType<ComboBoxItem>().Single(i=>i.Tag is RoomPreset{Name:"3v3",BuiltIn:true});
                patchChoice.SelectedItem="1.09v3";
                if((mapChoice.SelectedItem as GameMap)?.Path!=v3Preset.MapPath||roomPresets.IsCustom)throw new Exception("Changing patch did not apply its built-in 3v3 map default");
            }
        }
        editor.Close();
        CheckCustomPatchDefaults();
        CheckDetectedDefaults();
        Console.WriteLine("PASS patch-scoped presets, legacy preset migration, default room patch, and map/preset refresh when changing room patch");
    }
    static void CheckCustomPatchDefaults()
    {
        string fixture=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-custom-patch-map-"+Guid.NewGuid().ToString("N"))),language=L.Code;
        Directory.CreateDirectory(fixture);L.Apply("en");
        try
        {
            string executable=Path.Combine(fixture,"lotrbfme2.exe"),switcher=Path.Combine(fixture,"BFME2 Ecth's Patch Switcher");File.WriteAllBytes(executable,[]);
            foreach(string source in new[]{"100","106","109","109v2",Path.Combine("109v3","release")})Directory.CreateDirectory(Path.Combine(switcher,source));
            WriteMapArchive(Path.Combine(switcher,"100","Maps.big"));WriteMapArchive(Path.Combine(switcher,"106","Maps.big"));
            (string Name,int Resources)[] variants=[("3v3",2500),("2v2",1400),("1v1",1000),("1v1",4000),("4v4",1200),("Manual teams",3000),("FFA",2000)];
            foreach(var variant in variants)
            {
                var settings=Settings.InMemory();settings.GamePath=executable;var editor=new RoomEditor(null,settings,new Room{Title="Custom patch map"});
                try
                {
                    var nodes=Nodes(editor).ToArray();
                    var patch=nodes.OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="PATCH");
                    var map=nodes.OfType<SearchableMapBox>().Single();var presets=nodes.OfType<RoomPresetPicker>().Single();
                    var teams=nodes.OfType<ComboBox>().Single(c=>c.Items.Cast<object>().Any(item=>item.ToString()=="Manual teams"));
                    var resources=nodes.OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="STARTING RESOURCES");
                    var command=nodes.OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="COMMAND POINTS");
                    var capacity=nodes.OfType<TextBox>().Single(t=>t.MaxLength==2);
                    var custom=nodes.OfType<CheckBox>().Single(c=>c.Content as string=="Custom Heroes");var ring=nodes.OfType<CheckBox>().Single(c=>c.Content as string=="Ring Heroes");
                    object Layout(string name)=>teams.Items.Cast<object>().Single(item=>item.ToString()==name);
                    presets.SelectedIndex=0;teams.SelectedItem=Layout(variant.Name);patch.SelectedItem="1.09v2";
                    resources.SelectedItem=variant.Resources;command.SelectedItem=command.Items.Cast<object>().Single(item=>item.ToString()=="4×");var selectedCommand=command.SelectedItem;
                    capacity.Text="13";custom.IsChecked=true;ring.IsChecked=true;
                    var manual=map.Items.Cast<GameMap>().Single(m=>m.Path==GameMap.Rhun.Path);map.SelectedItem=manual;
                    void RulesUnchanged()
                    {
                        if(!presets.IsCustom||!map.IsEnabled||(int)resources.SelectedItem!=variant.Resources||!ReferenceEquals(command.SelectedItem,selectedCommand)||capacity.Text!="13"||custom.IsChecked!=true||ring.IsChecked!=true||teams.SelectedItem?.ToString()!=variant.Name)throw new Exception("Custom patch change replaced rules, room capacity, team layout or editability for "+variant.Name+" / "+variant.Resources);
                    }
                    void ExpectedMap(string target)
                    {
                        var defaults=RoomPreset.Defaults(GameInstallation.Maps(executable,target),target);
                        string expected=variant.Name is "Manual teams" or "FFA"?manual.Path:defaults.First(p=>p.Mode==variant.Name).MapPath;
                        if(expected.Length==0||(map.SelectedItem as GameMap)?.Path!=expected)throw new Exception("Custom "+variant.Name+" did not choose the "+target+" map default while the old map was still installed");
                        RulesUnchanged();
                    }
                    // Both versions contain the manually chosen Rhûn. The new
                    // version must nevertheless apply its own format default.
                    patch.SelectedItem="1.09v3";ExpectedMap("1.09v3");
                    manual=map.Items.Cast<GameMap>().Single(m=>m.Path==GameMap.Rhun.Path);map.SelectedItem=manual;
                    patch.SelectedItem="1.09v3";
                    patch.RaiseEvent(new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
                    if((map.SelectedItem as GameMap)?.Path!=manual.Path)throw new Exception("Selecting the same patch reset a deliberate Custom map");RulesUnchanged();
                    teams.SelectedItem=Layout(variant.Name=="3v3"?"2v2":"3v3");
                    if((map.SelectedItem as GameMap)?.Path!=manual.Path)throw new Exception("Changing only Starting Teams overwrote a manual Custom map");teams.SelectedItem=Layout(variant.Name);
                    patch.SelectedItem="1.06";ExpectedMap("1.06");
                    manual=map.Items.Cast<GameMap>().Single(m=>m.Path==GameMap.Rhun.Path);map.SelectedItem=manual;
                    if((map.SelectedItem as GameMap)?.Path!=manual.Path)throw new Exception("A post-switch Custom map choice was replaced");RulesUnchanged();
                }
                finally{editor.Close();}
            }
            Console.WriteLine("PASS Custom version-change map defaults for 3v3/2v2/1v1 at1000/4000/4v4, same-path 1.09 transitions, unchanged custom rules, manual/FFA retention and post-switch map edits");
        }
        finally
        {
            L.Apply(language);string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fixture)&&fixture.StartsWith(temp,StringComparison.OrdinalIgnoreCase))Directory.Delete(fixture,true);
        }
    }
    static void WriteMapArchive(string path)
    {
        (string Path,int Players)[] maps=[(GameMap.Rhun.Path,6),(@"maps\map mp buckland\map mp buckland.map",4),(@"maps\map wor rohan\map wor rohan.map",2),(@"maps\map et lebennin iv\map et lebennin iv.map",6),(@"maps\map et lebennin\map et lebennin.map",4),(@"maps\map mp fords of isen ii\map mp fords of isen ii.map",2),(@"maps\map mp tournament hills\map mp tournament hills.map",8),(@"maps\map wor gondor\map wor gondor.map",8)];
        string Key(string value)=>string.Concat(value.Select(c=>char.IsAsciiLetterOrDigit(c)?c.ToString():"_"+((int)c).ToString("X2")));
        string cache=string.Concat(maps.Select(m=>$"MapCache {Key(m.Path)}\n isMultiplayer = yes\n numPlayers = {m.Players}\nEND\n"));
        var entries=new[]{(Name:@"maps\mapcache.ini",Data:Encoding.ASCII.GetBytes(cache))}.Concat(maps.Select(m=>(Name:m.Path,Data:Encoding.ASCII.GetBytes("fixture-map")))).ToArray();
        int header=16+entries.Sum(e=>8+Encoding.ASCII.GetByteCount(e.Name)+1),offset=header;
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream,Encoding.ASCII);
        void Be(int value){Span<byte> bytes=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(bytes,value);writer.Write(bytes);}
        writer.Write(Encoding.ASCII.GetBytes("BIGF"));writer.Write(header+entries.Sum(e=>e.Data.Length));Be(entries.Length);Be(header);
        foreach(var entry in entries){Be(offset);Be(entry.Data.Length);writer.Write(Encoding.ASCII.GetBytes(entry.Name));writer.Write((byte)0);offset+=entry.Data.Length;}
        foreach(var entry in entries)writer.Write(entry.Data);
    }
    static void CheckDetectedDefaults()
    {
        var fixture=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-room-patch-default-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            var unknown=Settings.InMemory();unknown.GamePath=Path.Combine(fixture,"missing.exe");
            var unknownEditor=new RoomEditor(null,unknown,new Room());
            if(Nodes(unknownEditor).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="PATCH").SelectedItem?.ToString()!="1.00")throw new Exception("Unverified installation did not fall back to 1.00");
            unknownEditor.Close();
            foreach(string patch in new[]{"1.06","1.09v3.2 ß13"})
            {
                var game=Path.Combine(fixture,patch=="1.06"?"stable":"older-beta");Directory.CreateDirectory(game);
                var switcher=Path.Combine(game,"BFME2 Ecth's Patch Switcher");
                var stock=Path.Combine(switcher,"100");var selected=Path.Combine(switcher,patch=="1.06"?"106":"older-beta");Directory.CreateDirectory(stock);Directory.CreateDirectory(selected);
                File.WriteAllText(Path.Combine(stock,"Maps.big"),"BIGFroom-default-base");File.WriteAllText(Path.Combine(stock,"game.dat"),"base-game");
                File.WriteAllText(Path.Combine(selected,"game.dat"),"selected-game");
                if(patch=="1.06")File.WriteAllText(Path.Combine(selected,"Maps.big"),"BIGFroom-default106");
                else
                {
                    string manifest=Path.Combine(switcher,"data","GUI","web_cached_data","releases_new.json");Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
                    File.WriteAllText(manifest,JsonSerializer.Serialize(new{patches_2026=new{room_fixture=new[]{new{name=patch,source="older-beta",dependency="1.00",tupla=new[]{1.09,4d,13d}}}}}));
                    File.WriteAllText(Path.Combine(selected,"#BT2DC-room-fixture.big"),"BIGFroom-default-oldbeta");
                    File.Copy(Path.Combine(selected,"#BT2DC-room-fixture.big"),Path.Combine(game,"#BT2DC-room-fixture.big"));
                }
                File.Copy(Path.Combine(selected,"game.dat"),Path.Combine(game,"game.dat"));
                File.Copy(Path.Combine(patch=="1.06"?selected:stock,"Maps.big"),Path.Combine(game,"Maps.big"));
                var testSettings=Settings.InMemory();testSettings.GamePath=Path.Combine(game,"lotrbfme2.exe");File.WriteAllBytes(testSettings.GamePath,[]);
                if(GamePatches.Active(testSettings.GamePath)!=patch)throw new Exception("Synthetic installed patch was not detected: "+patch);
                var detectedEditor=new RoomEditor(null,testSettings,new Room());
                var choice=Nodes(detectedEditor).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="PATCH");
                if(choice.SelectedItem?.ToString()!=patch)throw new Exception("Room did not preselect its verified installed patch: "+patch);
                if(patch!="1.06")
                {
                    if(GamePatches.Labels(testSettings.GamePath).Contains(patch))throw new Exception("Detected older beta leaked into the normal menu");
                    choice.ApplyTemplate();
                    var popup=(System.Windows.Controls.Primitives.Popup)choice.Template.FindName("PART_Popup",choice);
                    popup.Child.Measure(new Size(320,600));popup.Child.Arrange(new Rect(0,0,320,600));popup.Child.UpdateLayout();
                    var current=(ComboBoxItem)choice.ItemContainerGenerator.ContainerFromItem(patch);
                    if(current==null||current.Visibility!=Visibility.Collapsed||current.IsEnabled||choice.IsTextSearchEnabled)throw new Exception("Older beta was not hidden from normal dropdown/keyboard choices");
                    if(!GamePatches.IsKnown(patch))throw new Exception("Hidden detected beta lost room compatibility");
                }
                detectedEditor.Close();
            }
            Console.WriteLine("PASS detected stable/older-beta room defaults, hidden legacy beta choice, retained beta compatibility and unverified 1.00 fallback");
        }
        finally
        {
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fixture)&&fixture.StartsWith(temp,StringComparison.OrdinalIgnoreCase))Directory.Delete(fixture,true);
        }
    }
    static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Nodes(child))yield return node;
    }
}

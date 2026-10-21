using Fellowship;
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
        if(patchChoice.SelectedItem?.ToString()!="1.00")throw new Exception("New room patch did not default to 1.00");
        roomPresets.SelectedIndex=0;
        var available=patchChoice.Items.OfType<string>().FirstOrDefault(p=>GamePatches.Normalize(p)==patch);
        if(available!=null)
        {
            string oldMap=(mapChoice.SelectedItem as GameMap)?.Path??"";
            patchChoice.SelectedItem=available;
            var expected=GameInstallation.Maps(settings.GamePath,patch);
            if(!mapChoice.Items.Cast<GameMap>().Select(m=>m.Path).SequenceEqual(expected.Select(m=>m.Path)))throw new Exception("Patch selector retained the previous map catalog");
            if(expected.Any(m=>m.Path.Equals(oldMap,StringComparison.OrdinalIgnoreCase))&&(mapChoice.SelectedItem as GameMap)?.Path!=oldMap)throw new Exception("Patch selector discarded a map available on both patches");
            if(roomPresets.Items.OfType<ComboBoxItem>().Select(i=>i.Tag).OfType<RoomPreset>().Any(p=>GamePatches.Normalize(p.Patch)!=patch))throw new Exception("Room editor retained presets for the previous patch");
            patchChoice.SelectedItem="1.00";
            if(!mapChoice.Items.Cast<GameMap>().Select(m=>m.Path).SequenceEqual(GameInstallation.Maps(settings.GamePath,"1.00").Select(m=>m.Path)))throw new Exception("Switching back retained the alternate patch map catalog");
        }
        editor.Close();
        Console.WriteLine("PASS patch-scoped presets, legacy preset migration, default room patch, and map/preset refresh when changing room patch");
    }
    static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Nodes(child))yield return node;
    }
}

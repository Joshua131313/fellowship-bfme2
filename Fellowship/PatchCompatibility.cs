namespace Fellowship;

public static class PatchCompatibility
{
    public static string? Warning(string executable,string required)
    {
        var installed=GamePatches.Active(executable);required=GamePatches.Normalize(required);
        if(installed=="Unknown")return $"This room uses BFME2 {required}. Your installed patch could not be verified. You can join the room; check it in the patch switcher before playing.";
        return GamePatches.Normalize(installed)==required?null:
            $"This room uses BFME2 {required}, but your installed patch is {installed}. You can join the room; switch patches before playing.";
    }
}

using System.IO;
namespace Fellowship;

// Completion belongs to this installation, independently of preference restores
// and older Settings instances that may still save their previous state.
public static class OnboardingState
{
    public static bool IsCompleted(string folder)=>File.Exists(Path.Combine(folder,"onboarding-completed"));
    public static void Complete(string folder)
    {
        Directory.CreateDirectory(folder);
        if(!IsCompleted(folder))File.WriteAllText(Path.Combine(folder,"onboarding-completed"),"1");
    }
}

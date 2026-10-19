using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Fellowship;
public static class WindowAttention
{
    [StructLayout(LayoutKind.Sequential)]struct FlashInfo{public uint Size;public nint Window;public uint Flags;public uint Count;public uint Timeout;}
    [DllImport("user32.dll")]static extern bool FlashWindowEx(ref FlashInfo info);
    public static void Flash(Window window)=>Send(window,14); // Taskbar, until foreground.
    public static void Stop(Window window)=>Send(window,0);
    static void Send(Window window,uint flags)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var info=new FlashInfo{Size=(uint)Marshal.SizeOf<FlashInfo>(),Window=handle,Flags=flags,Count=5};FlashWindowEx(ref info);
    }
}

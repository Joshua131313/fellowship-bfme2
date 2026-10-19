using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Fellowship;

public static class GameIcon
{
    static string? cachedPath;
    static ImageSource? cachedIcon;
    public static ImageSource Load(string path)
    {
        if(string.IsNullOrWhiteSpace(path))path=GameInstallation.Find();
        if(cachedIcon!=null&&cachedPath==path)return cachedIcon;
        cachedPath=path;
        try
        {
            if(File.Exists(path))
            {
                using var icon=System.Drawing.Icon.ExtractAssociatedIcon(path);
                if(icon!=null){var bitmap=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());bitmap.Freeze();return cachedIcon=bitmap;}
            }
        }
        catch(Exception error) when(error is IOException or ArgumentException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){}
        return cachedIcon=new BitmapImage(new Uri("pack://application:,,,/Assets/Fellowship.ico"));
    }
}

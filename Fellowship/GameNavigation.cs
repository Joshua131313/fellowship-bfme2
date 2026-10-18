using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using Drawing=System.Drawing;

namespace Fellowship;

// Opt-in, English/16:9 navigation. All input runs in the isolated local helper.
// Unknown screens, changed focus, Escape, or an unresponsive button stop the flow.
public static class GameNavigation
{
    public sealed class Landmark
    {
        public string Name {get;set;}="";
        public double X {get;set;}
        public double Y {get;set;}
        public double Width {get;set;}
        public double Height {get;set;}
        public byte[] Samples {get;set;}=[];
    }
    public static double Difference(byte[] expected,byte[] actual)
    {
        if(expected.Length!=1024||actual.Length!=expected.Length)return double.PositiveInfinity;
        return expected.Zip(actual,(a,b)=>Math.Abs(a-b)).Average();
    }
    static byte[] Sample(Drawing.Bitmap frame,Landmark landmark)
    {
        var result=new byte[1024];
        for(int y=0;y<16;y++)for(int x=0;x<64;x++)
        {
            int px=(int)Math.Round((landmark.X+(x+0.5)*landmark.Width/64)*frame.Width);
            int py=(int)Math.Round((landmark.Y+(y+0.5)*landmark.Height/16)*frame.Height);
            if(px<0||py<0||px>=frame.Width||py>=frame.Height)return [];
            var color=frame.GetPixel(px,py);
            result[y*64+x]=(byte)Math.Round((color.R*299+color.G*587+color.B*114)/1000.0);
        }
        return result;
    }
    public static List<Landmark> LoadLandmarks()
    {
        using var stream=typeof(GameNavigation).Assembly.GetManifestResourceStream("Fellowship.Assets.navigation.json")!;
        return JsonSerializer.Deserialize<List<Landmark>>(stream)??[];
    }
    public static string Recognize(Drawing.Bitmap frame,List<Landmark> landmarks)
    {
        var scores=landmarks.Select(l=>(l.Name,Score:Difference(l.Samples,Sample(frame,l)))).OrderBy(v=>v.Score).ToArray();
        // Live 5120px DirectX captures differ from the downsampled reference by ~12 levels.
        // Require both a close match and a clear margin over every competing menu.
        if(scores.Length<2)return "";
        double tolerance=scores[0].Name is "Network" or "HostLobby"?24:16;
        return scores[0].Score<tolerance&&scores[1].Score-scores[0].Score>=8?scores[0].Name:"";
    }
    public static async Task Run(int pid,bool host)
    {
        var landmarks=LoadLandmarks();
        using var process=Process.GetProcessById(pid);
        var deadline=DateTime.UtcNow.AddMinutes(5);
        var clicked=new HashSet<string>();
        string previous="";int stable=0;bool started=false;
        var stepDeadline=deadline;
        var diagnosticTime=DateTime.MinValue;
        File.AppendAllText(HostPreset.LogPath,"Navigation: waiting for a stable main menu (up to five minutes). No input during loading.\r\n");
        while(DateTime.UtcNow<deadline)
        {
            if(process.HasExited)throw new IOException("BFME2 closed during navigation.");
            var window=FindGameWindow(pid);
            if((GetAsyncKeyState(0x1b)&0x8000)!=0)throw new IOException("Navigation cancelled with Escape. Continue manually in BFME2.");
            if(window==0||GetForegroundWindow()!=window)
            {
                if(started)throw new IOException("Navigation stopped because BFME2 lost focus. Continue manually.");
                if(DateTime.UtcNow>diagnosticTime)
                {
                    File.AppendAllText(HostPreset.LogPath,window==0?"Navigation: waiting for the game window.\r\n":"Navigation: waiting for BFME2 to be foreground.\r\n");
                    diagnosticTime=DateTime.UtcNow.AddSeconds(10);
                }
                await Task.Delay(100);continue;
            }
            var oldDpi=SetThreadDpiAwarenessContext(new nint(-4));
            string scene="";POINT origin=default;RECT rect=default;
            try
            {
                if(!GetClientRect(window,out rect)||!ClientToScreen(window,ref origin))throw new IOException("Cannot locate the BFME2 window.");
                var ratio=rect.Right/(double)rect.Bottom;
                if(rect.Right>=640&&rect.Bottom>=360&&Math.Abs(ratio-16.0/9)<0.03)
                {
                    using var frame=new Drawing.Bitmap(rect.Right,rect.Bottom);
                    using(var graphics=Drawing.Graphics.FromImage(frame))graphics.CopyFromScreen(origin.X,origin.Y,0,0,frame.Size);
                    scene=Recognize(frame,landmarks);
                    if(DateTime.UtcNow>diagnosticTime)
                    {
                        var scores=string.Join(", ",landmarks.Select(l=>$"{l.Name}={Difference(l.Samples,Sample(frame,l)):F1}"));
                        File.AppendAllText(HostPreset.LogPath,$"Navigation: {rect.Right}x{rect.Bottom}; screen={scene}; differences: {scores}.\r\n");
                        diagnosticTime=DateTime.UtcNow.AddSeconds(5);
                    }
                }
                else if(started)throw new IOException("The game window changed size. Navigation stopped.");
                else if(DateTime.UtcNow>diagnosticTime)
                {
                    File.AppendAllText(HostPreset.LogPath,$"Navigation: waiting for game layout; current size {rect.Right}x{rect.Bottom}.\r\n");
                    diagnosticTime=DateTime.UtcNow.AddSeconds(5);
                }
            }
            finally{SetThreadDpiAwarenessContext(oldDpi);}
            if(started&&DateTime.UtcNow>stepDeadline)
                throw new IOException("BFME2 did not reach the next recognized menu. Navigation stopped; continue manually.");
            if(scene==previous&&scene!="")stable++;else{stable=0;previous=scene;}
            if(stable<2){await Task.Delay(100);continue;}
            // Loading screens or an already-open lobby must never start the click sequence.
            if(!started&&scene!="Main"){await Task.Delay(100);continue;}
            if(scene=="HostLobby")
            {
                File.AppendAllText(HostPreset.LogPath,"Navigation: native host lobby detected. Assign teams and starting positions manually.\r\n");return;
            }
            if(scene=="Network"&&!host)
            {
                File.AppendAllText(HostPreset.LogPath,"Navigation: Network reached. Select and join your host manually; automatic guest selection is not implemented.\r\n");return;
            }
            if(clicked.Contains(scene))
            {
                if(DateTime.UtcNow>stepDeadline)throw new IOException("BFME2 did not respond to the "+scene+" navigation step. Continue manually.");
                await Task.Delay(100);continue;
            }
            (double X,double Y)? point=scene switch
            {
                "Main"=>(925.0/2048,1065.0/1152),
                "Multiplayer"=>(925.0/2048,900.0/1152),
                "Network" when host=>(1703.0/2048,530.0/1152),
                _=>null
            };
            if(point==null)throw new IOException("No verified navigation action for this menu. Continue manually.");
            if(!started)
            {
                deadline=DateTime.UtcNow.AddSeconds(60);
                File.AppendAllText(HostPreset.LogPath,"Navigation: stable main menu confirmed; beginning navigation.\r\n");
            }
            started=true;
            await Click(window,origin.X+(int)(rect.Right*point.Value.X),origin.Y+(int)(rect.Bottom*point.Value.Y));
            clicked.Add(scene);stepDeadline=DateTime.UtcNow.AddSeconds(12);
            File.AppendAllText(HostPreset.LogPath,"Navigation: clicked "+scene+" control; waiting for screen confirmation.\r\n");
            await Task.Delay(150);
        }
        throw new IOException("Navigation timed out waiting for a recognized BFME2 menu. Continue manually.");
    }
    static nint FindGameWindow(int pid)
    {
        nint match=0;
        EnumWindows((window,_)=>
        {
            GetWindowThreadProcessId(window,out uint owner);
            if(owner!=pid||!IsWindowVisible(window))return true;
            var title=new StringBuilder(256);GetWindowText(window,title,title.Capacity);
            if(title.ToString()=="The Lord of the Rings(tm), The Battle for Middle-earth(tm) II")match=window;
            return match==0;
        },0);
        return match;
    }
    static async Task Click(nint window,int x,int y)
    {
        if(GetForegroundWindow()!=window)throw new IOException("BFME2 lost focus before navigation input.");
        var dpi=SetThreadDpiAwarenessContext(new nint(-4));
        try{if(!SetCursorPos(x,y))throw new IOException("Could not position the game cursor.");}
        finally{SetThreadDpiAwarenessContext(dpi);}
        await Task.Delay(60);
        if(GetForegroundWindow()!=window)throw new IOException("BFME2 lost focus before navigation input.");
        var down=new INPUT{Type=0,Mouse=new MOUSEINPUT{Flags=2}};
        var up=new INPUT{Type=0,Mouse=new MOUSEINPUT{Flags=4}};
        if(SendInput(1,[down],Marshal.SizeOf<INPUT>())!=1)throw new IOException("Windows blocked navigation input.");
        try{await Task.Delay(120);}finally{SendInput(1,[up],Marshal.SizeOf<INPUT>());}
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct RECT{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT{public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)] struct INPUT{public uint Type;public MOUSEINPUT Mouse;}
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    delegate bool EnumWindow(nint window,nint parameter);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,nint parameter);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(nint window,StringBuilder text,int count);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool GetClientRect(nint window,out RECT rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(nint window,ref POINT point);
    [DllImport("user32.dll")]static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,INPUT[] input,int size);
}


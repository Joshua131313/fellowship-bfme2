using System.Net.NetworkInformation;
namespace Fellowship;
public sealed class PlayerLatency
{
    readonly Dictionary<string,(DateTimeOffset At,long? Value)> values=new();
    bool measuring;
    public long? Get(string ip,string local)=>ip==local?0:values.TryGetValue(ip,out var entry)&&DateTimeOffset.UtcNow-entry.At<TimeSpan.FromSeconds(20)?entry.Value:null;
    public async Task Refresh(IEnumerable<string> addresses,string local)
    {
        if(measuring)return;measuring=true;
        try
        {
            var targets=addresses.Where(a=>a!=local&&UI.ValidAddress(a)).Distinct().Take(256).ToArray();
            using var slots=new SemaphoreSlim(12);
            await Task.WhenAll(targets.Select(async ip=>
            {
                if(values.TryGetValue(ip,out var old)&&DateTimeOffset.UtcNow-old.At<TimeSpan.FromSeconds(5))return;
                await slots.WaitAsync();
                try{using var ping=new Ping();var result=await ping.SendPingAsync(ip,1200);values[ip]=(DateTimeOffset.UtcNow,result.Status==IPStatus.Success?result.RoundtripTime:null);}
                catch(Exception error) when(error is PingException or System.Net.Sockets.SocketException or InvalidOperationException){values[ip]=(DateTimeOffset.UtcNow,null);}
                finally{slots.Release();}
            }));
            foreach(var ip in values.Keys.Where(ip=>!targets.Contains(ip)).ToArray())values.Remove(ip);
        }
        finally{measuring=false;}
    }
}
public sealed class PingBadge:System.Windows.Controls.StackPanel
{
    public static readonly System.Windows.DependencyProperty MillisecondsProperty=System.Windows.DependencyProperty.Register(nameof(Milliseconds),typeof(long?),typeof(PingBadge),new System.Windows.PropertyMetadata(null,(d,e)=>((PingBadge)d).Render()));
    public long? Milliseconds{get=>(long?)GetValue(MillisecondsProperty);set=>SetValue(MillisecondsProperty,value);}
    public PingBadge(){Orientation=System.Windows.Controls.Orientation.Horizontal;VerticalAlignment=System.Windows.VerticalAlignment.Center;Render();}
    void Render()
    {
        Children.Clear();var ms=Milliseconds;string color=ms==null?UI.Muted:ms<100?UI.Green:ms<180?"#E5BC70":"#EE8690";int bars=ms==null?0:ms<60?4:ms<100?3:ms<180?2:1;
        for(int i=0;i<4;i++)Children.Add(new System.Windows.Controls.Border{Width=3,Height=4+i*3,CornerRadius=new(1),Background=UI.Brush(i<bars?color:"#343E4D"),Margin=new(0,0,2,0),VerticalAlignment=System.Windows.VerticalAlignment.Bottom});
        var text=UI.Text(ms.HasValue?$"{ms} ms":"—",10,color);text.Margin=new(6,0,0,0);Children.Add(text);
        ToolTip=ms.HasValue?"Network round-trip latency from this computer. Not BFME2 frame delay.":"Ping unavailable. The player may still be connected if ICMP is blocked.";
    }
}

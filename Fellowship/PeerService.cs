using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace Fellowship;

// A peer listens only on the interface selected by the user. No hosted service.
public sealed partial class PeerService : IDisposable
{
    public const int Port = 28742;
    const int MaxFrame = 32768;
    readonly Settings settings;
    readonly ConcurrentDictionary<string,TaskCompletionSource<string>> placementRequests=new();
    readonly object gate = new();
    readonly ConcurrentDictionary<string, Connection> guests = new();
    readonly CancellationTokenSource stop = new();
    TcpListener? listener;
    Connection? upstream;
    public Room? Current { get; private set; }
    public bool IsHost { get; private set; }
    public string LocalId { get; private set; } = "host";
    public string Address { get; private set; } = "";
    public event Action? Changed;
    public event Action<string>? Chat;
    public event Action<string, string, string>? PrivateMessage;
    public event Action? LaunchRequested;
    public event Action<string>? Notice;
    public PeerService(Settings settings) { this.settings = settings; }
    public static List<(string Name, string Address)> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => (n.Name, a.Address.ToString()))).ToList();
    public void Start(string address)
    {
        if (listener != null) return;
        var candidate = new TcpListener(IPAddress.Parse(address), Port);
        try { candidate.Start(); }
        catch { candidate.Stop(); throw; }
        Address = address;
        listener = candidate;
        _ = AcceptLoop();
        StartDiscovery();
    }
    async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            try { var client = await listener!.AcceptTcpClientAsync(stop.Token); _ = Accept(client); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Notice?.Invoke(e.Message); break; }
        }
    }
    async Task Accept(TcpClient tcp)
    {
        using var c = new Connection(tcp);
        string id = Guid.NewGuid().ToString("N");
        bool joined = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await c.Read(timeout.Token);
            if (hello == null) return;
            var remoteAddress=((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
            if (hello.Type == "query")
            {
                Room? snapshot;
                lock (gate) snapshot = !IsHost || Current == null || !CanSee(remoteAddress) ? null : JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(Current));
                await c.Send(new() { Type = "room", Room = snapshot, Name=settings.Name }); return;
            }
            if (hello.Type == "dm")
            {
                var remote = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
                PrivateMessage?.Invoke(remote, Clean(hello.Name, 40), Clean(hello.Text, 2000));
                await c.Send(new() { Type = "ack" }); return;
            }
            if(hello.Type=="resume")
            {
                lock(gate)
                {
                    if(IsHost&&Current!=null&&resumeTickets.TryGetValue(hello.Id,out var ticket)&&ticket==hello.Token&&Current.Players.Any(p=>p.Id==hello.Id&&p.Address==remoteAddress)&&!guests.ContainsKey(hello.Id))
                    {id=hello.Id;resumeTickets.Remove(id);guests[id]=c;joined=true;}
                }
                if(!joined){await c.Send(new(){Type="error",Text="Room handoff expired or was not authorized."});return;}
            }
            else if (hello.Type != "join") return;
            if (hello.Version != "1.00") { await c.Send(new() { Type = "error", Text = "Only BFME2 1.00 is supported by this build." }); return; }
            string error = "";
            if(!joined)lock (gate)
            {
                if (transferring) error="Host transfer in progress. Try again shortly.";
                else if (!IsHost || Current == null) error = "This peer is not hosting a room.";
                else if (!CanSee(remoteAddress)) error = "This room is for the host's saved friends only.";
                else if (Current.Players.Count >= Current.Capacity) error = "The room is full.";
                else
                {
                    guests[id] = c;
                    Current.Players.Add(new() { Id = id, Name = Clean(hello.Name, 40), Address=remoteAddress,CanHostTransfer=hello.CanHostTransfer });
                    joined = true;
                }
            }
            if (!joined) { await c.Send(new() { Type = "error", Text = error }); return; }
            await c.Send(new() { Type = "welcome", Id = id });
            await BroadcastState();
            while (await c.Read(stop.Token) is { } msg)
            {
                if(msg.Type=="handoff-ready")
                {
                    if(handoffTarget==id&&handoffId==msg.RequestId)handoffReady?.TrySetResult(msg.Text);
                }
                else if (msg.Type == "chat")
                {
                    string? name;
                    lock (gate) name = Current?.Players.FirstOrDefault(p => p.Id == id)?.Name;
                    await SendRoomChat(name ?? "Player", msg.Text);
                }
                else if(msg.Type=="placement")
                {
                    string failure="";
                    try{ApplyPlacement(id,msg.MapPath,msg.Spot,msg.Team);await BroadcastState();}
                    catch(InvalidOperationException e){failure=e.Message;}
                    await c.Send(new(){Type="placement-result",RequestId=msg.RequestId,Text=failure});
                }
                else if (msg.Type == "ready")
                {
                    lock (gate)
                    {
                        var p = Current?.Players.FirstOrDefault(p => p.Id == id);
                        if (p != null) p.Ready = msg.Ready;
                    }
                    await BroadcastState();
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or JsonException or InvalidDataException) { }
        finally
        {
            if (joined&&guests.TryGetValue(id,out var active)&&ReferenceEquals(active,c))
            {
                guests.TryRemove(id, out _);
                lock (gate) Current?.Players.RemoveAll(p => p.Id == id);
                await BroadcastState();
            }
        }
    }
    public void Host(Room room)
    {
        if (room.Patch != "1.00") throw new InvalidOperationException("Only BFME2 1.00 is supported by this build.");
        room.Match.Validate();
        if(room.Capacity is <2 or >8 || string.IsNullOrWhiteSpace(room.Title))throw new InvalidOperationException("Enter a room name and choose between 2 and 8 players.");
        Leave();
        lock (gate)
        {
            IsHost = true; LocalId = "host"; Current = room;
            room.Spots=GameMapData.Spots(settings.GamePath,room.MapPath);
            room.Host = settings.Name;room.HostAddress=Address;
            var saved=settings.LastHostedSetup;
            int team=saved?.Team is >=0 and <=4?saved.Team:0;
            int spot=saved!=null&&saved.MapPath==room.MapPath&&room.Spots.Any(s=>s.Number==saved.Spot)?saved.Spot:0;
            room.Players = [new() { Id = "host", Name = settings.Name, Address=Address, Ready = true,CanHostTransfer=listener!=null,Team=team,Spot=spot }];
        }
        Changed?.Invoke();
    }
    public async Task Join(string address)
    {
        Leave();
        var c = await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        try
        {
            await c.Send(new() { Type = "join", Name = settings.Name,CanHostTransfer=listener!=null });
            using var timeout = new CancellationTokenSource(5000);
            var welcome = await c.Read(timeout.Token);
            if (welcome?.Type != "welcome") throw new IOException(welcome?.Text ?? "No response from host.");
            LocalId = welcome.Id;
            upstream = c;
            _ = ReceiveHost(c);
        }
        catch { c.Dispose(); throw; }
    }
    async Task ReceiveHost(Connection c)
    {
        try
        {
            while (await c.Read(stop.Token) is { } msg)
            {
                // A disposed connection can still finish a buffered read after switching rooms.
                // Never let its final messages replace the new room or launch its game.
                if(!ReferenceEquals(upstream,c))break;
                if(msg.Type=="become-host")
                {
                    if(listener==null||msg.Room==null||msg.Tickets==null){await c.Send(new(){Type="handoff-ready",RequestId=msg.RequestId,Text="The next host must enable its VPN listener and update Fellowship."});continue;}
                    lock(gate){upstream=null;Current=msg.Room;Current.HostAddress=Address;IsHost=true;LocalId="host";resumeTickets=new(msg.Tickets);}
                    await c.Send(new(){Type="handoff-ready",RequestId=msg.RequestId});
                    Changed?.Invoke();_ = ExpireReservations(msg.Room.Id,resumeTickets);return;
                }
                if(msg.Type=="redirect")
                {
                    upstream=null;Current=null;c.Dispose();
                    try{await ResumeRoom(msg.Text,msg.Id,msg.Token);}catch(Exception ex){Notice?.Invoke("Could not reconnect to the new host: "+ex.Message);Changed?.Invoke();}
                    return;
                }
                if(msg.Type=="kicked")
                {upstream=null;Current=null;Changed?.Invoke();Notice?.Invoke("The host removed you from the room.");return;}
                if (msg.Type == "room"&&Room.AcceptsUpdate(Current,msg.Room)) { Current = msg.Room;if(Current!=null)Current.HostAddress=c.RemoteAddress; Changed?.Invoke(); }
                if(msg.Type=="placement-result"&&placementRequests.TryRemove(msg.RequestId,out var pending))pending.TrySetResult(msg.Text);
                if (msg.Type == "chat") Chat?.Invoke(msg.Text);
                if (msg.Type == "launch") LaunchRequested?.Invoke();
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or JsonException or InvalidDataException) { }
        finally
        {
            if (ReferenceEquals(upstream, c))
            {
                upstream = null; Current = null; Changed?.Invoke();
                Notice?.Invoke("Connection to the host ended. You can join again when the host is available.");
            }
            c.Dispose();
        }
    }
    public static async Task<Room?> Query(string address,string? source=null)
    {
        using var c = await Connect(address,source);
        await c.Send(new() { Type = "query" });
        using var timeout = new CancellationTokenSource(3000);
        return (await c.Read(timeout.Token))?.Room;
    }
    public async Task SendPrivate(string address, string text)
    {
        using var c = await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        await c.Send(new() { Type = "dm", Name = settings.Name, Text = Clean(text, 2000) });
        using var timeout = new CancellationTokenSource(3000);
        if ((await c.Read(timeout.Token))?.Type != "ack") throw new IOException("Message was not acknowledged.");
    }
    void ApplyPlacement(string id,string mapPath,int? spot,int? team)
    {
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("The room is closed.");
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if(mapPath!=Current.MapPath)throw new InvalidOperationException("The map changed. Choose a spot on the new map.");
            var player=Current.Players.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Player is no longer in the room.");
            if(team is <0 or >4)throw new InvalidOperationException("Choose no team or team 1–4.");
            if(spot.HasValue&&spot!=0&&!Current.Spots.Any(s=>s.Number==spot))throw new InvalidOperationException("That starting spot is unavailable on this map.");
            if(spot>0&&Current.Players.Any(p=>p.Id!=id&&p.Spot==spot))throw new InvalidOperationException("Another player has claimed this spot. Choose another.");
            if((!spot.HasValue||spot==player.Spot)&&(!team.HasValue||team==player.Team))return;
            if(spot.HasValue)player.Spot=spot.Value;if(team.HasValue)player.Team=team.Value;

        }
    }
    public async Task SelectPlacement(int? spot=null,int? team=null,string? expectedMap=null)
    {
        var room=Current??throw new InvalidOperationException("Join a room first.");
        var mapPath=expectedMap??room.MapPath;
        if(IsHost){ApplyPlacement("host",mapPath,spot,team);await BroadcastState();return;}
        var connection=upstream??throw new InvalidOperationException("Disconnected from host.");
        var id=Guid.NewGuid().ToString("N");var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);placementRequests[id]=completion;
        try
        {
            await connection.Send(new(){Type="placement",RequestId=id,MapPath=mapPath,Spot=spot,Team=team});
            string failure=await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(failure.Length>0)throw new InvalidOperationException(failure);
        }
        catch(TimeoutException){throw new IOException("Host did not confirm the selection. Both players need this version of Fellowship.");}
        finally{placementRequests.TryRemove(id,out _);}
    }
    public async Task SetReady(bool ready)
    {
        if (!IsHost && upstream != null) await upstream.Send(new() { Type = "ready", Ready = ready });
    }
    public async Task UpdateRoom(string map, string patch, string rules)
    {
        if (patch != "1.00") throw new InvalidOperationException("Only BFME2 1.00 is supported by this build.");
        lock (gate)
        {
            if (!IsHost || Current == null) return;
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            Current.Map = Clean(map, 100); Current.Patch = Clean(patch, 40); Current.Rules = Clean(rules, 1000);

        }
        await BroadcastState();
    }
    bool CanSee(string remote) => Current?.FriendsOnly!=true || settings.Friends.Any(f=>f.Address==remote);
    public async Task EditRoom(Room draft)
    {
        draft.Match.Validate();
        if(draft.Patch!="1.00"||draft.Capacity<2||draft.Capacity>8||string.IsNullOrWhiteSpace(draft.Title))throw new InvalidOperationException("Check the room name, patch and player limit.");
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("Only the host can edit this room.");
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if(draft.Capacity<Current.Players.Count)throw new InvalidOperationException("The player limit cannot be lower than the current roster.");
            if(Current.MapPath!=draft.MapPath)
            {
                Current.Spots=GameMapData.Spots(settings.GamePath,draft.MapPath);
                foreach(var p in Current.Players)p.Spot=0;
            }
            Current.Title=Clean(draft.Title,80);Current.Capacity=draft.Capacity;Current.Map=Clean(draft.Map,100);Current.MapPath=draft.MapPath;
            Current.Match=draft.Match;Current.Rules=draft.Match.Summary;Current.FriendsOnly=draft.FriendsOnly;

            foreach(var p in Current.Players.Where(p=>p.Id!="host"&&!CanSee(p.Address)).ToArray())
                if(guests.TryGetValue(p.Id,out var c))c.Dispose();
        }
        await BroadcastState();
    }
    public Task Say(string text) => IsHost ? SendRoomChat(settings.Name, text) : upstream?.Send(new() { Type = "chat", Text = Clean(text, 2000) }) ?? Task.CompletedTask;
    async Task SendRoomChat(string name, string text)
    {
        var line = $"{name}   {Clean(text, 2000)}";
        Chat?.Invoke(line);
        await Broadcast(new() { Type = "chat", Text = line });
    }
    public async Task Launch()
    {
        lock (gate)
        {
            if (!IsHost || Current == null) return;
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if (Current.Players.Any(p => !p.Ready)) throw new InvalidOperationException("Every player must be ready before launching.");
            Current.Phase = "Game launched • room stays open";
        }
        await Broadcast(new() { Type = "launch" });
        LaunchRequested?.Invoke();
        lock (gate)
        {
            if (Current != null) foreach (var p in Current.Players) if (p.Id != "host") p.Ready = false;
        }
        await BroadcastState();
    }
    async Task BroadcastState()
    {
        Room? snapshot;
        lock (gate)
        {
            if(Current!=null)Current.Revision++;
            snapshot = Current == null ? null : JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(Current));
        }
        Changed?.Invoke();
        await Broadcast(new() { Type = "room", Room = snapshot });
    }
    async Task Broadcast(Wire msg)
    {
        await Task.WhenAll(guests.Values.Select(async c => { try { await c.Send(msg); } catch { c.Dispose(); } }));
    }
    static string Clean(string s, int max) => new(s.Take(max).Where(c => !char.IsControl(c) || c == '\n').ToArray());
    static async Task<Connection> Connect(string address,string? source=null)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Enter an IPv4 address from Radmin VPN.");
        var tcp = source==null?new TcpClient():new TcpClient(new IPEndPoint(IPAddress.Parse(source),0));
        try { using var timeout = new CancellationTokenSource(3000); await tcp.ConnectAsync(ip, Port, timeout.Token); return new(tcp); }
        catch { tcp.Dispose(); throw; }
    }
    public void Leave()
    {
        var old = upstream; upstream = null; old?.Dispose();
        foreach(var pending in placementRequests.Values)pending.TrySetResult("Disconnected from room.");placementRequests.Clear();
        lock (gate) { IsHost = false; Current = null; }
        foreach (var c in guests.Values) c.Dispose();
        guests.Clear(); Changed?.Invoke();
    }
    public void Dispose() { discovery?.Dispose(); stop.Cancel(); listener?.Stop(); Leave(); stop.Dispose(); }
    sealed class Connection : IDisposable
    {
        public string RemoteAddress=>((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly SemaphoreSlim writes = new(1);
        public Connection(TcpClient tcp)
        {
            this.tcp = tcp; tcp.NoDelay = true; stream = tcp.GetStream();
            // Detect a lost VPN connection even while nobody is chatting or changing readiness.
            tcp.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.KeepAlive,true);
            tcp.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveTime,15);
            tcp.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveInterval,5);
            tcp.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveRetryCount,3);
        }
        public async Task Send(Wire msg)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
            if (bytes.Length > MaxFrame) throw new InvalidDataException("Message is too large.");
            await writes.WaitAsync();
            try
            {
                using var timeout = new CancellationTokenSource(5000);
                await stream.WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length)), timeout.Token);
                await stream.WriteAsync(bytes, timeout.Token);
            }
            finally { writes.Release(); }
        }
        public async Task<Wire?> Read(CancellationToken ct)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, ct);
            var size = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header));
            if (size <= 0 || size > MaxFrame) throw new InvalidDataException("Invalid message size.");
            var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct);
            return JsonSerializer.Deserialize<Wire>(bytes);
        }
        public void Dispose() => tcp.Dispose();
    }
}

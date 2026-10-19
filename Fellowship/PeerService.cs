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
    CancellationTokenSource? networkStop;
    TcpListener? listener;
    Connection? upstream;
    public Room? Current { get; private set; }
    public bool IsHost { get; private set; }
    string recoveryToken="";
    public string LocalId { get; private set; } = "host";
    public string Address { get; private set; } = "";
    public event Action? Changed;
    public event Action<string>? Chat;
    public event Action<string,bool>? RoomChat;
    public event Action? RoomMessageReceived;
    volatile bool gameRunning;
    public event Action<string, string, string>? PrivateMessage;
    public event Action<string,string,Room>? Invitation;
    public event Action? LaunchRequested;
    public Func<Task<string>>? LaunchHandler { get; set; }
    public event Action<string>? Notice;
    public PeerService(Settings settings) { this.settings = settings; }
    public static List<(string Name, string Address)> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => (n.Name, a.Address.ToString()))).ToList();
    public void Start(string address)
    {
        if(listener!=null&&Address==address)return;
        if(Current!=null)throw new InvalidOperationException("Leave the room before switching networks.");
        var candidate = new TcpListener(IPAddress.Parse(address), Port);
        try { candidate.Start(); }
        catch { candidate.Stop(); throw; }
        networkStop?.Cancel();networkStop?.Dispose();listener?.Stop();discovery?.Dispose();
        networkStop=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        Address = address;
        listener = candidate;
        _ = AcceptLoop(candidate,networkStop.Token);
        StartDiscovery();
    }
    async Task AcceptLoop(TcpListener server,CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var client = await server.AcceptTcpClientAsync(token); _ = Accept(client); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { if(!token.IsCancellationRequested)Notice?.Invoke(e.Message); break; }
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
            if(settings.IsBlocked(remoteAddress)&&hello.Type!="query"){await c.Send(new(){Type="error",Text="This peer is unavailable."});return;}
            if (hello.Type == "query")
            {
                Room? snapshot;
                lock (gate) snapshot = !IsHost || Current == null || (!CanSee(remoteAddress)||settings.IsBlocked(remoteAddress)) ? null : JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(Current));
                await c.Send(new() { Type = "room", Room = snapshot, Name=settings.Name,GameRunning=gameRunning,IdleSeconds=IdlePresence.Seconds() }); return;
            }
            if(hello.Type=="invite")
            {
                if(!settings.Friends.Any(f=>f.Address==remoteAddress)||hello.Room is not {} invite||!IPAddress.TryParse(invite.HostAddress,out var hostIp)||hostIp.AddressFamily!=AddressFamily.InterNetwork||invite.Id.Length>64)
                {await c.Send(new(){Type="error",Text="Only saved friends can invite you."});return;}
                Invitation?.Invoke(remoteAddress,Clean(hello.Name,40),new(){Id=invite.Id,HostAddress=invite.HostAddress,Title=Clean(invite.Title,80)});
                await c.Send(new(){Type="ack"});return;
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
                else if(Current.Isolated&&!hello.SupportsIsolation)error="This isolated room requires Fellowship 0.1.37 or newer.";
                else if (Current.Players.Count >= Current.Capacity) error = "The room is full.";
                else
                {
                    guests[id] = c;
                    var player=new Player { Team=Current.NextTeam(),PreferredColor=hello.Color??0,LastSelectedColor=hello.LastSelectedColor,Id = id, Name = Clean(hello.Name, 40), Address=remoteAddress,CanHostTransfer=hello.CanHostTransfer,PreferredMapSpots=hello.PreferredMapSpots??new() };
                    Current.ApplyPreferences(player,0,hello.Spot??0,hello.MapPath);
                    Current.ApplyColorChoice(player,player.PreferredColor,player.LastSelectedColor);
                    Current.Players.Add(player);
                    joined = true;
                }
            }
            if (!joined) { await c.Send(new() { Type = "error", Text = error }); return; }
            await c.Send(new() { Type = "welcome", Id = id });
            await BroadcastState();
            while (await c.Read(stop.Token) is { } msg)
            {
                if(settings.IsBlocked(remoteAddress))break;
                if(msg.Type=="handoff-ready")
                {
                    if(handoffTarget==id&&handoffId==msg.RequestId)handoffReady?.TrySetResult(msg.Text);
                }
                else if(msg.Type=="game-status")
                {
                    lock(gate){var player=Current?.Players.FirstOrDefault(p=>p.Id==id);if(player!=null){player.GameRunning=msg.GameRunning;player.IdleSeconds=IdlePresence.Normalize(msg.IdleSeconds);}}
                    await BroadcastState();
                }
                else if(msg.Type=="launch-result"){Notice?.Invoke($"{Current?.Players.FirstOrDefault(p=>p.Id==id)?.Name??"Player"}: {Clean(msg.Text,300)}");}
                else if(msg.Type=="native-setup"&&msg.Room is {} setup)
                {
                    try{await ApplyNativeRules(setup);}catch(InvalidOperationException){} // Invalid/stale observations cannot replace current rules.
                }
                else if (msg.Type == "chat")
                {
                    string? name;
                    lock (gate) name = Current?.Players.FirstOrDefault(p => p.Id == id)?.Name;
                    await SendRoomChat(name ?? "Player", msg.Text,id);
                }
                else if(msg.Type=="placement")
                {
                    string failure="";
                    try{ApplyPlacement(id,msg.MapPath,msg.Spot,msg.Team,msg.Color);await BroadcastState();}
                    catch(InvalidOperationException e){failure=e.Message;}
                    await c.Send(new(){Type="placement-result",RequestId=msg.RequestId,Text=failure});
                }
                else if(msg.Type=="nickname")
                {
                    string nickname=NormalizeNickname(msg.Name);
                    if(nickname.Length==0)continue;
                    lock(gate){var player=Current?.Players.FirstOrDefault(p=>p.Id==id);if(player!=null)player.Name=nickname;}
                    await BroadcastState();
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
    public void Host(Room room,List<MapSpot>? preparedSpots=null)
    {
        if (room.Patch != "1.00") throw new InvalidOperationException("Only BFME2 1.00 is supported by this build.");
        room.Match.Validate();
        if(room.Capacity is <2 or >64 || string.IsNullOrWhiteSpace(room.Title))throw new InvalidOperationException("Enter a room name and choose between 2 and 64 players.");
        Leave();
        lock (gate)
        {
            IsHost = true; LocalId = "host"; Current = room;
            recoveryToken=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            room.Isolated=false;
            room.Spots=preparedSpots??GameMapData.Spots(settings.GamePath,room.MapPath);
            room.Host = settings.Name;room.HostAddress=Address;
            var saved=settings.LastHostedSetup;
            int team=room.InitialTeams.Length>0?room.InitialTeams[0]:saved?.Team is >=0 and <=4?saved.Team:0;
            int spot=saved!=null&&saved.MapPath==room.MapPath&&room.Spots.Any(s=>s.Number==saved.Spot)?saved.Spot:0;
            room.Players = [new() { Id = "host",PreferredColor=settings.PreferredColor,LastSelectedColor=settings.LastSelectedColor, Name = settings.Name, Address=Address, Ready = true,PreferredMapSpots=new(settings.PreferredMapSpots),CanHostTransfer=listener!=null,Team=team,Spot=spot,Color=saved?.Color is >=0 and <=10?saved.Color:0 }];
            room.ApplyColorChoice(room.Players[0],settings.PreferredColor,settings.LastSelectedColor>0?settings.LastSelectedColor:room.Players[0].Color);
            room.ApplyPreferences(room.Players[0],0,settings.PreferredSpot(room.MapPath),room.MapPath);
        }
        Changed?.Invoke();
        _ = Discover(presence:true);
    }
    public async Task Join(string address)
    {
        if(settings.IsBlocked(address))throw new InvalidOperationException("Unblock this player before joining.");
        var advertised=await Query(address,string.IsNullOrEmpty(Address)?null:Address);
        if(advertised==null)throw new IOException("This room is no longer available or is private.");
        Leave();
        var c = await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        try
        {
            await c.Send(new() { Type = "join",LastSelectedColor=settings.LastSelectedColor, PreferredMapSpots=new(settings.PreferredMapSpots),Name = settings.Name,CanHostTransfer=listener!=null,Color=settings.PreferredColor,Spot=settings.PreferredSpot(advertised.MapPath),MapPath=advertised.MapPath });
            using var timeout = new CancellationTokenSource(5000);
            var welcome = await c.Read(timeout.Token);
            if (welcome?.Type != "welcome") throw new IOException(welcome?.Text ?? "No response from host.");
            LocalId = welcome.Id;
            upstream = c;
            _ = ReceiveHost(c);
            while(ReferenceEquals(upstream,c)&&Current==null&&!timeout.IsCancellationRequested)await Task.Delay(25);
            if(Current==null||!ReferenceEquals(upstream,c))throw new IOException("The host did not finish sending the room. Please retry.");
        }
        catch { if(ReferenceEquals(upstream,c)){upstream=null;Current=null;}c.Dispose(); throw; }
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
                    lock(gate){upstream=null;Current=msg.Room;var hostPlayer=Current.Players.FirstOrDefault(p=>p.Id=="host");if(hostPlayer!=null)hostPlayer.Ready=true;Current.HostAddress=Address;IsHost=true;LocalId="host";resumeTickets=new(msg.Tickets);recoveryToken=msg.Token;}
                    await c.Send(new(){Type="handoff-ready",RequestId=msg.RequestId});
                    _ = Discover(presence:true);Changed?.Invoke();_ = ExpireReservations(msg.Room.Id,resumeTickets);return;
                }
                if(msg.Type=="redirect")
                {
                    upstream=null;Current=null;c.Dispose();
                    try{await ResumeRoom(msg.Text,msg.Id,msg.Token);}catch(Exception ex){Notice?.Invoke("Could not reconnect to the new host: "+ex.Message);Changed?.Invoke();}
                    return;
                }
                if(msg.Type=="kicked")
                {upstream=null;Current=null;Changed?.Invoke();Notice?.Invoke("The host removed you from the room.");return;}
                if (msg.Type == "room"&&Room.AcceptsUpdate(Current,msg.Room))
                {
                    Current=msg.Room;recoveryToken=msg.Token;if(Current!=null)Current.HostAddress=c.RemoteAddress;
                    Changed?.Invoke();
                }
                if(msg.Type=="placement-result"&&placementRequests.TryRemove(msg.RequestId,out var pending))pending.TrySetResult(msg.Text);
                if (msg.Type == "chat") DeliverRoomChat(msg.Text,msg.Id);
                if (msg.Type == "launch") _=ReceiveLaunch(c);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or JsonException or InvalidDataException) { }
        finally
        {
            if (ReferenceEquals(upstream, c))
            {
                upstream = null;
                if(!await RecoverHost())
                {
                    Current = null; Changed?.Invoke();
                    Notice?.Invoke("Connection to the host ended. Rejoin from the room browser.");
                }
            }
            c.Dispose();
        }
    }
    public static async Task<Room?> Query(string address,string? source=null)=>(await QueryPeer(address,source)).Room;
    public static async Task<(string Name,Room? Room,bool GameRunning,int? IdleSeconds)> QueryPeer(string address,string? source=null)
    {
        using var c = await Connect(address,source);
        await c.Send(new() { Type = "query" });
        using var timeout = new CancellationTokenSource(3000);
        var reply=await c.Read(timeout.Token);
        if(reply?.Type!="room")throw new IOException("Invalid peer response.");
        return (Clean(reply.Name,40),reply.Room,reply.GameRunning,IdlePresence.Normalize(reply.IdleSeconds));
    }
    public async Task SendPrivate(string address, string text)
    {
        if(settings.IsBlocked(address))throw new InvalidOperationException("Unblock this player before messaging.");
        using var c = await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        await c.Send(new() { Type = "dm", Name = settings.Name, Text = Clean(text, 2000) });
        using var timeout = new CancellationTokenSource(3000);
        if ((await c.Read(timeout.Token))?.Type != "ack") throw new IOException("Message was not acknowledged.");
    }
    static string NormalizeNickname(string name)=>new string(name.Where(c=>!char.IsControl(c)).Take(40).ToArray()).Trim();
    public async Task SetNickname(string name)
    {
        string nickname=NormalizeNickname(name);if(nickname.Length==0)throw new InvalidOperationException("Enter a nickname.");
        lock(gate){settings.Name=nickname;settings.Save();if(IsHost&&Current!=null){Current.Host=nickname;var player=Current.Players.FirstOrDefault(p=>p.Id==LocalId);if(player!=null)player.Name=nickname;}}
        if(IsHost)await BroadcastState();
        else if(upstream!=null)await upstream.Send(new(){Type="nickname",Name=nickname});
        Changed?.Invoke();
    }
    public async Task SetBlocked(string name,string address,bool blocked)
    {
        if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||address==Address)throw new InvalidOperationException("Choose another player's VPN address.");
        Player[] affected;
        lock(gate)
        {
            if(transferring)throw new InvalidOperationException("Wait for host transfer to finish.");
            settings.BlockedUsers=settings.BlockedUsers.Where(p=>p.Address!=address).Concat(blocked?new[]{new Friend(Clean(name,40),address)}:Array.Empty<Friend>()).ToArray();
            settings.Save();affected=Current?.Players.Where(p=>p.Address==address).ToArray()??[];
        }
        if(blocked&&affected.Length>0)
        {
            if(IsHost){foreach(var player in affected)await Kick(player.Id);}
        }
        Changed?.Invoke();
    }
    void ApplyPlacement(string id,string mapPath,int? spot,int? team,int? color)
    {
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("The room is closed.");
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if(mapPath!=Current.MapPath)throw new InvalidOperationException("The map changed. Choose a spot on the new map.");
            var player=Current.Players.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Player is no longer in the room.");
            if(color is <0 or >10)throw new InvalidOperationException("Choose a BFME2 player color.");
            if(color>0&&Current.Players.Any(p=>p.Id!=id&&p.Color==color))throw new InvalidOperationException("Another player has claimed this color. Choose another.");
            if(team is <0 or >4)throw new InvalidOperationException("Choose no team or team 1–4.");
            if(spot.HasValue&&spot!=0&&!Current.Spots.Any(s=>s.Number==spot))throw new InvalidOperationException("That starting spot is unavailable on this map.");
            if(spot>0&&Current.Players.Any(p=>p.Id!=id&&p.Spot==spot))throw new InvalidOperationException("Another player has claimed this spot. Choose another.");
            if((!spot.HasValue||spot==player.Spot)&&(!team.HasValue||team==player.Team)&&(!color.HasValue||color==player.Color))return;
            if(spot.HasValue)player.Spot=spot.Value;if(team.HasValue)player.Team=team.Value;if(color.HasValue){player.Color=color.Value;player.LastSelectedColor=color.Value;}

        }
    }
    public async Task SelectPlacement(int? spot=null,int? team=null,string? expectedMap=null,int? color=null)
    {
        var room=Current??throw new InvalidOperationException("Join a room first.");
        var mapPath=expectedMap??room.MapPath;
        if(IsHost){ApplyPlacement("host",mapPath,spot,team,color);await BroadcastState();RememberColor(color);return;}
        var connection=upstream??throw new InvalidOperationException("Disconnected from host.");
        var id=Guid.NewGuid().ToString("N");var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);placementRequests[id]=completion;
        try
        {
            await connection.Send(new(){Type="placement",RequestId=id,MapPath=mapPath,Spot=spot,Team=team,Color=color});
            string failure=await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(failure.Length>0)throw new InvalidOperationException(failure);
            RememberColor(color);
        }
        catch(TimeoutException){throw new IOException("Host did not confirm the selection. Both players need this version of Fellowship.");}
        finally{placementRequests.TryRemove(id,out _);}
    }
    void RememberColor(int? color){if(color.HasValue&&settings.LastSelectedColor!=color.Value){settings.LastSelectedColor=color.Value;settings.Save();}}
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
    bool CanSee(string remote) => !settings.IsBlocked(remote)&&(Current?.FriendsOnly!=true || settings.Friends.Any(f=>f.Address==remote));
    public Task SyncNativeRules(Room observed)
    {
        if(IsHost)return ApplyNativeRules(observed);
        return upstream?.Send(new(){Type="native-setup",Room=new(){Id=observed.Id,Revision=observed.Revision,MapPath=observed.MapPath,Match=observed.Match}})??Task.CompletedTask;
    }
    async Task ApplyNativeRules(Room observed)
    {
        observed.Match.Validate();
        var map=GameInstallation.Maps(settings.GamePath).FirstOrDefault(m=>m.Path.Equals(observed.MapPath,StringComparison.OrdinalIgnoreCase));
        if(map==null)throw new InvalidOperationException("Map is not installed.");
        Task update;
        lock(gate)
        {
            if(!IsHost||Current==null||transferring||Current.Id!=observed.Id||Current.Revision!=observed.Revision)return;
            var draft=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(Current))!;
            draft.Map=map.Name;draft.MapPath=map.Path;draft.Match=observed.Match;
            update=EditRoom(draft);
        }
        await update;
    }
    public async Task EditRoom(Room draft)
    {
        draft.Match.Validate();
        if(draft.Patch!="1.00"||draft.Capacity<2||draft.Capacity>64||string.IsNullOrWhiteSpace(draft.Title))throw new InvalidOperationException("Check the room name, patch and player limit.");
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("Only the host can edit this room.");
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if(draft.Capacity<Current.Players.Count)throw new InvalidOperationException("The player limit cannot be lower than the current roster.");
            bool mapChanged=Current.MapPath!=draft.MapPath;
            if(mapChanged)
            {
                Current.Spots=GameMapData.Spots(settings.GamePath,draft.MapPath);
                foreach(var p in Current.Players){p.LastMapSpots[Current.MapPath]=p.Spot;p.Spot=0;}
            }
            Current.Title=Clean(draft.Title,80);Current.Capacity=draft.Capacity;Current.Map=Clean(draft.Map,100);Current.MapPath=draft.MapPath;
            Current.Mode=draft.Mode;Current.Match=draft.Match;Current.Rules=draft.Match.Summary;Current.FriendsOnly=draft.FriendsOnly;
            if(mapChanged)
            {
                if(Current.Players.FirstOrDefault(p=>p.Id==LocalId) is {} local){local.PreferredMapSpots=new(settings.PreferredMapSpots);local.PreferredColor=settings.PreferredColor;}
                // Roster order is join order; allocate newest members first, under the room lock.
                Current.ApplyMapSpotChoices();
            }

            foreach(var p in Current.Players.Where(p=>p.Id!="host"&&!CanSee(p.Address)).ToArray())
                if(guests.TryGetValue(p.Id,out var c))c.Dispose();
        }
        await BroadcastState();
    }
    public Task Say(string text) => Current==null?Task.FromException(new IOException("You are not connected to a room.")):IsHost ? SendRoomChat(settings.Name, text) : upstream?.Send(new() { Type = "chat", Text = Clean(text, 2000) }) ?? Task.FromException(new IOException("The room connection is unavailable."));
    async Task SendRoomChat(string name, string text,string id="host")
    {
        var line = $"{name}   {Clean(text, 2000)}";
        DeliverRoomChat(line,id);
        await Broadcast(new() { Type = "chat", Text = line,Id=id });
    }
    void DeliverRoomChat(string text,string id)
    {
        var sender=Current?.Players.FirstOrDefault(p=>p.Id==id);
        bool blocked=sender!=null?settings.IsBlocked(sender.Address):Current?.Players.Any(p=>settings.IsBlocked(p.Address)&&text.StartsWith(p.Name+"   ",StringComparison.Ordinal))==true;
        RoomChat?.Invoke(text,blocked);
        if(!blocked&&id!=LocalId)RoomMessageReceived?.Invoke();
        if(!blocked)Chat?.Invoke(text);
    }
    public async Task SetHostGameRunning(bool running)
    {
        gameRunning=running;var idle=IdlePresence.Seconds();
        lock(gate)
        {
            if(Current==null||transferring)return;
            var self=Current.Players.FirstOrDefault(p=>p.Id==LocalId);if(self==null)return;
            string phase=running?"Host in BFME2":"Gathering";
            if(self.GameRunning==running&&self.IdleSeconds==idle&&(!IsHost||Current.Phase==phase))return;
            self.GameRunning=running;self.IdleSeconds=idle;if(IsHost)Current.Phase=phase;
        }
        if(IsHost)await BroadcastState();else if(upstream!=null)await upstream.Send(new(){Type="game-status",GameRunning=running,IdleSeconds=idle});
    }
    public async Task Invite(string address)
    {
        var room=Current??throw new InvalidOperationException("Join or host a room first.");
        if(settings.IsBlocked(address)||!settings.Friends.Any(f=>f.Address==address))throw new InvalidOperationException("Choose an unblocked friend.");
        using var connection=await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        await connection.Send(new(){Type="invite",Name=settings.Name,Room=new(){Id=room.Id,Title=room.Title,HostAddress=room.HostAddress}});
        using var timeout=new CancellationTokenSource(5000);
        var reply=await connection.Read(timeout.Token);if(reply?.Type!="ack")throw new IOException(reply?.Text??"Invitation was not acknowledged.");
    }
    async Task ReceiveLaunch(Connection connection)
    {
        try{await connection.Send(new(){Type="launch-result",Text="Launch request received; preparing BFME2 and waiting for any Windows permission prompt."});}catch{return;}
        string result;
        try{if(LaunchHandler!=null)result=await LaunchHandler();else{LaunchRequested?.Invoke();result="Launch request received.";}}
        catch(Exception error){result="Could not launch: "+error.Message;}
        try{await connection.Send(new(){Type="launch-result",Text=result});}catch(Exception error) when(error is IOException or SocketException or ObjectDisposedException){}
    }
    public async Task Launch()
    {
        lock (gate)
        {
            if (!IsHost || Current == null) return;
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if (!Current.AllGuestsReady) throw new InvalidOperationException("Every player must be ready before launching.");
            // Presence follows the real game process, not the launch request.
        }
        var failures=new List<string>();
        foreach(var player in Current!.Players.Where(p=>p.Id!="host").ToArray())
        {
            if(!guests.TryGetValue(player.Id,out var connection)){failures.Add(player.Name);continue;}
            try{await connection.Send(new(){Type="launch"});}catch{failures.Add(player.Name);}
        }
        if(failures.Count>0)Notice?.Invoke("Launch could not reach: "+string.Join(", ",failures)+". Ask them to reconnect or use Launch mine.");
        LaunchRequested?.Invoke();
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
        await Broadcast(new() { Type = "room", Room = snapshot,Token=recoveryToken });
        await Discover(presence:true);
    }
    async Task Broadcast(Wire msg)
    {
        await Task.WhenAll(guests.Values.Select(async c => { try { await c.Send(msg); } catch { c.Dispose(); } }));
    }
    static string Clean(string s, int max) => new(s.Take(max).Where(c => !char.IsControl(c) || c == '\n').ToArray());
    static async Task<Connection> Connect(string address,string? source=null)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Enter the host’s IPv4 address on your selected VPN network.");
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
        _ = Discover(presence:true);
    }
    public void Dispose() { networkStop?.Cancel();discovery?.Dispose(); stop.Cancel(); listener?.Stop(); Leave(); stop.Dispose(); }
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

using System.IO;
using System.Text.Json;
namespace Fellowship;
public sealed partial class PeerService
{
    public event Action<string,string,Room?>? HostTransferred;
    bool transferring;
    string handoffTarget="",handoffId="";
    TaskCompletionSource<string>? handoffReady;
    Dictionary<string,string> resumeTickets=new();
    public async Task LeaveRoom()
    {
        string? next;
        lock(gate)next=IsHost?Current?.Players.FirstOrDefault(p=>p.Id!="host")?.Id:null;
        if(next!=null)await Promote(next,true);else Leave();
    }
    public async Task Kick(string id)
    {
        Connection connection;
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("Only the host can remove players.");
            if(transferring)throw new InvalidOperationException("Host transfer in progress.");
            if(id=="host"||!guests.TryRemove(id,out connection!))throw new InvalidOperationException("That player is no longer connected.");
            Current.Players.RemoveAll(p=>p.Id==id);resumeTickets.Remove(id);
        }
        try{await connection.Send(new(){Type="kicked"});}finally{connection.Dispose();await BroadcastState();}
    }
    public async Task Promote(string id,bool leaving=false)
    {
        Room snapshot;Connection successor;Dictionary<string,string> tickets;string address,oldId=Guid.NewGuid().ToString("N");
        lock(gate)
        {
            if(!IsHost||Current==null)throw new InvalidOperationException("Only the host can promote players.");
            if(transferring)throw new InvalidOperationException("Host transfer already in progress.");
            var target=Current.Players.FirstOrDefault(p=>p.Id==id&&id!="host")??throw new InvalidOperationException("Select a connected player.");
            if(!target.CanHostTransfer||!guests.TryGetValue(id,out successor!))throw new InvalidOperationException("The next player must update Fellowship and connect its VPN listener before becoming host.");
            if(Current.Players.Any(p=>!p.CanHostTransfer&&p.Id!="host"))throw new InvalidOperationException("All room members must update Fellowship before transferring the host.");
            address=target.Address;
            snapshot=JsonSerializer.Deserialize<Room>(JsonSerializer.Serialize(Current))!;
            snapshot.Host=target.Name;snapshot.HostAddress=address;snapshot.Phase="Gathering";
            if(leaving)snapshot.Players.RemoveAll(p=>p.Id=="host");else snapshot.Players.Single(p=>p.Id=="host").Id=oldId;
            snapshot.Players.Single(p=>p.Id==id).Id="host";
            tickets=snapshot.Players.Where(p=>p.Id!="host").ToDictionary(p=>p.Id,_=>Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            handoffTarget=id;handoffId=Guid.NewGuid().ToString("N");handoffReady=new(TaskCreationOptions.RunContinuationsAsynchronously);transferring=true;
        }
        bool sent=false;
        try
        {
            // Confirm that the successor's listening address is reachable before relinquishing ownership.
            await Query(address,Address);
            await successor.Send(new(){Type="become-host",RequestId=handoffId,Room=snapshot,Tickets=tickets,Token=recoveryToken});sent=true;
            string result;
            try{result=await handoffReady!.Task.WaitAsync(TimeSpan.FromSeconds(7));}
            catch(TimeoutException)
            {
                var probe=await Query(address,Address);
                if(probe?.Id!=snapshot.Id||probe.HostAddress!=address)throw new IOException("New host did not confirm the room.");
                result="";
            }
            if(result.Length>0){sent=false;throw new IOException(result);}
            // Query through the successor's visibility policy before showing its room.
            try
            {
                var visible=await QueryPeer(address,Address);
                HostTransferred?.Invoke(address,visible.Name,visible.Room);
            }
            catch(IOException){ /* Discovery can retry without disrupting the confirmed transfer. */ }
            catch(System.Net.Sockets.SocketException){ }
            catch(OperationCanceledException){ }
            var others=guests.Where(p=>p.Key!=id).ToArray();
            foreach(var pair in others)
            {
                try{await pair.Value.Send(new(){Type="redirect",Text=address,Id=pair.Key,Token=tickets[pair.Key]});}
                catch{Notice?.Invoke("A player disconnected during transfer and may need to rejoin.");}
            }
            Leave();
            if(!leaving)await ResumeRoom(address,oldId,tickets[oldId]);
            Notice?.Invoke(leaving?"Room handed to "+snapshot.Host:"Host is now "+snapshot.Host);
        }
        catch
        {
            // Once activation was sent, never keep advertising a second copy of the room.
            if(sent){Leave();Notice?.Invoke("Host transfer was interrupted. Rejoin the room at "+address+" if it is available.");}
            throw;
        }
        finally{transferring=false;handoffReady=null;handoffTarget="";handoffId="";}
    }
    async Task<bool> RecoverHost()
    {
        var room=Current;var token=recoveryToken;var localId=LocalId;
        if(stop.IsCancellationRequested||room==null||token.Length!=64||listener==null)return false;
        // One designated successor prevents competing elections among remaining guests.
        var successor=room.Players.FirstOrDefault(p=>p.Id!="host"&&p.CanHostTransfer);
        if(successor==null||settings.IsBlocked(successor.Address))return false;
        Notice?.Invoke("Host disconnected. Recovering room…");
        if(successor.Id==localId)
        {
            lock(gate)
            {
                if(Current!=room||stop.IsCancellationRequested)return true;
                room.Players.RemoveAll(p=>p.Id=="host");
                room.Players.Single(p=>p.Id==localId).Id="host";
                room.Host=settings.Name;room.HostAddress=Address;room.Phase="Gathering";
                IsHost=true;LocalId="host";
                resumeTickets=room.Players.Where(p=>p.Id!="host").ToDictionary(p=>p.Id,_=>token);
            }
            _=ExpireReservations(room.Id,resumeTickets);
            await BroadcastState();Notice?.Invoke("You are now the room host.");return true;
        }
        for(int attempt=0;attempt<10;attempt++)
        {
            if(Current!=room||stop.IsCancellationRequested)return true;
            try
            {
                var active=await Query(successor.Address,Address);
                if(active?.Id==room.Id&&active.HostAddress==successor.Address)
                {
                    if(Current!=room||stop.IsCancellationRequested)return true;
                    await ResumeRoom(successor.Address,localId,token);
                    Notice?.Invoke("Room reconnected to "+successor.Name);return true;
                }
            }
            catch(Exception error) when(error is IOException or System.Net.Sockets.SocketException or OperationCanceledException){}
            await Task.Delay(500);
        }
        return Current!=room;
    }
    async Task ResumeRoom(string address,string id,string token)
    {
        var connection=await Connect(address,string.IsNullOrEmpty(Address)?null:Address);
        try
        {
            await connection.Send(new(){Type="resume",Id=id,Token=token});
            using var timeout=new CancellationTokenSource(5000);var welcome=await connection.Read(timeout.Token);
            if(welcome?.Type!="welcome")throw new IOException(welcome?.Text??"New host did not respond.");
            LocalId=welcome.Id;upstream=connection;_ = ReceiveHost(connection);
        }
        catch{connection.Dispose();throw;}
    }
    async Task ExpireReservations(string roomId,Dictionary<string,string> reservation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20),stop.Token);
            lock(gate)
            {
                if(!IsHost||Current?.Id!=roomId||!ReferenceEquals(resumeTickets,reservation))return;
                Current.Players.RemoveAll(p=>resumeTickets.ContainsKey(p.Id));resumeTickets.Clear();
            }
            await BroadcastState();
        }
        catch(OperationCanceledException){}
    }
}

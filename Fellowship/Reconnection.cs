using System.IO;
using System.Net.Sockets;
using System.Text.Json;
namespace Fellowship;

public sealed partial class PeerService
{
    int sessionGeneration;
    CancellationTokenSource? reconnectStop;
    // The transport test can hold a partition open without changing the machine's network.
    Func<string,bool>? reconnectAllowed=null;
    readonly Dictionary<string,DateTimeOffset> reconnectDeadlines=new();
    Dictionary<string,string>? reservationWatch;
    sealed class ResumeRejectedException(string message):IOException(message);

    void BeginReconnect()
    {
        CancellationTokenSource cancellation;string roomId,address,id,token;int generation;
        lock(gate)
        {
            if(stop.IsCancellationRequested||Current==null||IsHost||IsReconnecting)return;
            IsReconnecting=true;roomId=Current.Id;address=Current.HostAddress;id=LocalId;token=recoveryToken;generation=sessionGeneration;
            cancellation=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);reconnectStop=cancellation;
        }
        foreach(var pending in placementRequests.Values)pending.TrySetResult("Reconnecting to room…");placementRequests.Clear();
        CancelChats("Reconnecting to room…");
        Changed?.Invoke();Notice?.Invoke("Connection lost. Reconnecting to the room…");
        _=ReconnectRoom(address,id,token,roomId,generation,cancellation);
    }
    async Task ReconnectRoom(string address,string id,string token,string roomId,int generation,CancellationTokenSource cancellation)
    {
        try
        {
            while(!cancellation.IsCancellationRequested)
            {
                lock(gate)if(sessionGeneration!=generation||Current?.Id!=roomId||IsHost)return;
                try
                {
                    if(reconnectAllowed?.Invoke(address)!=false)
                    {
                        await ResumeRoom(address,id,token,roomId,generation,cancellation.Token);
                        Notice?.Invoke("Room connection restored.");return;
                    }
                }
                catch(ResumeRejectedException error)
                {
                    lock(gate)
                    {
                        if(sessionGeneration!=generation||Current?.Id!=roomId)return;
                        Leave();
                    }
                    Notice?.Invoke(error.Message);return;
                }
                catch(Exception error) when(error is IOException or SocketException or OperationCanceledException or ObjectDisposedException or JsonException){}
                await Task.Delay(1000,cancellation.Token);
            }
        }
        catch(OperationCanceledException){}
        finally
        {
            lock(gate)if(ReferenceEquals(reconnectStop,cancellation))reconnectStop=null;
            cancellation.Dispose();
        }
    }
    async Task ResumeRoom(string address,string id,string token,string roomId,int generation,CancellationToken cancellation)
    {
        if(settings.IsBlocked(address))throw new ResumeRejectedException("Unblock this player before joining.");
        var connection=await Connect(address,string.IsNullOrEmpty(Address)?null:Address,AllowsPeer);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            lock(gate)if(sessionGeneration!=generation||IsHost||Current!=null&&Current.Id!=roomId)throw new OperationCanceledException();
            await connection.Send(new(){Type="resume",RoomId=roomId,Id=id,Token=token});
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(5000);
            var welcome=await connection.Read(timeout.Token);
            if(welcome?.Id=="resume-rejected")throw new ResumeRejectedException(welcome.Text);
            if(welcome?.Type!="welcome"||welcome.Id!=id)throw new IOException(welcome?.Text??"New host did not respond.");
            // A welcome alone is not enough to resume lobby synchronization. Only the
            // owner's current snapshot can replace our cached selections and history.
            Wire? state;
            var buffered=new List<Wire>();
            do
            {
                state=await connection.Read(timeout.Token);
                if(state?.Type is "chat" or "chat-result")
                {
                    if(buffered.Count>=64)throw new IOException("Invalid peer response.");
                    buffered.Add(state);
                }
            }while(state?.Type!="room"&&state?.Type!="kicked"&&state!=null);
            if(state?.Type!="room"||state.Room is not {} room||room.Id!=roomId||!room.Players.Any(p=>p.Id==id&&p.Address==Address)||!AllowsRoom(room))
                throw new IOException("The host did not finish sending the room. Please retry.");
            lock(gate)
            {
                cancellation.ThrowIfCancellationRequested();
                if(sessionGeneration!=generation||IsHost||Current!=null&&Current.Id!=roomId)throw new OperationCanceledException();
                if(settings.IsBlocked(address)||!AllowsRoom(room))throw new ResumeRejectedException("This peer is unavailable.");
                Current=room;Current.HostAddress=address;LocalId=id;recoveryToken=state.Token;upstream=connection;upstreamChatAck=welcome.SupportsChatAck;IsReconnecting=false;
            }
            Changed?.Invoke();
            foreach(var message in buffered)
            {
                if(message.Type=="chat")DeliverRoomChat(message.Text,message.Id,message.RequestId,connection);
                else lock(gate)if(ReferenceEquals(upstream,connection)&&chatRequests.TryRemove(message.RequestId,out var sent))sent.TrySetResult("");
            }
            _=ReceiveHost(connection);
        }
        catch{connection.Dispose();throw;}
    }
    // Called with gate held; membership remains authoritative at the original owner.
    void ReservePlayer(string id)
    {
        if(Current?.Players.FirstOrDefault(p=>p.Id==id) is not {} player)return;
        player.Reconnecting=true;
        if(!resumeTickets.ContainsKey(id))resumeTickets[id]=recoveryToken;
        reconnectDeadlines[id]=DateTimeOffset.UtcNow.AddSeconds(120);
        _=ExpireReservations(Current.Id,resumeTickets);
    }
    async Task ExpireReservations(string roomId,Dictionary<string,string> reservation)
    {
        lock(gate)
        {
            if(ReferenceEquals(reservationWatch,reservation))return;
            reservationWatch=reservation;
        }
        try
        {
            while(true)
            {
                await Task.Delay(1000,stop.Token);
                bool changed=false;
                lock(gate)
                {
                    if(!IsHost||Current?.Id!=roomId||!ReferenceEquals(resumeTickets,reservation))return;
                    foreach(var id in reconnectDeadlines.Where(p=>p.Value<=DateTimeOffset.UtcNow).Select(p=>p.Key).ToArray())
                    {
                        Current.Players.RemoveAll(p=>p.Id==id);resumeTickets.Remove(id);reconnectDeadlines.Remove(id);changed=true;
                    }
                }
                if(changed)await BroadcastState();
            }
        }
        catch(OperationCanceledException){}
        finally{lock(gate)if(ReferenceEquals(reservationWatch,reservation))reservationWatch=null;}
    }
}

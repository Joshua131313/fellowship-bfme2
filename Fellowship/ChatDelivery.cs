using System.Collections.Concurrent;
using System.IO;
namespace Fellowship;

public sealed partial class PeerService
{
    readonly ConcurrentDictionary<string,TaskCompletionSource<string>> chatRequests=new();
    readonly HashSet<string> receivedMessages=new();
    readonly Queue<string> messageOrder=new();
    bool RememberMessage(string scope,string id)
    {
        if(id.Length==0)return true; // Older clients do not send correlation IDs.
        if(id.Length>128)throw new InvalidDataException("Invalid message size.");
        lock(gate)
        {
            string key=scope+"|"+id;
            if(!receivedMessages.Add(key))return false;
            messageOrder.Enqueue(key);
            while(messageOrder.Count>1024)receivedMessages.Remove(messageOrder.Dequeue());
            return true;
        }
    }
    void CancelChats(string reason)
    {
        lock(gate){foreach(var pending in chatRequests.Values)pending.TrySetResult(reason);chatRequests.Clear();}
    }
    public async Task Say(string text,string messageId="")
    {
        if(messageId.Length==0)messageId=Guid.NewGuid().ToString("N");
        Connection? connection;string roomId;bool host,ack;
        var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(gate)
        {
            if(IsReconnecting)throw new IOException("Reconnecting to room…");
            if(Current==null)throw new IOException("You are not connected to a room.");
            roomId=Current.Id;host=IsHost;connection=upstream;ack=upstreamChatAck;
            if(!host&&connection==null)throw new IOException("The room connection is unavailable.");
            if(!host&&ack&&!chatRequests.TryAdd(messageId,completion))throw new IOException("Sending…");
        }
        if(host){await SendRoomChat(settings.Name,text,"host",messageId,roomId);return;}
        try
        {
            var sending=connection!.Send(new(){Type="chat",Text=Clean(text,2000),RequestId=messageId});
            // An older host does not return correlation IDs. Keep its established
            // send behavior; the UI reconciles its legacy own-message echo.
            if(!ack){await sending;return;}
            if(await Task.WhenAny(sending,completion.Task)==completion.Task)
            {
                _=ObserveSend(sending);
                string earlyFailure=await completion.Task;
                if(earlyFailure.Length>0)throw new IOException(earlyFailure);
                return;
            }
            await sending;
            string failure=await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if(failure.Length>0)throw new IOException(failure);
        }
        catch(TimeoutException){throw new IOException("Message was not acknowledged.");}
        finally{((ICollection<KeyValuePair<string,TaskCompletionSource<string>>>)chatRequests).Remove(new(messageId,completion));}
    }
    static async Task ObserveSend(Task sending){try{await sending;}catch{}}
    async Task SendRoomChat(string name,string text,string id="host",string messageId="",string expectedRoom="",Connection? source=null)
    {
        Connection[] recipients;string line=$"{name}   {Clean(text,2000)}";
        lock(gate)
        {
            if(!IsHost||Current==null||expectedRoom.Length>0&&Current.Id!=expectedRoom||source!=null&&(!guests.TryGetValue(id,out var active)||!ReferenceEquals(active,source)))throw new IOException("Disconnected from room.");
            string sender=Current.Players.FirstOrDefault(p=>p.Id==id)?.Address??id;
            // A promoted host may already have received this message as a guest.
            if(messageId.Length>0&&receivedMessages.Contains("room-received|"+Current.Id+"|"+sender+"|"+messageId))return;
            if(!RememberMessage("room-send|"+Current.Id+"|"+sender,messageId))return;
            recipients=guests.Values.Where(c=>c.Welcomed).ToArray();
            DeliverRoomChat(line,id,messageId);
        }
        var message=new Wire{Type="chat",Text=line,Id=id,RequestId=messageId};
        await Task.WhenAll(recipients.Select(async c=>{try{await c.Send(message);}catch{c.Dispose();}}));
    }
    void DeliverRoomChat(string text,string id,string messageId="",Connection? expected=null)
    {
        lock(gate)
        {
            if(expected!=null&&!ReferenceEquals(upstream,expected))return;
            bool mine=id==LocalId;
            if(mine&&chatRequests.TryRemove(messageId,out var sent))sent.TrySetResult("");
            var sender=Current?.Players.FirstOrDefault(p=>p.Id==id);
            if(!RememberMessage("room-received|"+Current?.Id+"|"+(sender?.Address??id),messageId))return;
            bool blocked=sender!=null?settings.IsBlocked(sender.Address):Current?.Players.Any(p=>settings.IsBlocked(p.Address)&&text.StartsWith(p.Name+"   ",StringComparison.Ordinal))==true;
            RoomChatReceived?.Invoke(text,blocked,mine,messageId);
            RoomChat?.Invoke(text,blocked,mine);
            if(!blocked&&!mine)RoomMessageReceived?.Invoke();
            if(!blocked)Chat?.Invoke(text);
        }
    }
}

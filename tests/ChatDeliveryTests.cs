using Fellowship;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

static class ChatDeliveryTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed record Received(string Text,bool Blocked,bool Mine,string Id);
    static Settings SettingsFor(string name){var settings=Settings.InMemory();settings.Name=name;return settings;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object Upstream(PeerService peer)=>typeof(PeerService).GetField("upstream",Hidden)!.GetValue(peer)??throw new Exception("Chat fixture has no upstream");
    static object HostConnection(PeerService host,string id)
    {
        object guests=typeof(PeerService).GetField("guests",Hidden)!.GetValue(host)!;
        return guests.GetType().GetProperty("Item")!.GetValue(guests,[id])!;
    }
    static SemaphoreSlim Writes(object connection)=>(SemaphoreSlim)connection.GetType().GetField("writes",Hidden)!.GetValue(connection)!;
    static void HoldRecovery(PeerService peer)=>typeof(PeerService).GetField("reconnectAllowed",Hidden)!.SetValue(peer,(Func<string,bool>)(_=>false));
    static async Task Until(Func<bool> condition,string description)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(8);
        do{if(condition()){Console.WriteLine("PASS "+description);return;}await Task.Delay(20);}while(DateTimeOffset.UtcNow<deadline);
        throw new Exception("FAIL "+description);
    }
    static async Task Failed(Task action,string expected,string description)
    {
        try{await action.WaitAsync(TimeSpan.FromSeconds(3));}
        catch(IOException error)when(error.Message.Contains(expected,StringComparison.OrdinalIgnoreCase)){Console.WriteLine("PASS "+description);return;}
        throw new Exception("FAIL "+description);
    }
    static ConcurrentQueue<Received> Watch(PeerService peer)
    {
        var received=new ConcurrentQueue<Received>();
        peer.RoomChatReceived+=(text,blocked,mine,id)=>received.Enqueue(new(text,blocked,mine,id));return received;
    }
    static int Count(ConcurrentQueue<Received> received,string id)=>received.Count(item=>item.Id==id);

    public static async Task Run()
    {
        await RoomAcknowledgementsAndRetries();
        await OlderHostUsesWriteCompletion();
        await PrivateRetries();
        await PendingSendInterrupted(false);
        await PendingSendInterrupted(true);
        await QueuedSendInterrupted(false);
        await QueuedSendInterrupted(true);
        using(var peer=new PeerService(SettingsFor("Outside room")))await Failed(peer.Say("Cannot send outside a room"),"not connected","room message outside a room fails explicitly");
        Console.WriteLine("PASS acknowledged room chat, correlated events, duplicate-safe retries, private delivery and interrupted sends");
    }
    static async Task RoomAcknowledgementsAndRetries()
    {
        using var host=new PeerService(SettingsFor("Chat owner"));using var sender=new PeerService(SettingsFor("Chat sender"));using var recipient=new PeerService(SettingsFor("Other recipient"));
        host.Start("127.0.0.211");sender.Start("127.0.0.212");recipient.Start("127.0.0.213");host.Host(new(){Title="Acknowledged chat",Capacity=3});
        await sender.Join(host.Address);await recipient.Join(host.Address);
        var hostMessages=Watch(host);var senderMessages=Watch(sender);var recipientMessages=Watch(recipient);
        const string first="room-message-one",body="The same message body is valid more than once.";
        var writes=Writes(HostConnection(host,sender.LocalId));await writes.WaitAsync();
        Task send;
        try
        {
            send=sender.Say(body,first);
            await Until(()=>Count(hostMessages,first)==1,"host receives a pending correlated room message");
            Check(!send.IsCompleted&&Count(senderMessages,first)==0,"Room send reported success before the host's acknowledgement or echo reached the sender");
            await Failed(sender.Say(body,first),"Sending","concurrent retry of a pending message does not create a second send");
        }
        finally{writes.Release();}
        await send.WaitAsync(TimeSpan.FromSeconds(3));
        await Until(()=>Count(senderMessages,first)==1&&Count(recipientMessages,first)==1,"acknowledged room message retains its ID on every recipient");
        Check(senderMessages.Single(item=>item.Id==first) is{Mine:true,Blocked:false},"Sender's confirmed echo lost its own-message status");
        Check(hostMessages.Single(item=>item.Id==first) is{Mine:false,Blocked:false}&&recipientMessages.Single(item=>item.Id==first) is{Mine:false,Blocked:false},"Remote room message was marked as the recipient's own message");
        Check(senderMessages.Single(item=>item.Id==first).Text.Contains(body,StringComparison.Ordinal),"Confirmed room echo lost its body");
        await sender.Say(body,first).WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        Check(Count(hostMessages,first)==1&&Count(senderMessages,first)==1&&Count(recipientMessages,first)==1,"Retrying an acknowledged room message created a duplicate bubble");

        const string second="room-message-two";await sender.Say(body,second);
        await Until(()=>Count(hostMessages,second)==1&&Count(senderMessages,second)==1&&Count(recipientMessages,second)==1,"new message ID delivers an intentionally repeated room message body");
        const string hostId="host-message-one";await host.Say("A host message",hostId);await host.Say("A host message",hostId);
        await Until(()=>Count(senderMessages,hostId)==1&&Count(recipientMessages,hostId)==1,"host retries deliver only one remote room message");
        Check(Count(hostMessages,hostId)==1&&hostMessages.Single(item=>item.Id==hostId).Mine,"Host's own message was duplicated or lost its own-message status");

        // Request IDs are scoped to a sender; different users may generate the
        // same ID without suppressing a legitimate message.
        await recipient.Say("Another sender using the same ID",first);
        await Until(()=>Count(hostMessages,first)==2&&Count(senderMessages,first)==2&&Count(recipientMessages,first)==2,"room deduplication separates different senders using the same request ID");
    }
    static async Task PrivateRetries()
    {
        using var first=new PeerService(SettingsFor("Private sender"));using var second=new PeerService(SettingsFor("Other private sender"));using var recipient=new PeerService(SettingsFor("Private recipient"));
        first.Start("127.0.0.214");second.Start("127.0.0.215");recipient.Start("127.0.0.216");
        var received=new ConcurrentQueue<(string Address,string Name,string Text)>();recipient.PrivateMessage+=(address,name,text)=>received.Enqueue((address,name,text));
        const string id="private-message-one",body="Repeated private content";
        await first.SendPrivate(recipient.Address,body,id);await first.SendPrivate(recipient.Address,body,id);
        Check(received.Count==1&&received.Single()==(first.Address,"Private sender",body),"Acknowledged private-message retry duplicated or changed the recipient's message");
        await first.SendPrivate(recipient.Address,body,"private-message-two");
        Check(received.Count==2,"A different private-message ID with the same body was incorrectly suppressed");
        await second.SendPrivate(recipient.Address,body,id);
        Check(received.Count==3&&received.Count(item=>item.Address==second.Address)==1,"Private-message deduplication suppressed another sender with the same ID");
        Console.WriteLine("PASS private retry acknowledgement suppresses duplicates while preserving new IDs and independent senders");
    }
    static async Task OlderHostUsesWriteCompletion()
    {
        using var host=new PeerService(SettingsFor("Legacy owner"));using var guest=new PeerService(SettingsFor("Legacy guest"));
        host.Start("127.0.0.221");guest.Start("127.0.0.222");host.Host(new(){Capacity=2});await guest.Join(host.Address);
        typeof(PeerService).GetField("upstreamChatAck",Hidden)!.SetValue(guest,false);
        var hostMessages=Watch(host);var guestMessages=Watch(guest);const string id="legacy-write-completion";
        var writes=Writes(HostConnection(host,guest.LocalId));await writes.WaitAsync();
        try
        {
            await guest.Say("Compatibility message",id).WaitAsync(TimeSpan.FromSeconds(3));
            await Until(()=>Count(hostMessages,id)==1,"legacy host receives the message without acknowledgement capability");
            Check(Count(guestMessages,id)==0,"Legacy fixture unexpectedly received an echo while host writes were held");
            Check(PendingCount(guest)==0,"Legacy send retained a correlation entry while waiting for an unsupported acknowledgement");
            Console.WriteLine("PASS legacy host send completes after writing without waiting for an acknowledgement or echo");
        }
        finally{writes.Release();}
        await Until(()=>Count(guestMessages,id)==1,"legacy compatibility send still receives its eventual own-message echo");
    }
    static int PendingCount(PeerService peer)
    {
        object requests=typeof(PeerService).GetField("chatRequests",Hidden)!.GetValue(peer)!;
        return (int)requests.GetType().GetProperty("Count")!.GetValue(requests)!;
    }
    static async Task QueuedSendInterrupted(bool reconnect)
    {
        using var host=new PeerService(SettingsFor("Queued-send owner"));using var guest=new PeerService(SettingsFor("Queued sender"));
        host.Start(reconnect?"127.0.0.225":"127.0.0.223");guest.Start(reconnect?"127.0.0.226":"127.0.0.224");host.Host(new(){Capacity=2});await guest.Join(host.Address);
        var hostMessages=Watch(host);var guestMessages=Watch(guest);string id=reconnect?"queued-reconnect":"queued-leave";
        object connection=Upstream(guest);var writes=Writes(connection);await writes.WaitAsync();
        try
        {
            Task send=guest.Say("Still queued before the socket write",id);
            await Until(()=>PendingCount(guest)==1,"queued socket send retains its pending acknowledgement entry");
            Check(!send.IsCompleted&&Count(hostMessages,id)==0,"Queued guest write unexpectedly completed or reached the host");
            if(reconnect){HoldRecovery(guest);((IDisposable)connection).Dispose();await Until(()=>guest.IsReconnecting,"queued guest transport loss enters reconnecting state");}
            else guest.Leave();
            await Failed(send,reconnect?"Reconnecting":"Disconnected",reconnect?"queued socket write fails promptly when reconnecting":"queued socket write fails promptly when leaving");
            Check(PendingCount(guest)==0&&Count(guestMessages,id)==0,"Cancelled queued send retained a pending entry or emitted a confirmed local echo");
        }
        finally{writes.Release();}
    }
    static async Task PendingSendInterrupted(bool reconnect)
    {
        using var host=new PeerService(SettingsFor("Interrupted-send owner"));using var guest=new PeerService(SettingsFor("Interrupted sender"));
        host.Start(reconnect?"127.0.0.219":"127.0.0.217");guest.Start(reconnect?"127.0.0.220":"127.0.0.218");host.Host(new(){Capacity=2});await guest.Join(host.Address);
        var hostMessages=Watch(host);var guestMessages=Watch(guest);string id=reconnect?"interrupted-reconnect":"interrupted-leave";
        var writes=Writes(HostConnection(host,guest.LocalId));await writes.WaitAsync();
        try
        {
            Task send=guest.Say("Awaiting confirmation",id);
            await Until(()=>Count(hostMessages,id)==1,"interrupted message reaches host before its acknowledgement");
            Check(!send.IsCompleted&&Count(guestMessages,id)==0,"Pending message appeared confirmed without host feedback");
            if(reconnect){HoldRecovery(guest);((IDisposable)Upstream(guest)).Dispose();await Until(()=>guest.IsReconnecting,"chat transport loss enters reconnecting state");}
            else guest.Leave();
            await Failed(send,reconnect?"Reconnecting":"Disconnected",reconnect?"transport loss promptly fails pending room send":"explicit leave promptly fails pending room send");
            Check(Count(guestMessages,id)==0,"Interrupted pending message was emitted as a confirmed local echo");
            Check(PendingCount(guest)==0,"Interrupted send left a pending correlation entry");
            if(reconnect)await Failed(guest.Say("Cannot send while reconnecting"),"Reconnecting","new room send is rejected while the room is reconnecting");
        }
        finally{writes.Release();}
    }
}

using Fellowship;
using System.IO;

static class RadminConnectionTests
{
    public static void Run()
    {
        foreach(var caption in new[]{"Disable Network","Désactiver le réseau","Desactivar red","تعطيل الشبكة","Ağı devre dışı bırak"})
            if(RadminNetworks.ConnectionCommand(caption)!=true)throw new Exception("Enabled-state command translation was not recognized");
        foreach(var caption in new[]{"&Enable Network","Activer le réseau","Activar red","تفعيل الشبكة","Ağı etkinleştir"})
            if(RadminNetworks.ConnectionCommand(caption)!=false)throw new Exception("Disabled-state command translation was not recognized");
        foreach(var caption in new[]{"Leave Network","Delete Network","Supprimer le réseau","Quitter le réseau","Properties","Disable","","Open Chat"})
            if(RadminNetworks.ConnectionCommand(caption)!=null)throw new Exception("An unrelated native Radmin action was accepted");
        string[] names=["A","B","C"];
        var states=new Dictionary<string,bool>{{"A",false},{"B",true},{"C",true}};
        var writes=new List<(string,bool)>();
        bool Read(string group)=>states[group];
        void Set(string group,bool enabled){writes.Add((group,enabled));states[group]=enabled;}
        var result=RadminNetworks.ApplyConnections("A",names,Read,Set);
        if(!result.Complete||writes[0]!=("A",true)||states["B"]||states["C"]||!states["A"])throw new Exception("Exclusive selection did not connect the target before disabling other groups");
        writes.Clear();RadminNetworks.ApplyConnections("A",names,Read,Set);
        if(writes.Count!=0)throw new Exception("Re-selecting an exclusive group changed correct connections");
        RadminNetworks.ApplyConnections("B",names,Read,Set);
        if(states["A"]||!states["B"]||states["C"])throw new Exception("Switching from one exclusive group to another failed");
        RadminNetworks.ApplyConnections("",names,Read,Set);
        if(states.Values.Any(v=>!v))throw new Exception("All Radmin groups did not reconnect disabled networks");
        writes.Clear();
        foreach(var invalid in new[]{new[]{"A","A"},Array.Empty<string>()})
        {try{RadminNetworks.ApplyConnections("A",invalid,Read,Set);throw new Exception("Invalid group list accepted");}catch(IOException){}}
        try{RadminNetworks.ApplyConnections("Missing",names,Read,Set);throw new Exception("Missing group accepted");}catch(IOException){}
        if(writes.Count!=0)throw new Exception("Invalid selection changed network connections");
        try{RadminNetworks.ApplyConnections("A",names,g=>g=="C"?throw new IOException("Unreadable native menu"):Read(g),Set);throw new Exception("Preflight failure ignored");}catch(IOException){}
        if(writes.Count!=0)throw new Exception("A preflight read failure changed network connections");
        states["A"]=false;
        bool fail=true;
        void FailingSet(string group,bool enabled)
        {Set(group,enabled);if(group=="B"&&!enabled&&fail){fail=false;throw new IOException("Native command failed after changing state");}}
        try{RadminNetworks.ApplyConnections("A",names,Read,FailingSet);throw new Exception("Failed mutation accepted");}
        catch(IOException error){if(!error.Message.Contains("restored"))throw;}
        if(states["A"]||!states["B"]||!states["C"])throw new Exception("Failed mutation did not restore the exact previous states");
        try{RadminNetworks.ApplyConnections("A",names,Read,(_,_)=>{});throw new Exception("Unapplied native command was treated as success");}catch(IOException){}
        if(states["A"]||!states["B"]||!states["C"])throw new Exception("Verification failure changed original connections");
        Console.WriteLine("PASS exclusive Radmin connection selection, target-first ordering, All reconnect, idempotence, missing/duplicate groups, preflight failure, rollback, verification, localized action allowlist and rejection of Leave/Delete");
    }
}

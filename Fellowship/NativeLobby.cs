using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Fellowship;

// Read-only adapter for the exact BFME2 1.00 binary validated by HostPreset.
// Selection changes must go through the game's UI so its LAN messages are sent.
public sealed class NativeLobby : IDisposable
{
    public sealed record Slot(int Row,int State,int Spot,int Team,bool Local);
    public sealed record Snapshot(uint Identity,string Map,string HostAddress,List<Slot> Slots)
    {
        public Slot Local => Slots.Single(s=>s.Local);
    }
    readonly SafeProcessHandle handle;
    readonly long imageBase;
    public NativeLobby(int pid)
    {
        using var process=Process.GetProcessById(pid);
        var module=process.MainModule??throw new IOException("Cannot inspect BFME2.");
        HostPreset.ValidateBinary(Path.Combine(Path.GetDirectoryName(module.FileName)!,"lotrbfme2.exe"));
        imageBase=module.BaseAddress.ToInt64();
        handle=OpenProcess(0x410,false,pid);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    byte[] Read(long address,int count)
    {
        var data=new byte[count];
        if(!ReadProcessMemory(handle,(nint)address,data,(nuint)count,out var got)||got!=(nuint)count)
            throw new IOException("Cannot read the BFME2 LAN lobby. Continue manually.");
        return data;
    }
    public Snapshot? Current() => Decode(Read,imageBase);
    public static Snapshot? Decode(Func<long,int,byte[]> read,long imageBase)
    {
        long Address(int va)=>imageBase+va-0x400000;
        uint U(long a)=>BitConverter.ToUInt32(read(a,4));
        int I(long a)=>BitConverter.ToInt32(read(a,4));
        void Require(bool value){if(!value)throw new InvalidDataException("Unrecognized BFME2 LAN state. Continue manually.");}
        uint network=U(Address(0xdf9800));
        if(network==0)return null;
        Require(U(network)==Address(0xc3afb8));
        uint lobby=U(network+0x44);
        if(lobby==0)return null;
        Require(U(lobby)==Address(0xc3ae60));
        if(read(lobby+0x10,1)[0]==0)return null;
        var local=read(network+0x48,6);
        var mapPointer=U(lobby+0x40);
        Require(mapPointer!=0);
        int length=BitConverter.ToUInt16(read(mapPointer+4,2));
        Require(length is >0 and <512);
        string map=Encoding.ASCII.GetString(read(mapPointer+8,length));
        Require(map.StartsWith("maps\\",StringComparison.OrdinalIgnoreCase)&&map.EndsWith(".map",StringComparison.OrdinalIgnoreCase));
        var slots=new List<Slot>();
        for(int row=0;row<8;row++)
        {
            uint slot=U(lobby+0x18+4*row);
            Require(slot==lobby+0xb0+0x1d0*row);
            int state=I(slot+4),spot=I(slot+0x10),team=I(slot+0x1c);
            Require(state is >=0 and <=6&&spot is >=-1 and <=8&&team is >=-1 and <=3);
            var address=read(slot+0x38,6);
            bool isLocal=state==6&&address.Take(4).SequenceEqual(local.Take(4))&&
                (BitConverter.ToUInt16(address,4)==BitConverter.ToUInt16(local,4)||
                 BitConverter.ToUInt16(address,4)==unchecked((ushort)(BitConverter.ToUInt16(local,4)+8)));
            slots.Add(new(row,state,spot,team,isLocal));
        }
        if(slots.Count(s=>s.Local)!=1)return null;
        var hostIp=read(lobby+0xb0+0x38,4);Array.Reverse(hostIp);
        return new(lobby,map,new System.Net.IPAddress(hostIp).ToString(),slots);
    }
    public void Dispose()=>handle.Dispose();
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint size,out nuint read);
}

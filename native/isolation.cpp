#include "bridge.h"
// Game-only IPv4 UDP routing. No system firewall rules or VPN settings change.
API BOOL W VirtualProtect(void*,U32,U32,U32*);
API BOOL W GetModuleHandleExW(U32,const wchar_t*,HMODULE*);
API void W AcquireSRWLockShared(void*);API void W ReleaseSRWLockShared(void*);
API void W AcquireSRWLockExclusive(void*);API void W ReleaseSRWLockExclusive(void*);
typedef int(W* SendTo)(U32,const char*,int,int,const void*,int);
typedef int(W* RecvFrom)(U32,char*,int,int,void*,int*);
typedef void(W* SetError)(int);
struct Address { U16 family,port;U32 ip;U8 zero[8]; };
static SendTo originalSend;static RecvFrom originalReceive;static SetError setError;
static void* policyLock;static bool installed,enabled;static int peerCount;static U32 peers[64],broadcast;
static bool Allowed(U32 ip){for(int i=0;i<peerCount;i++)if(peers[i]==ip)return true;return false;}
static int W RoomSend(U32 socket,const char* data,int size,int flags,const void* destination,int length){
 AcquireSRWLockShared(&policyLock);
 if(!enabled){ReleaseSRWLockShared(&policyLock);return originalSend(socket,data,size,flags,destination,length);}
 if(!destination||length<16||((const Address*)destination)->family!=2){ReleaseSRWLockShared(&policyLock);setError(10013);return -1;}
 Address target;Copy(&target,destination,16);
 if(target.ip==0xffffffff||target.ip==broadcast){
  // BFME2's discovery payload is unchanged; only the destination changes.
  for(int i=0;i<peerCount;i++){target.ip=peers[i];originalSend(socket,data,size,flags,&target,16);}
  ReleaseSRWLockShared(&policyLock);return size;
 }
 bool allowed=Allowed(target.ip);ReleaseSRWLockShared(&policyLock);
 return allowed?originalSend(socket,data,size,flags,destination,length):size;
}
static int W RoomReceive(U32 socket,char* data,int size,int flags,void* source,int* sourceLength){
 AcquireSRWLockShared(&policyLock);bool active=enabled;ReleaseSRWLockShared(&policyLock);
 if(!active)return originalReceive(socket,data,size,flags,source,sourceLength);
 Address sender;int length=16;
 // Consume one packet, including a disallowed MSG_PEEK packet, without blocking
 // in a drain loop. BFME2 treats WSAEWOULDBLOCK as an empty receive poll.
 int result=originalReceive(socket,data,size,flags,&sender,&length);
 if(result<0)return result;
 AcquireSRWLockShared(&policyLock);bool allowed=!enabled||(length>=16&&sender.family==2&&Allowed(sender.ip));ReleaseSRWLockShared(&policyLock);
 if(!allowed){if(flags&2){length=16;originalReceive(socket,data,size,flags&~2,&sender,&length);}setError(10035);return -1;}
 if(source&&sourceLength){if(*sourceLength<length){setError(10014);return -1;}Copy(source,&sender,length);*sourceLength=length;}
 return result;
}
static bool Patch(U32 address,void* function){U32 old;if(!VirtualProtect((void*)address,4,4,&old))return false;*(void* volatile*)address=function;U32 ignored;VirtualProtect((void*)address,4,old,&ignored);return true;}
int ConfigureIsolation(Request* r){
 if(r->isolate<0||r->isolate>1||r->peerCount<0||r->peerCount>64)return -30;
 for(int i=0;i<r->peerCount;i++)if(!r->peers[i]||r->peers[i]==0xffffffff)return -30;
 AcquireSRWLockExclusive(&policyLock);
 if(r->isolate&&!installed){
  HMODULE winsock=GetModuleHandleW(L"WSOCK32.dll");
  originalSend=(SendTo)GetProcAddress(winsock,"sendto");originalReceive=(RecvFrom)GetProcAddress(winsock,"recvfrom");setError=(SetError)GetProcAddress(winsock,"WSASetLastError");
  if(!originalSend||!originalReceive||!setError||*(SendTo*)0xbb7934!=originalSend||*(RecvFrom*)0xbb7988!=originalReceive){ReleaseSRWLockExclusive(&policyLock);return -31;}
  // IAT targets must outlive SetWindowsHookEx: pin until BFME2 exits.
  HMODULE pin;if(!GetModuleHandleExW(5,(const wchar_t*)&RoomSend,&pin)){ReleaseSRWLockExclusive(&policyLock);return -31;}
  if(!Patch(0xbb7934,(void*)&RoomSend)){ReleaseSRWLockExclusive(&policyLock);return -31;}
  if(!Patch(0xbb7988,(void*)&RoomReceive)){Patch(0xbb7934,(void*)originalSend);ReleaseSRWLockExclusive(&policyLock);return -31;}
  installed=true;
 }
 peerCount=r->peerCount;Copy(peers,r->peers,peerCount*4);broadcast=r->broadcast;enabled=r->isolate!=0;
 ReleaseSRWLockExclusive(&policyLock);return 4;
}

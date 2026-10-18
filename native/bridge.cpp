#include "bridge.h"
static bool Readable(U32 address,U32 size){MBI m;if(!address||!VirtualQuery((void*)address,&m,sizeof(m)))return false;return m.State==0x1000&&!(m.Protect&(0x100|1))&&address+size>=address&&address+size<=(U32)m.Base+m.RegionSize;}
static U32 Value(U32 address){return *(U32*)address;}
static int Apply(Request* r){
 if(r->magic!=0x46424d32||r->version!=1||r->pid!=GetCurrentProcessId()||r->row<0||r->row>=8||r->team< -1||r->team>3||r->spot< -1||r->spot>7||r->readOnly<0||r->readOnly>1)return -1;
 if((U32)GetModuleHandleW(0)!=0x400000)return -2;
 if(!Readable(0xdf9800,4)||!Readable(0xdfe1f4,4))return -2;
 U32 network=Value(0xdf9800),setup=Value(0xdfe1f4);
 if(!Readable(network,0x50)||Value(network)!=0xc3afb8||!Readable(setup,0x3b0))return -3;
 U32 lobby=Value(network+0x44),adapter=Value(setup+0x58);
 if(lobby!=r->lobby||!Readable(lobby,0xf40)||Value(lobby)!=0xc3ae60||!*(U8*)(lobby+0x10))return -4;
 if(!Readable(adapter,4)||Value(adapter)!=0xc3a9a8||adapter+0xc!=setup||Value(0xc3a9a8+0x30)!=0x84361b||Value(0xc3a9a8+0x34)!=0x843374)return -5;
 // A pending start/close transition is not an editable setup session.
 if(*(U8*)(setup+0x2c2)||*(U8*)(setup+0x2c3))return -6;
 U32 map=Value(lobby+0x40);if(!Readable(map,8))return -7;U16 len=*(U16*)(map+4);
 if(!len||len>=512||!Readable(map+8,len)||r->map[len]!=0||!Equal((void*)(map+8),r->map,len))return -7;
 U32 slot=Value(lobby+0x18+r->row*4);
 if(slot!=lobby+0xb0+0x1d0*r->row||Value(slot+4)!=6||!Equal((void*)(slot+0x38),(void*)(network+0x48),4))return -8;
 U16 localPort=*(U16*)(network+0x4c),port=*(U16*)(slot+0x3c);if(port!=localPort&&port!=(U16)(localPort+8))return -8;
 for(int row=0;row<8;row++){U32 other=Value(lobby+0x18+row*4);if(other!=lobby+0xb0+0x1d0*row)return -4;if(row!=r->row&&Value(other+4)>1&&r->spot>=0&&(int)Value(other+0x10)==r->spot)return -9;}
 typedef bool(__thiscall* ValidSpot)(void*,int);
 if(r->spot>=0&&!((ValidSpot)0x83c791)((void*)setup,r->spot))return -10;
 r->beforeTeam=(int)Value(slot+0x1c);r->beforeSpot=(int)Value(slot+0x10);r->thread=GetCurrentThreadId();
 if(!r->readOnly){
  typedef bool(__thiscall* Setter)(void*,void*,int);
  if(r->team!=r->beforeTeam&&!((Setter)0x843374)((void*)adapter,(void*)slot,r->team))return -11;
  if(r->spot!=r->beforeSpot&&!((Setter)0x84361b)((void*)adapter,(void*)slot,r->spot))return -12;
  typedef void(__thiscall* Refresh)(void*);((Refresh)0x83c5e6)((void*)setup);
 }
 r->afterTeam=(int)Value(slot+0x1c);r->afterSpot=(int)Value(slot+0x10);
 return r->readOnly?1:(r->afterTeam==r->team&&r->afterSpot==r->spot?2:-13);
}
extern "C" __declspec(dllexport) I32 W PlacementHook(int code,U32 w,I32 l){
 if(code>=0){CWP* msg=(CWP*)l;if(msg->message==MessageId()){
  wchar_t name[100];MappingName(name,GetCurrentProcessId(),msg->wParam,(U32)msg->lParam);
  HANDLE mapping=OpenFileMappingW(6,0,name);if(mapping){Request* r=(Request*)MapViewOfFile(mapping,6,0,0,sizeof(Request));if(r){if(r->status==0)r->status=Apply(r);UnmapViewOfFile(r);}CloseHandle(mapping);}
 }}return CallNextHookEx(0,code,w,l);
}
extern "C" BOOL W DllEntry(HMODULE,U32,void*){return 1;}

#include "isolation.cpp"
static int sent,lastError;static U32 destinations[64];static U16 targetPort;static U32 incoming;
static int W FakeSend(U32,const char*,int n,int,const void* address,int){destinations[sent++]=((Address*)address)->ip;targetPort=((Address*)address)->port;return n;}
static int W FakeReceive(U32,char*,int,int,void* address,int* length){Address a={2,123,incoming,{0}};Copy(address,&a,16);*length=16;return 3;}
static void W FakeError(int error){lastError=error;}
extern "C" void W Entry(){
 originalSend=FakeSend;originalReceive=FakeReceive;setError=FakeError;enabled=true;peerCount=2;peers[0]=0x0100007f;peers[1]=0x0200007f;broadcast=0xffffff1a;
 Address a={2,4321,0xffffffff,{0}};char data[16];
 if(RoomSend(1,"abc",3,0,&a,16)!=3||sent!=2||destinations[0]!=peers[0]||destinations[1]!=peers[1]||targetPort!=4321)ExitProcess(1);
 sent=0;a.ip=broadcast;RoomSend(1,"abc",3,0,&a,16);if(sent!=2)ExitProcess(2);
 sent=0;a.ip=0x0300007f;RoomSend(1,"abc",3,0,&a,16);if(sent!=0)ExitProcess(3);
 a.ip=peers[1];RoomSend(1,"abc",3,0,&a,16);if(sent!=1)ExitProcess(4);
 int size=16;incoming=0x0300007f;if(RoomReceive(1,data,16,0,&a,&size)!=-1||lastError!=10035)ExitProcess(5);
 incoming=peers[0];size=16;if(RoomReceive(1,data,16,0,&a,&size)!=3||a.ip!=incoming)ExitProcess(6);
 enabled=false;a.ip=0x0300007f;sent=0;RoomSend(1,"abc",3,0,&a,16);if(sent!=1)ExitProcess(7);
 incoming=0x0300007f;size=16;if(RoomReceive(1,data,16,0,&a,&size)!=3)ExitProcess(8);
 ExitProcess(0);
}

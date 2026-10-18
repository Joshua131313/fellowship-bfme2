#include "bridge.h"
static Request request;
static HWND gameWindow;
static BOOL W Find(HWND window,I32 pid){U32 actual=0;GetWindowThreadProcessId(window,&actual);wchar_t type[80];const wchar_t* expected=L"E99E8455-CC9B-488a-BA22-0E8A8F74F9FA";if(actual==(U32)pid&&GetClassNameW(window,type,80)==36&&Equal(type,expected,72)){gameWindow=window;return 0;}return 1;}

static bool HashMatches(U32 pid){
 wchar_t path[1024];U32 length=1024;HANDLE process=OpenProcess(0x1000,0,pid);if(!process)return false;
 BOOL ok=QueryFullProcessImageNameW(process,0,path,&length);CloseHandle(process);if(!ok)return false;
 HANDLE file=CreateFileW(path,0x80000000,1,0,3,0,0);if(file==(HANDLE)-1)return false;
 U32 size=GetFileSize(file,0);HANDLE mapping=CreateFileMappingW(file,0,2,0,0,0);U8* bytes=mapping?(U8*)MapViewOfFile(mapping,4,0,0,0):0;
 HANDLE algorithm=0;U8 digest[32];bool valid=false;
 const U8 expected[32]={0xd7,0x92,0x65,0x81,0x26,0x38,0xd1,0x41,0xc4,0xf0,0x2e,0x59,0x24,0xb0,0x31,0x00,0xd3,0xf6,0x9e,0xac,0xa1,0x0a,0x93,0x79,0xcf,0xe1,0x4b,0x07,0x4f,0xba,0xf7,0x16};
 if(bytes&&size<100000000&&BCryptOpenAlgorithmProvider(&algorithm,L"SHA256",0,0)>=0){if(BCryptHash(algorithm,0,0,bytes,size,digest,32)>=0)valid=Equal(digest,expected,32);BCryptCloseAlgorithmProvider(algorithm,0);}
 if(bytes)UnmapViewOfFile(bytes);if(mapping)CloseHandle(mapping);CloseHandle(file);return valid;
}
static int Execute(){
 if(request.magic!=0x46424d32||request.version!=2)return -1;
 if(!HashMatches(request.pid))return -20;
 EnumWindows(Find,(I32)request.pid);if(!gameWindow)return -21;
 U32 pid=0;U32 thread=GetWindowThreadProcessId(gameWindow,&pid);if(pid!=request.pid||!thread)return -21;
 wchar_t dll[1024];U32 n=GetModuleFileNameW(0,dll,1024);if(!n||n>=990)return -22;while(n&&dll[n-1]!=L'\\')n--;
 const wchar_t* tail=L"Fellowship.Native.v017a.dll";while(*tail)dll[n++]=*tail++;dll[n]=0;
 HMODULE module=LoadLibraryW(dll);if(!module)return -22;
 void* callback=GetProcAddress(module,"_PlacementHook@12");if(!callback){FreeLibrary(module);return -22;}
 U32 nonce=0;if(BCryptGenRandom(0,(U8*)&nonce,4,2)<0){FreeLibrary(module);return -23;}
 wchar_t name[100];MappingName(name,pid,GetCurrentProcessId(),nonce);
 HANDLE mapping=CreateFileMappingW((HANDLE)-1,0,4,0,sizeof(Request),name);
 Request* shared=mapping?(Request*)MapViewOfFile(mapping,6,0,0,sizeof(Request)):0;
 int result=-23;if(shared){Copy(shared,&request,sizeof(Request));
  HHOOK hook=SetWindowsHookExW(3,callback,module,thread);
  if(hook){BOOL sent=PostMessageW(gameWindow,MessageId(),GetCurrentProcessId(),(I32)nonce);
   for(int tick=0;sent&&shared->status==0&&tick<500;tick++)Sleep(10);
   UnhookWindowsHookEx(hook);Copy(&request,shared,sizeof(Request));result=sent&&request.status?request.status:-25;
  }else result=-24;
  UnmapViewOfFile(shared);
 }
 
 if(mapping)CloseHandle(mapping);FreeLibrary(module);return result;
}
extern "C" void W Entry(){
 int count=0;wchar_t** args=CommandLineToArgvW(GetCommandLineW(),&count);if(!args||count!=2)ExitProcess(100);
 HANDLE file=CreateFileW(args[1],0xc0000000,0,0,3,0,0);if(file==(HANDLE)-1)ExitProcess(101);
 U32 read=0;if(GetFileSize(file,0)!=sizeof(Request)||!ReadFile(file,&request,sizeof(Request),&read,0)||read!=sizeof(Request)){CloseHandle(file);ExitProcess(102);}
 request.status=0;request.status=Execute();SetFilePointer(file,0,0,0);U32 wrote=0;WriteFile(file,&request,sizeof(Request),&wrote,0);CloseHandle(file);LocalFree(args);ExitProcess(request.status>0?0:1);
}

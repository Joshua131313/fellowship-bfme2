#pragma once
// Minimal Win32 ABI declarations keep this tiny x86 bridge independent of a CRT.
typedef unsigned long U32; typedef long I32; typedef unsigned short U16; typedef unsigned char U8;
typedef void* HANDLE; typedef HANDLE HWND; typedef HANDLE HMODULE; typedef HANDLE HHOOK; typedef int BOOL;
#define API extern "C" __declspec(dllimport)
#define W __stdcall
struct MBI { void* Base;void* AllocationBase;U32 AllocationProtect;U32 RegionSize;U32 State;U32 Protect;U32 Type; };
struct CWP { HWND hwnd;U32 message;U32 wParam;I32 lParam;U32 time;I32 x,y; };
API U32 W GetCurrentProcessId(); API U32 W GetCurrentThreadId(); API HMODULE W GetModuleHandleW(const wchar_t*);
API U32 W GetModuleFileNameW(HMODULE,wchar_t*,U32); API U32 W GetLastError();
API U32 W VirtualQuery(const void*,MBI*,U32); API U32 W RegisterWindowMessageW(const wchar_t*);
API HANDLE W CreateFileMappingW(HANDLE,void*,U32,U32,U32,const wchar_t*);API HANDLE W OpenFileMappingW(U32,BOOL,const wchar_t*);
API void* W MapViewOfFile(HANDLE,U32,U32,U32,U32);API BOOL W UnmapViewOfFile(const void*);API BOOL W CloseHandle(HANDLE);
API I32 W CallNextHookEx(HHOOK,int,U32,I32);API HHOOK W SetWindowsHookExW(int,void*,HMODULE,U32);API BOOL W UnhookWindowsHookEx(HHOOK);
API BOOL W EnumWindows(BOOL(W*)(HWND,I32),I32);API U32 W GetWindowThreadProcessId(HWND,U32*);API BOOL W IsWindowVisible(HWND);API HWND W GetWindow(HWND,U32);
API I32 W SendMessageTimeoutW(HWND,U32,U32,I32,U32,U32,U32*);
API HMODULE W LoadLibraryW(const wchar_t*);API void* W GetProcAddress(HMODULE,const char*);API BOOL W FreeLibrary(HMODULE);
API const wchar_t* W GetCommandLineW(); API wchar_t** W CommandLineToArgvW(const wchar_t*,int*);API HANDLE W LocalFree(HANDLE);
API HANDLE W CreateFileW(const wchar_t*,U32,U32,void*,U32,U32,HANDLE);API BOOL W ReadFile(HANDLE,void*,U32,U32*,void*);API BOOL W WriteFile(HANDLE,const void*,U32,U32*,void*);API U32 W SetFilePointer(HANDLE,I32,I32*,U32);API U32 W GetFileSize(HANDLE,U32*);
API HANDLE W OpenProcess(U32,BOOL,U32);API BOOL W QueryFullProcessImageNameW(HANDLE,U32,wchar_t*,U32*);API void W ExitProcess(U32);
API I32 W BCryptOpenAlgorithmProvider(HANDLE*,const wchar_t*,const wchar_t*,U32);API I32 W BCryptHash(HANDLE,U8*,U32,U8*,U32,U8*,U32);API I32 W BCryptCloseAlgorithmProvider(HANDLE,U32);API I32 W BCryptGenRandom(HANDLE,U8*,U32,U32);
struct Request { U32 magic,version,pid,lobby; I32 row,team,spot,readOnly,status,beforeTeam,beforeSpot,afterTeam,afterSpot; U32 thread; char map[512]; I32 mode; char targetMap[512]; I32 resources,commandPoints,customHeroes,ringHeroes,color; };
static_assert(sizeof(Request)==1104,"Protocol size");
inline void Copy(void* target,const void* source,U32 n){for(U32 i=0;i<n;i++)((U8*)target)[i]=((const U8*)source)[i];}
inline bool Equal(const void* a,const void* b,U32 n){for(U32 i=0;i<n;i++)if(((const U8*)a)[i]!=((const U8*)b)[i])return false;return true;}
inline void MappingName(wchar_t* out,U32 game,U32 helper,U32 nonce){const wchar_t* p=L"Local\\Fellowship.Native.";int n=0;while(*p)out[n++]=*p++;U32 values[3]={game,helper,nonce};for(int v=0;v<3;v++){for(int i=7;i>=0;i--)out[n++]=L"0123456789abcdef"[(values[v]>>(i*4))&15];out[n++]=L'.';}out[n]=0;}
inline U32 MessageId(){return RegisterWindowMessageW(L"Fellowship.BFME2.NativePlacement.v3");}

API BOOL W PostMessageW(HWND,U32,U32,I32);API void W Sleep(U32);

API int W GetClassNameW(HWND,wchar_t*,int);

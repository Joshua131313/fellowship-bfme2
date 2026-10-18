param([string]$Output = "$PSScriptRoot/bin")
$ErrorActionPreference='Stop'
$vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs=& $vswhere -latest -products '*' -property installationPath
$toolset=Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compilerRoot=Join-Path $toolset.FullName 'bin/Hostx64/x86'
$kits="${env:ProgramFiles(x86)}/Windows Kits/10/Lib"
$sdkVersion=Get-ChildItem $kits -Directory | Sort-Object Name -Descending | Where-Object {Test-Path (Join-Path $_.FullName 'um/x86/kernel32.lib')} | Select-Object -First 1
$sdk=Join-Path $sdkVersion.FullName 'um/x86'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
& "$compilerRoot/cl.exe" /nologo /c /Od /GS- /Zl /W4 /Fo"$Output/bridge.obj" "$PSScriptRoot/bridge.cpp"
if($LASTEXITCODE){throw 'Native bridge compilation failed'}
& "$compilerRoot/link.exe" /nologo /DLL /NODEFAULTLIB /DYNAMICBASE /NXCOMPAT /MACHINE:X86 /ENTRY:DllEntry /OUT:"$Output/Fellowship.Native.v012a.dll" "$Output/bridge.obj" /LIBPATH:"$sdk" kernel32.lib user32.lib
if($LASTEXITCODE){throw 'Native bridge link failed'}
& "$compilerRoot/cl.exe" /nologo /c /Od /GS- /Zl /W4 /Fo"$Output/host.obj" "$PSScriptRoot/host.cpp"
if($LASTEXITCODE){throw 'Native host compilation failed'}
& "$compilerRoot/link.exe" /nologo /NODEFAULTLIB /DYNAMICBASE /NXCOMPAT /MACHINE:X86 /SUBSYSTEM:WINDOWS /ENTRY:Entry /OUT:"$Output/Fellowship.NativeHost.exe" "$Output/host.obj" /LIBPATH:"$sdk" kernel32.lib user32.lib shell32.lib bcrypt.lib
if($LASTEXITCODE){throw 'Native host link failed'}

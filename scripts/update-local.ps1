# Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
# Local testing only. Never commits, pushes, publishes, or launches an installer.
param([string]$Dotnet='',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrEmpty($Dotnet)){$localDotnet=Join-Path $projectRoot 'work/dotnet/dotnet.exe';$Dotnet=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{'dotnet'}}
$installRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HoshiBar')).TrimEnd('\')
$legacyInstallRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HoshinoIsland')).TrimEnd('\')
$publishRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'work/local-publish'))
$backupRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'work/local-backup'))
if($installRoot -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HoshiBar')).TrimEnd('\')){throw 'Unexpected installation directory'}
Push-Location $projectRoot
try {
 if(!$SkipBuild){
  if(Test-Path -LiteralPath $publishRoot){Remove-Item -LiteralPath $publishRoot -Recurse -Force}
  $version=([xml](Get-Content -Raw src/HoshiBar.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
  & $Dotnet publish src/HoshiBar.csproj -c Release -r win-x64 --self-contained true -o $publishRoot -p:DebugType=None -p:DebugSymbols=false "-p:InformationalVersion=$version-local"
  if($LASTEXITCODE -ne 0){throw 'Local build failed'}
 }
 if(!(Test-Path -LiteralPath (Join-Path $publishRoot 'HoshiBar.exe'))){throw 'Local build is missing'}
 $watchPaths=@(
  (Join-Path $installRoot 'HoshiBar.exe'),
  (Join-Path $installRoot 'HoshinoIsland.exe'),
  (Join-Path $installRoot 'DIsland.exe'),
  (Join-Path $legacyInstallRoot 'HoshinoIsland.exe'),
  (Join-Path $legacyInstallRoot 'DIsland.exe')
 )
 $running=@(Get-Process -ErrorAction SilentlyContinue | Where-Object { try{$_.Path -in $watchPaths}catch{$false} })
 if($running.Count){
  $signal=$null
  try{$signal=[Threading.EventWaitHandle]::OpenExisting('Local\HoshiBar.DevExit')}catch{
   try{$signal=[Threading.EventWaitHandle]::OpenExisting('Local\HoshinoIsland.DevExit')}catch{throw 'FIRST_LOCAL_UPDATE: exit the running app once using its tray menu, then rerun with -SkipBuild. Later updates restart automatically.'}
  }
  try{$null=$signal.Set()}finally{$signal.Dispose()}
  foreach($process in $running){if(!$process.WaitForExit(12000)){throw 'HoshiBar is busy, listening, or has an unsaved draft/timer. Finish that first; no files were replaced.'}}
 }
 New-Item -ItemType Directory -Force -Path $backupRoot,$installRoot | Out-Null
 $replaced=[Collections.Generic.List[object]]::new()
 $previousExe=Join-Path $installRoot 'HoshiBar.exe'
 if(!(Test-Path -LiteralPath $previousExe) -and (Test-Path -LiteralPath (Join-Path $legacyInstallRoot 'HoshinoIsland.exe'))){$previousExe=Join-Path $legacyInstallRoot 'HoshinoIsland.exe'}
 function Backup-ReplacedFile([string]$relative){
  $target=[IO.Path]::GetFullPath((Join-Path $installRoot $relative));$backup=[IO.Path]::GetFullPath((Join-Path $backupRoot $relative))
  if(!$target.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or !$backup.StartsWith($backupRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Path escaped the application directory'}
  $existed=Test-Path -LiteralPath $target
  if($existed){New-Item -ItemType Directory -Force -Path (Split-Path $backup -Parent)|Out-Null;Copy-Item -LiteralPath $target -Destination $backup -Force}
  $replaced.Add([pscustomobject]@{Target=$target;Backup=$backup;Existed=$existed})
 }
 try{
  foreach($file in Get-ChildItem -LiteralPath $publishRoot -File -Recurse){
   $relative=$file.FullName.Substring($publishRoot.Length+1);Backup-ReplacedFile $relative
   $target=Join-Path $installRoot $relative
   New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent)|Out-Null
   Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  }
  foreach($legacy in @('HoshinoIsland.exe','HoshinoIsland.dll','HoshinoIsland.deps.json','HoshinoIsland.runtimeconfig.json','GlassDesk.ico','LICENSE-HOSHINO-MIT.txt','DIsland.exe','DIsland.dll','DIsland.deps.json','DIsland.runtimeconfig.json')){
   $old=Join-Path $installRoot $legacy
   if(Test-Path -LiteralPath $old){Backup-ReplacedFile $legacy;Remove-Item -LiteralPath $old}
  }
  Backup-ReplacedFile '.local-dev'
  [IO.File]::WriteAllText((Join-Path $installRoot '.local-dev'),'Local build; publish only after the user approves.')
 }catch{
  $updateError=$_
  foreach($entry in $replaced){if($entry.Existed){Copy-Item -LiteralPath $entry.Backup -Destination $entry.Target -Force}else{if(Test-Path -LiteralPath $entry.Target){Remove-Item -LiteralPath $entry.Target}}}
  if(Test-Path -LiteralPath $previousExe){Start-Process -FilePath $previousExe}
  throw $updateError
 }
 if((Test-Path -LiteralPath $legacyInstallRoot) -and ($legacyInstallRoot -ne $installRoot)){
  Remove-Item -LiteralPath $legacyInstallRoot -Recurse -Force -ErrorAction SilentlyContinue
 }
 $shell=New-Object -ComObject WScript.Shell
 $legacyStartMenu=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Hoshino Island'
 $startMenu=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/HoshiBar'
 $settingsLnkName='HoshiBar Settings.lnk'
 if((Test-Path -LiteralPath $legacyStartMenu) -or (Test-Path -LiteralPath $startMenu)){
  New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
  $s1=$shell.CreateShortcut((Join-Path $startMenu 'HoshiBar.lnk'));$s1.TargetPath=Join-Path $installRoot 'HoshiBar.exe';$s1.IconLocation=$s1.TargetPath;$s1.Save()
  $s2=$shell.CreateShortcut((Join-Path $startMenu $settingsLnkName));$s2.TargetPath=Join-Path $installRoot 'HoshiBar.exe';$s2.Arguments='--settings';$s2.IconLocation=$s2.TargetPath;$s2.Save()
  if(Test-Path -LiteralPath $legacyStartMenu){Remove-Item -LiteralPath $legacyStartMenu -Recurse -Force -ErrorAction SilentlyContinue}
 }
 $legacyDesktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Hoshino Island.lnk'
 $newDesktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'HoshiBar.lnk'
 if(Test-Path -LiteralPath $legacyDesktop){
  $sc=$shell.CreateShortcut($newDesktop);$sc.TargetPath=Join-Path $installRoot 'HoshiBar.exe';$sc.IconLocation=$sc.TargetPath;$sc.Save()
  Remove-Item -LiteralPath $legacyDesktop -Force -ErrorAction SilentlyContinue
 } elseif(Test-Path -LiteralPath $newDesktop){
  $sc=$shell.CreateShortcut($newDesktop);$sc.TargetPath=Join-Path $installRoot 'HoshiBar.exe';$sc.IconLocation=$sc.TargetPath;$sc.Save()
 }
 $uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}_is1'
 if(Test-Path -LiteralPath $uninstall){
  Set-ItemProperty -LiteralPath $uninstall -Name DisplayName -Value 'HoshiBar'
  Set-ItemProperty -LiteralPath $uninstall -Name DisplayIcon -Value (Join-Path $installRoot 'HoshiBar.exe')
  Set-ItemProperty -LiteralPath $uninstall -Name InstallLocation -Value ($installRoot+'\')
 }
 $isDefaultDesktop=$true
 try {
  Add-Type -Namespace HoshiDev -Name DeskCheck -MemberDefinition @'
   [System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
   [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetThreadDesktop(uint id);
   [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
   public static extern bool GetUserObjectInformation(System.IntPtr hObj, int nIndex, System.Text.StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
'@ -ErrorAction SilentlyContinue
  $hd=[HoshiDev.DeskCheck]::GetThreadDesktop([HoshiDev.DeskCheck]::GetCurrentThreadId())
  if($hd -ne [IntPtr]::Zero){
   $sb=[System.Text.StringBuilder]::new(128);$need=0
   if([HoshiDev.DeskCheck]::GetUserObjectInformation($hd,2,$sb,256,[ref]$need)){
    $dname=$sb.ToString()
    if($dname.Length -gt 0 -and $dname -ne 'Default'){$isDefaultDesktop=$false}
   }
  }
 } catch {}
 if($isDefaultDesktop){
  Start-Process -FilePath (Join-Path $installRoot 'HoshiBar.exe')
 }
 Write-Output 'LOCAL_UPDATE_COMPLETE'
} finally {Pop-Location}

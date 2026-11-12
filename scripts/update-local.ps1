# Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
# Local testing only. Never commits, pushes, publishes, or launches an installer.
param([string]$Dotnet='dotnet',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$installRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HoshinoIsland')).TrimEnd('\')
$publishRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'work/local-publish'))
$backupRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'work/local-backup'))
if($installRoot -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HoshinoIsland')).TrimEnd('\')){throw 'Unexpected installation directory'}
Push-Location $projectRoot
try {
 if(!$SkipBuild){
  $version=([xml](Get-Content -Raw src/HoshinoIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
  & $Dotnet publish src/HoshinoIsland.csproj -c Release -r win-x64 --self-contained true -o $publishRoot -p:DebugType=None -p:DebugSymbols=false "-p:InformationalVersion=$version-local"
  if($LASTEXITCODE -ne 0){throw 'Local build failed'}
 }
 if(!(Test-Path -LiteralPath (Join-Path $publishRoot 'HoshinoIsland.exe'))){throw 'Local build is missing'}
 $running=@(Get-Process -ErrorAction SilentlyContinue | Where-Object { try{$_.Path -in @((Join-Path $installRoot 'DIsland.exe'),(Join-Path $installRoot 'HoshinoIsland.exe'))}catch{$false} })
 if($running.Count){
  try{$signal=[Threading.EventWaitHandle]::OpenExisting('Local\HoshinoIsland.DevExit')}catch{throw 'FIRST_LOCAL_UPDATE: exit the old island once using its tray menu, then rerun with -SkipBuild. Later updates restart automatically.'}
  try{$null=$signal.Set()}finally{$signal.Dispose()}
  foreach($process in $running){if(!$process.WaitForExit(12000)){throw 'The island is busy, listening, or has an unsaved draft/timer. Finish that first; no files were replaced.'}}
 }
 New-Item -ItemType Directory -Force -Path $backupRoot,$installRoot | Out-Null
 $replaced=[Collections.Generic.List[object]]::new()
 $previousExe=Join-Path $installRoot 'HoshinoIsland.exe'
 if(Test-Path -LiteralPath (Join-Path $installRoot 'DIsland.exe')){$previousExe=Join-Path $installRoot 'DIsland.exe'}
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
  foreach($legacy in @('DIsland.exe','DIsland.dll','DIsland.deps.json','DIsland.runtimeconfig.json')){
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
 $shell=New-Object -ComObject WScript.Shell
 $startMenu=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Hoshino Island'
 $links=@((Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Hoshino Island.lnk'))
 if(Test-Path -LiteralPath $startMenu){$links+=@(Get-ChildItem -LiteralPath $startMenu -Filter '*.lnk' -File|ForEach-Object FullName)}
 foreach($link in $links){if(Test-Path -LiteralPath $link){$shortcut=$shell.CreateShortcut($link);if($shortcut.TargetPath.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)){$shortcut.TargetPath=Join-Path $installRoot 'HoshinoIsland.exe';$shortcut.IconLocation=$shortcut.TargetPath;$shortcut.Save()}}}
 $uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}_is1'
 if(Test-Path -LiteralPath $uninstall){Set-ItemProperty -LiteralPath $uninstall -Name DisplayIcon -Value (Join-Path $installRoot 'HoshinoIsland.exe')}
 Start-Process -FilePath (Join-Path $installRoot 'HoshinoIsland.exe')
 Write-Output 'LOCAL_UPDATE_COMPLETE'
} finally {Pop-Location}

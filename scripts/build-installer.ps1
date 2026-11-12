param([string]$Iscc="C:\Program Files (x86)\Inno Setup 6\ISCC.exe",[string]$Dotnet="dotnet")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if(!(Test-Path src/Assets/Speech/vosk-model-small-ru-0.22/am/final.mdl)){throw 'Download the official Russian model using scripts/prepare-speech.py first.'}
 $version=([xml](Get-Content -Raw src/HoshinoIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
 # Keep legacy binaries and local runtime files out of release staging.
 $staging=[IO.Path]::GetFullPath((Join-Path $root "work/release-publish-$version"))
 & $Dotnet publish src/HoshinoIsland.csproj -c Release -r win-x64 --self-contained true -o $staging -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 Copy-Item README.md (Join-Path $staging 'README-RU.md')
 $forbidden=@(Get-ChildItem -LiteralPath $staging -File -Recurse|Where-Object{$_.Name -in @('auth.json','assistant.json','settings.json','obsidian.json','history.txt','voice-status.json','.local-dev','DIsland.exe','DIsland.dll') -or $_.Name -like 'wake-profile*.json' -or $_.Extension -in @('.pfx','.p12','.key','.pth','.pt','.index')})
 if($forbidden.Count){throw ('Private or stale files in release staging: '+(($forbidden|ForEach-Object Name)-join ', '))}
 & $Iscc "/DAppVersion=$version" "/DAppSource=$staging" packaging/HoshinoIsland.iss
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $core=(Get-Item "outputs/Hoshino-Island-Core-Setup-$version.exe").FullName
 $coreHash=(Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant()
 & $Dotnet publish installer/HoshinoInstaller.csproj -c Release -r win-x64 --self-contained true -o work/installer-publish -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:PayloadPath=$core" "-p:PayloadSha256=$coreHash"
 if($LASTEXITCODE -ne 0){throw 'Custom installer compilation failed'}
 Copy-Item -LiteralPath work/installer-publish/Hoshino-Island-Setup.exe -Destination "outputs/Hoshino-Island-Setup-$version.exe"
 $file=Get-Item "outputs/Hoshino-Island-Setup-$version.exe"
 $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText($file.FullName+'.sha256',($hash+'  '+$file.Name+"`n"))
} finally {Pop-Location}

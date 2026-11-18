param([string]$Iscc="",[string]$Dotnet="")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrEmpty($Dotnet)){$localDotnet=Join-Path $root 'work/dotnet/dotnet.exe';$Dotnet=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{'dotnet'}}
if([string]::IsNullOrEmpty($Iscc)){$Iscc=@((Join-Path $root 'work/inno/ISCC.exe'),"C:\Program Files (x86)\Inno Setup 6\ISCC.exe","C:\Program Files\Inno Setup 6\ISCC.exe",(Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"))|Where-Object{Test-Path -LiteralPath $_}|Select-Object -First 1;if(!$Iscc){$Iscc='ISCC.exe'}}
Push-Location $root
try {
 if(!(Test-Path src/Assets/Speech/vosk-model-small-ru-0.22/am/final.mdl)){throw 'Download the official Russian model using scripts/prepare-speech.py first.'}
 $version=([xml](Get-Content -Raw src/HoshiBar.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
 # Keep legacy binaries and local runtime files out of release staging.
 $staging=[IO.Path]::GetFullPath((Join-Path $root "work/release-publish-$version"))
 & $Dotnet publish src/HoshiBar.csproj -c Release -r win-x64 --self-contained true -o $staging -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 Copy-Item README.md (Join-Path $staging 'README-RU.md')
 if(Test-Path README.en.md){Copy-Item README.en.md (Join-Path $staging 'README-EN.md')}
 $forbidden=@(Get-ChildItem -LiteralPath $staging -File -Recurse|Where-Object{$_.Name -in @('auth.json','assistant.json','settings.json','obsidian.json','history.txt','voice-status.json','.local-dev','DIsland.exe','DIsland.dll','HoshinoIsland.exe','HoshinoIsland.dll','GlassDesk.ico') -or $_.Name -like 'wake-profile*.json' -or $_.Extension -in @('.pfx','.p12','.key','.pth','.pt','.index')})
 if($forbidden.Count){throw ('Private or stale files in release staging: '+(($forbidden|ForEach-Object Name)-join ', '))}
 & $Iscc "/DAppVersion=$version" "/DAppSource=$staging" packaging/HoshiBar.iss
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $core=(Get-Item "outputs/HoshiBar-Core-Setup-$version.exe").FullName
 $coreHash=(Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant()
 & $Dotnet publish installer/HoshiBarInstaller.csproj -c Release -r win-x64 --self-contained true -o work/installer-publish -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:PayloadPath=$core" "-p:PayloadSha256=$coreHash"
 if($LASTEXITCODE -ne 0){throw 'Custom installer compilation failed'}
 Copy-Item -LiteralPath work/installer-publish/HoshiBar-Setup.exe -Destination "outputs/HoshiBar-Setup-$version.exe"
 $file=Get-Item "outputs/HoshiBar-Setup-$version.exe"
 $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText($file.FullName+'.sha256',($hash+'  '+$file.Name+"`n"))
} finally {Pop-Location}

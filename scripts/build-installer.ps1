param([string]$Iscc="C:\Program Files (x86)\Inno Setup 6\ISCC.exe",[string]$Dotnet="dotnet")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if(!(Test-Path src/Assets/Speech/vosk-model-small-ru-0.22/am/final.mdl)){throw 'Download the official Russian model using scripts/prepare-speech.py first.'}
 $version=([xml](Get-Content -Raw src/DIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
 & $Dotnet publish src/DIsland.csproj -c Release -r win-x64 --self-contained true -o publish -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 Copy-Item README.md publish/README-RU.md
 & $Iscc packaging/HoshinoIsland.iss
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

param([string]$Iscc="C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if(!(Test-Path src/Assets/Speech/vosk-model-small-ru-0.22/am/final.mdl)){throw 'Download the official Russian model using scripts/prepare-speech.py first.'}
 dotnet publish src/DIsland.csproj -c Release -r win-x64 --self-contained true -o publish -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 Copy-Item README.md publish/README-RU.md
 & $Iscc packaging/HoshinoIsland.iss
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $file=Get-Item outputs/Hoshino-Island-Setup-0.14.0.exe
 $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText($file.FullName+'.sha256',($hash+'  '+$file.Name+"`n"))
} finally {Pop-Location}

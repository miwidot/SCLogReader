# release.ps1 — lokal bauen + GitHub-Release erstellen.
# Aufruf:  .\release.ps1
# Nimmt die <Version> aus der csproj als Tag (z.B. 1.1.5 -> v1.1.5).
$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$csproj = Join-Path $root 'SCLogReader.csproj'

$ver = (Select-String -Path $csproj -Pattern '<Version>(.*?)</Version>').Matches[0].Groups[1].Value
if (-not $ver) { throw 'Keine <Version> in SCLogReader.csproj gefunden.' }
$tag = "v$ver"
Write-Host "==> Release $tag" -ForegroundColor Cyan

# 1) Single-file exe lokal bauen
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
  -o (Join-Path $root 'publish')
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

$exe = Join-Path $root 'publish\SCLogReader.exe'
if (-not (Test-Path $exe)) { throw "exe nicht gefunden: $exe" }
Write-Host ("   gebaut: {0:N1} MB" -f ((Get-Item $exe).Length/1MB)) -ForegroundColor Green

# 2) Tag setzen + pushen (falls noch nicht da)
git tag $tag 2>$null
git push origin $tag

# 3) GitHub-Release mit der exe erstellen
gh release create $tag $exe --title $tag --generate-notes
Write-Host "==> Release $tag erstellt: https://github.com/miwidot/SCLogReader/releases/tag/$tag" -ForegroundColor Cyan

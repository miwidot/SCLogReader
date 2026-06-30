# release.ps1 — lokal bauen + GitHub-Release mit ordentlichem Changelog.
# Ablauf: 1) Version aus csproj  2) [Unreleased] in CHANGELOG.md zu [version] stempeln
#         3) bauen  4) Release mit der Changelog-Sektion als Notes (+ Link).
$ErrorActionPreference = 'Stop'
$root   = $PSScriptRoot
$csproj = Join-Path $root 'SCLogReader.csproj'
$clPath = Join-Path $root 'CHANGELOG.md'

$ver = (Select-String -Path $csproj -Pattern '<Version>(.*?)</Version>').Matches[0].Groups[1].Value
if (-not $ver) { throw 'Keine <Version> in SCLogReader.csproj gefunden.' }
$tag  = "v$ver"
$date = Get-Date -Format 'yyyy-MM-dd'
Write-Host "==> Release $tag" -ForegroundColor Cyan

# CHANGELOG: [Unreleased] -> [version] - date, frische leere [Unreleased] oben einsetzen
$cl = Get-Content $clPath -Raw
if ($cl -match '(?ms)^## \[Unreleased\]\s*(.*?)(?=^## \[|\z)') {
  $body = $Matches[1].Trim()
  if (-not $body) { $body = '- (keine Einträge)' }
  $cl = $cl -replace '(?m)^## \[Unreleased\]\s*', "## [Unreleased]`r`n`r`n## [$ver] - $date`r`n"
  Set-Content $clPath $cl -Encoding UTF8
} else {
  $body = "Siehe Commits."
}

# Release-Notes zusammenbauen
$notes = "$body`r`n`r`n---`r`n📋 Vollständiges Changelog: https://github.com/miwidot/SCLogReader/blob/main/CHANGELOG.md"
$notesFile = Join-Path $env:TEMP "sclr_notes_$ver.md"
Set-Content $notesFile $notes -Encoding UTF8

# Changelog-Update committen
git add CHANGELOG.md
git commit -m "changelog: $tag" 2>&1 | Out-Null
git push 2>&1 | Out-Null

# Single-file exe bauen
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
  -o (Join-Path $root 'publish')
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$exe = Join-Path $root 'publish\SCLogReader.exe'
if (-not (Test-Path $exe)) { throw "exe nicht gefunden: $exe" }
Write-Host ("   gebaut: {0:N1} MB" -f ((Get-Item $exe).Length/1MB)) -ForegroundColor Green

# Tag + Release
git tag $tag 2>$null
git push origin $tag
gh release create $tag $exe --title $tag --notes-file $notesFile
Write-Host "==> Release $tag erstellt: https://github.com/miwidot/SCLogReader/releases/tag/$tag" -ForegroundColor Cyan

# sign.ps1 — signiert eine Datei mit dem Certum/SimplySign Code-Signing-Zertifikat.
# Voraussetzung: SimplySign Desktop läuft und ist eingeloggt (Cloud-Cert im Store).
# Aufruf:  .\sign.ps1 -File .\publish\SCLogReader.exe
param([Parameter(Mandatory)][string]$File)
$ErrorActionPreference = 'Stop'

$st = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
      Sort-Object FullName -Descending | Select-Object -First 1
if (-not $st) { throw 'signtool.exe nicht gefunden (Windows SDK fehlt).' }

# Cert per Subject wählen (überlebt Zertifikatserneuerung; Thumbprint würde sich ändern).
Write-Host "==> Signiere $File" -ForegroundColor Cyan
& $st.FullName sign /n 'Open Source Developer Martin Wilke' /fd sha256 `
    /tr 'http://time.certum.pl' /td sha256 $File
if ($LASTEXITCODE -ne 0) {
    throw 'Signieren fehlgeschlagen — läuft SimplySign Desktop und ist es eingeloggt?'
}
& $st.FullName verify /pa $File | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Signatur-Verifikation fehlgeschlagen.' }
Write-Host '==> Signatur OK' -ForegroundColor Green

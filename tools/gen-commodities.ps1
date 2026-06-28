# Generiert Core/CommoditiesData.cs aus scunpacked (resourceGUID -> Warenname).
# Bei neuem Patch einfach erneut ausführen. Danach ParserVersion in Database.cs erhöhen.
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\Core\CommoditiesData.cs'
$map = @{}
foreach ($url in @(
  'https://raw.githubusercontent.com/StarCitizenWiki/scunpacked-data/master/resources/commodities.json',
  'https://raw.githubusercontent.com/StarCitizenWiki/scunpacked-data/master/resources/resources.json')) {
  $data = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
  foreach ($e in $data) { if ($e.UUID -and $e.Name) { $map[$e.UUID] = $e.Name } }
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('namespace SCLogReader.Core;')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('/// <summary>Auto-generiert via tools/gen-commodities.ps1 aus scunpacked. resourceGUID -> Warenname.</summary>')
[void]$sb.AppendLine('public static partial class Commodities')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    static readonly Dictionary<string, string> Map = new()')
[void]$sb.AppendLine('    {')
foreach ($k in ($map.Keys | Sort-Object)) { $n = $map[$k] -replace '"', '\"'; [void]$sb.AppendLine("        [`"$k`"] = `"$n`",") }
[void]$sb.AppendLine('    };')
[void]$sb.AppendLine('}')
$sb.ToString() | Set-Content $out -Encoding UTF8
Write-Host "CommoditiesData.cs aktualisiert: $($map.Count) Waren"

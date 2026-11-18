$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
  $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) was not found.' }

$bin = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }

$exe = Join-Path $bin 'QuickSearch.exe'

& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
  /out:"$exe" `
  /win32icon:"$PSScriptRoot\search.ico" `
  /win32manifest:"$PSScriptRoot\app.manifest" `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  "$PSScriptRoot\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

"built $exe ($((Get-Item $exe).Length) bytes)"

# The glass shader (windows/Simpl/Components/Glass/Glass.fx) compiled for WPF's ShaderEffect (ps_3_0) with the Windows
# SDK's fxc — the newest one installed. Run on Windows (CI: .github/workflows/windows-glass.yml) before building.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fx = Join-Path $root 'Simpl/Components/Glass/Glass.fx'
$ps = Join-Path $root 'Simpl/Components/Glass/Glass.ps'
$fxc = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin/*/x64/fxc.exe' -ErrorAction SilentlyContinue |
  Sort-Object { [version]($_.Directory.Parent.Name) } | Select-Object -Last 1
if (-not $fxc) { throw 'fxc.exe was not found (the Windows SDK).' }
Write-Host "fxc: $($fxc.FullName)"
& $fxc.FullName /nologo /T ps_3_0 /E main /O3 /Fo $ps $fx
if ($LASTEXITCODE -ne 0) { throw "fxc failed ($LASTEXITCODE)" }
Write-Host "Glass.ps: $((Get-Item $ps).Length) bytes"

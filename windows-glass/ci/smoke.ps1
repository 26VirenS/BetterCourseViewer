# Starts an exe, lets it run, takes a picture of the screen and says whether it is still running (a crash at start is a
# failed check). Usage: smoke.ps1 -Exe <path> -Shot <png>
param([string]$Exe, [string]$Shot, [int]$Seconds = 12)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$dir = Split-Path -Parent (Resolve-Path $Exe)
$p = Start-Process -FilePath (Resolve-Path $Exe) -WorkingDirectory $dir -PassThru
Start-Sleep -Seconds $Seconds
# (its window in front of the runner's own consoles, for the picture)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }
"@
$p.Refresh()
if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [W]::ShowWindow($p.MainWindowHandle, 9) | Out-Null; [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Seconds 2 }
Write-Host "main window: $($p.MainWindowTitle) ($($p.MainWindowHandle))"
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
$alive = -not $p.HasExited
Write-Host "screen $($b.Width)x$($b.Height); still running after $Seconds s: $alive"
if (-not $alive) { Write-Host "exit code: $($p.ExitCode)" }
Get-ChildItem $dir -Filter *.log -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "--- $($_.Name)"; Get-Content $_.FullName -Tail 30 }
if ($alive) { Stop-Process -Id $p.Id -Force }

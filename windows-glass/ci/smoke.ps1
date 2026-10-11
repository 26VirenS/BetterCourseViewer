# Starts an exe, lets it run, and takes pictures of the screen; says whether it is still running (a crash at start is a
# failed check). -Keys: keys sent to its window first (liquidImgui hides itself from screen capture until F11).
# Usage: smoke.ps1 -Exe <path> -Shot <png> [-Keys "{F11}"]
param([string]$Exe, [string]$Shot, [string]$Keys = "", [int]$Seconds = 12)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
  public struct RECT { public int L, T, R, B; }
}
"@
function Snap([string]$file) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  Write-Host "picture: $file ($($b.Width)x$($b.Height))"
}
$dir = Split-Path -Parent (Resolve-Path $Exe)
$p = Start-Process -FilePath (Resolve-Path $Exe) -WorkingDirectory $dir -PassThru
Start-Sleep -Seconds $Seconds
$p.Refresh()
$h = $p.MainWindowHandle
Write-Host "main window: $($p.MainWindowTitle) ($h)"
if ($h -ne [IntPtr]::Zero) { [W]::ShowWindow($h, 9) | Out-Null; [W]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Seconds 1 }
if ($Keys) { [System.Windows.Forms.SendKeys]::SendWait($Keys); Start-Sleep -Seconds 2 }
Snap $Shot
# (a click in the middle of its window — past an intro, onto its first page — and another picture)
if ($h -ne [IntPtr]::Zero -and -not $p.HasExited) {
  $r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
  [W]::SetCursorPos([int](($r.L + $r.R) / 2), [int](($r.T + $r.B) / 2)) | Out-Null
  [W]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 80; [W]::mouse_event(4, 0, 0, 0, 0)
  Start-Sleep -Seconds 4
  Snap ($Shot -replace '\.png$', '-2.png')
}
$alive = -not $p.HasExited
Write-Host "still running after the pictures: $alive"
if (-not $alive) { Write-Host "exit code: $($p.ExitCode)"; exit 1 }
Stop-Process -Id $p.Id -Force

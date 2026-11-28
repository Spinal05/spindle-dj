# Starts SpindleRecovery.exe, waits for its main window, takes a screenshot and closes it.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Screenshot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

$p = Start-Process -FilePath $Exe -PassThru
$deadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
} while (-not $p.HasExited -and $p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)

if ($p.HasExited) { throw "SpindleRecovery.exe exited early with code $($p.ExitCode)" }
if ($p.MainWindowHandle -eq 0) { Stop-Process -Id $p.Id -Force; throw 'No main window appeared within 60 seconds' }
Write-Host "Main window: '$($p.MainWindowTitle)'"
Start-Sleep -Seconds 10   # let device detection finish

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$alive = -not $p.HasExited
Stop-Process -Id $p.Id -Force
if ($p.MainWindowTitle -ne 'Spindle Recovery') { throw "Unexpected window title '$($p.MainWindowTitle)'" }
if (-not $alive) { throw 'The application crashed after start-up' }
Write-Host 'GUI smoke test passed'

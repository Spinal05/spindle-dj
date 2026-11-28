# Creates two virtual disks for the Windows end-to-end tests (CI only; needs administrator rights):
#   Y: FAT32 "CAMERA"  and  Z: NTFS "DATA", each a 128 MB fixed VHD with an MBR partition table.
# Writes files, records their MD5, deletes some with Windows itself, and writes expected.txt as
#   fs|relative\path|md5|deleted(0/1)
# Prints the disk numbers to $env:GITHUB_OUTPUT (fat=N, ntfs=M).
param([string]$Work = (Join-Path $env:RUNNER_TEMP 'spindle'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Work | Out-Null
Add-Type -AssemblyName System.Drawing

function New-TestVhd([string]$Path, [string]$Fs, [string]$Label, [string]$Letter) {
    $script = @"
create vdisk file="$Path" maximum=128 type=fixed
select vdisk file="$Path"
attach vdisk
convert mbr
create partition primary
format fs=$Fs quick label=$Label
assign letter=$Letter
"@
    $s = Join-Path $Work "diskpart-$Letter.txt"
    Set-Content -Path $s -Value $script -Encoding ascii
    diskpart /s $s | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "diskpart failed for $Path" }
    return (Get-DiskImage -ImagePath $Path | Get-Disk).Number
}

function New-Photo([string]$Path, [int]$Seed) {
    $rnd = [System.Random]::new($Seed)
    $bmp = [System.Drawing.Bitmap]::new(800, 600)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb($rnd.Next(256), $rnd.Next(256), $rnd.Next(256)))
    for ($i = 0; $i -lt 80; $i++) {
        $b = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($rnd.Next(256), $rnd.Next(256), $rnd.Next(256)))
        $g.FillEllipse($b, $rnd.Next(800), $rnd.Next(600), $rnd.Next(40, 300), $rnd.Next(40, 300))
        $b.Dispose()
    }
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bmp.Dispose()
}

function New-Data([string]$Path, [int]$Size, [int]$Seed) {
    $buf = [byte[]]::new($Size)
    [System.Random]::new($Seed).NextBytes($buf)
    [System.IO.File]::WriteAllBytes($Path, $buf)
}

$fat = New-TestVhd (Join-Path $Work 'fat.vhd') 'fat32' 'CAMERA' 'Y'
$ntfs = New-TestVhd (Join-Path $Work 'ntfs.vhd') 'ntfs' 'DATA' 'Z'
$fmt = New-TestVhd (Join-Path $Work 'fmt.vhd') 'ntfs' 'OLDDATA' 'W'

# ---- FAT32 card: photos with long file names, a document, a note
New-Item -ItemType Directory -Force 'Y:\DCIM\100 Holiday', 'Y:\Documents' | Out-Null
1..4 | ForEach-Object { New-Photo "Y:\DCIM\100 Holiday\holiday photo $_.jpg" (100 + $_) }
New-Data 'Y:\Documents\report final.pdf' 60000 7
Set-Content -Path 'Y:\notes.txt' -Value 'keep me' -Encoding ascii
$fatDeleted = @('DCIM\100 Holiday\holiday photo 2.jpg', 'DCIM\100 Holiday\holiday photo 3.jpg', 'Documents\report final.pdf')
$fatAll = @('DCIM\100 Holiday\holiday photo 1.jpg', 'DCIM\100 Holiday\holiday photo 4.jpg', 'notes.txt') + $fatDeleted

# ---- NTFS data disk: a folder that gets deleted, Unicode name, resident file, fragmented file
New-Item -ItemType Directory -Force 'Z:\Users\Sam\Pictures\Summer 2026', 'Z:\Users\Sam\Documents', 'Z:\Projects' | Out-Null
1..3 | ForEach-Object { New-Photo "Z:\Users\Sam\Pictures\Summer 2026\beach $_.jpg" (200 + $_) }
New-Data 'Z:\Users\Sam\Documents\Résumé été.docx' 50000 11
Set-Content -Path 'Z:\Users\Sam\Documents\tiny.txt' -Value 'resident in the MFT' -Encoding ascii -NoNewline
New-Data 'Z:\Users\Sam\Documents\budget.xlsx' 30000 12
$frag = [System.IO.File]::Open('Z:\Projects\big.bin', 'Create', 'Write')
$frag.Close()
for ($i = 0; $i -lt 6; $i++) {
    New-Data "Z:\Projects\filler$i.bin" 65536 (300 + $i)
    $chunk = [byte[]]::new(98304); [System.Random]::new(400 + $i).NextBytes($chunk)
    $fs = [System.IO.File]::Open('Z:\Projects\big.bin', 'Append', 'Write'); $fs.Write($chunk, 0, $chunk.Length); $fs.Close()
}
Remove-Item 'Z:\Projects\filler1.bin', 'Z:\Projects\filler3.bin'
$ntfsDeleted = @('Users\Sam\Pictures\Summer 2026\beach 1.jpg', 'Users\Sam\Pictures\Summer 2026\beach 2.jpg',
                 'Users\Sam\Pictures\Summer 2026\beach 3.jpg', 'Users\Sam\Documents\Résumé été.docx',
                 'Users\Sam\Documents\tiny.txt', 'Projects\big.bin')
$ntfsAll = @('Users\Sam\Documents\budget.xlsx') + $ntfsDeleted

# ---- NTFS disk that will be quick-formatted: 300 files in nested folders
$fmtAll = [System.Collections.Generic.List[string]]::new()
foreach ($user in 'Dana', 'Lee') {
    foreach ($folder in 'Photos\Holiday', 'Photos\Birthday', 'Documents\Invoices') {
        New-Item -ItemType Directory -Force "W:\Users\$user\$folder" | Out-Null
        for ($i = 1; $i -le 50; $i++) {
            $ext = if ($folder -like 'Photos*') { 'jpg' } else { 'pdf' }
            $rel = "Users\$user\$folder\file $i.$ext"
            New-Data "W:\$rel" (3000 + ($i * 97) % 9000) (1000 + $fmtAll.Count)
            $fmtAll.Add($rel)
        }
    }
}

# ---- record checksums, then delete with Windows
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($p in $fatAll) { $lines.Add("fat|$p|$((Get-FileHash -Algorithm MD5 "Y:\$p").Hash)|$([int]($fatDeleted -contains $p))") }
foreach ($p in $ntfsAll) { $lines.Add("ntfs|$p|$((Get-FileHash -Algorithm MD5 "Z:\$p").Hash)|$([int]($ntfsDeleted -contains $p))") }
foreach ($p in $fmtAll) { $lines.Add("fmt|$p|$((Get-FileHash -Algorithm MD5 "W:\$p").Hash)|1") }
[System.IO.File]::WriteAllLines((Join-Path $Work 'expected.txt'), $lines, [System.Text.UTF8Encoding]::new($false))

# Flush file data to the disks BEFORE deleting: Windows discards cached data of files that are
# deleted before it was written, which would leave nothing on disk to recover (unlike real use,
# where files are deleted long after they were written).
Write-VolumeCache -DriveLetter Y, Z, W

$fatDeleted | ForEach-Object { Remove-Item -LiteralPath "Y:\$_" }
Remove-Item -LiteralPath 'Z:\Users\Sam\Pictures\Summer 2026' -Recurse
'Users\Sam\Documents\Résumé été.docx', 'Users\Sam\Documents\tiny.txt', 'Projects\big.bin' | ForEach-Object { Remove-Item -LiteralPath "Z:\$_" }
Write-VolumeCache -DriveLetter Y, Z

# Quick-format W: with Windows itself: the volume becomes empty, the old file records stay on disk.
# Windows sends TRIM during a format; SSDs and virtual disks then discard the old data, hard drives do not.
# Turn TRIM off for this format so the test disk behaves like a hard drive (the common recovery case).
fsutil behavior set DisableDeleteNotify NTFS 1 | Out-Host
Format-Volume -DriveLetter W -FileSystem NTFS -NewFileSystemLabel NEWDATA -Force -Confirm:$false | Out-Host
fsutil behavior set DisableDeleteNotify NTFS 0 | Out-Host
Write-VolumeCache -DriveLetter W

Get-Content (Join-Path $Work 'expected.txt') | Out-Host
"fat=$fat" >> $env:GITHUB_OUTPUT
"ntfs=$ntfs" >> $env:GITHUB_OUTPUT
"fmt=$fmt" >> $env:GITHUB_OUTPUT
"work=$Work" >> $env:GITHUB_OUTPUT
Write-Host "FAT32 VHD is disk $fat, NTFS VHD is disk $ntfs"

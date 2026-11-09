# Draws the AutoDraw logo (a drawing sheet with a bent part and dimension lines, in the red / dark style of the
# other tools) and writes installer\logo.png, installer\AutoDraw.ico and the Inno Setup wizard images.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-artwork.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\installer'
$red  = [System.Drawing.Color]::FromArgb(232, 72, 77)
$dark = [System.Drawing.Color]::FromArgb(51, 51, 51)

function Draw-Logo([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size) {
    $g.SmoothingMode = 'AntiAlias'
    $u = $size / 100.0
    $penDark = New-Object System.Drawing.Pen($dark, [Math]::Max(1.2, 5 * $u)); $penDark.LineJoin = 'Miter'
    $penRed  = New-Object System.Drawing.Pen($red, [Math]::Max(1.2, 3.5 * $u)); $penRed.LineJoin = 'Miter'
    $thin    = New-Object System.Drawing.Pen($dark, [Math]::Max(1, 2 * $u))
    function P($px, $py) { New-Object System.Drawing.PointF(($x + $px * $u), ($y + $py * $u)) }
    # the sheet with a title block
    $g.DrawRectangle($penDark, $x + 6 * $u, $y + 10 * $u, 88 * $u, 80 * $u)
    $g.DrawRectangle($penDark, $x + 58 * $u, $y + 70 * $u, 36 * $u, 20 * $u)
    # a bent part (L profile)
    $g.DrawPolygon($penRed, [System.Drawing.PointF[]]@((P 18 30), (P 52 30), (P 52 39), (P 28 39), (P 28 62), (P 18 62)))
    # dimension lines
    $g.DrawLine($thin, (P 18 22), (P 52 22)); $g.DrawLine($thin, (P 18 18), (P 18 26)); $g.DrawLine($thin, (P 52 18), (P 52 26))
    $g.DrawLine($thin, (P 62 30), (P 62 62)); $g.DrawLine($thin, (P 58 30), (P 66 30)); $g.DrawLine($thin, (P 58 62), (P 66 62))
}

function New-Image([int]$w, [int]$h, [float]$logo, [bool]$white) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    if ($white) { $g.Clear([System.Drawing.Color]::White) } else { $g.Clear([System.Drawing.Color]::Transparent) }
    Draw-Logo $g (($w - $logo) / 2) (($h - $logo) / 2) $logo
    $g.Dispose()
    return $bmp
}

$logo = New-Image 590 590 560 $false; $logo.Save((Join-Path $out 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$large = New-Image 328 628 280 $true;  $large.Save((Join-Path $out 'wizard-large.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$small = New-Image 110 110 96 $true;   $small.Save((Join-Path $out 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

# .ico with 16, 32, 48 and 256 px images (PNG-compressed entries)
$sizes = 16, 32, 48, 256
$pngs = foreach ($s in $sizes) { $b = New-Image $s $s $s $false; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); ,$ms.ToArray() }
$fs = [System.IO.File]::Create((Join-Path $out 'AutoDraw.ico'))
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"Artwork written to $out"

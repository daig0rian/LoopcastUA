# Generates client/LoopcastUA/Resources/app.ico from the speaker icon design
# Produces 4 sizes: 16x16, 32x32, 48x48, 256x256 (all PNG-in-ICO, 32bpp ARGB)

Add-Type -AssemblyName System.Drawing

function New-SpeakerBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode  = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s     = $size / 16.0
    $color = [System.Drawing.Color]::FromArgb(50, 180, 50)
    $brush = New-Object System.Drawing.SolidBrush($color)
    $pen   = New-Object System.Drawing.Pen($color, [float][Math]::Max(1.0, $s * 1.2))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    # Speaker body
    $g.FillRectangle($brush, [float](1*$s), [float](6*$s), [float](3*$s), [float](4*$s))

    # Speaker cone
    $pts = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float](4*$s), [float](6*$s)),
        [System.Drawing.PointF]::new([float](9*$s), [float](3*$s)),
        [System.Drawing.PointF]::new([float](9*$s), [float](12*$s)),
        [System.Drawing.PointF]::new([float](4*$s), [float](10*$s))
    )
    $g.FillPolygon($brush, $pts)

    # Sound waves
    $g.DrawArc($pen, [float](10*$s), [float](5*$s), [float](3*$s), [float](5*$s), -50, 100)
    $g.DrawArc($pen, [float](12*$s), [float](3*$s), [float](4*$s), [float](9*$s), -50, 100)

    $pen.Dispose(); $brush.Dispose(); $g.Dispose()
    return $bmp
}

$sizes   = @(16, 32, 48, 256)
$pngData = @{}
foreach ($sz in $sizes) {
    $bmp = New-SpeakerBitmap $sz
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData[$sz] = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$outDir = Join-Path $PSScriptRoot "..\client\LoopcastUA\Resources"
$null   = New-Item -ItemType Directory -Force $outDir
$outPath = Join-Path $outDir "app.ico"

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([uint16]0)               # reserved
$bw.Write([uint16]1)               # type: ICO
$bw.Write([uint16]$sizes.Count)    # image count

# ICONDIRENTRY (16 bytes each)
$dataOffset = 6 + 16 * $sizes.Count
$off = $dataOffset
foreach ($sz in $sizes) {
    $bw.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))   # width  (0 = 256)
    $bw.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))   # height (0 = 256)
    $bw.Write([byte]0)             # color count
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # planes
    $bw.Write([uint16]32)          # bit depth
    $bw.Write([uint32]$pngData[$sz].Length)
    $bw.Write([uint32]$off)
    $off += $pngData[$sz].Length
}

foreach ($sz in $sizes) { $bw.Write($pngData[$sz]) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$ms.Dispose(); $bw.Dispose()

Write-Host "Generated: $outPath ($([Math]::Round((Get-Item $outPath).Length/1KB,1)) KB)"

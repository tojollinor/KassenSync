param(
    [string]$SourcePath = "src\\KassenSync.App\\Assets\\OrdnerSyncLogoFull.png",
    [string]$OutputPath = "src\\KassenSync.App\\Assets\\OrdnerSync.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Resolve-RepoPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

$sourceFull = Resolve-RepoPath $SourcePath
$outputFull = Resolve-RepoPath $OutputPath

if (-not (Test-Path $sourceFull)) {
    throw "OrdnerSync logo source not found: $sourceFull"
}

$source = [System.Drawing.Bitmap]::new($sourceFull)

try {
    # Crop the symbol from the user-supplied complete square logo.
    $sx = [int][Math]::Round($source.Width * 245.0 / 1254.0)
    $sy = [int][Math]::Round($source.Height * 195.0 / 1254.0)
    $sw = [int][Math]::Round($source.Width * 770.0 / 1254.0)
    $sh = [int][Math]::Round($source.Height * 650.0 / 1254.0)

    $crop = [System.Drawing.Bitmap]::new($sw, $sh, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($crop)
    try {
        $g.DrawImage(
            $source,
            [System.Drawing.Rectangle]::new(0, 0, $sw, $sh),
            [System.Drawing.Rectangle]::new($sx, $sy, $sw, $sh),
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $g.Dispose()
    }

    try {
        # Only near-white pixels connected to the outside are background.
        # White sync-arrow pixels inside the logo therefore remain opaque.
        $count = $sw * $sh
        $candidate = [bool[]]::new($count)
        $outside = [bool[]]::new($count)
        $queue = [System.Collections.Generic.Queue[int]]::new()

        for ($y = 0; $y -lt $sh; $y++) {
            for ($x = 0; $x -lt $sw; $x++) {
                $p = $crop.GetPixel($x, $y)
                $dr = 255 - [int]$p.R
                $dg = 255 - [int]$p.G
                $db = 255 - [int]$p.B
                $candidate[($y * $sw) + $x] = (($dr*$dr + $dg*$dg + $db*$db) -lt 3025)
            }
        }

        function Add-OutsidePixel {
            param([int]$X, [int]$Y)
            $i = ($Y * $sw) + $X
            if ($candidate[$i] -and -not $outside[$i]) {
                $outside[$i] = $true
                $queue.Enqueue($i)
            }
        }

        for ($x = 0; $x -lt $sw; $x++) {
            Add-OutsidePixel $x 0
            Add-OutsidePixel $x ($sh - 1)
        }
        for ($y = 0; $y -lt $sh; $y++) {
            Add-OutsidePixel 0 $y
            Add-OutsidePixel ($sw - 1) $y
        }

        while ($queue.Count -gt 0) {
            $i = $queue.Dequeue()
            $x = $i % $sw
            $y = [int][Math]::Floor($i / $sw)

            if ($x -gt 0) {
                $n = $i - 1
                if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
            }
            if ($x -lt ($sw - 1)) {
                $n = $i + 1
                if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
            }
            if ($y -gt 0) {
                $n = $i - $sw
                if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
            }
            if ($y -lt ($sh - 1)) {
                $n = $i + $sw
                if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
            }
        }

        $opaque = [System.Drawing.Bitmap]::new($sw, $sh, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $minX = $sw
        $minY = $sh
        $maxX = -1
        $maxY = -1

        for ($y = 0; $y -lt $sh; $y++) {
            for ($x = 0; $x -lt $sw; $x++) {
                $i = ($y * $sw) + $x
                $p = $crop.GetPixel($x, $y)
                if ($outside[$i]) {
                    $opaque.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                }
                else {
                    $opaque.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $p.R, $p.G, $p.B))
                    if ($x -lt $minX) { $minX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }

        if ($maxX -lt $minX -or $maxY -lt $minY) {
            throw "No visible logo pixels found."
        }

        try {
            $visibleWidth = $maxX - $minX + 1
            $visibleHeight = $maxY - $minY + 1
            $visible = [System.Drawing.Bitmap]::new($visibleWidth, $visibleHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $vg = [System.Drawing.Graphics]::FromImage($visible)
            try {
                $vg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $vg.DrawImage(
                    $opaque,
                    [System.Drawing.Rectangle]::new(0, 0, $visibleWidth, $visibleHeight),
                    [System.Drawing.Rectangle]::new($minX, $minY, $visibleWidth, $visibleHeight),
                    [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $vg.Dispose()
            }

            try {
                $sizes = @(16, 24, 32, 48, 64, 128, 256)
                $frames = @()

                foreach ($size in $sizes) {
                    $canvas = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    $cg = [System.Drawing.Graphics]::FromImage($canvas)

                    try {
                        $cg.Clear([System.Drawing.Color]::Transparent)
                        $cg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                        $cg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                        $cg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                        $cg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                        $cg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

                        $pad = [Math]::Max(1, [int][Math]::Round($size * 18.0 / 256.0))
                        $available = $size - (2 * $pad)
                        $scale = [Math]::Min(
                            $available / [double]$visible.Width,
                            $available / [double]$visible.Height)

                        $dw = [Math]::Max(1, [int][Math]::Round($visible.Width * $scale))
                        $dh = [Math]::Max(1, [int][Math]::Round($visible.Height * $scale))
                        $dx = [int](($size - $dw) / 2)
                        $dy = [int](($size - $dh) / 2)

                        $cg.DrawImage($visible, [System.Drawing.Rectangle]::new($dx, $dy, $dw, $dh))
                    }
                    finally {
                        $cg.Dispose()
                    }

                    # Exterior stays transparent; every drawn icon pixel becomes fully opaque.
                    for ($y = 0; $y -lt $size; $y++) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $p = $canvas.GetPixel($x, $y)
                            if ($p.A -gt 0 -and $p.A -lt 255) {
                                $canvas.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $p.R, $p.G, $p.B))
                            }
                        }
                    }

                    $ms = [System.IO.MemoryStream]::new()
                    try {
                        $canvas.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                        $frames += [PSCustomObject]@{
                            Size = $size
                            Bytes = $ms.ToArray()
                        }
                    }
                    finally {
                        $ms.Dispose()
                        $canvas.Dispose()
                    }
                }

                [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputFull)) | Out-Null
                $stream = [System.IO.File]::Open($outputFull, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
                $writer = [System.IO.BinaryWriter]::new($stream)

                try {
                    $writer.Write([UInt16]0)
                    $writer.Write([UInt16]1)
                    $writer.Write([UInt16]$frames.Count)

                    $offset = 6 + (16 * $frames.Count)
                    foreach ($frame in $frames) {
                        $dim = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
                        $writer.Write([Byte]$dim)
                        $writer.Write([Byte]$dim)
                        $writer.Write([Byte]0)
                        $writer.Write([Byte]0)
                        $writer.Write([UInt16]1)
                        $writer.Write([UInt16]32)
                        $writer.Write([UInt32]$frame.Bytes.Length)
                        $writer.Write([UInt32]$offset)
                        $offset += $frame.Bytes.Length
                    }

                    foreach ($frame in $frames) {
                        $writer.Write([Byte[]]$frame.Bytes)
                    }
                }
                finally {
                    $writer.Dispose()
                    $stream.Dispose()
                }
            }
            finally {
                $visible.Dispose()
            }
        }
        finally {
            $opaque.Dispose()
        }
    }
    finally {
        $crop.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "OrdnerSync icon generated from supplied raster logo: $outputFull"
